using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Backend.Data;
using Backend.Modules.Campaign.Enums;
using Backend.Modules.Campaign.FullMedia.Scoring;
using Backend.Modules.MediaAsset;
using Backend.Modules.MediaAsset.Enums;
using Backend.Modules.MediaFolder;
using Backend.Modules.Notification;
using Backend.Modules.PageContext;
using Backend.Modules.Post;
using Backend.Modules.Post.Enums;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Backend.Modules.Campaign.FullMedia;

/// <summary>
/// Pipeline Full Media: Vision → chấm điểm → tạo bài. Mỗi ảnh một scope ghi ngắn;
/// không giữ DbContext khi chờ AI.
/// </summary>
public class FullMediaPipelineService(
    IServiceScopeFactory scopeFactory,
    IOptions<FullMediaPipelineOptions> options,
    TimeProvider timeProvider,
    ILogger<FullMediaPipelineService> logger)
{
    public const string Actor = "campaign-worker";
    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true };

    /// <summary>Cho phép test gắn Random seed (jitter).</summary>
    public Random Random { get; set; } = Random.Shared;

    /// <summary>Hook test: thay GenerateCaptionTextForPageAsync (không ghi MediaAsset).</summary>
    internal Func<Guid, Guid, CancellationToken, Task<string>>? CaptionFactory { get; set; }

    public async Task ResetStuckAsync(CancellationToken ct = default)
    {
        using var scope = scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var now = timeProvider.GetUtcNow().UtcDateTime;
        await db.CampaignMediaEvaluations
            .Where(e => !e.IsDeleted
                && (e.Status == CampaignMediaEvaluationStatus.Scoring
                    || e.Status == CampaignMediaEvaluationStatus.Verifying))
            .ExecuteUpdateAsync(s => s
                .SetProperty(e => e.Status, CampaignMediaEvaluationStatus.Pending)
                .SetProperty(e => e.UpdatedAt, now)
                .SetProperty(e => e.UpdatedBy, Actor), ct);
    }

    public async Task<int> ProcessDueAsync(CancellationToken ct = default)
    {
        var opts = options.Value;
        if (!opts.Enabled)
            return 0;

        var processed = 0;
        var maxTick = Math.Max(1, opts.MaxImagesPerTick);

        while (processed < maxTick && !ct.IsCancellationRequested)
        {
            if (!await CanAffordNextAsync(ct))
                break;

            var work = await PickNextWorkAsync(ct);
            if (work is null)
                break;

            try
            {
                await ProcessClaimedAsync(work, ct);
                processed++;
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                logger.LogError(ex,
                    "FullMedia pipeline lỗi ảnh campaign={CampaignId} media={MediaId}",
                    work.CampaignId, work.MediaAssetId);
                await MarkFailedOrPendingAsync(work.EvaluationId, "pipeline_error", ct);
            }
        }

        return processed;
    }

    /// <summary>Xử lý một ảnh đã biết (test / gọi trực tiếp). Tự tạo evaluation Pending rồi claim.</summary>
    public async Task ProcessImageAsync(Guid campaignId, Guid mediaAssetId, CancellationToken ct = default)
    {
        await EnsureEvaluationAsync(campaignId, mediaAssetId, ct);
        var claimed = await TryClaimAsync(campaignId, mediaAssetId, ct);
        if (claimed is null)
            return;
        await ProcessClaimedAsync(new WorkItem(claimed.Value, campaignId, mediaAssetId, IsRescore: false), ct);
    }

    private async Task<WorkItem?> PickNextWorkAsync(CancellationToken ct)
    {
        using var scope = scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var gen = scope.ServiceProvider.GetRequiredService<CampaignGenerationService>();
        var folders = scope.ServiceProvider.GetRequiredService<MediaFolderRepository>();
        var now = timeProvider.GetUtcNow().UtcDateTime;

        var campaigns = await db.Campaigns.AsNoTracking()
            .Where(c => !c.IsDeleted
                && c.Status == CampaignStatus.Running
                && c.Mode == CampaignMode.FullMedia
                && c.QualityPausedAt == null
                && c.SourceFolderId != null)
            .OrderBy(c => c.CreatedAt)
            .ToListAsync(ct);

        foreach (var campaign in campaigns)
        {
            var folderId = campaign.SourceFolderId!.Value;
            var tree = await folders.GetDescendantFolderIdsAsync(folderId, ct);
            tree.Add(folderId);

            var mediaIds = await db.MediaAssets.AsNoTracking()
                .Where(m => !m.IsDeleted
                    && m.FolderId != null
                    && tree.Contains(m.FolderId.Value)
                    && m.MimeType.StartsWith("image/"))
                .OrderBy(m => m.CreatedAt)
                .Select(m => m.Id)
                .ToListAsync(ct);

            var channelIds = await gen.ResolveRunningChannelIdsAsync(campaign.Id, ct);

            foreach (var mediaId in mediaIds)
            {
                if (await AlreadyPostedOnAnyPageAsync(db, campaign.Id, mediaId, ct))
                    continue;

                var eval = await db.CampaignMediaEvaluations.AsNoTracking()
                    .FirstOrDefaultAsync(e => !e.IsDeleted
                        && e.CampaignId == campaign.Id
                        && e.MediaAssetId == mediaId, ct);

                if (eval is null)
                {
                    await EnsureEvaluationAsync(campaign.Id, mediaId, ct);
                    var claimed = await TryClaimAsync(campaign.Id, mediaId, ct);
                    if (claimed is not null)
                        return new WorkItem(claimed.Value, campaign.Id, mediaId, IsRescore: false);
                    continue;
                }

                if (eval.Status == CampaignMediaEvaluationStatus.Pending
                    && (eval.NextAttemptAt is null || eval.NextAttemptAt <= now))
                {
                    var claimed = await TryClaimAsync(campaign.Id, mediaId, ct);
                    if (claimed is not null)
                        return new WorkItem(claimed.Value, campaign.Id, mediaId, IsRescore: false);
                }

                if (eval.Status == CampaignMediaEvaluationStatus.Rejected
                    && eval.Attempts < Math.Max(0, options.Value.MaxRescores))
                {
                    var inputHash = await ComputeInputHashAsync(db, campaign.Id, mediaId, channelIds, ct);
                    if (!string.IsNullOrEmpty(inputHash)
                        && !string.Equals(inputHash, eval.InputHash, StringComparison.Ordinal))
                    {
                        await db.CampaignMediaEvaluations
                            .Where(e => e.Id == eval.Id && e.Status == CampaignMediaEvaluationStatus.Rejected)
                            .ExecuteUpdateAsync(s => s
                                .SetProperty(e => e.Status, CampaignMediaEvaluationStatus.Pending)
                                .SetProperty(e => e.UpdatedAt, now), ct);
                        var claimed = await TryClaimAsync(campaign.Id, mediaId, ct);
                        if (claimed is not null)
                            return new WorkItem(claimed.Value, campaign.Id, mediaId, IsRescore: true);
                    }
                }
            }
        }

        return null;
    }

    private async Task ProcessClaimedAsync(WorkItem work, CancellationToken ct)
    {
        try
        {
            await ProcessClaimedCoreAsync(work, ct);
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            // Timeout giai đoạn (StageTimeoutSeconds) — không nuốt stoppingToken.
            await FinalizePendingAsync(work.EvaluationId, "stage_timeout", 0, ct);
        }
    }

    private async Task ProcessClaimedCoreAsync(WorkItem work, CancellationToken ct)
    {
        var opts = options.Value;
        var minScore = opts.MinFitScore;
        var now = timeProvider.GetUtcNow().UtcDateTime;

        // --- Đọc ngữ cảnh (scope ngắn) ---
        CampaignSnapshot snap;
        using (var scope = scopeFactory.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var gen = scope.ServiceProvider.GetRequiredService<CampaignGenerationService>();
            var campaign = await db.Campaigns.AsNoTracking()
                .FirstAsync(c => c.Id == work.CampaignId && !c.IsDeleted, ct);
            if (campaign.Status != CampaignStatus.Running || campaign.QualityPausedAt is not null)
            {
                await ReleaseToPendingAsync(work.EvaluationId, "campaign_paused", ct);
                return;
            }

            var channelIds = await gen.ResolveRunningChannelIdsAsync(campaign.Id, ct);
            var profiles = await LoadScoreableProfilesAsync(db, channelIds, ct);
            snap = new CampaignSnapshot(campaign, channelIds, profiles);
        }

        if (snap.Scoreable.Count == 0)
        {
            await FinalizePendingAsync(work.EvaluationId, "no_scoreable_pages", cost: 0, ct);
            return;
        }

        decimal cost = 0;
        VisionDescription descA;
        string stateA;
        VisionDescription? descB = null;

        if (work.IsRescore)
        {
            // T13: không gọi Vision — chỉ đọc mô tả đã lưu.
            using var scope = scopeFactory.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var loadedA = await LoadCachedDescriptionAsync(db, work.MediaAssetId, VisionPass.A, ct);
            var loadedB = await LoadCachedDescriptionAsync(db, work.MediaAssetId, VisionPass.B, ct);
            if (loadedA is null)
            {
                await FinalizePendingAsync(work.EvaluationId, "vision_cache_missing", 0, ct);
                return;
            }

            descA = loadedA;
            stateA = descA.ToState();
            descB = loadedB;
        }
        else
        {
            VisionOutcome outcomeA;
            using (var scope = scopeFactory.CreateScope())
            {
                var vision = scope.ServiceProvider.GetRequiredService<IMediaVisionDescriber>();
                using var stageCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
                stageCts.CancelAfter(TimeSpan.FromSeconds(Math.Max(1, opts.StageTimeoutSeconds)));
                outcomeA = await vision.GetUsableAsync(work.MediaAssetId, VisionPass.A, stageCts.Token);
            }

            if (outcomeA.Status == VisionUsability.Unusable)
            {
                await FinalizeStatusAsync(work.EvaluationId, CampaignMediaEvaluationStatus.Unusable,
                    outcomeA.ReasonCode ?? "unusable", bestScore: null, bestChannel: null, 0, ct);
                return;
            }

            if (outcomeA.Status != VisionUsability.Usable || outcomeA.Description is null)
            {
                await FinalizePendingAsync(work.EvaluationId, outcomeA.ReasonCode ?? "vision_failed", 0, ct);
                return;
            }

            descA = outcomeA.Description;
            stateA = descA.ToState();
        }

        var candidates = snap.Scoreable
            .Select(p => new PageFitCandidate(p.ChannelId, p.Profile.Text))
            .ToList();

        PageFitResult score1;
        try
        {
            using var scope = scopeFactory.CreateScope();
            var scorer = scope.ServiceProvider.GetRequiredService<IPageFitScorer>();
            using var stageCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            stageCts.CancelAfter(TimeSpan.FromSeconds(Math.Max(1, opts.StageTimeoutSeconds)));
            score1 = await scorer.ScoreAsync(stateA, candidates, stageCts.Token);
        }
        catch (PageFitPermanentException ex)
        {
            logger.LogWarning(ex, "PageFit permanent campaign={CampaignId} media={MediaId}",
                work.CampaignId, work.MediaAssetId);
            await NotifyAsync("Full Media: lỗi chấm điểm vĩnh viễn",
                $"campaign={work.CampaignId} media={work.MediaAssetId} code={ex.Message}", ct);
            await FinalizePendingAsync(work.EvaluationId, "pagefit_permanent", 0, ct);
            return;
        }
        catch (PageFitTransientException ex)
        {
            await FinalizePendingAsync(work.EvaluationId, "pagefit_transient", 0, ct);
            logger.LogWarning(ex, "PageFit transient campaign={CampaignId}", work.CampaignId);
            return;
        }

        cost += score1.EstimatedCostUsd;
        await PersistScoresAsync(work, pass: 1, score1, minScore, ct);

        var pass1Ids = score1.Scores
            .Where(kv => FitThreshold.Passes(kv.Value, minScore))
            .Select(kv => kv.Key)
            .ToList();

        if (pass1Ids.Count == 0)
        {
            var best = score1.Scores.OrderByDescending(kv => kv.Value).First();
            var inputHash = ComputeHashFromProfiles(snap.Scoreable, stateA);
            await FinalizeStatusAsync(work.EvaluationId, CampaignMediaEvaluationStatus.Rejected,
                "below_threshold", best.Value, best.Key, cost, ct, inputHash);
            return;
        }

        string stateB;
        if (work.IsRescore)
        {
            if (descB is null)
            {
                await FinalizePendingAsync(work.EvaluationId, "vision_b_cache_missing", cost, ct);
                return;
            }

            if (string.Equals(descA.Model, descB.Model, StringComparison.Ordinal))
            {
                await FinalizePendingAsync(work.EvaluationId, "vision_not_independent", cost, ct);
                return;
            }

            stateB = descB.ToState();
        }
        else
        {
            // Vision B chỉ khi lượt một có page đạt
            VisionOutcome outcomeB;
            using (var scope = scopeFactory.CreateScope())
            {
                var vision = scope.ServiceProvider.GetRequiredService<IMediaVisionDescriber>();
                using var stageCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
                stageCts.CancelAfter(TimeSpan.FromSeconds(Math.Max(1, opts.StageTimeoutSeconds)));
                outcomeB = await vision.GetUsableAsync(work.MediaAssetId, VisionPass.B, stageCts.Token);
            }

            if (outcomeB.Status != VisionUsability.Usable || outcomeB.Description is null)
            {
                await FinalizePendingAsync(work.EvaluationId, outcomeB.ReasonCode ?? "vision_b_failed", cost, ct);
                return;
            }

            // N2: hai model phải khác nhau
            if (string.Equals(descA.Model, outcomeB.Description.Model, StringComparison.Ordinal))
            {
                await NotifyAsync("Full Media: Vision A/B không độc lập",
                    $"campaign={work.CampaignId} media={work.MediaAssetId} reason=vision_not_independent", ct);
                await FinalizePendingAsync(work.EvaluationId, "vision_not_independent", cost, ct);
                return;
            }

            stateB = outcomeB.Description.ToState();
        }

        var pass1Candidates = candidates.Where(c => pass1Ids.Contains(c.ChannelId)).ToList();

        PageFitResult score2;
        try
        {
            using var scope = scopeFactory.CreateScope();
            var scorer = scope.ServiceProvider.GetRequiredService<IPageFitScorer>();
            using var stageCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            stageCts.CancelAfter(TimeSpan.FromSeconds(Math.Max(1, opts.StageTimeoutSeconds)));
            score2 = await scorer.ScoreAsync(stateB, pass1Candidates, stageCts.Token);
        }
        catch (PageFitPermanentException)
        {
            await NotifyAsync("Full Media: lỗi chấm điểm vĩnh viễn (lượt 2)",
                $"campaign={work.CampaignId} media={work.MediaAssetId}", ct);
            await FinalizePendingAsync(work.EvaluationId, "pagefit_permanent", cost, ct);
            return;
        }
        catch (PageFitTransientException)
        {
            await FinalizePendingAsync(work.EvaluationId, "pagefit_transient", cost, ct);
            return;
        }

        cost += score2.EstimatedCostUsd;
        await PersistScoresAsync(work, pass: 2, score2, minScore, ct);

        var accepted = pass1Ids
            .Where(id => score2.Scores.TryGetValue(id, out var s) && FitThreshold.Passes(s, minScore))
            .ToList();

        var inputHashFinal = ComputeHashFromProfiles(snap.Scoreable, stateA);

        if (accepted.Count == 0)
        {
            var best = score2.Scores.Concat(score1.Scores)
                .GroupBy(kv => kv.Key)
                .Select(g => g.OrderByDescending(x => x.Value).First())
                .OrderByDescending(kv => kv.Value)
                .First();
            await FinalizeStatusAsync(work.EvaluationId, CampaignMediaEvaluationStatus.Rejected,
                "below_threshold_pass2", best.Value, best.Key, cost, ct, inputHashFinal);
            return;
        }

        await CreatePostsForAcceptedAsync(work, snap, accepted, score2, cost, inputHashFinal, ct);
    }

    private async Task<VisionDescription?> LoadCachedDescriptionAsync(
        AppDbContext db, Guid mediaId, VisionPass pass, CancellationToken ct)
    {
        var row = await db.MediaVisionProfiles.AsNoTracking()
            .Where(v => !v.IsDeleted && v.MediaAssetId == mediaId && v.Pass == pass)
            .OrderByDescending(v => v.CreatedAt)
            .FirstOrDefaultAsync(ct);
        if (row is null) return null;
        try
        {
            var dto = JsonSerializer.Deserialize<VisionDescriptionDto>(row.DescriptionJson, JsonOptions);
            if (dto is null) return null;
            return new VisionDescription(
                dto.Topic ?? "", dto.Audience ?? "", dto.Tone ?? "", dto.Setting ?? "",
                dto.TextInImage ?? "", dto.Keywords ?? [], dto.Confidence, dto.Unclear, row.Model);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private async Task CreatePostsForAcceptedAsync(
        WorkItem work,
        CampaignSnapshot snap,
        List<Guid> acceptedChannelIds,
        PageFitResult score2,
        decimal cost,
        string inputHash,
        CancellationToken ct)
    {
        var now = timeProvider.GetUtcNow().UtcDateTime;
        DateTime slotUtc;
        DateTime scheduledAt;

        using (var scope = scopeFactory.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var campaign = await db.Campaigns.AsNoTracking()
                .FirstAsync(c => c.Id == work.CampaignId, ct);
            var publishTimes = DeserializeStringList(campaign.PublishTimesJson);
            var weekdays = DeserializeIntList(campaign.WeekdaysJson);
            var slots = CampaignGenerationService.BuildSlots(campaign, publishTimes, weekdays, now);
            if (slots.Count == 0)
            {
                await FinalizePendingAsync(work.EvaluationId, "no_slots", cost, ct);
                return;
            }

            slotUtc = slots[0];
            scheduledAt = CampaignGenerationService.ApplyJitter(
                slotUtc, campaign.JitterMinutes, now, Random);
        }

        var created = 0;
        double? bestScore = null;
        Guid? bestChannel = null;

        foreach (var channelId in acceptedChannelIds)
        {
            if (score2.Scores.TryGetValue(channelId, out var sc))
            {
                if (bestScore is null || sc > bestScore)
                {
                    bestScore = sc;
                    bestChannel = channelId;
                }
            }

            try
            {
                string caption;
                using (var stageCts = CancellationTokenSource.CreateLinkedTokenSource(ct))
                {
                    stageCts.CancelAfter(TimeSpan.FromSeconds(Math.Max(1, options.Value.StageTimeoutSeconds)));
                    if (CaptionFactory is not null)
                    {
                        caption = await CaptionFactory(work.MediaAssetId, channelId, stageCts.Token);
                    }
                    else
                    {
                        using var scope = scopeFactory.CreateScope();
                        var intelligence = scope.ServiceProvider.GetRequiredService<MediaIntelligenceService>();
                        caption = await intelligence.GenerateCaptionTextForPageAsync(
                            work.MediaAssetId, channelId, stageCts.Token);
                    }
                }

                using (var scope = scopeFactory.CreateScope())
                {
                    var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
                    if (await AlreadyPostedOnPageAsync(db, work.CampaignId, work.MediaAssetId, channelId, ct))
                        continue;

                    var author = snap.Campaign.CreatedByUserId ?? Guid.Empty;
                    var postId = Guid.NewGuid();
                    db.Posts.Add(new PostModel
                    {
                        Id = postId,
                        Title = "Full Media",
                        Content = caption,
                        SocialChannelId = channelId,
                        GenerationFlow = GenerationFlow.FullMedia,
                        Status = PostStatus.Scheduled,
                        CampaignId = work.CampaignId,
                        CampaignSlotAt = slotUtc,
                        ScheduledPublishAt = scheduledAt,
                        ScheduleTimezone = CampaignGenerationService.ScheduleTimezone,
                        UserId = author,
                        ApprovedBy = Actor,
                        ApprovedAt = now,
                        CreatedAt = now,
                        CreatedBy = Actor,
                    });
                    db.PostMedias.Add(new PostMediaModel
                    {
                        Id = Guid.NewGuid(),
                        PostId = postId,
                        MediaId = work.MediaAssetId,
                        MediaRole = MediaRole.Primary,
                        SortOrder = 0,
                        CreatedAt = now,
                        CreatedBy = Actor,
                    });

                    try
                    {
                        await db.SaveChangesAsync(ct);
                        created++;
                    }
                    catch (DbUpdateException ex) when (CampaignGenerationService.IsUniqueViolation(ex))
                    {
                        logger.LogDebug(ex, "FullMedia slot collision campaign={CampaignId} channel={ChannelId}",
                            work.CampaignId, channelId);
                    }
                }
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex,
                    "Caption/post thất bại channel={ChannelId} media={MediaId} — tiếp tục page khác",
                    channelId, work.MediaAssetId);
            }
        }

        if (created == 0)
        {
            await FinalizePendingAsync(work.EvaluationId, "no_posts_created", cost, ct);
            return;
        }

        await FinalizeStatusAsync(work.EvaluationId, CampaignMediaEvaluationStatus.Accepted,
            "accepted", bestScore, bestChannel, cost, ct, inputHash);
    }

    private async Task PersistScoresAsync(
        WorkItem work, int pass, PageFitResult result, double minScore, CancellationToken ct)
    {
        using var scope = scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var now = timeProvider.GetUtcNow().UtcDateTime;
        foreach (var (channelId, score) in result.Scores)
        {
            var existing = await db.CampaignMediaScores
                .FirstOrDefaultAsync(s => !s.IsDeleted
                    && s.CampaignId == work.CampaignId
                    && s.MediaAssetId == work.MediaAssetId
                    && s.SocialChannelId == channelId
                    && s.Pass == pass, ct);
            if (existing is null)
            {
                db.CampaignMediaScores.Add(new CampaignMediaScoreModel
                {
                    Id = Guid.NewGuid(),
                    CampaignId = work.CampaignId,
                    MediaAssetId = work.MediaAssetId,
                    SocialChannelId = channelId,
                    Pass = pass,
                    Score = score,
                    Passed = FitThreshold.Passes(score, minScore),
                    ScoredAt = now,
                    CreatedAt = now,
                    CreatedBy = Actor,
                });
            }
            else
            {
                existing.Score = score;
                existing.Passed = FitThreshold.Passes(score, minScore);
                existing.ScoredAt = now;
                existing.UpdatedAt = now;
            }
        }

        await db.SaveChangesAsync(ct);
    }

    private async Task<Guid?> TryClaimAsync(Guid campaignId, Guid mediaId, CancellationToken ct)
    {
        using var scope = scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var now = timeProvider.GetUtcNow().UtcDateTime;
        var updated = await db.CampaignMediaEvaluations
            .Where(e => !e.IsDeleted
                && e.CampaignId == campaignId
                && e.MediaAssetId == mediaId
                && e.Status == CampaignMediaEvaluationStatus.Pending
                && (e.NextAttemptAt == null || e.NextAttemptAt <= now))
            .ExecuteUpdateAsync(s => s
                .SetProperty(e => e.Status, CampaignMediaEvaluationStatus.Scoring)
                .SetProperty(e => e.UpdatedAt, now)
                .SetProperty(e => e.UpdatedBy, Actor)
                .SetProperty(e => e.Attempts, e => e.Attempts + 1), ct);
        if (updated == 0) return null;
        return await db.CampaignMediaEvaluations.AsNoTracking()
            .Where(e => e.CampaignId == campaignId && e.MediaAssetId == mediaId && !e.IsDeleted)
            .Select(e => e.Id)
            .FirstAsync(ct);
    }

    private async Task EnsureEvaluationAsync(Guid campaignId, Guid mediaId, CancellationToken ct)
    {
        using var scope = scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var exists = await db.CampaignMediaEvaluations
            .AnyAsync(e => !e.IsDeleted && e.CampaignId == campaignId && e.MediaAssetId == mediaId, ct);
        if (exists) return;
        var now = timeProvider.GetUtcNow().UtcDateTime;
        db.CampaignMediaEvaluations.Add(new CampaignMediaEvaluationModel
        {
            Id = Guid.NewGuid(),
            CampaignId = campaignId,
            MediaAssetId = mediaId,
            Status = CampaignMediaEvaluationStatus.Pending,
            CreatedAt = now,
            CreatedBy = Actor,
        });
        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException ex) when (IsUniqueViolation(ex))
        {
            // race — bỏ qua
        }
    }

    private async Task FinalizePendingAsync(Guid evaluationId, string reason, decimal cost, CancellationToken ct)
        => await FinalizeStatusAsync(evaluationId, CampaignMediaEvaluationStatus.Pending, reason,
            null, null, cost, ct);

    private async Task ReleaseToPendingAsync(Guid evaluationId, string reason, CancellationToken ct)
        => await FinalizePendingAsync(evaluationId, reason, 0, ct);

    private async Task MarkFailedOrPendingAsync(Guid evaluationId, string reason, CancellationToken ct)
        => await FinalizePendingAsync(evaluationId, reason, 0, ct);

    private async Task FinalizeStatusAsync(
        Guid evaluationId,
        CampaignMediaEvaluationStatus status,
        string? reason,
        double? bestScore,
        Guid? bestChannel,
        decimal costDelta,
        CancellationToken ct,
        string? inputHash = null)
    {
        using var scope = scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var now = timeProvider.GetUtcNow().UtcDateTime;
        var eval = await db.CampaignMediaEvaluations.FirstAsync(e => e.Id == evaluationId, ct);
        eval.Status = status;
        eval.ReasonCode = reason;
        if (bestScore is not null) eval.BestScore = bestScore;
        if (bestChannel is not null) eval.BestChannelId = bestChannel;
        eval.CostUsd += costDelta;
        eval.UpdatedAt = now;
        eval.UpdatedBy = Actor;
        if (status is CampaignMediaEvaluationStatus.Accepted
            or CampaignMediaEvaluationStatus.Rejected
            or CampaignMediaEvaluationStatus.Unusable)
        {
            eval.EvaluatedAt = now;
        }

        if (inputHash is not null)
            eval.InputHash = inputHash;
        if (status == CampaignMediaEvaluationStatus.Pending)
            eval.NextAttemptAt = now.AddMinutes(5);
        await db.SaveChangesAsync(ct);
        logger.LogInformation(
            "FullMedia evaluation {EvaluationId} → {Status} reason={Reason} best={Best}",
            evaluationId, status, reason, bestScore);
    }

    private async Task NotifyAsync(string title, string message, CancellationToken ct)
    {
        using var scope = scopeFactory.CreateScope();
        var notifications = scope.ServiceProvider.GetService<NotificationService>();
        if (notifications is null) return;
        await notifications.AddAsync(
            NotificationKind.PostFailed,
            NotificationSource.System,
            Actor,
            title,
            message,
            ct: ct);
    }

    private async Task<bool> CanAffordNextAsync(CancellationToken ct)
    {
        var opts = options.Value;
        using var scope = scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var dayStart = timeProvider.GetUtcNow().UtcDateTime.Date;
        var dayEnd = dayStart.AddDays(1);

        var todayCount = await db.CampaignMediaEvaluations.AsNoTracking()
            .CountAsync(e => !e.IsDeleted
                && e.EvaluatedAt != null
                && e.EvaluatedAt >= dayStart
                && e.EvaluatedAt < dayEnd
                && (e.Status == CampaignMediaEvaluationStatus.Accepted
                    || e.Status == CampaignMediaEvaluationStatus.Rejected
                    || e.Status == CampaignMediaEvaluationStatus.Unusable), ct);
        if (todayCount >= opts.MaxImagesPerDay)
            return false;

        // MaxDailyCostUsd theo ngày UTC từ tổng CostUsd (ổn định qua restart).
        var todayCost = await db.CampaignMediaEvaluations.AsNoTracking()
            .Where(e => !e.IsDeleted
                && e.UpdatedAt != null
                && e.UpdatedAt >= dayStart
                && e.UpdatedAt < dayEnd)
            .SumAsync(e => (decimal?)e.CostUsd, ct) ?? 0m;

        return todayCost < opts.MaxDailyCostUsd;
    }

    private static async Task<bool> AlreadyPostedOnAnyPageAsync(
        AppDbContext db, Guid campaignId, Guid mediaId, CancellationToken ct)
    {
        var postIds = await db.Posts.AsNoTracking()
            .Where(p => !p.IsDeleted && p.CampaignId == campaignId && p.Status != PostStatus.Cancelled)
            .Select(p => p.Id)
            .ToListAsync(ct);
        if (postIds.Count == 0) return false;
        return await db.PostMedias.AsNoTracking()
            .AnyAsync(m => !m.IsDeleted && m.MediaId == mediaId && postIds.Contains(m.PostId), ct);
    }

    private static async Task<bool> AlreadyPostedOnPageAsync(
        AppDbContext db, Guid campaignId, Guid mediaId, Guid channelId, CancellationToken ct)
    {
        var postIds = await db.Posts.AsNoTracking()
            .Where(p => !p.IsDeleted
                && p.CampaignId == campaignId
                && p.SocialChannelId == channelId
                && p.Status != PostStatus.Cancelled)
            .Select(p => p.Id)
            .ToListAsync(ct);
        if (postIds.Count == 0) return false;
        return await db.PostMedias.AsNoTracking()
            .AnyAsync(m => !m.IsDeleted && m.MediaId == mediaId && postIds.Contains(m.PostId), ct);
    }

    private static async Task<List<ScoreablePage>> LoadScoreableProfilesAsync(
        AppDbContext db, IReadOnlyList<Guid> channelIds, CancellationToken ct)
    {
        var contexts = await db.PageContexts.AsNoTracking()
            .Where(p => !p.IsDeleted && channelIds.Contains(p.SocialChannelId))
            .ToListAsync(ct);
        var list = new List<ScoreablePage>();
        foreach (var ctx in contexts)
        {
            var profile = PageProfileSnapshot.Build(ctx);
            if (!profile.IsScoreable) continue;
            list.Add(new ScoreablePage(ctx.SocialChannelId, profile));
        }

        return list;
    }

    private static async Task<string?> ComputeInputHashAsync(
        AppDbContext db, Guid campaignId, Guid mediaId, IReadOnlyList<Guid> channelIds, CancellationToken ct)
    {
        var scoreable = await LoadScoreableProfilesAsync(db, channelIds, ct);
        var vision = await db.MediaVisionProfiles.AsNoTracking()
            .Where(v => !v.IsDeleted && v.MediaAssetId == mediaId && v.Pass == VisionPass.A)
            .OrderByDescending(v => v.CreatedAt)
            .FirstOrDefaultAsync(ct);
        if (vision is null) return null;
        try
        {
            var desc = JsonSerializer.Deserialize<VisionDescriptionDto>(vision.DescriptionJson, JsonOptions);
            if (desc is null) return null;
            var state = new VisionDescription(
                desc.Topic ?? "", desc.Audience ?? "", desc.Tone ?? "", desc.Setting ?? "",
                desc.TextInImage ?? "", desc.Keywords ?? [], desc.Confidence, desc.Unclear,
                vision.Model).ToState();
            return ComputeHashFromProfiles(scoreable, state);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    internal static string ComputeHashFromProfiles(IReadOnlyList<ScoreablePage> pages, string descriptionState)
    {
        var parts = pages.Select(p => p.Profile.Hash).OrderBy(h => h, StringComparer.Ordinal)
            .Append(Sha256Hex(descriptionState));
        return Sha256Hex(string.Join('|', parts));
    }

    private static string Sha256Hex(string text)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(text));
        return Convert.ToHexString(bytes).ToLowerInvariant();
    }

    private static bool IsUniqueViolation(DbUpdateException ex)
    {
        for (var inner = ex.InnerException; inner is not null; inner = inner.InnerException)
        {
            if (inner is SqliteException sqlite
                && (sqlite.SqliteExtendedErrorCode == 2067 || sqlite.SqliteExtendedErrorCode == 1555))
                return true;
        }

        return false;
    }

    private static List<int> DeserializeIntList(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return [];
        try { return JsonSerializer.Deserialize<List<int>>(json, JsonOptions) ?? []; }
        catch (JsonException) { return []; }
    }

    private static List<string> DeserializeStringList(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return [];
        try { return JsonSerializer.Deserialize<List<string>>(json, JsonOptions) ?? []; }
        catch (JsonException) { return []; }
    }

    private sealed record WorkItem(Guid EvaluationId, Guid CampaignId, Guid MediaAssetId, bool IsRescore);

    private sealed record CampaignSnapshot(
        CampaignModel Campaign,
        IReadOnlyList<Guid> ChannelIds,
        IReadOnlyList<ScoreablePage> Scoreable);

    internal sealed record ScoreablePage(Guid ChannelId, PageProfile Profile);

    private sealed class VisionDescriptionDto
    {
        public string? Topic { get; set; }
        public string? Audience { get; set; }
        public string? Tone { get; set; }
        public string? Setting { get; set; }
        public string? TextInImage { get; set; }
        public List<string>? Keywords { get; set; }
        public double Confidence { get; set; }
        public bool Unclear { get; set; }
    }
}
