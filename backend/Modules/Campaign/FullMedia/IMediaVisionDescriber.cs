namespace Backend.Modules.Campaign.FullMedia;

public interface IMediaVisionDescriber
{
    Task<VisionDescription> DescribeAsync(Guid mediaAssetId, VisionPass pass, CancellationToken ct = default);

    Task<VisionOutcome> GetUsableAsync(Guid mediaAssetId, VisionPass pass, CancellationToken ct = default);
}
