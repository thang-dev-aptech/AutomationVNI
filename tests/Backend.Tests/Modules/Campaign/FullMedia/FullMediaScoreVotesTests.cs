using Backend.Data;
using Backend.Modules.Campaign;
using Backend.Modules.Campaign.Enums;
using Backend.Modules.Campaign.FullMedia;
using Backend.Modules.Campaign.FullMedia.Scoring;
using Backend.Modules.MediaAsset;
using Backend.Modules.MediaFolder;
using Backend.Modules.PageContext;
using Backend.Modules.Post;
using Backend.Modules.Post.Enums;
using Backend.Modules.SocialChannel;
using Backend.Modules.SocialChannel.Enums;
using Backend.Shared.Repositories;
using Backend.Tests.Shared;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Xunit;

namespace Backend.Tests.Modules.Campaign.FullMedia;

/// <summary>
/// R-047: kiểm phiếu lưu cùng điểm; chấm lại chỉ dùng lại điểm dừng sớm (cận dưới) khi cận trên
/// vẫn không đạt ngưỡng HIỆN TẠI.
/// </summary>
public sealed class FullMediaScoreVotesTests : IAsyncLifetime
{
    private readonly SqliteConnection _connection;
    private readonly DbContextOptions<AppDbContext> _options;
    private readonly AdjustableTimeProvider _clock =
        new(new DateTimeOffset(2026, 10, 7, 3, 0, 0, TimeSpan.Zero));
    private readonly List<ServiceProvider> _providers = [];

    public FullMediaScoreVotesTests()
    {
        _connection = SqliteTestDatabase.OpenInMemory();
        _options = new DbContextOptionsBuilder<AppDbContext>().UseSqlite(_connection).Options;
        using var db = new AppDbContext(_options);
        db.Database.EnsureCreated();
    }

    public Task InitializeAsync() => Task.CompletedTask;
    public async Task DisposeAsync()
    {
        foreach (var p in _providers) await p.DisposeAsync();
        await _connection.DisposeAsync();
    }

    // yes=0 cast=1 total=3 → điểm cận dưới 0, cận trên 2/3 (≈0.667).
    private static readonly PageFitVoteTally EarlyExitLow = new(0, 1, 3);
    private static readonly PageFitVoteTally Full = new(3, 3, 3);

    /// <summary>Lượt đầu ở ngưỡng 0.94: mọi page dừng sớm (0/1/3), Rejected. Seed Vision cache, ép rescore.</summary>
    private async Task<(Guid CampaignId, Guid MediaId, List<Guid> Pages, FullMediaPipelineService Pipeline, FakeScorer Scorer)>
        RejectedByEarlyExitAsync(int pageCount = 1, double minFit = 0.94, PageFitVoteTally? tally = null, double score = 0.0)
    {
        var (campaignId, folderId, pages) = await SeedCampaignAsync(pageCount);
        var mediaId = await SeedMediaAsync(folderId);
        var scorer = new FakeScorer();
        scorer.PassScores[1] = pages.ToDictionary(p => p, _ => score);
        scorer.PassVotes[1] = pages.ToDictionary(p => p, _ => tally ?? EarlyExitLow);
        var pipeline = CreatePipeline(new FakeVision(), scorer, o => o.MinFitScore = minFit);
        await pipeline.ProcessImageAsync(campaignId, mediaId);
        await SeedVisionProfilesAsync(mediaId);
        await ForceRescoreAsync();
        scorer.ResetTracking();
        return (campaignId, mediaId, pages, pipeline, scorer);
    }

    private async Task ForceRescoreAsync()
    {
        await using var db = new AppDbContext(_options);
        var eval = await db.CampaignMediaEvaluations.SingleAsync();
        eval.InputHash = "stale-hash-force-rescore";
        await db.SaveChangesAsync();
    }

    private FullMediaPipelineService NewPipelineWithThreshold(FakeScorer scorer, double minFit)
        => CreatePipeline(new FakeVision(), scorer, o => o.MinFitScore = minFit);

    // --- Persist ---

    [Fact]
    public async Task PersistScores_WritesVotesColumns_AndUpdatesExistingRow()
    {
        var (campaignId, mediaId, pages, pipeline, scorer) = await RejectedByEarlyExitAsync();
        await using (var db = new AppDbContext(_options))
        {
            var row = await db.CampaignMediaScores.SingleAsync(s => s.Pass == 1);
            Assert.Equal((0, 1, 3), (row.VotesYes, row.VotesCast, row.VotesTotal));
        }

        // Hạ ngưỡng → chấm lại, cùng dòng được cập nhật với kiểm phiếu mới (không lẫn dữ liệu cũ).
        scorer.PassScores[1] = pages.ToDictionary(p => p, _ => 1.0 / 3.0);
        scorer.PassVotes[1] = pages.ToDictionary(p => p, _ => new PageFitVoteTally(1, 3, 3));
        var lowered = NewPipelineWithThreshold(scorer, 0.66);
        await lowered.ProcessDueAsync();
        await using (var db = new AppDbContext(_options))
        {
            var row = await db.CampaignMediaScores.SingleAsync(s => s.Pass == 1);
            Assert.Equal((1, 3, 3), (row.VotesYes, row.VotesCast, row.VotesTotal));
        }

        // Bộ chấm không báo kiểm phiếu (Logprobs) → dòng cũ phải về NULL = điểm đầy đủ.
        await using (var db = new AppDbContext(_options))
        {
            var ctx = await db.PageContexts.FirstAsync(p => p.SocialChannelId == pages[0]);
            ctx.Topics = "Đổi hồ sơ để chấm lại";
            await db.SaveChangesAsync();
        }

        await ForceRescoreAsync();
        scorer.PassVotes.Clear();
        scorer.PassScores[1] = pages.ToDictionary(p => p, _ => 0.5);
        await lowered.ProcessDueAsync();
        await using var db2 = new AppDbContext(_options);
        var rows = await db2.CampaignMediaScores.Where(s => s.Pass == 1).ToListAsync();
        var final = Assert.Single(rows);
        Assert.Equal((null, null, null), (final.VotesYes, final.VotesCast, final.VotesTotal));
    }

    // --- Reuse qua pipeline ---

    [Fact]
    public async Task Reuse_EarlyExitScore_StillBelowThreshold_Reused()
    {
        var (_, _, _, pipeline, scorer) = await RejectedByEarlyExitAsync();

        await pipeline.ProcessDueAsync();

        Assert.Equal(0, scorer.Calls); // cận trên 0.667 < 0.94 → dùng lại, không request chấm
        Assert.Empty(scorer.ChannelsScored);
    }

    [Fact]
    public async Task Reuse_EarlyExitScore_ThresholdLowered_Rescored()
    {
        var (_, _, pages, _, scorer) = await RejectedByEarlyExitAsync();
        // Chấm lại vẫn thấp (full vote 1/3) — chỉ kiểm có request chấm cho page.
        scorer.PassScores[1] = pages.ToDictionary(p => p, _ => 1.0 / 3.0);
        scorer.PassVotes[1] = pages.ToDictionary(p => p, _ => new PageFitVoteTally(1, 3, 3));
        var lowered = NewPipelineWithThreshold(scorer, 0.66);

        await lowered.ProcessDueAsync();

        Assert.Contains(pages[0], scorer.ChannelsScored);
        Assert.Equal(1, scorer.Calls);
    }

    [Fact]
    public async Task Reuse_ThresholdRaised_StillReusesEarlyExit()
    {
        // Dừng sớm hợp lệ ở ngưỡng 0.7: yes=1 cast=2 total=3 → cận dưới 1/3, cận trên 2/3 < 0.7.
        var (_, _, _, _, scorer) = await RejectedByEarlyExitAsync(minFit: 0.7, tally: new PageFitVoteTally(1, 2, 3), score: 1.0 / 3.0);
        var raised = NewPipelineWithThreshold(scorer, 0.94);

        await raised.ProcessDueAsync();

        Assert.Equal(0, scorer.Calls);
    }

    [Fact]
    public async Task Reuse_ProfileHashChanged_Rescored()
    {
        var (_, _, pages, pipeline, scorer) = await RejectedByEarlyExitAsync();
        await using (var db = new AppDbContext(_options))
        {
            var ctx = await db.PageContexts.FirstAsync(p => p.SocialChannelId == pages[0]);
            ctx.Topics = "Đổi chủ đề → đổi ProfileHash";
            await db.SaveChangesAsync();
        }

        await pipeline.ProcessDueAsync();

        Assert.Contains(pages[0], scorer.ChannelsScored);
    }

    [Fact]
    public async Task Rescore_ThresholdUnchanged_NoExtraScorerCalls()
    {
        // Hồi quy: hai page — một điểm đầy đủ (2/3 full), một dừng sớm; ngưỡng không đổi → không chấm thừa.
        var (campaignId, folderId, pages) = await SeedCampaignAsync(2);
        var mediaId = await SeedMediaAsync(folderId);
        var scorer = new FakeScorer();
        scorer.PassScores[1] = new Dictionary<Guid, double> { [pages[0]] = 2.0 / 3.0, [pages[1]] = 0.0 };
        scorer.PassVotes[1] = new Dictionary<Guid, PageFitVoteTally>
        {
            [pages[0]] = new(2, 3, 3),
            [pages[1]] = EarlyExitLow,
        };
        var pipeline = CreatePipeline(new FakeVision(), scorer);
        await pipeline.ProcessImageAsync(campaignId, mediaId);
        await SeedVisionProfilesAsync(mediaId);
        await ForceRescoreAsync();
        scorer.ResetTracking();

        await pipeline.ProcessDueAsync();

        Assert.Equal(0, scorer.Calls);
    }

    [Fact]
    public async Task Rescore_ThresholdLowered_PageNowAccepted_CreatesPost()
    {
        var (campaignId, mediaId, pages, _, scorer) = await RejectedByEarlyExitAsync(
            minFit: 0.94, tally: new PageFitVoteTally(1, 2, 3), score: 1.0 / 3.0);
        scorer.PassScores[1] = pages.ToDictionary(p => p, _ => 1.0);
        scorer.PassScores[2] = pages.ToDictionary(p => p, _ => 1.0);
        scorer.PassVotes[1] = pages.ToDictionary(p => p, _ => Full);
        scorer.PassVotes[2] = pages.ToDictionary(p => p, _ => Full);
        var lowered = NewPipelineWithThreshold(scorer, 0.66);

        await lowered.ProcessDueAsync();

        await using var db = new AppDbContext(_options);
        Assert.Equal(CampaignMediaEvaluationStatus.Accepted,
            (await db.CampaignMediaEvaluations.SingleAsync()).Status);
        Assert.Equal(1, await db.Posts.CountAsync(p => p.CampaignId == campaignId
            && db.PostMedias.Any(m => m.PostId == p.Id && m.MediaId == mediaId)));
        Assert.Equal(2, scorer.Calls); // lượt 1 chấm lại (cận trên 0.667 ≥ 0.66) + lượt 2
    }

    // --- Passed đã lưu không quyết định nhận/loại ---

    [Fact]
    public async Task Passed_NotTrustedFromStoredRow()
    {
        var (campaignId, folderId, pages) = await SeedCampaignAsync(1, times: ["15:00", "16:00"]);
        var mediaId = await SeedMediaAsync(folderId);
        await OccupyAllSlotsAsync(campaignId, pages[0], times: ["15:00", "16:00"]);
        var pipeline = CreatePipeline(new FakeVision(), AcceptAll(pages));
        await pipeline.ProcessImageAsync(campaignId, mediaId);

        await using (var db = new AppDbContext(_options))
        {
            // Điểm vẫn đạt ngưỡng hiện tại nhưng cờ Passed lưu (ghi ở ngưỡng cũ) là false.
            foreach (var s in db.CampaignMediaScores)
                s.Passed = false;
            await db.SaveChangesAsync();
        }

        await ClearOccupyingPostsAsync(campaignId);
        await ReadyPendingRetryAsync();
        await pipeline.ProcessImageAsync(campaignId, mediaId);

        await using var db2 = new AppDbContext(_options);
        Assert.Equal(1, await db2.Posts.CountAsync(p =>
            db2.PostMedias.Any(m => m.PostId == p.Id && m.MediaId == mediaId)));
    }

    // --- Migration ---

    [Fact]
    public void Migration_AddFullMediaScoreVotes_ColumnsNullable_ExistingRowsUnchanged()
    {
        var dir = Path.GetFullPath(Path.Combine(
            AppContext.BaseDirectory, "..", "..", "..", "..", "..", "backend", "Migrations"));
        Assert.Contains(Directory.GetFiles(dir, "*AddFullMediaScoreVotes.cs"),
            f => !f.EndsWith(".Designer.cs", StringComparison.Ordinal));
        Assert.Contains(Directory.GetFiles(dir, "*AddFullMediaScoreVotes.Designer.cs"), _ => true);

        // Schema thật (EnsureCreated từ model): ba cột nullable; dòng không khai báo kiểm phiếu đọc lại NULL.
        using var cmd = _connection.CreateCommand();
        cmd.CommandText = "SELECT name, \"notnull\" FROM pragma_table_info('CampaignMediaScores') WHERE name LIKE 'Votes%'";
        using var reader = cmd.ExecuteReader();
        var cols = new Dictionary<string, long>();
        while (reader.Read()) cols[reader.GetString(0)] = reader.GetInt64(1);
        Assert.Equal(["VotesCast", "VotesTotal", "VotesYes"], cols.Keys.OrderBy(x => x).ToArray());
        Assert.All(cols.Values, nn => Assert.Equal(0, nn));

        var migration = File.ReadAllText(Directory.GetFiles(dir, "*AddFullMediaScoreVotes.cs").Single());
        Assert.Equal(3, System.Text.RegularExpressions.Regex.Matches(migration, "nullable: true").Count);
        Assert.DoesNotContain("defaultValue", migration);
    }

    // --- helpers ---

    private async Task AssertPendingReasonAsync(string reason)
    {
        await using var db = new AppDbContext(_options);
        var eval = await db.CampaignMediaEvaluations.SingleAsync();
        Assert.Equal(CampaignMediaEvaluationStatus.Pending, eval.Status);
        Assert.Equal(reason, eval.ReasonCode);
    }

    private async Task ReadyPendingRetryAsync()
    {
        _clock.Advance(TimeSpan.FromHours(2));
        await using var db = new AppDbContext(_options);
        var eval = await db.CampaignMediaEvaluations.SingleOrDefaultAsync();
        if (eval is null) return;
        eval.NextAttemptAt = null;
        if (eval.Status == CampaignMediaEvaluationStatus.Scoring
            || eval.Status == CampaignMediaEvaluationStatus.Verifying)
            eval.Status = CampaignMediaEvaluationStatus.Pending;
        await db.SaveChangesAsync();
    }

    private FullMediaPipelineService CreatePipeline(
        FakeVision vision, FakeScorer scorer, Action<FullMediaPipelineOptions>? configure = null)
    {
        var opts = new FullMediaPipelineOptions
        {
            Enabled = true,
            MinFitScore = 0.94,
            MaxImagesPerTick = 10,
            MaxImagesPerDay = 1000,
            MaxDailyCostUsd = 1000,
            MaxRescores = 3,
            MaxPendingAttempts = 5,
            StageTimeoutSeconds = 5,
            CircuitBreakerFailures = 5,
            CircuitBreakerCooldownSeconds = 120,
        };
        configure?.Invoke(opts);
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<TimeProvider>(_clock);
        services.AddSingleton(Options.Create(opts));
        services.AddSingleton(new FullMediaCircuitBreaker(_clock));
        services.AddScoped(_ => new AppDbContext(_options));
        services.AddScoped<CampaignGenerationService>();
        services.AddScoped<RecycleSourcePicker>();
        services.AddScoped<MediaFolderRepository>();
        services.AddScoped<IUserContext, StubUserContext>();
        services.AddSingleton<IMediaVisionDescriber>(vision);
        services.AddSingleton<IPageFitScorer>(scorer);
        services.AddScoped<FullMediaPipelineService>();
        var sp = services.BuildServiceProvider();
        _providers.Add(sp);
        var pipeline = sp.GetRequiredService<FullMediaPipelineService>();
        pipeline.CaptionFactory ??= (_, page, _) => Task.FromResult($"ok-{page}");
        pipeline.Random = new Random(42);
        return pipeline;
    }

    private async Task<(Guid CampaignId, Guid FolderId, List<Guid> Pages)> SeedCampaignAsync(
        int pageCount, string[]? times = null)
    {
        times ??= ["15:00"];
        await using var db = new AppDbContext(_options);
        var pages = new List<Guid>();
        for (var i = 0; i < pageCount; i++)
        {
            var ch = Guid.NewGuid();
            pages.Add(ch);
            db.SocialChannels.Add(new SocialChannelModel
            {
                Id = ch,
                PageName = $"P{i}",
                ExternalPageId = $"e{i}",
                Platform = SocialPlatform.Facebook,
                CreatedAt = DateTime.UtcNow,
            });
            db.PageContexts.Add(new PageContextModel
            {
                Id = Guid.NewGuid(),
                SocialChannelId = ch,
                BrandName = "B",
                ToneOfVoice = "tone",
                Topics = $"topics-{i}",
                CreatedAt = DateTime.UtcNow,
            });
        }

        var folderId = Guid.NewGuid();
        db.MediaFolders.Add(new MediaFolderModel { Id = folderId, Name = "src", CreatedAt = DateTime.UtcNow });
        var campaignId = Guid.NewGuid();
        db.Campaigns.Add(new CampaignModel
        {
            Id = campaignId,
            Name = "FM",
            Mode = CampaignMode.FullMedia,
            SourceFolderId = folderId,
            Status = CampaignStatus.Running,
            ScheduleMode = CampaignScheduleMode.AllWeek,
            PublishTimesJson = System.Text.Json.JsonSerializer.Serialize(times),
            WeekdaysJson = "[]",
            JitterMinutes = 0,
            StartDate = new DateTime(2026, 10, 1, 0, 0, 0, DateTimeKind.Utc),
            CreatedByUserId = Guid.NewGuid(),
            CreatedAt = DateTime.UtcNow,
        });
        foreach (var p in pages)
        {
            db.Set<CampaignChannelModel>().Add(new CampaignChannelModel
            {
                Id = Guid.NewGuid(),
                CampaignId = campaignId,
                SocialChannelId = p,
                CreatedAt = DateTime.UtcNow,
            });
        }

        await db.SaveChangesAsync();
        return (campaignId, folderId, pages);
    }

    private async Task<Guid> SeedMediaAsync(Guid folderId)
    {
        await using var db = new AppDbContext(_options);
        var id = Guid.NewGuid();
        db.MediaAssets.Add(new MediaAssetModel
        {
            Id = id,
            FolderId = folderId,
            MimeType = "image/jpeg",
            StoragePath = $"{id}.jpg",
            FileName = $"{id}.jpg",
            CreatedAt = DateTime.UtcNow,
        });
        await db.SaveChangesAsync();
        return id;
    }

    private async Task SeedVisionProfilesAsync(Guid mediaId)
    {
        const string json =
            """{"topic":"t","audience":"a","tone":"tone","setting":"set","textInImage":"","keywords":["k"],"confidence":0.9,"unclear":false}""";
        await using var db = new AppDbContext(_options);
        db.MediaVisionProfiles.Add(new MediaVisionProfileModel
        {
            Id = Guid.NewGuid(),
            MediaAssetId = mediaId,
            FileHash = "fh",
            Pass = VisionPass.A,
            DescriptionJson = json,
            Confidence = 0.9,
            Model = "model-a",
            CreatedAt = DateTime.UtcNow,
        });
        db.MediaVisionProfiles.Add(new MediaVisionProfileModel
        {
            Id = Guid.NewGuid(),
            MediaAssetId = mediaId,
            FileHash = "fh",
            Pass = VisionPass.B,
            DescriptionJson = json,
            Confidence = 0.9,
            Model = "model-b",
            CreatedAt = DateTime.UtcNow,
        });
        await db.SaveChangesAsync();
    }

    private async Task OccupyAllSlotsAsync(Guid campaignId, Guid channelId, string[] times)
    {
        await using var db = new AppDbContext(_options);
        var campaign = await db.Campaigns.FirstAsync(c => c.Id == campaignId);
        var slots = CampaignGenerationService.BuildSlots(
            campaign, times.ToList(), [], _clock.GetUtcNow().UtcDateTime);
        foreach (var slot in slots)
        {
            db.Posts.Add(new PostModel
            {
                Id = Guid.NewGuid(),
                Title = "fill",
                SocialChannelId = channelId,
                CampaignId = campaignId,
                CampaignSlotAt = slot,
                Status = PostStatus.Scheduled,
                GenerationFlow = GenerationFlow.Recycle,
                UserId = Guid.NewGuid(),
                CreatedAt = DateTime.UtcNow,
            });
        }

        await db.SaveChangesAsync();
    }

    private async Task ClearOccupyingPostsAsync(Guid campaignId)
    {
        await using var db = new AppDbContext(_options);
        var posts = await db.Posts.Where(p => p.CampaignId == campaignId).ToListAsync();
        db.Posts.RemoveRange(posts);
        await db.SaveChangesAsync();
    }

    private static FakeScorer AcceptAll(IReadOnlyList<Guid> pages)
    {
        var s = new FakeScorer();
        s.PassScores[1] = pages.ToDictionary(p => p, _ => 0.95);
        s.PassScores[2] = pages.ToDictionary(p => p, _ => 0.96);
        return s;
    }

    private sealed class FakeVision : IMediaVisionDescriber
    {
        public int CallsA;
        public int CallsB;

        public Task<VisionDescription> DescribeAsync(Guid mediaAssetId, VisionPass pass, CancellationToken ct = default)
            => Task.FromResult(Desc(pass));

        public Task<VisionOutcome> GetUsableAsync(Guid mediaAssetId, VisionPass pass, CancellationToken ct = default)
        {
            if (pass == VisionPass.A) Interlocked.Increment(ref CallsA);
            else Interlocked.Increment(ref CallsB);
            return Task.FromResult(new VisionOutcome(VisionUsability.Usable, Desc(pass)));
        }

        private static VisionDescription Desc(VisionPass pass)
            => new("t", "a", "tone", "set", "", ["k"], 0.9, false,
                pass == VisionPass.A ? "model-a" : "model-b");
    }

    private sealed class FakeScorer : IPageFitScorer
    {
        public int Calls;
        public List<Guid> ChannelsScored { get; } = [];
        public Dictionary<int, Dictionary<Guid, double>> PassScores { get; } = new();
        /// <summary>Kiểm phiếu theo lượt; không khai báo = Votes null (điểm đầy đủ).</summary>
        public Dictionary<int, Dictionary<Guid, PageFitVoteTally>> PassVotes { get; } = new();
        private int _seq;

        public void ResetTracking()
        {
            Calls = 0;
            ChannelsScored.Clear();
            Interlocked.Exchange(ref _seq, 0);
        }

        public Task<PageFitResult> ScoreAsync(
            string imageStateText, IReadOnlyList<PageFitCandidate> pages, CancellationToken ct = default)
        {
            Interlocked.Increment(ref Calls);
            foreach (var p in pages) ChannelsScored.Add(p.ChannelId);
            var pass = ((Interlocked.Increment(ref _seq) - 1) % 2) + 1;
            if (!PassScores.TryGetValue(pass, out var map))
                map = pages.ToDictionary(p => p.ChannelId, _ => 0.5);
            var scores = pages.ToDictionary(
                p => p.ChannelId,
                p => map.TryGetValue(p.ChannelId, out var s) ? s : 0.0);
            Dictionary<Guid, PageFitVoteTally>? votes = null;
            if (PassVotes.TryGetValue(pass, out var vmap))
            {
                votes = pages.Where(p => vmap.ContainsKey(p.ChannelId))
                    .ToDictionary(p => p.ChannelId, p => vmap[p.ChannelId]);
            }

            return Task.FromResult(new PageFitResult(scores, 0.01m, pages.Count, votes));
        }
    }

    private sealed class StubUserContext : IUserContext
    {
        public Guid? GetCurrentUserId() => Guid.Empty;
        public string? GetCurrentUserName() => "t";
        public IReadOnlyList<string> GetCurrentUserRoles() => ["Admin"];
    }

    private sealed class AdjustableTimeProvider(DateTimeOffset utcNow) : TimeProvider
    {
        private DateTimeOffset _utcNow = utcNow;
        public override DateTimeOffset GetUtcNow() => _utcNow;
        public void Advance(TimeSpan by) => _utcNow += by;
    }
}
