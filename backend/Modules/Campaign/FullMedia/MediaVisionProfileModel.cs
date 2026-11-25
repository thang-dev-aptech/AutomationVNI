using Backend.Shared;

namespace Backend.Modules.Campaign.FullMedia;

/// <summary>Lưu đệm mô tả Vision theo (MediaAssetId, FileHash, Pass). Không FK.</summary>
public class MediaVisionProfileModel : BaseEntity
{
    public Guid MediaAssetId { get; set; }
    public string FileHash { get; set; } = string.Empty;
    public VisionPass Pass { get; set; }
    public string DescriptionJson { get; set; } = string.Empty;
    public double Confidence { get; set; }
    public bool Unclear { get; set; }
    public string Model { get; set; } = string.Empty;
}
