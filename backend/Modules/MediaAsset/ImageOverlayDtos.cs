namespace Backend.Modules.MediaAsset;

public class ImageOverlayRequest
{
    public string SourceStorageKey { get; set; } = string.Empty;
    public string PostTitle { get; set; } = string.Empty;
    public string Headline { get; set; } = string.Empty;
    public string CtaText { get; set; } = "Đăng ký ngay";
    public string OutputFolder { get; set; } = "rendered";
    public string? LogoStorageKey { get; set; }
}

public class ImageOverlayResult
{
    public string StorageKey { get; set; } = string.Empty;
    public string ContentType { get; set; } = "image/png";
    public long SizeBytes { get; set; }
    public int Width { get; set; }
    public int Height { get; set; }
    public bool TextRendered { get; set; }
    public bool UsedFallbackCopy { get; set; }
}
