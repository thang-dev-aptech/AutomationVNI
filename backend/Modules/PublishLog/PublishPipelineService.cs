using Backend.Data;
using Backend.Modules.GenerationJob;
using Backend.Modules.GenerationJob.Enums;
using Backend.Modules.MediaAsset;
using Backend.Modules.MediaAsset.Enums;
using Backend.Modules.Post;
using Backend.Modules.Post.Enums;
using Backend.Modules.PublishLog.Enums;
using Backend.Modules.SocialChannel;
using Backend.Modules.SocialChannel.Enums;
using Backend.Shared.Repositories;
using Backend.Shared.SocialPublish;
using Microsoft.EntityFrameworkCore;

namespace Backend.Modules.PublishLog;

public class PublishPipelineService(
    AppDbContext context,
    PostRepository postRepository,
    PublishLogRepository publishLogRepository,
    SocialChannelRepository socialChannelRepository,
    PostMediaRepository postMediaRepository,
    MediaAssetRepository mediaAssetRepository,
    ISocialPublishService socialPublishService,
    IUserContext userContext,
    ILogger<PublishPipelineService> logger) : IPublishPipelineService
{
    private const int MaxPublishAttempts = 3;

    private static readonly PostStatus[] PublishablePostStatuses =
        [PostStatus.Approved, PostStatus.Scheduled, PostStatus.Publishing];

    public Task<ProcessPublishLogResponse> ProcessAsync(Guid publishLogId, CancellationToken ct = default)
        => ExecutePublishAsync(publishLogId, forceReal: false, ct);

    public Task<ProcessPublishLogResponse> ProcessRealAsync(Guid publishLogId, CancellationToken ct = default)
        => ExecutePublishAsync(publishLogId, forceReal: true, ct);

    /// <summary>
    /// Đăng ngay: tìm publish log Pending của post rồi xử lý (mock/real theo config) → Published.
    /// Trả null nếu không có log Pending. Ném nếu publish thất bại (post/log đã được đánh dấu bên trong).
    /// </summary>
    public async Task<ProcessPublishLogResponse?> ProcessPendingForPostAsync(
        Guid postId, CancellationToken ct = default)
    {
        var log = await publishLogRepository.GetActiveAsync(postId, ct);
        if (log is null || log.Status != PublishStatus.Pending)
            return null;
        return await ProcessAsync(log.Id, ct);
    }

    /// <summary>
    /// Gỡ kẹt: nếu publish-now lỗi precondition (thiếu media…) khiến post kẹt ở Publishing,
    /// hủy log Pending/Processing và đưa post về Approved/Scheduled.
    /// </summary>
    public async Task RevertStuckPublishingAsync(Guid postId, CancellationToken ct = default)
    {
        var post = await postRepository.GetByIdAsync(postId, ct);
        if (post is null || post.Status != PostStatus.Publishing)
            return;

        var log = await publishLogRepository.GetActiveAsync(postId, ct);
        if (log is not null && log.Status is PublishStatus.Pending or PublishStatus.Processing)
        {
            log.Status = PublishStatus.Cancelled;
            ApplyLogUpdate(log);
        }

        post.Status = post.ScheduledPublishAt.HasValue ? PostStatus.Scheduled : PostStatus.Approved;
        ApplyPostUpdate(post);
        await CancelPublishJobAsync(postId, ct);
        await context.SaveChangesAsync(ct);
    }

    private async Task<ProcessPublishLogResponse> ExecutePublishAsync(
        Guid publishLogId, bool forceReal, CancellationToken ct)
    {
        var log = await RequireLogAsync(publishLogId, ct);
        EnsureLogStatus(log, "xử lý", PublishStatus.Pending);

        var post = await RequirePostAsync(log.PostId, ct);
        await EnsureNotAlreadyPublishedAsync(post, log.Id, ct);
        EnsurePostStatus(post, "publish", PublishablePostStatuses);
        var channel = await ValidatePublishReadyAsync(post, ct);

        log.Status = PublishStatus.Processing;
        log.ErrorCode = null;
        log.ErrorMessage = null;
        ApplyLogUpdate(log);

        post.Status = PostStatus.Publishing;
        ApplyPostUpdate(post);
        await MarkPublishJobProcessingAsync(post.Id, ct);
        await context.SaveChangesAsync(ct);

        var media = await ResolvePublishMediaAsync(post.Id, ct);
        var publishRequest = new SocialPublishRequest
        {
            PostId = post.Id,
            PublishLogId = log.Id,
            SocialChannelId = channel.Id,
            Platform = channel.Platform,
            PageExternalId = channel.ExternalPageId,
            AccessToken = channel.AccessToken,
            Caption = post.Content ?? string.Empty,
            MediaPreviewUrl = media.PublicUrl,
            MediaStorageKey = media.StorageKey,
            MediaFileName = media.FileName,
            MediaMimeType = media.MimeType,
            ForceReal = forceReal
        };

        var publishResult = await socialPublishService.PublishAsync(publishRequest, ct);

        if (!publishResult.Success)
        {
            await ApplyPublishFailureAsync(log, post, publishResult, ct);
            throw new InvalidOperationException(
                $"Publish failed [{publishResult.ErrorCode}]: {SanitizeErrorMessage(publishResult.ErrorMessage ?? "Unknown error")}");
        }

        var responseJson = publishResult.RawResponseSanitized
            ?? $$"""{"externalPostId":"{{publishResult.PublishedExternalId}}","publishedUrl":"{{publishResult.PublishedUrl}}","mock":{{(publishResult.UsedMock ? "true" : "false")}}}""";

        log.Status = PublishStatus.Success;
        log.ExternalPostId = publishResult.PublishedExternalId;
        log.PublishedUrl = publishResult.PublishedUrl;
        log.ResponsePayload = responseJson;
        log.PublishedAt = DateTime.UtcNow;
        ApplyLogUpdate(log);

        post.Status = PostStatus.Published;
        post.ExternalPostId = publishResult.PublishedExternalId;
        post.PublishedUrl = publishResult.PublishedUrl;
        post.PublishedAt = DateTime.UtcNow;
        post.GenerationError = null;
        ApplyPostUpdate(post);

        await CompletePublishJobAsync(post.Id, responseJson, ct);
        await context.SaveChangesAsync(ct);

        logger.LogInformation(
            "Publish succeeded for post {PostId}, mock={UsedMock}",
            post.Id, publishResult.UsedMock);

        return new ProcessPublishLogResponse
        {
            PublishLogId = log.Id,
            PostId = post.Id,
            Status = log.Status,
            ExternalPostId = publishResult.PublishedExternalId,
            PublishedUrl = publishResult.PublishedUrl,
            ResponsePayload = responseJson
        };
    }

    private async Task ApplyPublishFailureAsync(
        PublishLogModel log,
        PostModel post,
        SocialPublishResult result,
        CancellationToken ct)
    {
        var errorCode = result.ErrorCode ?? "PUBLISH_FAILED";
        var errorMessage = SanitizeErrorMessage(result.ErrorMessage ?? "Publish failed");

        log.ErrorCode = errorCode;
        log.ErrorMessage = errorMessage;
        log.ResponsePayload = result.RawResponseSanitized;
        log.Status = log.AttemptNumber >= MaxPublishAttempts
            ? PublishStatus.DeadLetter
            : PublishStatus.Failed;
        ApplyLogUpdate(log);

        if (IsTokenOrPermissionError(errorCode))
            post.Status = PostStatus.NeedFix;
        else if (log.AttemptNumber >= MaxPublishAttempts)
            post.Status = PostStatus.Failed;
        else
            post.Status = post.Status == PostStatus.Scheduled
                ? PostStatus.Scheduled
                : PostStatus.Approved;

        post.GenerationError = $"[{errorCode}] {errorMessage}";
        ApplyPostUpdate(post);
        await FailPublishJobAsync(post.Id, errorCode, errorMessage, ct);
        await context.SaveChangesAsync(ct);
    }

    /// <summary>
    /// Lỗi token/quyền thì retry vô nghĩa — đẩy bài sang NeedFix để người dùng kết nối lại kênh.
    /// Threads dùng tiền tố riêng nên phải liệt kê tường minh, nếu không sẽ bị coi là lỗi tạm thời
    /// và retry đủ 3 lần một cách vô ích.
    /// </summary>
    private static bool IsTokenOrPermissionError(string errorCode)
        => errorCode is "FB_TOKEN_MISSING" or "FB_TOKEN_INVALID" or "FB_PERMISSION_DENIED"
            or "THREADS_TOKEN_MISSING" or "THREADS_TOKEN_INVALID" or "THREADS_PERMISSION_DENIED";

    public async Task<PublishLogModel> FailAsync(
        Guid publishLogId, FailPublishLogRequest request, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(request.ErrorCode))
            throw new ArgumentException("ErrorCode không được để trống");

        var log = await RequireLogAsync(publishLogId, ct);
        EnsureLogStatus(log, "fail", PublishStatus.Pending, PublishStatus.Processing);

        var post = await RequirePostAsync(log.PostId, ct);
        var errorCode = request.ErrorCode.Trim();
        var errorMessage = SanitizeErrorMessage(request.ErrorMessage);

        log.ErrorCode = errorCode;
        log.ErrorMessage = errorMessage;

        if (log.AttemptNumber >= MaxPublishAttempts)
        {
            log.Status = PublishStatus.DeadLetter;
            post.Status = PostStatus.Failed;
        }
        else
        {
            log.Status = PublishStatus.Failed;
            post.Status = post.Status == PostStatus.Scheduled
                ? PostStatus.Scheduled
                : PostStatus.Approved;
        }

        post.GenerationError = $"[{errorCode}] {errorMessage}";
        ApplyLogUpdate(log);
        ApplyPostUpdate(post);
        await FailPublishJobAsync(post.Id, errorCode, errorMessage, ct);
        await context.SaveChangesAsync(ct);
        return log;
    }

    public async Task<PublishLogModel> RetryAsync(Guid publishLogId, CancellationToken ct = default)
    {
        var log = await RequireLogAsync(publishLogId, ct);
        EnsureLogStatus(log, "retry", PublishStatus.Failed, PublishStatus.DeadLetter, PublishStatus.RateLimited);

        if (log.AttemptNumber >= MaxPublishAttempts)
            throw new ArgumentException("Publish log đã hết số lần retry");

        var post = await RequirePostAsync(log.PostId, ct);
        await EnsureNotAlreadyPublishedAsync(post, log.Id, ct);

        log.Status = PublishStatus.Pending;
        log.ErrorCode = null;
        log.ErrorMessage = null;
        log.PublishedAt = null;
        log.ExternalPostId = null;
        log.PublishedUrl = null;
        log.ResponsePayload = null;
        ApplyLogUpdate(log);

        post.Status = PostStatus.Publishing;
        post.GenerationError = null;
        ApplyPostUpdate(post);

        await RetryPublishJobAsync(post.Id, ct);
        await context.SaveChangesAsync(ct);
        return log;
    }

    public async Task<PublishLogModel> CancelAsync(Guid publishLogId, CancellationToken ct = default)
    {
        var log = await RequireLogAsync(publishLogId, ct);
        EnsureLogStatus(log, "cancel", PublishStatus.Pending, PublishStatus.Processing);

        var post = await RequirePostAsync(log.PostId, ct);

        log.Status = PublishStatus.Cancelled;
        ApplyLogUpdate(log);

        if (post.Status == PostStatus.Publishing)
        {
            post.Status = post.ScheduledPublishAt.HasValue
                ? PostStatus.Scheduled
                : PostStatus.Approved;
            ApplyPostUpdate(post);
        }

        await CancelPublishJobAsync(post.Id, ct);
        await context.SaveChangesAsync(ct);
        return log;
    }

    public async Task<ProcessDueScheduledResult> ProcessDueScheduledAsync(
        int batchSize, CancellationToken ct = default)
    {
        var now = DateTime.UtcNow;
        var posts = await context.Set<PostModel>()
            .Where(x => !x.IsDeleted
                && x.Status == PostStatus.Scheduled
                && x.ScheduledPublishAt != null
                && x.ScheduledPublishAt <= now)
            .OrderBy(x => x.ScheduledPublishAt)
            .Take(batchSize)
            .ToListAsync(ct);

        var result = new ProcessDueScheduledResult { Picked = posts.Count };
        logger.LogInformation("Scheduler picked {Count} scheduled post(s) due for publish", posts.Count);

        foreach (var post in posts)
        {
            try
            {
                var item = await ProcessScheduledPostAsync(post, ct);
                result.Items.Add(item);

                switch (item.Outcome)
                {
                    case "Succeeded":
                        result.Succeeded++;
                        logger.LogInformation(
                            "Scheduled publish succeeded for post {PostId}, publishLog {PublishLogId}",
                            post.Id, item.PublishLogId);
                        break;
                    case "Skipped":
                        result.Skipped++;
                        logger.LogInformation(
                            "Scheduled publish skipped for post {PostId}: {Message}",
                            post.Id, item.Message);
                        break;
                    default:
                        result.Failed++;
                        break;
                }
            }
            catch (Exception ex)
            {
                result.Failed++;
                logger.LogWarning(ex, "Scheduled publish failed for post {PostId}", post.Id);

                try
                {
                    await HandleScheduledFailureAsync(post, ex, ct);
                }
                catch (Exception inner)
                {
                    logger.LogError(inner, "Failed to record scheduled publish error for post {PostId}", post.Id);
                }

                result.Items.Add(new ProcessDueScheduledItem
                {
                    PostId = post.Id,
                    Outcome = "Failed",
                    Message = SanitizeErrorMessage(ex.Message)
                });
            }
        }

        return result;
    }

    private async Task<ProcessDueScheduledItem> ProcessScheduledPostAsync(
        PostModel post, CancellationToken ct)
    {
        if (post.Status == PostStatus.Published || !string.IsNullOrWhiteSpace(post.ExternalPostId))
            return Skipped(post.Id, "Post already published");

        if (await publishLogRepository.HasSuccessAsync(post.Id, ct))
            return Skipped(post.Id, "Publish log success already exists");

        var activeLog = await publishLogRepository.GetActiveAsync(post.Id, ct);
        if (activeLog?.Status == PublishStatus.Processing)
            return Skipped(post.Id, "Publish already in progress");

        var log = activeLog?.Status == PublishStatus.Pending
            ? activeLog
            : await EnsurePendingPublishLogAsync(post, ct);

        var processResult = await ProcessAsync(log.Id, ct);
        return new ProcessDueScheduledItem
        {
            PostId = post.Id,
            PublishLogId = log.Id,
            Outcome = "Succeeded",
            PublishedUrl = processResult.PublishedUrl
        };
    }

    private async Task<PublishLogModel> EnsurePendingPublishLogAsync(PostModel post, CancellationToken ct)
    {
        var scheduledAt = post.ScheduledPublishAt ?? DateTime.UtcNow;
        var idempotencyKey = PublishIdempotency.BuildKey(post.Id, post.SocialChannelId, scheduledAt);

        var existing = await publishLogRepository.GetByIdempotencyKeyAsync(idempotencyKey, ct);
        if (existing is not null)
            return existing;

        if (await publishLogRepository.HasPendingAsync(post.Id, ct))
        {
            var pending = await publishLogRepository.GetActiveAsync(post.Id, ct);
            if (pending is not null) return pending;
        }

        var attempt = await publishLogRepository.GetNextAttemptNumberAsync(post.Id, ct);
        return await publishLogRepository.CreateAsync(new CreatePublishLogRequest
        {
            PostId = post.Id,
            SocialChannelId = post.SocialChannelId,
            AttemptNumber = attempt,
            Status = PublishStatus.Pending,
            IdempotencyKey = idempotencyKey
        }, ct);
    }

    private async Task HandleScheduledFailureAsync(PostModel post, Exception ex, CancellationToken ct)
    {
        var log = await publishLogRepository.GetActiveAsync(post.Id, ct)
            ?? await EnsurePendingPublishLogAsync(post, ct);

        await FailAsync(log.Id, new FailPublishLogRequest
        {
            ErrorCode = "SCHEDULER_ERROR",
            ErrorMessage = ex.Message
        }, ct);
    }

    private static ProcessDueScheduledItem Skipped(Guid postId, string message) => new()
    {
        PostId = postId,
        Outcome = "Skipped",
        Message = message
    };

    // --- helpers ---

    private async Task EnsureNotAlreadyPublishedAsync(PostModel post, Guid currentLogId, CancellationToken ct)
    {
        if (post.Status == PostStatus.Published || !string.IsNullOrWhiteSpace(post.ExternalPostId))
            throw new ArgumentException("Bài viết đã được đăng, không thể publish lại");

        var hasSuccess = await context.Set<PublishLogModel>()
            .AnyAsync(x => !x.IsDeleted
                && x.PostId == post.Id
                && x.Id != currentLogId
                && x.Status == PublishStatus.Success, ct);

        if (hasSuccess)
            throw new ArgumentException("Đã có publish log thành công cho bài viết này");
    }

    private async Task<SocialChannelModel> ValidatePublishReadyAsync(PostModel post, CancellationToken ct)
    {
        if (post.SocialChannelId == Guid.Empty)
            throw new ArgumentException("Bài viết chưa có kênh đăng");

        var channel = await socialChannelRepository.GetByIdAsync(post.SocialChannelId, ct);
        if (channel is null)
            throw new ArgumentException("Không tìm thấy kênh đăng bài");
        if (!channel.IsActive)
            throw new InvalidOperationException(
                "Kênh đăng bài đã bị ngắt kết nối hoặc không còn trên Meta. Hãy Connect Meta / chọn kênh khác.");

        if (string.IsNullOrWhiteSpace(post.Content))
            throw new ArgumentException("Bài viết chưa có nội dung để đăng");

        if (!await HasPublishableMediaAsync(post.Id, ct))
            throw new ArgumentException("Bài viết chưa có media (cover hoặc ảnh đính kèm)");

        return channel;
    }

    private async Task<PublishMediaInfo> ResolvePublishMediaAsync(Guid postId, CancellationToken ct)
    {
        var postMedias = await postMediaRepository.GetByPostAsync(postId, ct);
        if (postMedias.Count == 0) return PublishMediaInfo.Empty;

        var cover = postMedias.FirstOrDefault(x => x.MediaRole == MediaRole.Cover)
            ?? postMedias.FirstOrDefault(x => x.MediaRole == MediaRole.Primary)
            ?? postMedias.First();

        var media = await mediaAssetRepository.GetByIdAsync(cover.MediaId, ct);
        if (media is null || string.IsNullOrWhiteSpace(media.StoragePath))
            return PublishMediaInfo.Empty;

        var publicUrl = SocialPublishUrlHelper.IsPubliclyAccessibleUrl(media.PublicUrl)
            ? media.PublicUrl
            : null;

        return new PublishMediaInfo(
            publicUrl,
            media.StoragePath.Trim(),
            media.OriginalFileName ?? media.FileName,
            string.IsNullOrWhiteSpace(media.MimeType) ? null : media.MimeType.Trim());
    }

    private sealed record PublishMediaInfo(
        string? PublicUrl,
        string? StorageKey,
        string? FileName,
        string? MimeType)
    {
        public static PublishMediaInfo Empty { get; } = new(null, null, null, null);
    }

    private async Task<bool> HasPublishableMediaAsync(Guid postId, CancellationToken ct)
    {
        var media = await ResolvePublishMediaAsync(postId, ct);
        return !string.IsNullOrWhiteSpace(media.StorageKey);
    }

    private async Task<PostModel> RequirePostAsync(Guid id, CancellationToken ct)
    {
        var post = await postRepository.GetByIdAsync(id, ct);
        if (post is null) throw new KeyNotFoundException("Không tìm thấy bài viết");
        return post;
    }

    private async Task<PublishLogModel> RequireLogAsync(Guid id, CancellationToken ct)
    {
        var log = await publishLogRepository.GetByIdAsync(id, ct);
        if (log is null) throw new KeyNotFoundException("Không tìm thấy publish log");
        return log;
    }

    private async Task MarkPublishJobProcessingAsync(Guid postId, CancellationToken ct)
    {
        var job = await FindActivePublishJobAsync(postId, ct);
        if (job is null) return;

        job.Status = JobStatus.Processing;
        job.StartedAt = DateTime.UtcNow;
        ApplyJobUpdate(job);
    }

    private async Task CompletePublishJobAsync(Guid postId, string outputJson, CancellationToken ct)
    {
        var job = await FindActivePublishJobAsync(postId, ct);
        if (job is null) return;

        job.Status = JobStatus.Completed;
        job.CompletedAt = DateTime.UtcNow;
        job.OutputPayload = outputJson;
        job.ErrorMessage = null;
        job.ErrorCode = null;
        ApplyJobUpdate(job);
    }

    private async Task FailPublishJobAsync(
        Guid postId, string errorCode, string errorMessage, CancellationToken ct)
    {
        var job = await FindActivePublishJobAsync(postId, ct);
        if (job is null) return;

        job.Status = JobStatus.Failed;
        job.ErrorCode = errorCode;
        job.ErrorMessage = errorMessage;
        job.CompletedAt = DateTime.UtcNow;
        ApplyJobUpdate(job);
    }

    private async Task RetryPublishJobAsync(Guid postId, CancellationToken ct)
    {
        var job = await context.Set<GenerationJobModel>()
            .Where(x => !x.IsDeleted
                && x.PostId == postId
                && x.JobType == JobType.Publish
                && (x.Status == JobStatus.Failed || x.Status == JobStatus.DeadLetter))
            .OrderByDescending(x => x.CreatedAt)
            .FirstOrDefaultAsync(ct);

        if (job is null) return;

        job.Status = JobStatus.Retry;
        job.ErrorCode = null;
        job.ErrorMessage = null;
        job.StartedAt = null;
        job.CompletedAt = null;
        ApplyJobUpdate(job);
    }

    private async Task CancelPublishJobAsync(Guid postId, CancellationToken ct)
    {
        var job = await FindActivePublishJobAsync(postId, ct);
        if (job is null) return;

        job.Status = JobStatus.Cancelled;
        job.CompletedAt = DateTime.UtcNow;
        ApplyJobUpdate(job);
    }

    private async Task<GenerationJobModel?> FindActivePublishJobAsync(Guid postId, CancellationToken ct)
        => await context.Set<GenerationJobModel>()
            .Where(x => !x.IsDeleted
                && x.PostId == postId
                && x.JobType == JobType.Publish
                && (x.Status == JobStatus.Pending
                    || x.Status == JobStatus.Retry
                    || x.Status == JobStatus.Processing))
            .OrderByDescending(x => x.CreatedAt)
            .FirstOrDefaultAsync(ct);

    private static void EnsurePostStatus(PostModel post, string action, params PostStatus[] allowed)
    {
        if (!allowed.Contains(post.Status))
            throw new ArgumentException(
                $"Không thể {action} khi bài viết đang ở trạng thái '{post.Status}'");
    }

    private static void EnsureLogStatus(PublishLogModel log, string action, params PublishStatus[] allowed)
    {
        if (!allowed.Contains(log.Status))
            throw new ArgumentException(
                $"Không thể {action} publish log khi trạng thái là '{log.Status}'");
    }

    private static string SanitizeErrorMessage(string message)
    {
        if (string.IsNullOrWhiteSpace(message)) return "Unknown error";
        var trimmed = message.Trim();
        if (trimmed.Length > 500) trimmed = trimmed[..500];
        var lower = trimmed.ToLowerInvariant();
        if (lower.Contains("password") || lower.Contains("token") || lower.Contains("secret"))
            return "Error details redacted";
        return trimmed;
    }

    private void ApplyPostUpdate(PostModel post)
    {
        post.UpdatedAt = DateTime.UtcNow;
        post.UpdatedBy = userContext.GetCurrentUserName() ?? "scheduler";
    }

    private void ApplyLogUpdate(PublishLogModel log)
    {
        log.UpdatedAt = DateTime.UtcNow;
        log.UpdatedBy = userContext.GetCurrentUserName() ?? "scheduler";
    }

    private void ApplyJobUpdate(GenerationJobModel job)
    {
        job.UpdatedAt = DateTime.UtcNow;
        job.UpdatedBy = userContext.GetCurrentUserName() ?? "scheduler";
    }
}
