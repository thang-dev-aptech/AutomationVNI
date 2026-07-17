using Backend.Data;
using Backend.Modules.GenerationJob;
using Backend.Modules.GenerationJob.Enums;
using Backend.Modules.Post.Enums;
using Backend.Modules.PublishLog;
using Backend.Modules.PublishLog.Enums;
using Backend.Shared.Repositories;
using Microsoft.EntityFrameworkCore;

namespace Backend.Modules.Post;

public class PostWorkflowService(
    PostRepository postRepository,
    AppDbContext context,
    IUserContext userContext,
    GenerationJobRepository generationJobRepository,
    PublishLogRepository publishLogRepository)
{
    public async Task<PostModel?> GetPostAsync(Guid id, CancellationToken ct = default)
        => await postRepository.GetByIdAsync(id, ct);

    public async Task<PostModel> SubmitForReviewAsync(Guid id, CancellationToken ct = default)
    {
        var post = await RequirePostAsync(id, ct);
        EnsureStatus(post, "gửi duyệt", PostStatus.Draft, PostStatus.Ready);

        ValidateMinimumForReview(post);

        post.Status = PostStatus.WaitingReview;
        post.RejectionReason = null;
        ApplyUpdate(post);
        await context.SaveChangesAsync(ct);
        return post;
    }

    public async Task<PostModel> ApproveAsync(Guid id, CancellationToken ct = default)
    {
        var post = await RequirePostAsync(id, ct);
        EnsureStatus(post, "duyệt", PostStatus.WaitingReview);

        post.Status = PostStatus.Approved;
        post.ApprovedBy = userContext.GetCurrentUserName();
        post.ApprovedAt = DateTime.UtcNow;
        post.RejectionReason = null;
        ApplyUpdate(post);
        await context.SaveChangesAsync(ct);
        return post;
    }

    public async Task<PostModel> RejectAsync(Guid id, string reason, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(reason))
            throw new ArgumentException("Lý do từ chối không được để trống");

        var post = await RequirePostAsync(id, ct);
        EnsureStatus(post, "từ chối", PostStatus.WaitingReview);

        post.Status = PostStatus.Draft;
        post.RejectionReason = reason.Trim();
        post.ApprovedBy = null;
        post.ApprovedAt = null;
        ApplyUpdate(post);
        await context.SaveChangesAsync(ct);
        return post;
    }

    public async Task<PostModel> ScheduleAsync(
        Guid id, DateTime scheduledAt, string? timezone, CancellationToken ct = default)
    {
        if (scheduledAt <= DateTime.UtcNow)
            throw new ArgumentException("Thời gian lên lịch phải lớn hơn thời điểm hiện tại (UTC)");

        var post = await RequirePostAsync(id, ct);
        EnsureStatus(post, "lên lịch đăng", PostStatus.Approved);

        post.Status = PostStatus.Scheduled;
        post.ScheduledPublishAt = scheduledAt.ToUniversalTime();
        post.ScheduleTimezone = timezone?.Trim();
        ApplyUpdate(post);

        await CancelPendingPublishAsync(post.Id, ct);
        await CreatePendingPublishJobAsync(post, scheduledAt, ct);
        await CreatePendingPublishLogAsync(post, ct);

        await context.SaveChangesAsync(ct);
        return post;
    }

    public async Task<PostModel> CancelScheduleAsync(Guid id, CancellationToken ct = default)
    {
        var post = await RequirePostAsync(id, ct);
        EnsureStatus(post, "hủy lịch đăng", PostStatus.Scheduled);

        post.Status = PostStatus.Approved;
        post.ScheduledPublishAt = null;
        post.ScheduleTimezone = null;
        ApplyUpdate(post);

        await CancelPendingPublishAsync(post.Id, ct);
        await context.SaveChangesAsync(ct);
        return post;
    }

    public async Task<PostModel> PublishNowAsync(Guid id, CancellationToken ct = default)
    {
        var post = await RequirePostAsync(id, ct);
        EnsureStatus(post, "đăng ngay", PostStatus.Approved, PostStatus.Scheduled);

        post.Status = PostStatus.Publishing;
        ApplyUpdate(post);

        await CancelPendingPublishAsync(post.Id, ct);
        await CreatePendingPublishJobAsync(post, DateTime.UtcNow, ct);
        await CreatePendingPublishLogAsync(post, ct);

        await context.SaveChangesAsync(ct);
        return post;
    }

    public async Task<PostTimelineResponse> GetTimelineAsync(Guid id, CancellationToken ct = default)
    {
        var post = await RequirePostAsync(id, ct);
        var jobs = await generationJobRepository.GetByPostAsync(id, ct);
        var publishLogs = await publishLogRepository.GetByPostAsync(id, ct);

        var events = new List<TimelineEntryResponse>();

        events.Add(new TimelineEntryResponse
        {
            Type = "Post",
            Label = "Tạo bài viết",
            Status = PostStatus.Draft.ToString(),
            Timestamp = post.CreatedAt
        });

        if (post.ApprovedAt.HasValue)
        {
            events.Add(new TimelineEntryResponse
            {
                Type = "Review",
                Label = "Đã duyệt",
                Status = PostStatus.Approved.ToString(),
                Timestamp = post.ApprovedAt.Value,
                Detail = post.ApprovedBy
            });
        }

        if (!string.IsNullOrWhiteSpace(post.RejectionReason))
        {
            events.Add(new TimelineEntryResponse
            {
                Type = "Review",
                Label = "Bị từ chối",
                Status = PostStatus.Draft.ToString(),
                Timestamp = post.UpdatedAt ?? post.CreatedAt,
                Detail = post.RejectionReason
            });
        }

        foreach (var job in jobs)
        {
            events.Add(new TimelineEntryResponse
            {
                Type = "GenerationJob",
                Label = job.JobType.ToString(),
                Status = job.Status.ToString(),
                Timestamp = job.StartedAt ?? job.CreatedAt,
                Detail = job.ErrorMessage
            });
        }

        foreach (var log in publishLogs)
        {
            events.Add(new TimelineEntryResponse
            {
                Type = "PublishLog",
                Label = $"Publish attempt #{log.AttemptNumber}",
                Status = log.Status.ToString(),
                Timestamp = log.PublishedAt ?? log.CreatedAt,
                Detail = log.ErrorMessage ?? log.PublishedUrl ?? log.ExternalPostId
            });
        }

        if (post.PublishedAt.HasValue)
        {
            events.Add(new TimelineEntryResponse
            {
                Type = "Post",
                Label = "Đã đăng",
                Status = PostStatus.Published.ToString(),
                Timestamp = post.PublishedAt.Value,
                Detail = post.PublishedUrl ?? post.ExternalPostId
            });
        }

        return new PostTimelineResponse
        {
            PostId = post.Id,
            CurrentStatus = post.Status,
            Events = events.OrderBy(e => e.Timestamp).ToList()
        };
    }

    public bool IsOwner(PostModel post)
        => post.UserId == (userContext.GetCurrentUserId() ?? Guid.Empty);

    public bool IsInAnyRole(params string[] roles)
        => roles.Any(userContext.GetCurrentUserRoles().Contains);

    // --- private helpers ---

    private async Task<PostModel> RequirePostAsync(Guid id, CancellationToken ct)
    {
        var post = await postRepository.GetByIdAsync(id, ct);
        if (post is null)
            throw new KeyNotFoundException("Không tìm thấy bài viết");
        return post;
    }

    private static void EnsureStatus(PostModel post, string action, params PostStatus[] allowed)
    {
        if (!allowed.Contains(post.Status))
            throw new ArgumentException(
                $"Không thể {action} khi bài viết đang ở trạng thái '{post.Status}'");
    }

    private static void ValidateMinimumForReview(PostModel post)
    {
        if (string.IsNullOrWhiteSpace(post.Title))
            throw new ArgumentException("Tiêu đề không được để trống");
        if (string.IsNullOrWhiteSpace(post.Content))
            throw new ArgumentException("Nội dung không được để trống");
        if (post.SocialChannelId == Guid.Empty)
            throw new ArgumentException("Phải chọn kênh đăng bài");
    }

    private void ApplyUpdate(PostModel post)
    {
        post.UpdatedAt = DateTime.UtcNow;
        post.UpdatedBy = userContext.GetCurrentUserName();
    }

    private async Task CancelPendingPublishAsync(Guid postId, CancellationToken ct)
    {
        var pendingJobs = await context.Set<GenerationJobModel>()
            .Where(x => !x.IsDeleted
                && x.PostId == postId
                && x.JobType == JobType.Publish
                && (x.Status == JobStatus.Pending
                    || x.Status == JobStatus.Retry
                    || x.Status == JobStatus.Processing))
            .ToListAsync(ct);

        foreach (var job in pendingJobs)
        {
            job.Status = JobStatus.Cancelled;
            job.UpdatedAt = DateTime.UtcNow;
            job.UpdatedBy = userContext.GetCurrentUserName();
        }

        var pendingLogs = await context.Set<PublishLogModel>()
            .Where(x => !x.IsDeleted
                && x.PostId == postId
                && (x.Status == PublishStatus.Pending || x.Status == PublishStatus.Processing))
            .ToListAsync(ct);

        foreach (var log in pendingLogs)
        {
            log.Status = PublishStatus.Cancelled;
            log.UpdatedAt = DateTime.UtcNow;
            log.UpdatedBy = userContext.GetCurrentUserName();
        }
    }

    private async Task CreatePendingPublishJobAsync(
        PostModel post, DateTime scheduledAt, CancellationToken ct)
    {
        var exists = await context.Set<GenerationJobModel>()
            .AnyAsync(x => !x.IsDeleted
                && x.PostId == post.Id
                && x.JobType == JobType.Publish
                && (x.Status == JobStatus.Pending || x.Status == JobStatus.Retry), ct);
        if (exists) return;

        await generationJobRepository.CreateAsync(new CreateGenerationJobRequest
        {
            PostId = post.Id,
            JobType = JobType.Publish,
            FlowType = JobFlowType.FullAI,
            Priority = 0,
            ScheduledAt = scheduledAt,
            InputPayload = $"{{\"socialChannelId\":\"{post.SocialChannelId}\"}}"
        }, ct);
    }

    private async Task CreatePendingPublishLogAsync(PostModel post, CancellationToken ct)
    {
        if (await publishLogRepository.HasPendingAsync(post.Id, ct))
            return;

        if (await publishLogRepository.HasSuccessAsync(post.Id, ct))
            return;

        var attempt = await publishLogRepository.GetNextAttemptNumberAsync(post.Id, ct);
        var idempotencyKey = PublishIdempotency.BuildKey(
            post.Id,
            post.SocialChannelId,
            post.ScheduledPublishAt ?? DateTime.UtcNow);

        await publishLogRepository.CreateAsync(new CreatePublishLogRequest
        {
            PostId = post.Id,
            SocialChannelId = post.SocialChannelId,
            AttemptNumber = attempt,
            Status = PublishStatus.Pending,
            IdempotencyKey = idempotencyKey
        }, ct);
    }
}
