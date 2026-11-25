namespace Backend.Modules.Campaign.FullMedia.Scoring;

/// <summary>Lỗi tạm — pipeline có thể thử lại sau.</summary>
public sealed class PageFitTransientException : Exception
{
    public string ReasonCode { get; }

    public PageFitTransientException(string reasonCode, string message, Exception? inner = null)
        : base(message, inner)
    {
        ReasonCode = reasonCode;
    }
}
