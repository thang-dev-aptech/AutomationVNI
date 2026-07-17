using Backend.Shared;

namespace Backend.Modules.MediaEmbedding;

public class MediaEmbeddingModel : BaseEntity
{
    public Guid MediaAssetId { get; set; }
    public string ModelName { get; set; } = string.Empty;
    public int Dimensions { get; set; }
    public byte[] Embedding { get; set; } = [];
    public string? SourceText { get; set; }
}
