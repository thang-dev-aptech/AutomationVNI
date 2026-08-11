namespace Backend.Modules.MediaAsset;

public class ImageOverlayRequest
{
    public string SourceStorageKey { get; set; } = string.Empty;
    public string PostTitle { get; set; } = string.Empty;
    public string Headline { get; set; } = string.Empty;
    public string Subheadline { get; set; } = string.Empty;
    public List<string> BulletPoints { get; set; } = new();
    public string CtaText { get; set; } = "Đăng ký ngay";

    /// <summary>Dòng 1 footer — tên đơn vị/thương hiệu (PageContext.BrandName).</summary>
    public string? OrgLine { get; set; }

    /// <summary>Dòng 2 footer — hotline · website · hashtag (build từ PageContext).</summary>
    public string? ContactLine { get; set; }

    /// <summary>PageContext.BrandColors nguyên văn (chuỗi hex cách nhau bởi dấu phẩy) — khung chữ Template bám theo màu này.</summary>
    public string? BrandColors { get; set; }
    public string OutputFolder { get; set; } = "rendered";
    public string? LogoStorageKey { get; set; }
    
    // Layout Info
    public string LayoutStyle { get; set; } = "TopBottomSplit"; // TopBottomSplit, SidebarLeft, SidebarRight, FreeText
    
    // AI Regions (percentage 0-100)
    public float? SafeTextRegionX { get; set; }
    public float? SafeTextRegionY { get; set; }
    public float? SafeTextRegionWidth { get; set; }
    public float? SafeTextRegionHeight { get; set; }
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
