using System.Diagnostics;
using System.Text.Json;
using Backend.Data;
using Backend.Modules.ContentCrawl.Enums;
using Backend.Modules.PageContext;
using Backend.Modules.Post;
using Backend.Modules.Post.Enums;
using Backend.Shared.Ai;
using Backend.Shared.Repositories;
using Backend.Shared.Text;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Backend.Modules.ContentCrawl;

/// <summary>
/// Luồng: cào → lưu tin → chấm trùng → xào bản nháp → (người duyệt) → fan-out ra Post.
///
/// Sau khi fan-out, mọi thứ chạy trên đường ray sẵn có: PostGenerationWorker nhặt post
/// Queued, sinh nội dung theo PageContext từng kênh (kèm tư liệu gốc trong ExtraJson),
/// tự duyệt, rồi TryApplyPendingScheduleAsync áp lịch. Module này KHÔNG tự sinh bài,
/// KHÔNG tự lên lịch, KHÔNG tự đăng.
/// </summary>
public class ContentCrawlPipelineService(
    AppDbContext context,
    ContentCrawlRepository repository,
    PostRepository postRepository,
    PageContextRepository pageContextRepository,
    ContentDedupService dedupService,
    RssFeedReader rssReader,
    IAiTextGenerationService aiTextService,
    IUserContext userContext,
    IOptions<ContentCrawlOptions> options,
    ILogger<ContentCrawlPipelineService> logger)
{
    // ── Cào một nguồn ───────────────────────────────────────────────────────

    public async Task<CrawlRunModel> RunSourceAsync(
        Guid sourceId, string triggerSource = "worker", CancellationToken ct = default)
    {
        var source = await repository.GetSourceAsync(sourceId, ct)
            ?? throw new KeyNotFoundException($"Không tìm thấy nguồn cào {sourceId}");

        var stopwatch = Stopwatch.StartNew();
        var run = new CrawlRunModel
        {
            Id = Guid.NewGuid(),
            CrawlSourceId = source.Id,
            Status = CrawlRunStatus.Running,
            StartedAt = DateTime.UtcNow,
            TriggerSource = triggerSource,
            CreatedAt = DateTime.UtcNow,
            CreatedBy = userContext.GetCurrentUserName(),
        };
        context.Set<CrawlRunModel>().Add(run);
        await context.SaveChangesAsync(ct);

        try
        {
            var items = source.SourceType switch
            {
                CrawlSourceType.Rss => await rssReader.FetchAsync(source.Url, source.MaxItemsPerRun, ct),
                _ => throw new NotSupportedException(
                    $"Loại nguồn '{source.SourceType}' chưa hỗ trợ. Cào fanpage Facebook thuộc Phase 3 (qua OpenClaw).")
            };

            var (added, duplicate, filtered) = await IngestAsync(source, run.Id, items, ct);

            run.ItemsFetched = items.Count;
            run.ItemsNew = added;
            run.ItemsDuplicate = duplicate;
            run.ItemsFiltered = filtered;
            run.Status = CrawlRunStatus.Completed;
            run.FinishedAt = DateTime.UtcNow;
            run.DurationMs = (int)stopwatch.ElapsedMilliseconds;

            source.LastRunAt = DateTime.UtcNow;
            source.LastSuccessAt = DateTime.UtcNow;
            source.LastError = null;
            source.ConsecutiveFailures = 0;
            source.UpdatedAt = DateTime.UtcNow;

            await context.SaveChangesAsync(ct);
            logger.LogInformation(
                "Cào {Source}: {Fetched} bài, {New} mới, {Dup} trùng guid, {Filtered} bị lọc ({Ms}ms)",
                source.Name, items.Count, added, duplicate, filtered, run.DurationMs);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            run.Status = CrawlRunStatus.Failed;
            run.FinishedAt = DateTime.UtcNow;
            run.DurationMs = (int)stopwatch.ElapsedMilliseconds;
            run.ErrorMessage = Truncate(ex.Message, 900);

            source.LastRunAt = DateTime.UtcNow;
            source.LastError = Truncate(ex.Message, 900);
            source.ConsecutiveFailures++;
            source.UpdatedAt = DateTime.UtcNow;

            await context.SaveChangesAsync(CancellationToken.None);
            logger.LogError(ex, "Cào {Source} thất bại (lần lỗi thứ {N})", source.Name, source.ConsecutiveFailures);
        }

        return run;
    }

    /// <summary>Lưu tin mới. Chặn trùng guid ngay tại đây để khỏi tạo bản ghi rác.</summary>
    private async Task<(int Added, int Duplicate, int Filtered)> IngestAsync(
        CrawlSourceModel source, Guid runId, List<RssItem> items, CancellationToken ct)
    {
        var known = await repository.GetKnownGuidsAsync(source.Id, ct);
        var include = ContentCrawlRepository.DeserializeStringList(source.IncludeKeywords);
        var exclude = ContentCrawlRepository.DeserializeStringList(source.ExcludeKeywords);
        var cutoff = DateTime.UtcNow.AddHours(-Math.Max(1, source.LookbackHours));

        // Lần chạy ĐẦU của một nguồn: đánh Filtered cho bài quá cũ mà KHÔNG chấm trùng, kẻo
        // đốt sạch lượt gọi AI chỉ để mồi chỉ mục (vietnamnet trả tới 1000 bài).
        var isFirstRun = source.LastSuccessAt is null;

        int added = 0, duplicate = 0, filtered = 0;
        var now = DateTime.UtcNow;
        var userName = userContext.GetCurrentUserName();

        foreach (var item in items)
        {
            var guid = item.Guid ?? item.Link;
            if (known.Contains(guid)) { duplicate++; continue; }
            known.Add(guid);

            var tooOld = item.PublishedAtUtc.HasValue && item.PublishedAtUtc.Value < cutoff;
            var blocked = MatchesAny(item, exclude);
            var missesInclude = include.Count > 0 && !MatchesAny(item, include);
            var isFiltered = tooOld || blocked || missesInclude;

            var fingerprint = SimHashCalculator.Compute(item.Title, item.Summary);
            var article = new CrawledArticleModel
            {
                Id = Guid.NewGuid(),
                CrawlSourceId = source.Id,
                CrawlRunId = runId,
                Title = Truncate(item.Title, 500),
                Summary = item.Summary,
                SourceUrl = Truncate(item.Link, 1000),
                NormalizedUrl = NormalizeUrl(item.Link),
                SourceGuid = Truncate(guid, 1000),
                Author = Truncate(item.Author, 200),
                SourceCategory = Truncate(item.Category, 200),
                ThumbnailUrl = options.Value.ImportThumbnails ? Truncate(item.ThumbnailUrl, 1000) : Truncate(item.ThumbnailUrl, 1000),
                PublishedAt = item.PublishedAtUtc,
                FetchedAt = now,
                ContentHash = fingerprint.ContentHash,
                SimHash = fingerprint.SimHash,
                Status = isFiltered
                    ? CrawledArticleStatus.Filtered
                    : (isFirstRun && tooOld ? CrawledArticleStatus.Filtered : CrawledArticleStatus.New),
                RejectReason = isFiltered
                    ? (tooOld ? "Quá cũ so với LookbackHours"
                        : blocked ? "Dính từ khoá chặn"
                        : "Không khớp từ khoá bắt buộc")
                    : null,
                CreatedAt = now,
                CreatedBy = userName,
            };

            context.Set<CrawledArticleModel>().Add(article);
            if (isFiltered) filtered++; else added++;
        }

        await context.SaveChangesAsync(ct);
        return (added, duplicate, filtered);
    }

    // ── Chấm trùng + xào nháp ───────────────────────────────────────────────

    /// <summary>
    /// Xử lý một lô tin: chấm trùng rồi xào nháp. Chạy TUẦN TỰ trên một scope —
    /// SQLite ở journal mode mặc định serialize writer, ghi dồn từ nhiều scope sẽ ra SQLITE_BUSY.
    /// </summary>
    public async Task<int> ProcessPendingAsync(CancellationToken ct = default)
    {
        var opt = options.Value;
        var articles = await repository.GetPendingProcessAsync(opt.ProcessBatchSize, ct);
        if (articles.Count == 0) return 0;

        var window = await dedupService.LoadWindowAsync(ct);
        var processed = 0;

        foreach (var article in articles)
        {
            ct.ThrowIfCancellationRequested();
            try
            {
                if (article.Status is CrawledArticleStatus.New or CrawledArticleStatus.Deduping)
                {
                    article.Status = CrawledArticleStatus.Deduping;
                    article.DedupAttemptCount++;
                    await context.SaveChangesAsync(ct);

                    var verdict = await dedupService.CheckAsync(article, window, ct);
                    ApplyVerdict(article, verdict);
                    await context.SaveChangesAsync(ct);

                    if (article.Status == CrawledArticleStatus.Duplicate) { processed++; continue; }
                }

                if (article.Status is CrawledArticleStatus.Rewriting or CrawledArticleStatus.Pending
                    && !string.IsNullOrWhiteSpace(article.DraftContent))
                {
                    processed++;
                    continue;
                }

                if (opt.DraftRewriteEnabled)
                {
                    article.Status = CrawledArticleStatus.Rewriting;
                    article.RewriteAttemptCount++;
                    await context.SaveChangesAsync(ct);
                    await WriteDraftAsync(article, ct);
                }

                article.Status = CrawledArticleStatus.Pending;
                article.UpdatedAt = DateTime.UtcNow;

                // Vân tay ghi SAU khi sạch, để bài trùng không tự làm mồi cho lần chấm sau.
                await UpsertArticleFingerprintAsync(article, ct);
                await context.SaveChangesAsync(ct);
                processed++;
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                logger.LogError(ex, "Xử lý tin {Id} thất bại", article.Id);
                await ForceOutOfFlightAsync(article, ex.Message, ct);
                processed++;
            }
        }

        return processed;
    }

    private void ApplyVerdict(CrawledArticleModel article, DedupVerdict verdict)
    {
        article.DuplicateMethod = verdict.Method;
        article.DuplicateOfId = verdict.DuplicateOfId;
        article.DuplicateTarget = verdict.Target;
        article.DuplicateScore = verdict.Score;
        article.DuplicateReason = Truncate(verdict.Reason, 900);
        article.UpdatedAt = DateTime.UtcNow;

        if (verdict.IsDuplicate)
        {
            article.Status = CrawledArticleStatus.Duplicate;
            logger.LogInformation(
                "Tin {Id} trùng ({Method}, điểm {Score:F2}): {Reason}",
                article.Id, verdict.Method, verdict.Score ?? 0, verdict.Reason);
        }
    }

    /// <summary>
    /// Chốt chặn lặp vô hạn theo đúng luật của PostGenerationWorker: quá số lần thử thì ép
    /// bài ra khỏi trạng thái đang-xử-lý kèm ghi chú, để người kiểm tra.
    /// </summary>
    private async Task ForceOutOfFlightAsync(CrawledArticleModel article, string error, CancellationToken ct)
    {
        var max = Math.Max(1, options.Value.MaxProcessAttempts);
        article.ErrorMessage = Truncate(error, 900);
        article.UpdatedAt = DateTime.UtcNow;

        if (article.DedupAttemptCount >= max || article.RewriteAttemptCount >= max)
        {
            article.Status = CrawledArticleStatus.Pending;
            article.DuplicateReason ??= $"Xử lý lỗi {max} lần — cần người kiểm tra";
            logger.LogWarning("Tin {Id} lỗi {N} lần, đẩy sang Pending để người xem", article.Id, max);
        }
        else
        {
            article.Status = CrawledArticleStatus.New;
        }

        try { await context.SaveChangesAsync(ct); }
        catch (Exception ex) { logger.LogError(ex, "Không ghi được trạng thái lỗi cho tin {Id}", article.Id); }
    }

    /// <summary>Một lượt gọi AI viết bản nháp cho người duyệt đọc. Lỗi thì bỏ qua, không chặn luồng.</summary>
    private async Task WriteDraftAsync(CrawledArticleModel article, CancellationToken ct)
    {
        if (!aiTextService.IsAvailable())
        {
            logger.LogDebug("Bỏ qua xào nháp cho tin {Id}: AI chưa cấu hình", article.Id);
            return;
        }

        var brief = new SourceArticleBrief(
            article.Title, article.Summary, null, article.SourceUrl, article.PublishedAt);

        try
        {
            var result = await aiTextService.GenerateAsync(new AiTextGenerationRequest
            {
                Title = article.Title,
                Objective = options.Value.DefaultObjective,
                Category = article.SourceCategory,
                PromptOverride = SourceArticleHelper.BuildPromptBlock(brief),
            }, ct);

            article.DraftContent = result.Caption;

            var warnings = options.Value.FactGuardEnabled
                ? CrawlContentGuard.FindUnsupportedFacts(result.Caption, $"{article.Title}\n{article.Summary}")
                : [];
            if (warnings.Count > 0)
                logger.LogWarning("Bản nháp tin {Id} có {N} dữ kiện không có trong tư liệu gốc", article.Id, warnings.Count);

            article.DraftExtraJson = JsonSerializer.Serialize(new
            {
                hashtags = result.Hashtags,
                cta = result.Cta,
                imagePrompt = result.ImagePrompt,
                provider = result.Provider,
                model = result.Model,
                factWarnings = warnings,
            });
        }
        catch (AiProviderUnavailableException)
        {
            logger.LogDebug("Bỏ qua xào nháp cho tin {Id}: provider chưa sẵn sàng", article.Id);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // Bản nháp chỉ để người duyệt đọc — hỏng thì vẫn cho tin sang Pending, đừng
            // chặn cả luồng chỉ vì AI trục trặc.
            logger.LogWarning(ex, "Xào nháp tin {Id} thất bại, vẫn cho qua Pending", article.Id);
            article.ErrorMessage = Truncate($"Xào nháp lỗi: {ex.Message}", 900);
        }
    }

    // ── Duyệt → fan-out ─────────────────────────────────────────────────────

    public async Task<ApproveCrawledArticleResult> ApproveAsync(
        Guid articleId, ApproveCrawledArticleRequest request, CancellationToken ct = default)
    {
        var article = await repository.GetByIdAsync(articleId, ct)
            ?? throw new KeyNotFoundException("Không tìm thấy tin");

        // Cho phép duyệt cả tin bị đánh Duplicate — chấm trùng có thể sai, người duyệt lật lại được.
        if (article.Status is not (CrawledArticleStatus.Pending or CrawledArticleStatus.Duplicate))
            throw new ArgumentException($"Tin đang ở trạng thái {article.Status}, không duyệt được");

        var source = await repository.GetSourceAsync(article.CrawlSourceId, ct);
        var channelIds = request.ChannelIds is { Count: > 0 }
            ? request.ChannelIds
            : ContentCrawlRepository.DeserializeGuidList(source?.DefaultChannelIds);
        if (channelIds.Count == 0)
            throw new ArgumentException("Chưa chọn kênh đăng, và nguồn cũng chưa đặt kênh mặc định");

        var pageMap = await pageContextRepository.GetMapByChannelsAsync(channelIds, ct);
        var missing = channelIds
            .Where(id => !PageContextRepository.HasTemplateReady(pageMap.GetValueOrDefault(id)))
            .ToList();
        if (missing.Count > 0 && request.PromptTemplateId is null && options.Value.CrawlPromptTemplateId is null)
            throw new ArgumentException(
                $"{missing.Count} kênh chưa cấu hình PageContext hoặc template mặc định. " +
                "Chọn một danh mục prompt, hoặc cấu hình PageContext cho các kênh đó.");

        var brief = new SourceArticleBrief(
            article.Title, article.Summary, source?.Name, article.SourceUrl, article.PublishedAt);

        var bulk = await postRepository.CreateFanOutQueuedAsync(
            title: article.Title,
            channelIds: channelIds,
            generationFlow: request.GenerationFlow,
            promptTemplateId: request.PromptTemplateId ?? options.Value.CrawlPromptTemplateId,
            pageContextByChannel: pageMap,
            objective: request.Objective ?? options.Value.DefaultObjective,
            categoryId: source?.CategoryId,
            sourceArticle: brief,
            ct: ct);

        var scheduledTimes = new List<DateTime>();
        var autoSchedule = request.AutoSchedule ?? options.Value.AutoScheduleOnApprove;
        if (autoSchedule && bulk.PostIds.Count > 0)
            scheduledTimes = await ApplyPendingScheduleAsync(bulk.PostIds, request.StartAtUtc, ct);

        article.Status = CrawledArticleStatus.Approved;
        article.ResultBatchId = bulk.BatchId;
        article.ResultPostCount = bulk.Created;
        article.ReviewedBy = userContext.GetCurrentUserName();
        article.ReviewedAt = DateTime.UtcNow;
        article.UpdatedAt = DateTime.UtcNow;
        await UpsertArticleFingerprintAsync(article, ct);
        await context.SaveChangesAsync(ct);

        logger.LogInformation(
            "Duyệt tin {Id} → {Count} bài trên {Channels} kênh (batch {BatchId}, rải lịch: {Sched})",
            article.Id, bulk.Created, channelIds.Count, bulk.BatchId, autoSchedule);

        return new ApproveCrawledArticleResult
        {
            ArticleId = article.Id,
            BatchId = bulk.BatchId,
            Created = bulk.Created,
            PostIds = bulk.PostIds,
            ScheduledTimesUtc = scheduledTimes,
            Message = autoSchedule
                ? $"Đã tạo {bulk.Created} bài và rải lịch theo khung giờ vàng"
                : $"Đã tạo {bulk.Created} bài, chờ sinh nội dung rồi lên lịch thủ công",
        };
    }

    /// <summary>
    /// Ghi pendingSchedule vào ExtraJson từng post. PostGenerationWorker sẽ đọc và gọi
    /// ScheduleAsync SAU khi sinh nội dung xong — nhờ vậy không cần một dòng code lên lịch nào,
    /// và bài chỉ được lên lịch khi thực sự có nội dung.
    /// </summary>
    private async Task<List<DateTime>> ApplyPendingScheduleAsync(
        List<Guid> postIds, DateTime? startAtUtc, CancellationToken ct)
    {
        var opt = options.Value;
        var times = ScheduleSlotHelper.ComputeSlotTimesUtc(
            startAtUtc ?? DateTime.UtcNow,
            opt.ScheduleTimeSlots,
            opt.ScheduleTimezone,
            postIds.Count,
            opt.ScheduleJitterMinutes);

        var tz = PendingScheduleHelper.ResolveTimeZone(opt.ScheduleTimezone);
        var posts = await context.Set<PostModel>()
            .Where(p => !p.IsDeleted && postIds.Contains(p.Id))
            .ToListAsync(ct);

        for (var i = 0; i < posts.Count && i < times.Count; i++)
        {
            var local = TimeZoneInfo.ConvertTimeFromUtc(times[i], tz);
            posts[i].ExtraJson = MergeExtraJson(posts[i].ExtraJson, "pendingSchedule", new Dictionary<string, object?>
            {
                // "yyyy-MM-dd HH:mm:ss" là format đầu tiên trong danh sách của
                // PendingScheduleHelper.TryParseLocalToUtc — đừng đổi.
                ["atLocal"] = local.ToString("yyyy-MM-dd HH:mm:ss"),
                ["timezone"] = opt.ScheduleTimezone,
            });
            posts[i].UpdatedAt = DateTime.UtcNow;
        }

        await context.SaveChangesAsync(ct);
        return times;
    }

    public async Task<CrawledArticleModel> RejectAsync(Guid articleId, string? reason, CancellationToken ct = default)
    {
        var article = await repository.GetByIdAsync(articleId, ct)
            ?? throw new KeyNotFoundException("Không tìm thấy tin");

        article.Status = CrawledArticleStatus.Rejected;
        article.RejectReason = Truncate(reason, 900);
        article.ReviewedBy = userContext.GetCurrentUserName();
        article.ReviewedAt = DateTime.UtcNow;
        article.UpdatedAt = DateTime.UtcNow;
        await context.SaveChangesAsync(ct);
        return article;
    }

    /// <summary>Người duyệt lật lại phán quyết trùng — chấm trùng sai là chuyện bình thường.</summary>
    public async Task<CrawledArticleModel> MarkNotDuplicateAsync(Guid articleId, CancellationToken ct = default)
    {
        var article = await repository.GetByIdAsync(articleId, ct)
            ?? throw new KeyNotFoundException("Không tìm thấy tin");

        article.Status = CrawledArticleStatus.Pending;
        article.DuplicateMethod = DedupMethod.Manual;
        article.DuplicateOfId = null;
        article.DuplicateTarget = DuplicateTarget.None;
        article.DuplicateScore = null;
        article.DuplicateReason = "Người duyệt xác nhận KHÔNG trùng";
        article.UpdatedAt = DateTime.UtcNow;
        await UpsertArticleFingerprintAsync(article, ct);
        await context.SaveChangesAsync(ct);
        return article;
    }

    public async Task<CrawledArticleModel> RededupAsync(Guid articleId, CancellationToken ct = default)
    {
        var article = await repository.GetByIdAsync(articleId, ct)
            ?? throw new KeyNotFoundException("Không tìm thấy tin");

        var fingerprint = SimHashCalculator.Compute(article.Title, article.Summary);
        article.ContentHash = fingerprint.ContentHash;
        article.SimHash = fingerprint.SimHash;
        article.Status = CrawledArticleStatus.New;
        article.DedupAttemptCount = 0;
        article.DuplicateMethod = DedupMethod.None;
        article.DuplicateOfId = null;
        article.DuplicateTarget = DuplicateTarget.None;
        article.DuplicateScore = null;
        article.DuplicateReason = null;
        article.UpdatedAt = DateTime.UtcNow;
        await context.SaveChangesAsync(ct);
        return article;
    }

    // ── Vân tay ─────────────────────────────────────────────────────────────

    private async Task UpsertArticleFingerprintAsync(CrawledArticleModel article, CancellationToken ct)
    {
        var fingerprint = SimHashCalculator.Compute(article.Title, article.Summary);
        var existing = await context.Set<ContentFingerprintModel>()
            .FirstOrDefaultAsync(x => !x.IsDeleted
                && x.OwnerType == FingerprintOwner.CrawledArticle
                && x.OwnerId == article.Id, ct);

        if (existing is null)
        {
            context.Set<ContentFingerprintModel>().Add(new ContentFingerprintModel
            {
                Id = Guid.NewGuid(),
                OwnerType = FingerprintOwner.CrawledArticle,
                OwnerId = article.Id,
                ContentHash = fingerprint.ContentHash,
                SimHash = fingerprint.SimHash,
                Band0 = fingerprint.Band0,
                Band1 = fingerprint.Band1,
                Band2 = fingerprint.Band2,
                Band3 = fingerprint.Band3,
                TitleSnippet = Truncate(article.Title, 500),
                ContentAt = article.PublishedAt ?? article.FetchedAt,
                CreatedAt = DateTime.UtcNow,
            });
        }
        else
        {
            existing.ContentHash = fingerprint.ContentHash;
            existing.SimHash = fingerprint.SimHash;
            existing.Band0 = fingerprint.Band0;
            existing.Band1 = fingerprint.Band1;
            existing.Band2 = fingerprint.Band2;
            existing.Band3 = fingerprint.Band3;
            existing.TitleSnippet = Truncate(article.Title, 500);
            existing.ContentAt = article.PublishedAt ?? article.FetchedAt;
            existing.UpdatedAt = DateTime.UtcNow;
        }
    }

    /// <summary>
    /// Ghi vân tay cho bài ĐÃ ĐĂNG, để tin cào mới được so cả với thứ mình đã đăng rồi.
    ///
    /// Lấy vân tay từ Title + sourceArticle trong ExtraJson, KHÔNG lấy từ Content: Content là
    /// caption đã xào, đầy emoji/hashtag/CTA nên hồ sơ token khác hẳn tóm tắt báo, so sẽ luôn hụt.
    /// </summary>
    public async Task<int> SyncPublishedPostFingerprintsAsync(int max = 200, CancellationToken ct = default)
    {
        var existingIds = await context.Set<ContentFingerprintModel>()
            .Where(x => !x.IsDeleted && x.OwnerType == FingerprintOwner.Post)
            .Select(x => x.OwnerId)
            .ToListAsync(ct);
        var known = existingIds.ToHashSet();

        var since = DateTime.UtcNow.AddDays(-Math.Max(1, options.Value.Dedup.LookbackDays));
        var posts = await context.Set<PostModel>()
            .Where(p => !p.IsDeleted && p.Status == PostStatus.Published && p.PublishedAt >= since)
            .OrderByDescending(p => p.PublishedAt)
            .Take(Math.Clamp(max, 1, 1000))
            .ToListAsync(ct);

        var added = 0;
        foreach (var post in posts.Where(p => !known.Contains(p.Id)))
        {
            var brief = SourceArticleHelper.TryRead(post.ExtraJson);
            var title = brief?.Title ?? post.Title;
            var summary = brief?.Summary;
            var fingerprint = SimHashCalculator.Compute(title, summary);

            context.Set<ContentFingerprintModel>().Add(new ContentFingerprintModel
            {
                Id = Guid.NewGuid(),
                OwnerType = FingerprintOwner.Post,
                OwnerId = post.Id,
                ContentHash = fingerprint.ContentHash,
                SimHash = fingerprint.SimHash,
                Band0 = fingerprint.Band0,
                Band1 = fingerprint.Band1,
                Band2 = fingerprint.Band2,
                Band3 = fingerprint.Band3,
                TitleSnippet = Truncate(title, 500),
                ContentAt = post.PublishedAt ?? post.CreatedAt,
                SocialChannelId = post.SocialChannelId,
                CreatedAt = DateTime.UtcNow,
            });
            added++;
        }

        if (added > 0)
        {
            await context.SaveChangesAsync(ct);
            logger.LogInformation("Đồng bộ {Count} vân tay bài đã đăng", added);
        }
        return added;
    }

    // ── Helper ──────────────────────────────────────────────────────────────

    private static bool MatchesAny(RssItem item, List<string> keywords)
    {
        if (keywords.Count == 0) return false;
        var haystack = VietnameseTextHelper.NormalizeForHash($"{item.Title} {item.Summary}");
        return keywords.Any(k =>
            !string.IsNullOrWhiteSpace(k)
            && haystack.Contains(VietnameseTextHelper.NormalizeForHash(k), StringComparison.Ordinal));
    }

    /// <summary>Bỏ query/fragment và dấu / cuối — hai feed đăng lại cùng bài thường chỉ khác utm.</summary>
    private static string? NormalizeUrl(string? url)
    {
        if (string.IsNullOrWhiteSpace(url)) return null;
        if (!Uri.TryCreate(url.Trim(), UriKind.Absolute, out var uri)) return url.Trim().ToLowerInvariant();
        return $"{uri.Scheme}://{uri.Host}{uri.AbsolutePath.TrimEnd('/')}".ToLowerInvariant();
    }

    /// <summary>
    /// Merge một khoá vào ExtraJson, GIỮ các khoá khác. Ghi đè cả object sẽ phá pendingSchedule
    /// hoặc sourceArticle trong im lặng.
    /// </summary>
    private static string MergeExtraJson(string? extraJson, string key, object value)
    {
        var root = new Dictionary<string, object?>();
        if (!string.IsNullOrWhiteSpace(extraJson))
        {
            try
            {
                using var doc = JsonDocument.Parse(extraJson);
                if (doc.RootElement.ValueKind == JsonValueKind.Object)
                    foreach (var prop in doc.RootElement.EnumerateObject())
                        root[prop.Name] = prop.Value.Clone();
            }
            catch (JsonException) { /* ExtraJson hỏng thì dựng lại từ đầu */ }
        }
        root[key] = value;
        return JsonSerializer.Serialize(root);
    }

    private static string? Truncate(string? value, int max)
    {
        if (string.IsNullOrWhiteSpace(value)) return value;
        var text = value.Trim();
        return text.Length <= max ? text : text[..max];
    }
}
