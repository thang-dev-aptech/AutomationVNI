using Backend.Data;
using Backend.Shared;
using Backend.Shared.Repositories;
using Microsoft.EntityFrameworkCore;

namespace Backend.Modules.PublishLog;

public class PublishLogRepository : GenericRepository<PublishLogModel>
{
    public PublishLogRepository(AppDbContext context, IUserContext userContext)
        : base(context, userContext) { }

    public async Task<PagedResult<PublishLogResponse>> FilterAsync(
        PublishLogFilterRequest request, CancellationToken ct = default)
    {
        var query = QueryActive();

        if (request.PostId.HasValue)
            query = query.Where(x => x.PostId == request.PostId.Value);

        if (request.SocialChannelId.HasValue)
            query = query.Where(x => x.SocialChannelId == request.SocialChannelId.Value);

        if (request.Status.HasValue)
            query = query.Where(x => x.Status == request.Status.Value);

        if (request.FromDate.HasValue)
            query = query.Where(x => x.CreatedAt >= request.FromDate.Value);

        if (request.ToDate.HasValue)
            query = query.Where(x => x.CreatedAt <= request.ToDate.Value);

        if (!string.IsNullOrWhiteSpace(request.Keyword))
            query = query.Where(x => x.ErrorMessage != null && x.ErrorMessage.Contains(request.Keyword));

        var paged = await PaginateAsync(query, request.Index, request.Size, ct);
        return new PagedResult<PublishLogResponse>
        {
            Items = paged.Items.Select(ToResponse).ToList(),
            Total = paged.Total,
            Index = paged.Index,
            Size = paged.Size
        };
    }

    public async Task<List<PublishLogModel>> GetByPostAsync(Guid postId, CancellationToken ct = default)
        => await QueryActive()
            .Where(x => x.PostId == postId)
            .OrderByDescending(x => x.AttemptNumber)
            .ToListAsync(ct);

    public async Task<int> GetNextAttemptNumberAsync(Guid postId, CancellationToken ct = default)
    {
        var max = await QueryActive()
            .Where(x => x.PostId == postId)
            .MaxAsync(x => (int?)x.AttemptNumber, ct);
        return (max ?? 0) + 1;
    }

    public async Task<PublishLogModel> CreateAsync(
        CreatePublishLogRequest request, CancellationToken ct = default)
    {
        var entity = new PublishLogModel
        {
            PostId = request.PostId,
            SocialChannelId = request.SocialChannelId,
            AttemptNumber = request.AttemptNumber,
            Status = request.Status,
            ExternalPostId = request.ExternalPostId,
            PublishedUrl = request.PublishedUrl,
            RequestPayload = request.RequestPayload,
            ResponsePayload = request.ResponsePayload,
            ErrorCode = request.ErrorCode,
            ErrorMessage = request.ErrorMessage,
            PublishedAt = request.PublishedAt,
            IdempotencyKey = request.IdempotencyKey
        };

        return await base.CreateAsync(entity, ct);
    }

    public async Task<PublishLogModel?> UpdateAsync(
        Guid id, UpdatePublishLogRequest request, CancellationToken ct = default)
    {
        var entity = await GetByIdAsync(id, ct);
        if (entity is null) return null;

        if (request.Status.HasValue) entity.Status = request.Status.Value;
        if (request.ExternalPostId is not null) entity.ExternalPostId = request.ExternalPostId;
        if (request.PublishedUrl is not null) entity.PublishedUrl = request.PublishedUrl;
        if (request.ErrorCode is not null) entity.ErrorCode = request.ErrorCode;
        if (request.ErrorMessage is not null) entity.ErrorMessage = request.ErrorMessage;
        if (request.PublishedAt.HasValue) entity.PublishedAt = request.PublishedAt;
        if (request.ResponsePayload is not null) entity.ResponsePayload = request.ResponsePayload;

        ApplyUpdateAudit(entity);
        await Context.SaveChangesAsync(ct);
        return entity;
    }

    public async Task<bool> HasPendingAsync(Guid postId, CancellationToken ct = default)
        => await QueryActive()
            .AnyAsync(x => x.PostId == postId && x.Status == Enums.PublishStatus.Pending, ct);

    public async Task<bool> HasSuccessAsync(Guid postId, CancellationToken ct = default)
        => await QueryActive()
            .AnyAsync(x => x.PostId == postId && x.Status == Enums.PublishStatus.Success, ct);

    public async Task<PublishLogModel?> GetActiveAsync(Guid postId, CancellationToken ct = default)
        => await QueryActive()
            .Where(x => x.PostId == postId
                && (x.Status == Enums.PublishStatus.Pending
                    || x.Status == Enums.PublishStatus.Processing))
            .OrderByDescending(x => x.CreatedAt)
            .FirstOrDefaultAsync(ct);

    public async Task<PublishLogModel?> GetByIdempotencyKeyAsync(string key, CancellationToken ct = default)
        => await QueryActive()
            .FirstOrDefaultAsync(x => x.IdempotencyKey == key, ct);

    public static PublishLogResponse ToResponse(PublishLogModel e) => new()
    {
        Id = e.Id,
        PostId = e.PostId,
        SocialChannelId = e.SocialChannelId,
        AttemptNumber = e.AttemptNumber,
        Status = e.Status,
        ExternalPostId = e.ExternalPostId,
        PublishedUrl = e.PublishedUrl,
        ErrorCode = e.ErrorCode,
        ErrorMessage = e.ErrorMessage,
        ResponsePayload = e.ResponsePayload,
        PublishedAt = e.PublishedAt,
        IdempotencyKey = e.IdempotencyKey,
        CreatedAt = e.CreatedAt
    };
}
