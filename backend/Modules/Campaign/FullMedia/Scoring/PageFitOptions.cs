namespace Backend.Modules.Campaign.FullMedia.Scoring;

public sealed class PageFitOptions
{
    public const string SectionName = "PageFit";

    /// <summary>Rỗng = dùng DefaultVisionModel ?? DefaultTextModel của provider.</summary>
    public string Model { get; set; } = "";

    public int TimeoutSeconds { get; set; } = 60;

    public int TopLogprobs { get; set; } = 5;

    public int MinDelayBetweenRequestsMs { get; set; } = 200;

    public int MaxRetries { get; set; } = 2;

    public decimal InputPricePerMillionUsd { get; set; }

    public decimal OutputPricePerMillionUsd { get; set; }
}
