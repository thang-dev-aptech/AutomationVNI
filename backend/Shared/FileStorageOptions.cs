namespace Backend.Shared;

public class FileStorageOptions
{
    public string RootPath { get; set; } = "Storage/Files";
    public string PublicBaseUrl { get; set; } = "/api/files";
    public long MaxUploadBytes { get; set; } = 8_388_608;
    // .mp4/video/mp4: video Reels dựng bằng FFmpeg (SlideshowVideoRenderService) lưu qua
    // SaveBytesAsync — path đó không kiểm MaxUploadBytes (chỉ SaveAsync cho IFormFile mới kiểm),
    // nên không cần cap riêng cho video, chỉ cần whitelist đúng extension/content-type.
    public string[] AllowedExtensions { get; set; } = [".jpg", ".jpeg", ".png", ".webp", ".mp4"];
    public string[] AllowedContentTypes { get; set; } = ["image/jpeg", "image/png", "image/webp", "video/mp4"];
}
