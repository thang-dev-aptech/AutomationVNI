namespace Backend.Modules.Campaign.FullMedia;

/// <summary>Cấu hình Vision không hợp lệ (ví dụ lượt A/B không độc lập khi Enabled).</summary>
public sealed class VisionConfigurationException : InvalidOperationException
{
    public string ReasonCode { get; }

    public VisionConfigurationException(string reasonCode, string message)
        : base(message)
    {
        ReasonCode = reasonCode;
    }
}
