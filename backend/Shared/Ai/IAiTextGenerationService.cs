namespace Backend.Shared.Ai;

public interface IAiTextGenerationService
{
    bool IsAvailable(string? provider = null);

    Task<AiTextGenerationResult> GenerateAsync(
        AiTextGenerationRequest request,
        CancellationToken ct = default);
}
