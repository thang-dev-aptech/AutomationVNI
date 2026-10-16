namespace Backend.Modules.Campaign.FullMedia;

public interface IMediaVisionDescriber
{
    Task<VisionDescription> DescribeAsync(Guid mediaAssetId, VisionPass pass, CancellationToken ct = default);
}
