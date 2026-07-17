using Backend.Data;
using Backend.Shared;
using Backend.Shared.Repositories;
using Microsoft.EntityFrameworkCore;

namespace Backend.Modules.MediaEmbedding;

public class MediaEmbeddingRepository : GenericRepository<MediaEmbeddingModel>
{
    public MediaEmbeddingRepository(AppDbContext context, IUserContext userContext)
        : base(context, userContext) { }

    public async Task<PagedResult<MediaEmbeddingResponse>> FilterAsync(
        MediaEmbeddingFilterRequest request, CancellationToken ct = default)
    {
        var query = QueryActive();

        if (request.MediaAssetId.HasValue)
            query = query.Where(x => x.MediaAssetId == request.MediaAssetId.Value);

        if (!string.IsNullOrWhiteSpace(request.ModelName))
            query = query.Where(x => x.ModelName == request.ModelName.Trim());

        var paged = await PaginateAsync(query, request.Index, request.Size, ct);
        return new PagedResult<MediaEmbeddingResponse>
        {
            Items = paged.Items.Select(ToResponse).ToList(),
            Total = paged.Total,
            Index = paged.Index,
            Size = paged.Size
        };
    }

    public async Task<MediaEmbeddingModel?> GetByMediaAssetAsync(Guid mediaAssetId, CancellationToken ct = default)
        => await QueryActive().FirstOrDefaultAsync(x => x.MediaAssetId == mediaAssetId, ct);

    public async Task<MediaEmbeddingModel> CreateAsync(
        CreateMediaEmbeddingRequest request, CancellationToken ct = default)
    {
        var entity = new MediaEmbeddingModel
        {
            MediaAssetId = request.MediaAssetId,
            ModelName = request.ModelName.Trim(),
            Dimensions = request.Dimensions,
            Embedding = request.Embedding,
            SourceText = request.SourceText
        };

        return await base.CreateAsync(entity, ct);
    }

    public async Task<MediaEmbeddingModel?> UpdateAsync(
        Guid id, UpdateMediaEmbeddingRequest request, CancellationToken ct = default)
    {
        var entity = await GetByIdAsync(id, ct);
        if (entity is null) return null;

        if (request.Embedding is not null) entity.Embedding = request.Embedding;
        if (request.SourceText is not null) entity.SourceText = request.SourceText;
        if (request.ModelName is not null) entity.ModelName = request.ModelName.Trim();
        if (request.Dimensions.HasValue) entity.Dimensions = request.Dimensions.Value;

        ApplyUpdateAudit(entity);
        await Context.SaveChangesAsync(ct);
        return entity;
    }

    /// <summary>
    /// Tìm top-N embedding gần nhất với vector đầu vào bằng cosine similarity (MVP in-memory).
    /// Scale: thay bằng pgvector / Qdrant.
    /// </summary>
    public async Task<List<(Guid MediaAssetId, double Score)>> FindSimilarAsync(
        float[] queryVector, int topN = 5, double minScore = 0.8, CancellationToken ct = default)
    {
        var all = await QueryActive()
            .Select(x => new { x.MediaAssetId, x.Embedding })
            .ToListAsync(ct);

        return all
            .Select(x => (x.MediaAssetId, Score: CosineSimilarity(queryVector, ToFloatArray(x.Embedding))))
            .Where(x => x.Score >= minScore)
            .OrderByDescending(x => x.Score)
            .Take(topN)
            .ToList();
    }

    public static MediaEmbeddingResponse ToResponse(MediaEmbeddingModel e) => new()
    {
        Id = e.Id,
        MediaAssetId = e.MediaAssetId,
        ModelName = e.ModelName,
        Dimensions = e.Dimensions,
        SourceText = e.SourceText,
        CreatedAt = e.CreatedAt,
        UpdatedAt = e.UpdatedAt
    };

    private static float[] ToFloatArray(byte[] bytes)
    {
        var result = new float[bytes.Length / 4];
        Buffer.BlockCopy(bytes, 0, result, 0, bytes.Length);
        return result;
    }

    private static double CosineSimilarity(float[] a, float[] b)
    {
        if (a.Length != b.Length || a.Length == 0) return 0;
        double dot = 0, magA = 0, magB = 0;
        for (var i = 0; i < a.Length; i++)
        {
            dot += a[i] * b[i];
            magA += a[i] * a[i];
            magB += b[i] * b[i];
        }
        return magA == 0 || magB == 0 ? 0 : dot / (Math.Sqrt(magA) * Math.Sqrt(magB));
    }
}
