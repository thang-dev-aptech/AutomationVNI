namespace Backend.Modules.Campaign.FullMedia;

public enum VisionUsability
{
    Usable = 1,
    Unusable = 2,
    Failed = 3,
}

public sealed record VisionOutcome(
    VisionUsability Status,
    VisionDescription? Description,
    string? ReasonCode = null);
