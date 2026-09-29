namespace Backend.Modules.GoogleDrive;

/// <summary>
/// Abstraction gọi Google Drive API thật — tách khỏi worker để test bằng fake, theo đúng cách
/// Threads/TikTok/Meta tách IXxxOAuthService khỏi hạ tầng HTTP thật.
/// </summary>
public interface IGoogleDriveClient
{
    bool IsConfigured();

    /// <summary>Nguyên nhân cụ thể (thiếu FolderId / thiếu CredentialsPath / file không tồn tại), hoặc null khi sẵn sàng.</summary>
    string? DescribeConfigIssue();

    /// <summary>Lấy pageToken khởi điểm — chỉ gọi một lần lúc chưa từng đồng bộ (PageToken rỗng).</summary>
    Task<string> GetStartPageTokenAsync(CancellationToken ct = default);

    /// <summary>
    /// Một lượt changes.list, giới hạn tối đa <paramref name="maxResults"/> thay đổi mỗi lần gọi.
    /// NextPageToken LUÔN được trả về (advance kể cả khi Files/Folders rỗng) — người gọi phải lưu lại
    /// NextPageToken cho lượt sau dù danh sách có rỗng hay không.
    ///
    /// GDRIVE-02: không còn lọc cứng theo 1 FolderId ở tầng client — trả cả thay đổi thư mục
    /// (không trashed) kèm ParentIds thô và file kèm Parents thô; SyncService quyết định thuộc cây
    /// đã biết hay không. Vẫn loại application/vnd.google-apps.* không phải folder.
    ///
    /// QUAN TRỌNG: người gọi phải xử lý TOÀN BỘ GoogleDriveChangesPage trả về rồi mới lưu
    /// NextPageToken — không được tự cắt bớt (Take) danh sách sau khi nhận.
    /// </summary>
    Task<GoogleDriveChangesPage> ListChangesAsync(string? pageToken, int maxResults, CancellationToken ct = default);

    /// <summary>Tải nội dung nhị phân của một file thường (không dùng cho Google Docs/Sheets/Slides gốc).</summary>
    Task<byte[]> DownloadFileAsync(string fileId, CancellationToken ct = default);
}

/// <summary>Một trang kết quả changes.list — folders trước files để SyncService mở rộng cây cùng tick.</summary>
public class GoogleDriveChangesPage
{
    public List<GoogleDriveFolderInfo> Folders { get; set; } = [];
    public List<GoogleDriveFileInfo> Files { get; set; } = [];

    /// <summary>Con trỏ cho lượt poll kế tiếp — luôn có giá trị (changes.list luôn trả nextPageToken hoặc newStartPageToken).</summary>
    public string? NextPageToken { get; set; }
}

/// <summary>GDRIVE-02/05: thư mục phát hiện qua changes.list (ParentIds + Name thô từ Drive).</summary>
public class GoogleDriveFolderInfo
{
    public string FolderId { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public List<string> ParentIds { get; set; } = [];

    /// <summary>
    /// ApiClient thật luôn bỏ qua trashed; fake test có thể set true để xác nhận SyncService
    /// cũng loại thư mục đã trash dù parent nằm trong cây.
    /// </summary>
    public bool Trashed { get; set; }
}

public class GoogleDriveFileInfo
{
    public string FileId { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string MimeType { get; set; } = string.Empty;
    public long SizeBytes { get; set; }

    /// <summary>Parent trực tiếp từ Drive — SyncService nhận file khi Parents giao tập thư mục đã biết.</summary>
    public List<string> Parents { get; set; } = [];
}
