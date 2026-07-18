namespace Backend.Shared.Ai;

public interface IAiTextGenerationService
{
    bool IsAvailable(string? provider = null);

    Task<AiTextGenerationResult> GenerateAsync(
        AiTextGenerationRequest request,
        CancellationToken ct = default);

    /// <summary>Đề xuất N ý tưởng bài đăng ngắn gọn từ một chủ đề (cho bulk AI ideation).</summary>
    Task<List<string>> SuggestIdeasAsync(
        string topic, int count, string? category, CancellationToken ct = default);
}
