using Backend.Modules.SocialChannel.Enums;

namespace Backend.Shared.SocialPublish;

public class SocialPublishRequest
{
    public Guid PostId { get; set; }
    public Guid PublishLogId { get; set; }
    public Guid SocialChannelId { get; set; }
    public SocialPlatform Platform { get; set; }
    public string PageExternalId { get; set; } = string.Empty;
    public string? AccessToken { get; set; }
    public string Caption { get; set; } = string.Empty;
    public string? MediaPreviewUrl { get; set; }
    /// <summary>Storage key nội bộ (local disk). Dùng multipart upload lên Facebook khi không có URL public.</summary>
    public string? MediaStorageKey { get; set; }
    public string? MediaFileName { get; set; }
    public string? MediaMimeType { get; set; }

    /// <summary>
    /// Toàn bộ ảnh của bài (cover đứng đầu). Facebook đăng multi-photo qua attached_media;
    /// Threads hiện chỉ dùng ảnh đầu (các field Media* legacy ở trên).
    /// </summary>
    public List<SocialPublishMediaItem> MediaItems { get; set; } = [];

    public string? Link { get; set; }
    public bool ForceReal { get; set; }
}

public class SocialPublishMediaItem
{
    public string? PublicUrl { get; set; }
    public string? StorageKey { get; set; }
    public string? FileName { get; set; }
    public string? MimeType { get; set; }
}

public class SocialPublishResult
{
    public bool Success { get; set; }
    public bool UsedMock { get; set; }
    public string? PublishedExternalId { get; set; }
    public string? PublishedUrl { get; set; }
    public string? ErrorCode { get; set; }
    public string? ErrorMessage { get; set; }
    public string? RawResponseSanitized { get; set; }

    public static SocialPublishResult Succeeded(
        string externalId, string publishedUrl, string? rawResponse, bool usedMock = false) => new()
    {
        Success = true,
        UsedMock = usedMock,
        PublishedExternalId = externalId,
        PublishedUrl = publishedUrl,
        RawResponseSanitized = rawResponse
    };

    public static SocialPublishResult Failed(
        string errorCode, string errorMessage, string? rawResponse = null) => new()
    {
        Success = false,
        ErrorCode = errorCode,
        ErrorMessage = errorMessage,
        RawResponseSanitized = rawResponse
    };
}
