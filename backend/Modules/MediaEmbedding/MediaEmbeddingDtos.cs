using Backend.Shared;

namespace Backend.Modules.MediaEmbedding;

public class CreateMediaEmbeddingRequest
{
    public Guid MediaAssetId { get; set; }
    public string ModelName { get; set; } = string.Empty;
    public int Dimensions { get; set; }
    public byte[] Embedding { get; set; } = [];
    public string? SourceText { get; set; }
}

public class UpdateMediaEmbeddingRequest
{
    public byte[]? Embedding { get; set; }
    public string? SourceText { get; set; }
    public string? ModelName { get; set; }
    public int? Dimensions { get; set; }
}

public class MediaEmbeddingFilterRequest : PagedFilterRequest
{
    public Guid? MediaAssetId { get; set; }
    public string? ModelName { get; set; }
}

public class MediaEmbeddingResponse
{
    public Guid Id { get; set; }
    public Guid MediaAssetId { get; set; }
    public string ModelName { get; set; } = string.Empty;
    public int Dimensions { get; set; }
    public string? SourceText { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime? UpdatedAt { get; set; }
    // Embedding bytes không trả về mặc định — dùng endpoint riêng nếu cần
}
