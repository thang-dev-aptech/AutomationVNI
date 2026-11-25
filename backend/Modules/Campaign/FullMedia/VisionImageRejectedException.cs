namespace Backend.Modules.Campaign.FullMedia;

/// <summary>Ảnh bị từ chối vĩnh viễn (quá lớn / không phải ảnh) — không thử lại AI.</summary>
public sealed class VisionImageRejectedException : ArgumentException
{
    public string ReasonCode { get; }

    public VisionImageRejectedException(string reasonCode, string message)
        : base(message)
    {
        ReasonCode = reasonCode;
    }
}
