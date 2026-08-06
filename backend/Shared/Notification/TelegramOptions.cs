namespace Backend.Shared.Notification;

/// <summary>
/// Cấu hình bot Telegram. BotToken để RỖNG trong appsettings — nạp qua user-secrets ở dev
/// và biến môi trường ở production, giống Jwt:SecretKey và AiProviders:*:ApiKey.
/// </summary>
public class TelegramOptions
{
    public bool Enabled { get; set; }
    public string BotToken { get; set; } = "";
    public string BaseUrl { get; set; } = "https://api.telegram.org";

    /// <summary>
    /// Nơi bot gửi thông báo tin mới. Bỏ trống thì bot tự học: ai nhắn cho nó lần đầu sẽ
    /// thành chat nhận tin (ghi vào đây lúc chạy, không lưu lại) — đủ dùng cho một người.
    /// </summary>
    public string ChatId { get; set; } = "";

    /// <summary>
    /// CHỈ những chat id trong đây mới ra lệnh được. Rỗng = cho mọi chat (chỉ hợp lúc dev).
    ///
    /// Bot Telegram ai biết @username cũng nhắn được, mà lệnh /dang thì đăng thẳng lên
    /// fanpage thật. Ở production để rỗng nghĩa là mở cửa cho người lạ đăng bài hộ mình.
    /// </summary>
    public List<string> AllowedChatIds { get; set; } = [];

    /// <summary>
    /// Giây chờ của long-polling. Telegram giữ kết nối tới khi có update hoặc hết ngần này —
    /// 30s là mức thường dùng: đủ ít request mà vẫn phản hồi gần như tức thì.
    /// </summary>
    public int PollTimeoutSeconds { get; set; } = 30;

    /// <summary>Trần số tin gửi mỗi lượt quét, chặn việc cào về 30 bài rồi spam 30 tin nhắn.</summary>
    public int MaxNotifyPerTick { get; set; } = 5;
}
