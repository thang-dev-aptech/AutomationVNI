using Backend.Data;
using Backend.Modules.MediaEmbedding;
using Backend.Modules.Post.Enums;
using Backend.Shared.Repositories;
using Microsoft.EntityFrameworkCore;

namespace Backend.Modules.Post;

public class PostRecycleService(
    AppDbContext context,
    IUserContext userContext,
    MediaEmbeddingRepository embeddingRepo,
    RecycleSourcePicker sourcePicker,
    ILogger<PostRecycleService> logger)
{
    // embeddingRepo giữ constructor tương thích DI / VectorSearch tương lai.
    private readonly MediaEmbeddingRepository _ = embeddingRepo;

    public async Task<RecycleBatchResult> CreateRecycleBatchAsync(
        RecyclePostRequest request,
        CancellationToken ct = default)
    {
        if (request.ChannelIds == null || request.ChannelIds.Count == 0)
            throw new ArgumentException("Phải chọn ít nhất một kênh nguồn");
        if (request.Count < 1 || request.Count > 100)
            throw new ArgumentException("Số bài phải từ 1 đến 100");
        if (request.Flow is not GenerationFlow.Recycle and not GenerationFlow.RecycleRewrite)
            throw new ArgumentException("Flow phải là Recycle hoặc RecycleRewrite");

        var batchId = Guid.NewGuid();
        var now = DateTime.UtcNow;
        var actor = userContext.GetCurrentUserName();
        var userId = userContext.GetCurrentUserId() ?? Guid.Empty;
        var newPostIds = new List<Guid>();
        var totalRequested = request.Count * request.ChannelIds.Count;

        foreach (var channelId in request.ChannelIds)
        {
            // Không lọc media — hành vi recycle cũ (mọi Published trừ TextOnly).
            var sourcePosts = await sourcePicker.PickRandomPublishedAsync(
                channelId, request.Count, mediaFilter: null, ct);

            if (sourcePosts.Count == 0)
                continue;

            for (int i = 0; i < sourcePosts.Count; i++)
            {
                var source = sourcePosts[i];

                DateTime? scheduledAt = null;
                if (request.ScheduleEnabled && request.StartTimes != null && i < request.StartTimes.Count)
                {
                    int jitter = request.JitterMinutes;
                    int offsetMinutes = jitter > 0 ? Random.Shared.Next(-jitter, jitter + 1) : 0;
                    scheduledAt = request.StartTimes[i].AddMinutes(offsetMinutes);
                }

                var baseStatus = request.Flow == GenerationFlow.Recycle
                    ? PostStatus.Approved
                    : PostStatus.Queued;

                if (request.Flow == GenerationFlow.Recycle && request.ImageStrategy == RecycleImageStrategy.VectorSearch)
                {
                    baseStatus = PostStatus.Queued;
                }
                else if (scheduledAt.HasValue && request.Flow == GenerationFlow.Recycle)
                {
                    baseStatus = PostStatus.Scheduled;
                }

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

                if (request.ImageStrategy == RecycleImageStrategy.KeepOld)
                {
                    await sourcePicker.CopyMediaKeepOldAsync(newPost.Id, source.Id, now, actor, ct);
                }
            }
        }

        await context.SaveChangesAsync(ct);

        logger.LogInformation(
            "PostRecycleService created {Count} recycle posts (flow={Flow}, batchId={BatchId}, channels={ChannelsCount})",
            newPostIds.Count, request.Flow, batchId, request.ChannelIds.Count);

        return new RecycleBatchResult
        {
            BatchId = batchId,
            Created = newPostIds.Count,
            Skipped = totalRequested - newPostIds.Count,
            PostIds = newPostIds
        };
    }
}
