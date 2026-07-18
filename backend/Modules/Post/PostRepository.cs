using Backend.Data;
using Backend.Modules.Post.Enums;
using Backend.Shared;
using Backend.Shared.Repositories;
using Microsoft.EntityFrameworkCore;

namespace Backend.Modules.Post;

public class PostRepository : GenericRepository<PostModel>, IGenericRepository<PostModel>
{
    public PostRepository(AppDbContext context, IUserContext userContext)
        : base(context, userContext) { }

    public async Task<PagedResult<PostResponse>> FilterAsync(
        PostFilterRequest request,
        CancellationToken cancellationToken = default)
    {
        var query = QueryActive();

        if (!string.IsNullOrWhiteSpace(request.Keyword))
        {
            var keyword = request.Keyword.Trim();
            query = query.Where(x =>
                x.Title.Contains(keyword) || (x.Content != null && x.Content.Contains(keyword)));
        }

        if (request.Status.HasValue)
            query = query.Where(x => x.Status == request.Status.Value);

        if (request.SocialChannelId.HasValue)
            query = query.Where(x => x.SocialChannelId == request.SocialChannelId.Value);

        if (request.GenerationFlow.HasValue)
            query = query.Where(x => x.GenerationFlow == request.GenerationFlow.Value);

        if (request.FromDate.HasValue)
            query = query.Where(x => x.CreatedAt >= request.FromDate.Value);

        if (request.ToDate.HasValue)
            query = query.Where(x => x.CreatedAt <= request.ToDate.Value);

        var paged = await PaginateAsync(query, request.Index, request.Size, cancellationToken);
        return new PagedResult<PostResponse>
        {
            Items = paged.Items.Select(ToResponse).ToList(),
            Total = paged.Total,
            Index = paged.Index,
            Size = paged.Size
        };
    }

    public async Task<PostModel> CreateAsync(
        CreatePostRequest request,
        CancellationToken cancellationToken = default)
    {
        var entity = new PostModel
        {
            Title = request.Title.Trim(),
            SocialChannelId = request.SocialChannelId,
            CategoryId = request.CategoryId,
            GenerationFlow = request.GenerationFlow,
            TextTemplateId = request.TextTemplateId,
            ImageTemplateId = request.ImageTemplateId,
            UserId = GetCurrentUserId(),
            Status = PostStatus.Draft
        };

        if (!string.IsNullOrWhiteSpace(request.Objective))
            entity.ExtraJson = System.Text.Json.JsonSerializer.Serialize(
                new { input = new { objective = request.Objective.Trim() } });

        return await base.CreateAsync(entity, cancellationToken);
    }

    public async Task<PostModel?> UpdateAsync(
        Guid id,
        UpdatePostRequest request,
        CancellationToken cancellationToken = default)
    {
        var entity = await GetByIdAsync(id, cancellationToken);
        if (entity is null)
            return null;

        // Chỉ cho sửa nội dung khi chưa publish — không cho đổi status qua update thường
        if (entity.Status is PostStatus.Publishing or PostStatus.Published)
            throw new ArgumentException("Không thể sửa bài viết đang/đã đăng");

        if (request.Title is not null)
            entity.Title = request.Title.Trim();

        if (request.Content is not null)
            entity.Content = request.Content.Trim();

        if (request.CategoryId.HasValue)
            entity.CategoryId = request.CategoryId;

        ApplyUpdateAudit(entity);
        await Context.SaveChangesAsync(cancellationToken);
        return entity;
    }

    /// <summary>Tạo hàng loạt post (fan-out items × channels) ở Status=Queued cho worker sinh nền.</summary>
    public async Task<BulkCreateResult> BulkCreateAsync(
        BulkCreatePostRequest request, CancellationToken ct = default)
    {
        var items = (request.Items ?? []).Where(i => !string.IsNullOrWhiteSpace(i.Idea)).ToList();
        var channels = (request.ChannelIds ?? []).Where(c => c != Guid.Empty).Distinct().ToList();
        if (items.Count == 0) throw new ArgumentException("Danh sách ý tưởng trống");
        if (channels.Count == 0) throw new ArgumentException("Phải chọn ít nhất một kênh đăng");

        var batchId = Guid.NewGuid();
        var userId = GetCurrentUserId();
        var posts = new List<PostModel>();
        foreach (var ch in channels)
            foreach (var it in items)
            {
                var post = new PostModel
                {
                    Title = it.Idea.Trim(),
                    SocialChannelId = ch,
                    CategoryId = it.CategoryId ?? request.CategoryId,
                    GenerationFlow = request.GenerationFlow,
                    TextTemplateId = it.TextTemplateId ?? request.TextTemplateId,
                    ImageTemplateId = it.ImageTemplateId ?? request.ImageTemplateId,
                    BatchId = batchId,
                    UserId = userId,
                    Status = PostStatus.Queued
                };
                if (!string.IsNullOrWhiteSpace(it.Objective))
                    post.ExtraJson = System.Text.Json.JsonSerializer.Serialize(
                        new { input = new { objective = it.Objective!.Trim() } });
                posts.Add(post);
            }

        await MultiCreateAsync(posts, ct);
        return new BulkCreateResult
        {
            BatchId = batchId,
            Created = posts.Count,
            PostIds = posts.Select(p => p.Id).ToList()
        };
    }

    /// <summary>Lấy các post theo batchId hoặc danh sách id, lọc theo status cho phép.</summary>
    public async Task<List<PostModel>> ResolveTargetsAsync(
        Guid? batchId, List<Guid>? postIds, PostStatus[]? allowedStatuses, CancellationToken ct = default)
    {
        var q = QueryActive();
        if (batchId.HasValue) q = q.Where(p => p.BatchId == batchId.Value);
        else if (postIds is { Count: > 0 }) q = q.Where(p => postIds.Contains(p.Id));
        else return [];

        if (allowedStatuses is { Length: > 0 })
            q = q.Where(p => allowedStatuses.Contains(p.Status));

        return await q.OrderBy(p => p.CreatedAt).ToListAsync(ct);
    }

    public static PostResponse ToResponse(PostModel entity) => new()
    {
        Id = entity.Id,
        Title = entity.Title,
        Content = entity.Content,
        CategoryId = entity.CategoryId,
        SocialChannelId = entity.SocialChannelId,
        GenerationFlow = entity.GenerationFlow,
        TextTemplateId = entity.TextTemplateId,
        ImageTemplateId = entity.ImageTemplateId,
        Status = entity.Status,
        UserId = entity.UserId,
        ScheduledPublishAt = entity.ScheduledPublishAt,
        ScheduleTimezone = entity.ScheduleTimezone,
        PublishedAt = entity.PublishedAt,
        ExternalPostId = entity.ExternalPostId,
        PublishedUrl = entity.PublishedUrl,
        RejectionReason = entity.RejectionReason,
        ApprovedBy = entity.ApprovedBy,
        ApprovedAt = entity.ApprovedAt,
        CreatedAt = entity.CreatedAt,
        UpdatedAt = entity.UpdatedAt
    };
}
