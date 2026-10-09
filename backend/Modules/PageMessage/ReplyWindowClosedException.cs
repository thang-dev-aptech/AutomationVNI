namespace Backend.Modules.PageMessage;

/// <summary>Gửi Messenger ngoài cửa sổ 24h RESPONSE — không gọi Graph API.</summary>
public sealed class ReplyWindowClosedException(string message) : InvalidOperationException(message);
