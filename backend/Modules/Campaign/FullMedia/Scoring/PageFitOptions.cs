namespace Backend.Modules.Campaign.FullMedia.Scoring;

public sealed class PageFitOptions
{
    public const string SectionName = "PageFit";

    /// <summary>
    /// "Vote" (mặc định) = bỏ phiếu nhiều model trên văn bản;
    /// "Logprobs" = xác suất từ top_logprobs (provider có logprobs).
    /// </summary>
    public string Mode { get; set; } = "Vote";

    /// <summary>Tên provider trong AiProviders; rỗng = AiProviders:DefaultProvider.</summary>
    public string Provider { get; set; } = "";

    /// <summary>Rỗng = dùng DefaultVisionModel ?? DefaultTextModel của provider (chế độ Logprobs).</summary>
    public string Model { get; set; } = "";

    /// <summary>Danh sách model bỏ phiếu (chế độ Vote). Phải có ít nhất MinVoters model khác nhau.</summary>
    public List<string> Models { get; set; } = [];

    /// <summary>Tối thiểu số model khác nhau khi Mode=Vote. Mặc định 3.</summary>
    public int MinVoters { get; set; } = 3;

    /// <summary>
    /// max_tokens cho mỗi phiếu Vote. Model suy luận có thể cần &gt; 1 token.
    /// </summary>
    public int VoteMaxTokens { get; set; } = 16;

    public int TimeoutSeconds { get; set; } = 60;

    public int TopLogprobs { get; set; } = 5;

    public int MinDelayBetweenRequestsMs { get; set; } = 200;

    public int MaxRetries { get; set; } = 2;

    /// <summary>Trần chờ Retry-After (giây). Mặc định 60 — tránh chờ hàng giờ.</summary>
    public int MaxRetryAfterSeconds { get; set; } = 60;

    public decimal InputPricePerMillionUsd { get; set; }

    public decimal OutputPricePerMillionUsd { get; set; }
}
