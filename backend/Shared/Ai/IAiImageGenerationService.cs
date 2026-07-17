namespace Backend.Shared.Ai;

public interface IAiImageGenerationService
{
    /// <summary>True when the resolved provider has an ApiKey configured.</summary>
    bool IsAvailable(string? provider = null);

    Task<AiImageGenerationResult> GenerateAsync(
        AiImageGenerationRequest request, CancellationToken ct = default);
}
