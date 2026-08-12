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

/// <summary>Cấu hình dựng + đăng video slideshow Reels (ghép ảnh Template bằng FFmpeg).</summary>
public class ReelsOptions
{
    /// <summary>Đường dẫn binary FFmpeg — tương đối so với thư mục publish (Resources/ffmpeg/ffmpeg).</summary>
    public string FfmpegPath { get; set; } = "Resources/ffmpeg/ffmpeg";

    /// <summary>Nhạc nền cố định do người dùng tự cung cấp — để trống thì video không có âm thanh.</summary>
    public string? AudioTrackPath { get; set; } = "Resources/ReelsAudio/default.mp3";

    /// <summary>Số khung hình mặc định khi tạo post Reels mà không tự nhập ImageCount.</summary>
    public int DefaultFrameCount { get; set; } = 5;

    /// <summary>Mỗi khung hình hiển thị bao lâu trong video (giây).</summary>
    public double SecondsPerFrame { get; set; } = 3.5;

    /// <summary>Host riêng cho bước upload video — khác hẳn GraphBaseUrl (graph.facebook.com).</summary>
    public string RuploadBaseUrl { get; set; } = "https://rupload.facebook.com";

    public int TimeoutSeconds { get; set; } = 180;
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
