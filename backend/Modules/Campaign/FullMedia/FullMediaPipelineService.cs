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
    FullMediaCircuitBreaker circuitBreaker,
    ILogger<FullMediaPipelineService> logger)
{
    public const string Actor = "campaign-worker";
    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true };
    private static readonly int[] PendingBackoffMinutes = [5, 15, 60, 240];

    /// <summary>Lý do chờ điều kiện ngoài — không tính MaxPendingAttempts, không đếm cầu chì.</summary>
    internal static readonly HashSet<string> WaitingReasons = new(StringComparer.Ordinal)
    {
        "no_scoreable_pages",
        "campaign_paused",
        "no_free_slot",
        "slot_conflict",
        "no_slots",
    };

    internal static readonly HashSet<string> InfraReasons = new(StringComparer.Ordinal)
    {
        "pagefit_transient",
        "pagefit_permanent",
        "vision_failed",
        "vision_b_failed",
        "stage_timeout",
        "pipeline_error",
    };

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

        if (circuitBreaker.IsOpen)
            return 0;

        var processed = 0;
        var maxTick = Math.Max(1, opts.MaxImagesPerTick);

        while (processed < maxTick && !ct.IsCancellationRequested)
        {
            if (circuitBreaker.IsOpen)
                break;

            if (!await CanAffordNextAsync(ct))
                break;

            var work = await PickNextWorkAsync(ct);
            if (work is null)
                break;

            ImageProcessOutcome outcome;
            try
            {
                outcome = await ProcessClaimedAsync(work, ct);
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
                await FinalizePendingAsync(work.EvaluationId, "pipeline_error", 0, ct);
                outcome = ImageProcessOutcome.InfraFailure;
                processed++;
            }

            if (outcome == ImageProcessOutcome.TerminalOk)
            {
                circuitBreaker.RecordSuccessOrTerminal();
            }
            else if (outcome == ImageProcessOutcome.InfraFailure)
            {
                if (circuitBreaker.RecordInfraFailure(
                        opts.CircuitBreakerFailures,
                        opts.CircuitBreakerCooldownSeconds,
                        out var shouldNotify))
                {
                    if (shouldNotify)
                    {
                        await NotifyAsync(
                            "Full Media: ngắt mạch pipeline",
                            $"Circuit mở {opts.CircuitBreakerCooldownSeconds}s sau lỗi hạ tầng liên tiếp",
                            ct);
                    }

                    break;
                }
            }
        }

        return processed;
    }

    /// <summary>
    /// Đưa ảnh Failed về Pending để worker thử lại (không API — t6 sẽ gọi).
    /// Không xoá CampaignMediaScores. Chỉ đổi đúng một dòng đang Failed.
    /// </summary>
    public async Task<bool> ResetFailedToPendingAsync(
        Guid campaignId, Guid mediaAssetId, CancellationToken ct = default)
    {
        using var scope = scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var now = timeProvider.GetUtcNow().UtcDateTime;
        var updated = await db.CampaignMediaEvaluations
            .Where(e => !e.IsDeleted
                && e.CampaignId == campaignId
                && e.MediaAssetId == mediaAssetId
                && e.Status == CampaignMediaEvaluationStatus.Failed)
            .ExecuteUpdateAsync(s => s
                .SetProperty(e => e.Status, CampaignMediaEvaluationStatus.Pending)
                .SetProperty(e => e.ReasonCode, (string?)null)
                .SetProperty(e => e.NextAttemptAt, (DateTime?)null)
                .SetProperty(e => e.Attempts, 0)
                .SetProperty(e => e.UpdatedAt, now)
                .SetProperty(e => e.UpdatedBy, Actor), ct);
        return updated == 1;
    }

    /// <summary>Xử lý một ảnh đã biết (test / gọi trực tiếp). Tự tạo evaluation Pending rồi claim.</summary>
    public async Task ProcessImageAsync(Guid campaignId, Guid mediaAssetId, CancellationToken ct = default)
    {
        await EnsureEvaluationAsync(campaignId, mediaAssetId, ct);
        string? priorReason = null;
        using (var scope = scopeFactory.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            priorReason = await db.CampaignMediaEvaluations.AsNoTracking()
                .Where(e => !e.IsDeleted && e.CampaignId == campaignId && e.MediaAssetId == mediaAssetId)
                .Select(e => e.ReasonCode)
                .FirstOrDefaultAsync(ct);
        }

        var claimed = await TryClaimAsync(campaignId, mediaAssetId, ct);
        if (claimed is null)
            return;

        var postOnly = IsAwaitingSlotReason(priorReason)
            && await HasPass2ScoresAsync(campaignId, mediaAssetId, ct);
        await ProcessClaimedAsync(
            new WorkItem(claimed.Value, campaignId, mediaAssetId, IsRescore: false, IsPostOnly: postOnly),
            ct);
    }

    /// <summary>Chọn việc kế tiếp — internal để test T23.</summary>
    internal async Task<WorkItem?> PickNextWorkAsync(CancellationToken ct)
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
                var eval = await db.CampaignMediaEvaluations.AsNoTracking()
                    .FirstOrDefaultAsync(e => !e.IsDeleted
                        && e.CampaignId == campaign.Id
                        && e.MediaAssetId == mediaId, ct);

                if (eval?.Status == CampaignMediaEvaluationStatus.Accepted)
                    continue;
                if (eval?.Status == CampaignMediaEvaluationStatus.Failed)
                    continue;
                if (eval?.Status == CampaignMediaEvaluationStatus.Unusable)
                    continue;

                // Bỏ qua khi mọi page đang chạy đã có bài với ảnh này.
                if (channelIds.Count > 0
                    && await AllChannelsHaveMediaAsync(db, campaign.Id, mediaId, channelIds, ct))
                    continue;

                if (eval is null)
                {
                    await EnsureEvaluationAsync(campaign.Id, mediaId, ct);
                    var claimed = await TryClaimAsync(campaign.Id, mediaId, ct);
                    if (claimed is not null)
                        return new WorkItem(claimed.Value, campaign.Id, mediaId, IsRescore: false, IsPostOnly: false);
                    continue;
                }

                if (eval.Status == CampaignMediaEvaluationStatus.Pending
                    && (eval.NextAttemptAt is null || eval.NextAttemptAt <= now))
                {
                    var postOnly = IsAwaitingSlotReason(eval.ReasonCode)
                        && await HasPass2ScoresAsync(db, campaign.Id, mediaId, ct);
                    var claimed = await TryClaimAsync(campaign.Id, mediaId, ct);
                    if (claimed is not null)
                        return new WorkItem(claimed.Value, campaign.Id, mediaId, IsRescore: false, IsPostOnly: postOnly);
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
                            return new WorkItem(claimed.Value, campaign.Id, mediaId, IsRescore: true, IsPostOnly: false);
                    }
                }
            }
        }

        return null;
    }

    private async Task<ImageProcessOutcome> ProcessClaimedAsync(WorkItem work, CancellationToken ct)
    {
        try
        {
            return await ProcessClaimedCoreAsync(work, ct);
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            await FinalizePendingAsync(work.EvaluationId, "stage_timeout", 0, ct);
            return ImageProcessOutcome.InfraFailure;
        }
    }

    private async Task<ImageProcessOutcome> ProcessClaimedCoreAsync(WorkItem work, CancellationToken ct)
    {
        var opts = options.Value;
        var minScore = opts.MinFitScore;

        CampaignSnapshot snap;
        using (var scope = scopeFactory.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var gen = scope.ServiceProvider.GetRequiredService<CampaignGenerationService>();
            var campaign = await db.Campaigns.AsNoTracking()
                .FirstAsync(c => c.Id == work.CampaignId && !c.IsDeleted, ct);
            if (campaign.Status != CampaignStatus.Running || campaign.QualityPausedAt is not null)
            {
                await FinalizePendingAsync(work.EvaluationId, "campaign_paused", 0, ct);
                return ImageProcessOutcome.Waiting;
            }

            var channelIds = await gen.ResolveRunningChannelIdsAsync(campaign.Id, ct);
            var profiles = await LoadScoreableProfilesAsync(db, channelIds, ct);
            snap = new CampaignSnapshot(campaign, channelIds, profiles);
        }

        if (snap.Scoreable.Count == 0)
        {
            await FinalizePendingAsync(work.EvaluationId, "no_scoreable_pages", 0, ct);
            return ImageProcessOutcome.Waiting;
        }

        // F1-b: chỉ tạo bài lại — không Vision/PageFit
        if (work.IsPostOnly)
        {
            var acceptedFromScores = await LoadAcceptedChannelIdsFromScoresAsync(
                work.CampaignId, work.MediaAssetId, minScore, ct);
            if (acceptedFromScores.Count == 0)
            {
                await FinalizePendingAsync(work.EvaluationId, "no_free_slot", 0, ct);
                return ImageProcessOutcome.Waiting;
            }

            string? inputHash;
            using (var scope = scopeFactory.CreateScope())
            {
                var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
                inputHash = await db.CampaignMediaEvaluations.AsNoTracking()
                    .Where(e => e.Id == work.EvaluationId)
                    .Select(e => e.InputHash)
                    .FirstOrDefaultAsync(ct);
            }

            var emptyScores = new PageFitResult(
                acceptedFromScores.ToDictionary(id => id, _ => minScore),
                0m,
                0);
            return await CreatePostsForAcceptedAsync(
                work, snap, acceptedFromScores, emptyScores, cost: 0, inputHash ?? "", ct);
        }

        decimal cost = 0;
        VisionDescription descA;
        string stateA;
        VisionDescription? descB = null;

        if (work.IsRescore)
        {
            using var scope = scopeFactory.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var loadedA = await LoadCachedDescriptionAsync(db, work.MediaAssetId, VisionPass.A, ct);
            var loadedB = await LoadCachedDescriptionAsync(db, work.MediaAssetId, VisionPass.B, ct);
            if (loadedA is null)
            {
                await FinalizePendingAsync(work.EvaluationId, "vision_cache_missing", 0, ct);
                return ImageProcessOutcome.InfraFailure;
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
                    outcomeA.ReasonCode ?? "unusable", null, null, 0, ct);
                return ImageProcessOutcome.TerminalOk;
            }

            if (outcomeA.Status != VisionUsability.Usable || outcomeA.Description is null)
            {
                await FinalizePendingAsync(work.EvaluationId, outcomeA.ReasonCode ?? "vision_failed", 0, ct);
                return ImageProcessOutcome.InfraFailure;
            }

            descA = outcomeA.Description;
            stateA = descA.ToState();
        }

        var candidates = snap.Scoreable
            .Select(p => new PageFitCandidate(p.ChannelId, p.Profile.Text))
            .ToList();
        var profileHashes = snap.Scoreable.ToDictionary(p => p.ChannelId, p => p.Profile.Hash);
        var inputHashFinal = ComputeHashFromProfiles(snap.Scoreable, stateA);

        PageFitResult score1;
        try
        {
            score1 = await ScorePassSelectiveAsync(
                work, stateA, candidates, pass: 1, profileHashes, minScore, ct);
        }
        catch (PageFitPermanentException ex)
        {
            logger.LogWarning(ex, "PageFit permanent campaign={CampaignId} media={MediaId}",
                work.CampaignId, work.MediaAssetId);
            await NotifyAsync("Full Media: lỗi chấm điểm vĩnh viễn",
                $"campaign={work.CampaignId} media={work.MediaAssetId} code={ex.Message}", ct);
            await FinalizePendingAsync(work.EvaluationId, "pagefit_permanent", 0, ct);
            return ImageProcessOutcome.InfraFailure;
        }
        catch (PageFitTransientException ex)
        {
            await FinalizePendingAsync(work.EvaluationId, "pagefit_transient", 0, ct);
            logger.LogWarning(ex, "PageFit transient campaign={CampaignId}", work.CampaignId);
            return ImageProcessOutcome.InfraFailure;
        }

        cost += score1.EstimatedCostUsd;

        var pass1Ids = score1.Scores
            .Where(kv => FitThreshold.Passes(kv.Value, minScore))
            .Select(kv => kv.Key)
            .ToList();

        if (pass1Ids.Count == 0)
        {
            // Rescore không còn page cần chấm và không ai đạt → giữ Rejected, cập nhật InputHash.
            if (score1.Scores.Count == 0)
            {
                await FinalizeStatusAsync(work.EvaluationId, CampaignMediaEvaluationStatus.Rejected,
                    "below_threshold", null, null, cost, ct, inputHashFinal);
                return ImageProcessOutcome.TerminalOk;
            }

            var best = score1.Scores.OrderByDescending(kv => kv.Value).First();
            await FinalizeStatusAsync(work.EvaluationId, CampaignMediaEvaluationStatus.Rejected,
                "below_threshold", best.Value, best.Key, cost, ct, inputHashFinal);
            return ImageProcessOutcome.TerminalOk;
        }

        string stateB;
        if (work.IsRescore)
        {
            if (descB is null)
            {
                await FinalizePendingAsync(work.EvaluationId, "vision_b_cache_missing", cost, ct);
                return ImageProcessOutcome.InfraFailure;
            }

            if (string.Equals(descA.Model, descB.Model, StringComparison.Ordinal))
            {
                await FinalizePendingAsync(work.EvaluationId, "vision_not_independent", cost, ct);
                return ImageProcessOutcome.Waiting;
            }

            stateB = descB.ToState();
        }
        else
        {
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
                return ImageProcessOutcome.InfraFailure;
            }

            if (string.Equals(descA.Model, outcomeB.Description.Model, StringComparison.Ordinal))
            {
                await NotifyAsync("Full Media: Vision A/B không độc lập",
                    $"campaign={work.CampaignId} media={work.MediaAssetId} reason=vision_not_independent", ct);
                await FinalizePendingAsync(work.EvaluationId, "vision_not_independent", cost, ct);
                return ImageProcessOutcome.Waiting;
            }

            stateB = outcomeB.Description.ToState();
        }

        var pass1Candidates = candidates.Where(c => pass1Ids.Contains(c.ChannelId)).ToList();

        PageFitResult score2;
        try
        {
            score2 = await ScorePassSelectiveAsync(
                work, stateB, pass1Candidates, pass: 2, profileHashes, minScore, ct);
        }
        catch (PageFitPermanentException)
        {
            await NotifyAsync("Full Media: lỗi chấm điểm vĩnh viễn (lượt 2)",
                $"campaign={work.CampaignId} media={work.MediaAssetId}", ct);
            await FinalizePendingAsync(work.EvaluationId, "pagefit_permanent", cost, ct);
            return ImageProcessOutcome.InfraFailure;
        }
        catch (PageFitTransientException)
        {
            await FinalizePendingAsync(work.EvaluationId, "pagefit_transient", cost, ct);
            return ImageProcessOutcome.InfraFailure;
        }

        cost += score2.EstimatedCostUsd;

        var accepted = pass1Ids
            .Where(id => score2.Scores.TryGetValue(id, out var s) && FitThreshold.Passes(s, minScore))
            .ToList();

        if (accepted.Count == 0)
        {
            var best = score2.Scores.Concat(score1.Scores)
                .GroupBy(kv => kv.Key)
                .Select(g => g.OrderByDescending(x => x.Value).First())
                .OrderByDescending(kv => kv.Value)
                .First();
            await FinalizeStatusAsync(work.EvaluationId, CampaignMediaEvaluationStatus.Rejected,
                "below_threshold_pass2", best.Value, best.Key, cost, ct, inputHashFinal);
            return ImageProcessOutcome.TerminalOk;
        }

        return await CreatePostsForAcceptedAsync(work, snap, accepted, score2, cost, inputHashFinal, ct);
    }

    /// <summary>
    /// Chấm một lượt: khi IsRescore chỉ gọi bộ chấm cho page mới / đổi ProfileHash / ProfileHash NULL.
    /// Page khớp hash dùng lại điểm đã lưu (không ghi đè CostUsd).
    /// </summary>
    private async Task<PageFitResult> ScorePassSelectiveAsync(
        WorkItem work,
        string stateText,
        IReadOnlyList<PageFitCandidate> candidates,
        int pass,
        IReadOnlyDictionary<Guid, string> profileHashes,
        double minScore,
        CancellationToken ct)
    {
        var merged = new Dictionary<Guid, double>();
        var toScore = new List<PageFitCandidate>();

        if (work.IsRescore && candidates.Count > 0)
        {
            using var scope = scopeFactory.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var existing = await db.CampaignMediaScores.AsNoTracking()
                .Where(s => !s.IsDeleted
                    && s.CampaignId == work.CampaignId
                    && s.MediaAssetId == work.MediaAssetId
                    && s.Pass == pass)
                .ToListAsync(ct);
            var byChannel = existing.ToDictionary(s => s.SocialChannelId);

            foreach (var c in candidates)
            {
                if (byChannel.TryGetValue(c.ChannelId, out var row)
                    && !string.IsNullOrEmpty(row.ProfileHash)
                    && profileHashes.TryGetValue(c.ChannelId, out var cur)
                    && string.Equals(row.ProfileHash, cur, StringComparison.Ordinal))
                {
                    merged[c.ChannelId] = row.Score;
                }
                else
                {
                    toScore.Add(c);
                }
            }
        }
        else
        {
            toScore.AddRange(candidates);
        }

        if (toScore.Count == 0)
            return new PageFitResult(merged, 0m, 0);

        PageFitResult live;
        using (var scope = scopeFactory.CreateScope())
        {
            var scorer = scope.ServiceProvider.GetRequiredService<IPageFitScorer>();
            using var stageCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            stageCts.CancelAfter(TimeSpan.FromSeconds(Math.Max(1, options.Value.StageTimeoutSeconds)));
            live = await scorer.ScoreAsync(stateText, toScore, stageCts.Token);
        }

        foreach (var kv in live.Scores)
            merged[kv.Key] = kv.Value;

        await PersistScoresAsync(work, pass, live, minScore, profileHashes, ct);
        return new PageFitResult(merged, live.EstimatedCostUsd, live.RequestCount);
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

    private async Task<ImageProcessOutcome> CreatePostsForAcceptedAsync(
        WorkItem work,
        CampaignSnapshot snap,
        List<Guid> acceptedChannelIds,
        PageFitResult score2,
        decimal cost,
        string inputHash,
        CancellationToken ct)
    {
        var now = timeProvider.GetUtcNow().UtcDateTime;
        List<Guid> pagesNeedingPosts;
        List<DateTime> freeSlots;
        int jitter;
        Guid? authorId;

        using (var scope = scopeFactory.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            pagesNeedingPosts = [];
            foreach (var channelId in acceptedChannelIds)
            {
                if (!await AlreadyPostedOnPageAsync(db, work.CampaignId, work.MediaAssetId, channelId, ct))
                    pagesNeedingPosts.Add(channelId);
            }

            if (pagesNeedingPosts.Count == 0)
            {
                await FinalizeStatusAsync(work.EvaluationId, CampaignMediaEvaluationStatus.Accepted,
                    "accepted", score2.Scores.Values.DefaultIfEmpty(0).Max(),
                    acceptedChannelIds.FirstOrDefault(), cost, ct, inputHash);
                return ImageProcessOutcome.TerminalOk;
            }

            var campaign = await db.Campaigns.AsNoTracking()
                .FirstAsync(c => c.Id == work.CampaignId, ct);
            jitter = campaign.JitterMinutes;
            authorId = campaign.CreatedByUserId;
            var publishTimes = DeserializeStringList(campaign.PublishTimesJson);
            var weekdays = DeserializeIntList(campaign.WeekdaysJson);
            var slots = CampaignGenerationService.BuildSlots(campaign, publishTimes, weekdays, now);
            if (slots.Count == 0)
            {
                await FinalizePendingAsync(work.EvaluationId, "no_slots", cost, ct, inputHash,
                    nextAttemptAt: now.AddHours(1));
                return ImageProcessOutcome.Waiting;
            }

            var occupied = await GetOccupiedSlotsAsync(db, work.CampaignId, pagesNeedingPosts, ct);
            freeSlots = slots.Where(s => !occupied.Contains(s)).OrderBy(s => s).ToList();
            if (freeSlots.Count == 0)
            {
                await FinalizePendingAsync(work.EvaluationId, "no_free_slot", cost, ct, inputHash,
                    nextAttemptAt: now.AddHours(1));
                return ImageProcessOutcome.Waiting;
            }
        }

        const int maxSlotTries = 5;
        var tries = Math.Min(maxSlotTries, freeSlots.Count);
        double? bestScore = null;
        Guid? bestChannel = null;
        foreach (var id in pagesNeedingPosts)
        {
            if (score2.Scores.TryGetValue(id, out var sc)
                && (bestScore is null || sc > bestScore))
            {
                bestScore = sc;
                bestChannel = id;
            }
        }

        for (var tryIdx = 0; tryIdx < tries; tryIdx++)
        {
            var slotUtc = freeSlots[tryIdx];
            var scheduledAt = CampaignGenerationService.ApplyJitter(slotUtc, jitter, now, Random);
            var created = 0;
            var slotConflict = false;

            foreach (var channelId in pagesNeedingPosts)
            {
                try
                {
                    string caption;
                    using (var stageCts = CancellationTokenSource.CreateLinkedTokenSource(ct))
                    {
                        stageCts.CancelAfter(TimeSpan.FromSeconds(Math.Max(1, options.Value.StageTimeoutSeconds)));
                        if (CaptionFactory is not null)
                            caption = await CaptionFactory(work.MediaAssetId, channelId, stageCts.Token);
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
                            UserId = authorId ?? Guid.Empty,
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
                            logger.LogWarning(ex,
                                "FullMedia slot_conflict campaign={CampaignId} channel={ChannelId} slot={Slot} — thử khe kế",
                                work.CampaignId, channelId, slotUtc);
                            slotConflict = true;
                            break;
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

            if (created > 0 && !slotConflict)
            {
                await FinalizeStatusAsync(work.EvaluationId, CampaignMediaEvaluationStatus.Accepted,
                    "accepted", bestScore, bestChannel, cost, ct, inputHash);
                return ImageProcessOutcome.TerminalOk;
            }

            if (created > 0 && slotConflict)
            {
                // Một phần page đã tạo — chấp nhận Accepted nếu không còn page thiếu.
                using var scope = scopeFactory.CreateScope();
                var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
                var stillMissing = 0;
                foreach (var ch in pagesNeedingPosts)
                {
                    if (!await AlreadyPostedOnPageAsync(db, work.CampaignId, work.MediaAssetId, ch, ct))
                        stillMissing++;
                }

                if (stillMissing == 0)
                {
                    await FinalizeStatusAsync(work.EvaluationId, CampaignMediaEvaluationStatus.Accepted,
                        "accepted", bestScore, bestChannel, cost, ct, inputHash);
                    return ImageProcessOutcome.TerminalOk;
                }
            }

            if (!slotConflict && created == 0)
                break;
        }

        await FinalizePendingAsync(work.EvaluationId, "slot_conflict", cost, ct, inputHash,
            nextAttemptAt: now.AddHours(1));
        return ImageProcessOutcome.Waiting;
    }

    private async Task PersistScoresAsync(
        WorkItem work,
        int pass,
        PageFitResult result,
        double minScore,
        IReadOnlyDictionary<Guid, string> profileHashes,
        CancellationToken ct)
    {
        using var scope = scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var now = timeProvider.GetUtcNow().UtcDateTime;
        var costAssigned = false;
        foreach (var (channelId, score) in result.Scores)
        {
            var passCost = !costAssigned ? result.EstimatedCostUsd : 0m;
            costAssigned = true;
            profileHashes.TryGetValue(channelId, out var profileHash);
            if (profileHash is { Length: > 64 })
                profileHash = profileHash[..64];

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
                    CostUsd = passCost,
                    ProfileHash = profileHash,
                    CreatedAt = now,
                    CreatedBy = Actor,
                });
            }
            else
            {
                existing.Score = score;
                existing.Passed = FitThreshold.Passes(score, minScore);
                existing.ScoredAt = now;
                // Cộng dồn chi phí phát sinh — không ghi đè làm mất lượt trước (F4 / N-B).
                existing.CostUsd += passCost;
                existing.ProfileHash = profileHash;
                existing.UpdatedAt = now;
                existing.UpdatedBy = Actor;
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
            // race
        }
    }

    private Task FinalizePendingAsync(
        Guid evaluationId,
        string reason,
        decimal cost,
        CancellationToken ct,
        string? inputHash = null,
        DateTime? nextAttemptAt = null)
        => FinalizeStatusAsync(
            evaluationId,
            CampaignMediaEvaluationStatus.Pending,
            reason,
            null,
            null,
            cost,
            ct,
            inputHash,
            nextAttemptAt);

    private async Task FinalizeStatusAsync(
        Guid evaluationId,
        CampaignMediaEvaluationStatus status,
        string? reason,
        double? bestScore,
        Guid? bestChannel,
        decimal costDelta,
        CancellationToken ct,
        string? inputHash = null,
        DateTime? nextAttemptAtOverride = null)
    {
        using var scope = scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var now = timeProvider.GetUtcNow().UtcDateTime;
        var eval = await db.CampaignMediaEvaluations.FirstAsync(e => e.Id == evaluationId, ct);
        var waiting = reason is not null && WaitingReasons.Contains(reason);
        var infra = reason is not null && InfraReasons.Contains(reason);

        if (status == CampaignMediaEvaluationStatus.Pending && waiting)
        {
            // Không tính vào MaxPendingAttempts — hoàn Attempts tăng lúc claim.
            eval.Attempts = Math.Max(0, eval.Attempts - 1);
            eval.Status = CampaignMediaEvaluationStatus.Pending;
            eval.ReasonCode = reason;
            eval.NextAttemptAt = nextAttemptAtOverride ?? now.AddHours(1);
        }
        else if (status == CampaignMediaEvaluationStatus.Pending && !waiting)
        {
            var maxAttempts = Math.Max(1, options.Value.MaxPendingAttempts);
            if (eval.Attempts >= maxAttempts)
            {
                eval.Status = CampaignMediaEvaluationStatus.Failed;
                eval.ReasonCode = reason;
                eval.EvaluatedAt = now;
                eval.NextAttemptAt = null;
                eval.CostUsd += costDelta;
                if (bestScore is not null) eval.BestScore = bestScore;
                if (bestChannel is not null) eval.BestChannelId = bestChannel;
                if (inputHash is not null) eval.InputHash = inputHash;
                eval.UpdatedAt = now;
                eval.UpdatedBy = Actor;
                await db.SaveChangesAsync(ct);
                await NotifyAsync(
                    "Full Media: ảnh Failed sau quá số lần thử",
                    $"evaluation={evaluationId} reason={reason} attempts={eval.Attempts}",
                    ct);
                logger.LogInformation(
                    "FullMedia evaluation {EvaluationId} → Failed reason={Reason}",
                    evaluationId, reason);
                return;
            }

            eval.Status = CampaignMediaEvaluationStatus.Pending;
            eval.ReasonCode = reason;
            var backoffIdx = Math.Clamp(eval.Attempts - 1, 0, PendingBackoffMinutes.Length - 1);
            eval.NextAttemptAt = nextAttemptAtOverride
                ?? now.AddMinutes(PendingBackoffMinutes[backoffIdx]);
        }
        else
        {
            eval.Status = status;
            eval.ReasonCode = reason;
            if (status is CampaignMediaEvaluationStatus.Accepted
                or CampaignMediaEvaluationStatus.Rejected
                or CampaignMediaEvaluationStatus.Unusable
                or CampaignMediaEvaluationStatus.Failed)
            {
                eval.EvaluatedAt = now;
            }
        }

        if (bestScore is not null) eval.BestScore = bestScore;
        if (bestChannel is not null) eval.BestChannelId = bestChannel;
        eval.CostUsd += costDelta;
        eval.UpdatedAt = now;
        eval.UpdatedBy = Actor;
        if (inputHash is not null)
            eval.InputHash = inputHash;

        _ = infra; // phân loại dùng ở ProcessDue qua ReasonCode đọc lại nếu cần
        await db.SaveChangesAsync(ct);
        logger.LogInformation(
            "FullMedia evaluation {EvaluationId} → {Status} reason={Reason} best={Best}",
            evaluationId, eval.Status, reason, bestScore);
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

    internal async Task<bool> CanAffordNextAsync(CancellationToken ct)
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

        // F4 (ii): chỉ cộng CostUsd của lượt chấm có ScoredAt hôm nay (UTC).
        var todayCost = await db.CampaignMediaScores.AsNoTracking()
            .Where(s => !s.IsDeleted
                && s.ScoredAt >= dayStart
                && s.ScoredAt < dayEnd)
            .SumAsync(s => (decimal?)s.CostUsd, ct) ?? 0m;

        return todayCost < opts.MaxDailyCostUsd;
    }

    /// <summary>Kiểm tra đã đăng ảnh trên page — internal cho mutation T7.</summary>
    internal static Task<bool> AlreadyPostedOnPageAsync(
        AppDbContext db, Guid campaignId, Guid mediaId, Guid channelId, CancellationToken ct)
    {
        return AlreadyPostedOnPageCoreAsync(db, campaignId, mediaId, channelId, ct);
    }

    private static async Task<bool> AlreadyPostedOnPageCoreAsync(
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

    private static async Task<bool> AllChannelsHaveMediaAsync(
        AppDbContext db, Guid campaignId, Guid mediaId, IReadOnlyList<Guid> channelIds, CancellationToken ct)
    {
        foreach (var ch in channelIds)
        {
            if (!await AlreadyPostedOnPageCoreAsync(db, campaignId, mediaId, ch, ct))
                return false;
        }

        return channelIds.Count > 0;
    }

    private static async Task<HashSet<DateTime>> GetOccupiedSlotsAsync(
        AppDbContext db, Guid campaignId, IReadOnlyList<Guid> channelIds, CancellationToken ct)
    {
        var slots = await db.Posts.AsNoTracking()
            .Where(p => !p.IsDeleted
                && p.CampaignId == campaignId
                && channelIds.Contains(p.SocialChannelId)
                && p.CampaignSlotAt != null
                && p.Status != PostStatus.Cancelled)
            .Select(p => p.CampaignSlotAt!.Value)
            .ToListAsync(ct);
        return slots.ToHashSet();
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

    private async Task<List<Guid>> LoadAcceptedChannelIdsFromScoresAsync(
        Guid campaignId, Guid mediaId, double minScore, CancellationToken ct)
    {
        using var scope = scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var pass2 = await db.CampaignMediaScores.AsNoTracking()
            .Where(s => !s.IsDeleted
                && s.CampaignId == campaignId
                && s.MediaAssetId == mediaId
                && s.Pass == 2
                && s.Passed)
            .Select(s => new { s.SocialChannelId, s.Score })
            .ToListAsync(ct);
        return pass2
            .Where(s => FitThreshold.Passes(s.Score, minScore))
            .Select(s => s.SocialChannelId)
            .Distinct()
            .ToList();
    }

    private async Task<bool> HasPass2ScoresAsync(Guid campaignId, Guid mediaId, CancellationToken ct)
    {
        using var scope = scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        return await HasPass2ScoresAsync(db, campaignId, mediaId, ct);
    }

    private static Task<bool> HasPass2ScoresAsync(
        AppDbContext db, Guid campaignId, Guid mediaId, CancellationToken ct)
        => db.CampaignMediaScores.AsNoTracking()
            .AnyAsync(s => !s.IsDeleted
                && s.CampaignId == campaignId
                && s.MediaAssetId == mediaId
                && s.Pass == 2
                && s.Passed, ct);

    private static bool IsAwaitingSlotReason(string? reason)
        => reason is "no_free_slot" or "slot_conflict";

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

    internal sealed record WorkItem(
        Guid EvaluationId,
        Guid CampaignId,
        Guid MediaAssetId,
        bool IsRescore,
        bool IsPostOnly);

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
