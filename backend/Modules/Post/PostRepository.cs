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
            UserId = GetCurrentUserId(),
            Status = PostStatus.Draft
        };

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

    public static PostResponse ToResponse(PostModel entity) => new()
    {
        Id = entity.Id,
        Title = entity.Title,
        Content = entity.Content,
        CategoryId = entity.CategoryId,
        SocialChannelId = entity.SocialChannelId,
        GenerationFlow = entity.GenerationFlow,
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
