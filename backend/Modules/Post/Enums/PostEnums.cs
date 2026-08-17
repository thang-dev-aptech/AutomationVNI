namespace Backend.Modules.Post.Enums;

public enum PostStatus
{
    Draft          = 1,
    Queued         = 2,
    Generating     = 3,
    Ready          = 4,
    Scheduled      = 5,
    Publishing     = 6,
    Published      = 7,
    Failed         = 8,
    Cancelled      = 9,
    WaitingReview  = 10,
    Approved       = 11,
    GeneratingMedia = 12,
    NeedMedia       = 13,
    RenderingTemplate = 14,
    NeedFix           = 15
}

/// <summary>
/// Chế độ đăng TikTok — lựa chọn ở bước ĐĂNG, chỉ có ý nghĩa với post nhắm kênh TikTok.
/// DirectPost (mặc định, null cũng coi như DirectPost): API đăng thẳng, không gắn được nhạc
/// TikTok thật. InboxDraft: gửi video vào Inbox app TikTok thật của người dùng — họ tự mở app
/// thêm caption/quyền riêng tư/chọn nhạc TikTok thật rồi đăng thủ công (cách hợp pháp duy nhất
/// qua API chính thức để có nhạc TikTok thật).
/// </summary>
public enum TikTokPostMode
{
    DirectPost = 1,
    InboxDraft = 2
}

public enum GenerationFlow
{
    FullAI         = 1,
    RAG            = 2,
    Recycle        = 3,
    RecycleRewrite = 4,
    /// <summary>
    /// Chỉ sinh CHỮ, không sinh ảnh — dùng cho bài từ tin đã cào: tóm tắt + link nguồn.
    /// Tiết kiệm một lượt gọi AI ảnh cho mỗi bài, mà tin tức vốn cũng không cần banner.
    /// Bài tự tạo tay vẫn dùng FullAI và vẫn sinh ảnh như cũ.
    /// </summary>
    TextOnly       = 5,

    /// <summary>
    /// AI chỉ sinh CHỮ; ảnh lấy ngẫu nhiên từ MediaFolder gắn PageContext của page rồi ghép chữ
    /// đè lên (RichTemplateRenderService) thay vì sinh ảnh AI. Không có folder/folder rỗng → lỗi
    /// rõ ràng, không tự fallback sang FullAI.
    /// </summary>
    Template       = 6
}
