namespace Backend.Shared.SocialPublish;

public class SocialPublishOptions
{
    public string DefaultProvider { get; set; } = "facebook";
    public bool UseRealFacebook { get; set; }
    public bool UseRealThreads { get; set; }
    public FacebookPublishOptions Facebook { get; set; } = new();
    public ThreadsPublishOptions Threads { get; set; } = new();
}

public class FacebookPublishOptions
{
    public string GraphBaseUrl { get; set; } = "https://graph.facebook.com";
    public string GraphVersion { get; set; } = "v20.0";
    public int TimeoutSeconds { get; set; } = 90;
}

public class ThreadsPublishOptions
{
    public string GraphBaseUrl { get; set; } = "https://graph.threads.net";
    public string GraphVersion { get; set; } = "v1.0";
    public int TimeoutSeconds { get; set; } = 90;

    /// <summary>
    /// Base URL công khai của chính app này, dùng để dựng URL tuyệt đối cho ảnh.
    /// Threads KHÔNG nhận upload multipart — nó tự đi tải ảnh từ image_url, nên ảnh phải
    /// nằm sau một URL HTTPS mà Meta truy cập được. MediaAsset preview đang là AllowAnonymous
    /// nên chỉ cần ghép base URL vào đường dẫn tương đối.
    /// Để trống thì bài có ảnh sẽ đăng dạng text-only.
    /// </summary>
    public string PublicBaseUrl { get; set; } = string.Empty;

    /// <summary>
    /// Chờ giữa bước tạo container và bước publish, để Meta kịp tải và xử lý ảnh.
    /// Docs khuyến nghị ~30s cho media; bài text thì bỏ qua hoàn toàn.
    /// </summary>
    public int MediaProcessingDelaySeconds { get; set; } = 15;

    /// <summary>Giới hạn cứng của Threads. Caption dài hơn sẽ bị cắt kèm cảnh báo.</summary>
    public int MaxTextLength { get; set; } = 500;
}
