namespace Backend.Shared;

public class FileStorageOptions
{
    public string RootPath { get; set; } = "Storage/Files";
    public string PublicBaseUrl { get; set; } = "/api/files";
    public long MaxUploadBytes { get; set; } = 8_388_608;
    public string[] AllowedExtensions { get; set; } = [".jpg", ".jpeg", ".png", ".webp"];
    public string[] AllowedContentTypes { get; set; } = ["image/jpeg", "image/png", "image/webp"];
}
