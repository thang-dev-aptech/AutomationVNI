using Backend.Data;
using Backend.Shared;
using Backend.Shared.Repositories;
using Microsoft.EntityFrameworkCore;

namespace Backend.Modules.GenerationJob;

public class GenerationJobRepository : GenericRepository<GenerationJobModel>
{
    public GenerationJobRepository(AppDbContext context, IUserContext userContext)
        : base(context, userContext) { }

    public async Task<PagedResult<GenerationJobResponse>> FilterAsync(
        GenerationJobFilterRequest request, CancellationToken ct = default)
    {
        var query = QueryActive();

        if (request.PostId.HasValue)
            query = query.Where(x => x.PostId == request.PostId.Value);

        if (request.JobType.HasValue)
            query = query.Where(x => x.JobType == request.JobType.Value);

        if (request.Status.HasValue)
            query = query.Where(x => x.Status == request.Status.Value);

        if (request.FlowType.HasValue)
            query = query.Where(x => x.FlowType == request.FlowType.Value);

        if (!string.IsNullOrWhiteSpace(request.Keyword))
            query = query.Where(x => x.ErrorMessage != null && x.ErrorMessage.Contains(request.Keyword));

        var paged = await PaginateAsync(query, request.Index, request.Size, ct);
        return new PagedResult<GenerationJobResponse>
        {
            Items = paged.Items.Select(ToResponse).ToList(),
            Total = paged.Total,
            Index = paged.Index,
            Size = paged.Size
        };
    }

    public async Task<List<GenerationJobModel>> GetPendingJobsAsync(int batchSize = 10, CancellationToken ct = default)
        => await QueryActive()
            .Where(x => (x.Status == Enums.JobStatus.Pending || x.Status == Enums.JobStatus.Retry) &&
                        (x.ScheduledAt == null || x.ScheduledAt <= DateTime.UtcNow))
            .OrderBy(x => x.Priority)
            .ThenBy(x => x.CreatedAt)
            .Take(batchSize)
            .ToListAsync(ct);

    public async Task<List<GenerationJobModel>> GetByPostAsync(Guid postId, CancellationToken ct = default)
        => await QueryActive()
            .Where(x => x.PostId == postId)
            .OrderBy(x => x.CreatedAt)
            .ToListAsync(ct);

    public async Task<GenerationJobModel> CreateAsync(
        CreateGenerationJobRequest request, CancellationToken ct = default)
    {
        var entity = new GenerationJobModel
        {
            PostId = request.PostId,
            JobType = request.JobType,
            FlowType = request.FlowType,
            Priority = request.Priority,
            MaxRetries = request.MaxRetries,
            ScheduledAt = request.ScheduledAt,
            InputPayload = request.InputPayload,
            IdempotencyKey = request.IdempotencyKey,
            Status = Enums.JobStatus.Pending
        };

        return await base.CreateAsync(entity, ct);
    }

    public async Task<GenerationJobModel?> UpdateAsync(
        Guid id, UpdateGenerationJobRequest request, CancellationToken ct = default)
    {
        var entity = await GetByIdAsync(id, ct);
        if (entity is null) return null;

        if (request.Status.HasValue) entity.Status = request.Status.Value;
        if (request.StartedAt.HasValue) entity.StartedAt = request.StartedAt;
        if (request.CompletedAt.HasValue) entity.CompletedAt = request.CompletedAt;
        if (request.ScheduledAt.HasValue) entity.ScheduledAt = request.ScheduledAt;
        if (request.ErrorMessage is not null) entity.ErrorMessage = request.ErrorMessage;
        if (request.OutputPayload is not null) entity.OutputPayload = request.OutputPayload;
        if (request.RetryCount.HasValue) entity.RetryCount = request.RetryCount.Value;

        ApplyUpdateAudit(entity);
        await Context.SaveChangesAsync(ct);
        return entity;
    }

    public static GenerationJobResponse ToResponse(GenerationJobModel e) => new()
    {
        Id = e.Id,
        PostId = e.PostId,
        JobType = e.JobType,
        Status = e.Status,
        FlowType = e.FlowType,
        Priority = e.Priority,
        RetryCount = e.RetryCount,
        MaxRetries = e.MaxRetries,
        IdempotencyKey = e.IdempotencyKey,
        ErrorCode = e.ErrorCode,
        ScheduledAt = e.ScheduledAt,
        StartedAt = e.StartedAt,
        CompletedAt = e.CompletedAt,
        ErrorMessage = e.ErrorMessage,
        OutputPayload = e.OutputPayload,
        CreatedAt = e.CreatedAt
    };
}
