using Backend.Data;
using Backend.Modules.Post.Enums;
using Backend.Modules.MediaAsset;
using Backend.Modules.MediaAsset.Enums;
using Backend.Shared.Repositories;
using Microsoft.EntityFrameworkCore;

using Backend.Modules.MediaEmbedding;

namespace Backend.Modules.Post;

public class PostRecycleService(
    AppDbContext context,
    IUserContext userContext,
    MediaEmbeddingRepository embeddingRepo,
    ILogger<PostRecycleService> logger)
{
    public async Task<RecycleBatchResult> CreateRecycleBatchAsync(
        RecyclePostRequest request,
        CancellationToken ct = default)
    {
        if (request.ChannelId == Guid.Empty)
            throw new ArgumentException("Phải chọn kênh nguồn");
        if (request.Count < 1 || request.Count > 100)
            throw new ArgumentException("Số bài phải từ 1 đến 100");
        if (request.Flow is not GenerationFlow.Recycle and not GenerationFlow.RecycleRewrite)
            throw new ArgumentException("Flow phải là Recycle hoặc RecycleRewrite");

        var sourcePosts = await context.Set<PostModel>()
            .Where(p => !p.IsDeleted
                && p.Status == PostStatus.Published
                && p.SocialChannelId == request.ChannelId)
            .OrderBy(_ => EF.Functions.Random())
            .Take(request.Count)
            .ToListAsync(ct);

        if (sourcePosts.Count == 0)
            return new RecycleBatchResult { Skipped = request.Count };

        var batchId = Guid.NewGuid();
        var now = DateTime.UtcNow;
        var actor = userContext.GetCurrentUserName();
        var userId = userContext.GetCurrentUserId() ?? Guid.Empty;
        var newPostIds = new List<Guid>();

        for (int i = 0; i < sourcePosts.Count; i++)
        {
            var source = sourcePosts[i];
            
            DateTime? scheduledAt = null;
            if (request.ScheduleEnabled && request.StartTime.HasValue)
            {
                var interval = request.IntervalMinutes ?? 60;
                scheduledAt = request.StartTime.Value.AddMinutes(i * interval);
            }

            var baseStatus = request.Flow == GenerationFlow.Recycle
                ? PostStatus.Approved
                : PostStatus.Queued;

            if (scheduledAt.HasValue && request.Flow == GenerationFlow.Recycle)
            {
                baseStatus = PostStatus.Scheduled;
            }

            // Flag for VectorSearch: use ImageTemplateId = Guid.Empty
            Guid? imageTemplateId = source.ImageTemplateId;
            if (request.ImageStrategy == RecycleImageStrategy.VectorSearch)
            {
                imageTemplateId = Guid.Empty;
            }

            var newPost = new PostModel
            {
                Id = Guid.NewGuid(),
                Title = source.Title,
                Content = request.Flow == GenerationFlow.Recycle ? source.Content : null,
                CategoryId = source.CategoryId,
                SocialChannelId = source.SocialChannelId,
                TextTemplateId = source.TextTemplateId,
                ImageTemplateId = imageTemplateId,
                GenerationFlow = request.Flow,
                Status = baseStatus,
                SourcePostId = source.Id,
                BatchId = batchId,
                UserId = userId,
                CreatedAt = now,
                CreatedBy = actor,
                ApprovedBy = request.Flow == GenerationFlow.Recycle ? actor : null,
                ApprovedAt = request.Flow == GenerationFlow.Recycle ? now : null,
                ScheduledPublishAt = scheduledAt
            };
            context.Add(newPost);
            newPostIds.Add(newPost.Id);

            var sourceMediaList = await context.Set<PostMediaModel>()
                .Where(m => !m.IsDeleted && m.PostId == source.Id)
                .ToListAsync(ct);

            if (request.ImageStrategy == RecycleImageStrategy.KeepOld)
            {
                foreach (var media in sourceMediaList)
                {
                    context.Add(new PostMediaModel
                    {
                        Id = Guid.NewGuid(),
                        PostId = newPost.Id,
                        MediaId = media.MediaId,
                        MediaRole = media.MediaRole,
                        SortOrder = media.SortOrder,
                        CreatedAt = now,
                        CreatedBy = actor,
                    });
                }
            }
            else if (request.ImageStrategy == RecycleImageStrategy.VectorSearch && request.Flow == GenerationFlow.Recycle)
            {
                throw new ArgumentException("Vector Search không hỗ trợ cho luồng Copy nguyên văn (Verbatim).");
            }
            // Nếu ImageStrategy == VectorSearch VÀ Flow == RecycleRewrite, ta KHÔNG add PostMedia ở đây.
            // Worker (GenerationJobPipelineService) sẽ xử lý sau khi sinh text.
        }

        await context.SaveChangesAsync(ct);

        logger.LogInformation(
            "PostRecycleService created {Count} recycle posts (flow={Flow}, batchId={BatchId}, channelId={ChannelId})",
            newPostIds.Count, request.Flow, batchId, request.ChannelId);

        return new RecycleBatchResult
        {
            BatchId = batchId,
            Created = newPostIds.Count,
            Skipped = request.Count - newPostIds.Count,
            PostIds = newPostIds
        };
    }
}
