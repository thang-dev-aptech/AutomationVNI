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
    /// Một lượt changes.list. NextPageToken LUÔN được trả về (advance kể cả khi Files rỗng) —
    /// người gọi phải lưu lại NextPageToken cho lượt sau dù danh sách file có rỗng hay không.
    /// </summary>
    Task<GoogleDriveChangesPage> ListChangesAsync(string? pageToken, CancellationToken ct = default);

    /// <summary>Tải nội dung nhị phân của một file thường (không dùng cho Google Docs/Sheets/Slides gốc).</summary>
    Task<byte[]> DownloadFileAsync(string fileId, CancellationToken ct = default);
}

/// <summary>Một trang kết quả changes.list, đã lọc theo folder/trashed/folder-type/google-apps mimeType.</summary>
public class GoogleDriveChangesPage
{
    public List<GoogleDriveFileInfo> Files { get; set; } = [];

    /// <summary>Con trỏ cho lượt poll kế tiếp — luôn có giá trị (changes.list luôn trả nextPageToken hoặc newStartPageToken).</summary>
    public string? NextPageToken { get; set; }
}

public class GoogleDriveFileInfo
{
    public string FileId { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string MimeType { get; set; } = string.Empty;
    public long SizeBytes { get; set; }
}
