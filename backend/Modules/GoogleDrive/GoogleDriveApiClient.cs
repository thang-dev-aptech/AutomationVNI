using Google.Apis.Auth.OAuth2;
using Google.Apis.Drive.v3;
using Google.Apis.Services;
using Microsoft.Extensions.Options;

namespace Backend.Modules.GoogleDrive;

/// <summary>
/// Cài đặt thật của IGoogleDriveClient, dùng Google Service Account (đọc-only) — không phải OAuth
/// cá nhân như Meta/Threads/TikTok, vì đây là tích hợp đọc 1 folder dùng chung, không cần consent
/// per-user. Xem quyết định GDRIVE-01 trong pcs để biết lý do chọn changes.list thay vì
/// modifiedTime filter.
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

    public async Task<GoogleDriveChangesPage> ListChangesAsync(string? pageToken, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(pageToken))
            throw new ArgumentException(
                "pageToken không được rỗng — gọi GetStartPageTokenAsync trước lượt poll đầu tiên.",
                nameof(pageToken));

        var folderId = options.Value.FolderId;
        using var service = BuildDriveService();

        var request = service.Changes.List(pageToken);
        request.Fields = "nextPageToken, newStartPageToken, changes(fileId, removed, file(id, name, mimeType, size, trashed, parents))";
        request.PageSize = 100;

        var response = await request.ExecuteAsync(ct);

        var files = new List<GoogleDriveFileInfo>();
        foreach (var change in response.Changes ?? [])
        {
            if (change.Removed == true) continue;

            var file = change.File;
            if (file is null) continue;
            if (file.Trashed == true) continue;
            if (file.MimeType is null) continue;
            if (file.MimeType == FolderMimeType) continue;
            if (file.MimeType.StartsWith(GoogleAppsMimePrefix, StringComparison.Ordinal)) continue;
            if (file.Parents is null || !file.Parents.Contains(folderId)) continue;

            files.Add(new GoogleDriveFileInfo
            {
                FileId = file.Id ?? change.FileId,
                Name = file.Name ?? string.Empty,
                MimeType = file.MimeType,
                SizeBytes = file.Size ?? 0,
            });
        }

        // changes.list LUÔN trả một trong hai: NextPageToken (còn trang tiếp trong CÙNG lượt gọi
        // này) hoặc NewStartPageToken (đã bắt kịp, dùng làm điểm bắt đầu cho LƯỢT SAU). Advance
        // theo cái nào có sẵn — không bao giờ để null, kể cả khi Files rỗng.
        return new GoogleDriveChangesPage
        {
            Files = files,
            NextPageToken = response.NextPageToken ?? response.NewStartPageToken,
        };
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
