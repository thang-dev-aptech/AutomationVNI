using Google.Apis.Auth.OAuth2;
using Google.Apis.Drive.v3;
using Google.Apis.Services;
using Microsoft.Extensions.Options;

namespace Backend.Modules.GoogleDrive;

/// <summary>
/// Cài đặt thật của IGoogleDriveClient, dùng Google Service Account (đọc-only) — không phải OAuth
/// cá nhân như Meta/Threads/TikTok, vì đây là tích hợp đọc 1 folder dùng chung, không cần consent
/// per-user. Xem quyết định GDRIVE-01/GDRIVE-02 trong pcs: changes.list + tập thư mục đã biết
/// (không có API ancestor trên Drive.v3).
/// </summary>
public class GoogleDriveApiClient(IOptions<GoogleDriveOptions> options) : IGoogleDriveClient
{
    private const string FolderMimeType = "application/vnd.google-apps.folder";
    private const string GoogleAppsMimePrefix = "application/vnd.google-apps.";
    private const string ApplicationName = "VNI Automation";

    public string? DescribeConfigIssue()
    {
        var o = options.Value;

        if (string.IsNullOrWhiteSpace(o.FolderId))
            return "Thiếu GoogleDrive:FolderId. Set qua appsettings.Production.json hoặc biến môi trường GoogleDrive__FolderId.";

        if (string.IsNullOrWhiteSpace(o.CredentialsPath))
            return "Thiếu GoogleDrive:CredentialsPath. Trỏ tới file khoá Service Account JSON qua " +
                   "appsettings.Production.json hoặc biến môi trường GoogleDrive__CredentialsPath — " +
                   "KHÔNG đặt giá trị thật trong appsettings.json.";

        if (!File.Exists(o.CredentialsPath))
            return $"Không tìm thấy file khoá Service Account tại '{o.CredentialsPath}'. " +
                   "Kiểm tra lại GoogleDrive:CredentialsPath hoặc đường dẫn mount trên server.";

        return null;
    }

    public bool IsConfigured() => DescribeConfigIssue() is null;

    public async Task<string> GetStartPageTokenAsync(CancellationToken ct = default)
    {
        using var service = BuildDriveService();
        var response = await service.Changes.GetStartPageToken().ExecuteAsync(ct);
        return response.StartPageTokenValue
            ?? throw new InvalidOperationException("Google Drive không trả về startPageToken.");
    }

    public async Task<GoogleDriveChangesPage> ListChangesAsync(
        string? pageToken, int maxResults, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(pageToken))
            throw new ArgumentException(
                "pageToken không được rỗng — gọi GetStartPageTokenAsync trước lượt poll đầu tiên.",
                nameof(pageToken));

        using var service = BuildDriveService();

        var request = service.Changes.List(pageToken);
        request.Fields = "nextPageToken, newStartPageToken, changes(fileId, removed, file(id, name, mimeType, size, trashed, parents))";
        // Giới hạn NGAY tại tầng gọi Drive — không phải cắt bớt sau khi nhận về. NextPageToken
        // server trả luôn khớp đúng với PageSize này, nên người gọi xử lý hết Files/Folders là đủ.
        request.PageSize = Math.Clamp(maxResults, 1, 1000);

        var response = await request.ExecuteAsync(ct);

        var folders = new List<GoogleDriveFolderInfo>();
        var files = new List<GoogleDriveFileInfo>();
        var removedOrTrashedIds = new List<string>();
        foreach (var change in response.Changes ?? [])
        {
            // GDRIVE-05 Task B: trước đây continue thẳng, không có tín hiệu nào surface ra —
            // SyncService không cách nào biết một file/folder đã biết vừa biến mất để cascade
            // soft-delete. Bây giờ surface ID ra RemovedOrTrashedIds; SyncService tự tra map/
            // MediaAssetModel — ID chưa từng được biết sẽ không khớp gì và bị bỏ qua tự nhiên.
            if (change.Removed == true)
            {
                if (!string.IsNullOrWhiteSpace(change.FileId))
                    removedOrTrashedIds.Add(change.FileId);
                continue;
            }

            var file = change.File;
            if (file is null) continue;

            if (file.Trashed == true)
            {
                var trashedId = file.Id ?? change.FileId;
                if (!string.IsNullOrWhiteSpace(trashedId))
                    removedOrTrashedIds.Add(trashedId);
                continue;
            }

            if (file.MimeType is null) continue;

            var parentIds = (file.Parents ?? []).Where(p => !string.IsNullOrWhiteSpace(p)).ToList();

            if (file.MimeType == FolderMimeType)
            {
                // GDRIVE-02: trả thư mục (không trashed) kèm ParentIds thô — SyncService quyết định
                // có nằm trong cây đã biết hay không. Không lọc theo 1 FolderId ở đây.
                folders.Add(new GoogleDriveFolderInfo
                {
                    FolderId = file.Id ?? change.FileId ?? string.Empty,
                    Name = file.Name ?? string.Empty,
                    ParentIds = parentIds,
                    Trashed = false,
                });
                continue;
            }

            // Google Docs/Sheets/Slides gốc (và mọi google-apps.* khác folder) bỏ qua hoàn toàn.
            if (file.MimeType.StartsWith(GoogleAppsMimePrefix, StringComparison.Ordinal)) continue;

            files.Add(new GoogleDriveFileInfo
            {
                FileId = file.Id ?? change.FileId ?? string.Empty,
                Name = file.Name ?? string.Empty,
                MimeType = file.MimeType,
                SizeBytes = file.Size ?? 0,
                Parents = parentIds,
            });
        }

        // changes.list LUÔN trả một trong hai: NextPageToken (còn trang tiếp trong CÙNG lượt gọi
        // này) hoặc NewStartPageToken (đã bắt kịp, dùng làm điểm bắt đầu cho LƯỢT SAU).
        return new GoogleDriveChangesPage
        {
            Folders = folders,
            Files = files,
            RemovedOrTrashedIds = removedOrTrashedIds,
            NextPageToken = response.NextPageToken ?? response.NewStartPageToken,
        };
    }

    public async Task<GoogleDriveFolderTree> ListFolderTreeAsync(string rootFolderId, CancellationToken ct = default)
    {
        using var service = BuildDriveService();
        var tree = new GoogleDriveFolderTree();
        var queue = new Queue<string>();
        var visited = new HashSet<string>(StringComparer.Ordinal);

        if (!string.IsNullOrWhiteSpace(rootFolderId))
        {
            queue.Enqueue(rootFolderId);
            visited.Add(rootFolderId);
        }

        while (queue.Count > 0)
        {
            var parentId = queue.Dequeue();
            string? pageToken = null;

            do
            {
                var request = service.Files.List();
                // Escape dấu nháy đơn phòng khi ID có ký tự lạ — ID Drive thật không có, nhưng
                // đừng nối chuỗi ngây thơ vào một câu query.
                request.Q = $"'{parentId.Replace("'", "\\'")}' in parents and trashed = false";
                request.Fields = "nextPageToken, files(id, name, mimeType, size, parents)";
                request.PageSize = 1000;
                request.PageToken = pageToken;

                var response = await request.ExecuteAsync(ct);
                foreach (var file in response.Files ?? [])
                {
                    if (file.MimeType is null) continue;
                    var parentIds = (file.Parents ?? []).Where(p => !string.IsNullOrWhiteSpace(p)).ToList();

                    if (file.MimeType == FolderMimeType)
                    {
                        var folderId = file.Id ?? string.Empty;
                        tree.Folders.Add(new GoogleDriveFolderInfo
                        {
                            FolderId = folderId,
                            Name = file.Name ?? string.Empty,
                            ParentIds = parentIds,
                        });
                        if (!string.IsNullOrWhiteSpace(folderId) && visited.Add(folderId))
                            queue.Enqueue(folderId);
                        continue;
                    }

                    if (file.MimeType.StartsWith(GoogleAppsMimePrefix, StringComparison.Ordinal)) continue;

                    tree.Files.Add(new GoogleDriveFileInfo
                    {
                        FileId = file.Id ?? string.Empty,
                        Name = file.Name ?? string.Empty,
                        MimeType = file.MimeType,
                        SizeBytes = file.Size ?? 0,
                        Parents = parentIds,
                    });
                }

                pageToken = response.NextPageToken;
            } while (!string.IsNullOrEmpty(pageToken));
        }

        return tree;
    }

    public async Task<byte[]> DownloadFileAsync(string fileId, CancellationToken ct = default)
    {
        using var service = BuildDriveService();
        using var stream = new MemoryStream();

        var progress = await service.Files.Get(fileId).DownloadAsync(stream, ct);
        if (progress.Status == Google.Apis.Download.DownloadStatus.Failed)
            throw progress.Exception ?? new InvalidOperationException($"Tải file Google Drive '{fileId}' thất bại.");

        return stream.ToArray();
    }

    private DriveService BuildDriveService()
    {
        var issue = DescribeConfigIssue();
        if (issue is not null)
            throw new InvalidOperationException(issue);

        var credential = GoogleCredential.FromFile(options.Value.CredentialsPath)
            .CreateScoped(DriveService.Scope.DriveReadonly);

        return new DriveService(new BaseClientService.Initializer
        {
            HttpClientInitializer = credential,
            ApplicationName = ApplicationName,
        });
    }
}
