namespace Backend.Modules.Campaign.FullMedia.Scoring;

/// <summary>Lỗi cấu hình/provider không dùng được — không thử lại.</summary>
public sealed class PageFitPermanentException : Exception
{
    public string ReasonCode { get; }

    public PageFitPermanentException(string reasonCode, string message, Exception? inner = null)
        : base(message, inner)
    {
        ReasonCode = reasonCode;
    }
}
