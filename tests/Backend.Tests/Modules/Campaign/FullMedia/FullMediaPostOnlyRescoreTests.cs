using Backend.Data;
using Backend.Modules.Campaign;
using Backend.Modules.Campaign.Enums;
using Backend.Modules.Campaign.FullMedia;
using Backend.Modules.Campaign.FullMedia.Scoring;
using Backend.Modules.MediaAsset;
using Backend.Modules.MediaFolder;
using Backend.Modules.Notification;
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
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;

namespace Backend.Tests.Modules.Campaign.FullMedia;

/// <summary>R-047 review FULLMEDIA-01 f9e9c8a ghi chú 2: chờ khe không bỏ âm thầm page dừng sớm chưa tin được.</summary>
public sealed class FullMediaPostOnlyRescoreTests : IAsyncLifetime
{
    private readonly SqliteConnection _connection;
    private readonly DbContextOptions<AppDbContext> _options;
    private readonly AdjustableTimeProvider _clock =
        new(new DateTimeOffset(2026, 10, 7, 3, 0, 0, TimeSpan.Zero));
    private readonly List<ServiceProvider> _providers = [];

    public FullMediaPostOnlyRescoreTests()
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

    private sealed record Scenario(Guid CampaignId, Guid MediaId, List<Guid> Pages, FakeVision Vision, FakeScorer Scorer);

    // P0 Valid (điểm đầy đủ cả 2 lượt), P1 dừng sớm lượt 1 (0,1,3, điểm 0) ở ngưỡng 0.94 -> chờ khe.
    private async Task<Scenario> SetupWaitingAsync()
    {
        var (campaignId, folderId, pages) = await SeedCampaignAsync(2, times: ["15:00", "16:00"]);
        var mediaId = await SeedMediaAsync(folderId);
        await OccupyAllSlotsAsync(campaignId, pages[0], times: ["15:00", "16:00"]);
        await OccupyAllSlotsAsync(campaignId, pages[1], times: ["15:00", "16:00"]);
        var vision = new FakeVision();
        var scorer = new FakeScorer();
        scorer.PassScores[1] = new Dictionary<Guid, double> { [pages[0]] = 0.95, [pages[1]] = 0.0 };
        scorer.PassVotes[1] = new Dictionary<Guid, PageFitVoteTally>
        {
            [pages[0]] = new(3, 3, 3),
            [pages[1]] = new(0, 1, 3),
        };
        scorer.PassScores[2] = new Dictionary<Guid, double> { [pages[0]] = 0.96 };
        scorer.PassVotes[2] = new Dictionary<Guid, PageFitVoteTally> { [pages[0]] = new(3, 3, 3) };
        var pipeline = CreatePipeline(vision, scorer, o => o.MinFitScore = 0.94);
        await pipeline.ProcessImageAsync(campaignId, mediaId);
        await AssertEvalAsync(CampaignMediaEvaluationStatus.Pending, "no_free_slot");
        await SeedVisionProfilesAsync(mediaId);
        await ClearOccupyingPostsAsync(campaignId);
        return new Scenario(campaignId, mediaId, pages, vision, scorer);
    }

    // Chạy post-only ở ngưỡng đã hạ 0.66: P1 cận trên 2/3 >= 0.66 -> (U).
    private async Task<FullMediaPipelineService> RunPostOnlyAtLoweredAsync(Scenario s, Action<FullMediaPipelineOptions>? cfg = null)
    {
        var p = CreatePipeline(s.Vision, s.Scorer, o => { o.MinFitScore = 0.66; cfg?.Invoke(o); });
        await ReadyPendingRetryAsync();
        await p.ProcessImageAsync(s.CampaignId, s.MediaId);
        return p;
    }

    private void SetupRescoreScores(Scenario s, double p1Score, PageFitVoteTally p1Tally)
    {
        s.Scorer.PassScores[1] = new Dictionary<Guid, double> { [s.Pages[0]] = 0.95, [s.Pages[1]] = p1Score };
        s.Scorer.PassVotes[1] = new Dictionary<Guid, PageFitVoteTally> { [s.Pages[0]] = new(3, 3, 3), [s.Pages[1]] = p1Tally };
        s.Scorer.PassScores[2] = new Dictionary<Guid, double> { [s.Pages[0]] = 0.96, [s.Pages[1]] = p1Score };
        s.Scorer.PassVotes[2] = new Dictionary<Guid, PageFitVoteTally> { [s.Pages[0]] = new(3, 3, 3), [s.Pages[1]] = p1Tally };
        s.Scorer.ResetTracking();
    }

    [Fact]
    public async Task PostOnly_OneValidOneUnknown_NoPostYet_PendingScoresNeedRescore()
    {
        var s = await SetupWaitingAsync();
        await RunPostOnlyAtLoweredAsync(s);

        await AssertEvalAsync(CampaignMediaEvaluationStatus.Pending, "scores_need_rescore");
        await using var db = new AppDbContext(_options);
        Assert.Equal(0, await db.Posts.CountAsync(p => db.PostMedias.Any(m => m.PostId == p.Id && m.MediaId == s.MediaId)));
        var eval = await db.CampaignMediaEvaluations.SingleAsync();
        Assert.NotNull(eval.NextAttemptAt);
        // NextAttemptAt ngắn <= 1 phút kể từ lúc đặt (clock chưa advance sau Ready nên so với now của clock +2h đã advance trong Ready... dùng độ lệch tuyệt đối nhỏ)
        Assert.True(eval.NextAttemptAt!.Value <= _clock.GetUtcNow().UtcDateTime.AddMinutes(1));
    }

    [Fact]
    public async Task WaitingReason_ScoresNeedRescore_DoesNotConsumeAttemptsFirstTime()
    {
        var s = await SetupWaitingAsync();
        await RunPostOnlyAtLoweredAsync(s);

        await using var db = new AppDbContext(_options);
        var eval = await db.CampaignMediaEvaluations.SingleAsync();
        Assert.Equal("scores_need_rescore", eval.ReasonCode);
        Assert.Equal(0, eval.Attempts);
        Assert.Equal(CampaignMediaEvaluationStatus.Pending, eval.Status);
    }

    [Fact]
    public async Task PostOnly_UnknownPage_RescoredOnly_NoVisionCall_NoExtraRequestsForValidPages()
    {
        var s = await SetupWaitingAsync();
        await RunPostOnlyAtLoweredAsync(s);
        SetupRescoreScores(s, 1.0, new PageFitVoteTally(3, 3, 3));
        var callsA = s.Vision.CallsA;
        var callsB = s.Vision.CallsB;
        var p = CreatePipeline(s.Vision, s.Scorer, o => o.MinFitScore = 0.66);
        await ReadyPendingRetryAsync();
        await p.ProcessImageAsync(s.CampaignId, s.MediaId);

        Assert.Equal(callsA, s.Vision.CallsA);
        Assert.Equal(callsB, s.Vision.CallsB);
        // Chỉ page (U) được chấm, ở cả 2 lượt; page Valid không tốn request.
        Assert.Equal(2, s.Scorer.Calls);
        Assert.Equal([s.Pages[1], s.Pages[1]], s.Scorer.ChannelsScored);
        await AssertEvalAsync(CampaignMediaEvaluationStatus.Accepted, "accepted");
    }

    [Fact]
    public async Task PostOnly_ThresholdLowered_UnknownPageNowAccepted_BothPagesPostedSameSlotAndJitter()
    {
        var s = await SetupWaitingAsync();
        // vòng 1: chờ khe -> hạ ngưỡng -> post-only phát hiện (U)
        await RunPostOnlyAtLoweredAsync(s);
        await AssertEvalAsync(CampaignMediaEvaluationStatus.Pending, "scores_need_rescore");
        // vòng 2: chấm lại chọn lọc qua ProcessDueAsync
        SetupRescoreScores(s, 1.0, new PageFitVoteTally(3, 3, 3));
        var p = CreatePipeline(s.Vision, s.Scorer, o => o.MinFitScore = 0.66);
        await ReadyPendingRetryAsync();
        await p.ProcessDueAsync();

        await AssertEvalAsync(CampaignMediaEvaluationStatus.Accepted, "accepted");
        await using var db = new AppDbContext(_options);
        var posts = await db.Posts
            .Where(x => db.PostMedias.Any(m => m.PostId == x.Id && m.MediaId == s.MediaId))
            .ToListAsync();
        Assert.Equal(2, posts.Count);
        Assert.Equal(s.Pages.OrderBy(x => x), posts.Select(x => x.SocialChannelId).OrderBy(x => x));
        Assert.Single(posts.Select(x => x.CampaignSlotAt).Distinct());
        Assert.Single(posts.Select(x => x.ScheduledPublishAt).Distinct());
    }

    [Fact]
    public async Task PostOnly_ThresholdLowered_UnknownPageStillFails_OnlyValidPosted()
    {
        var s = await SetupWaitingAsync();
        await RunPostOnlyAtLoweredAsync(s);
        // chấm lại: P1 điểm đầy đủ 1/3 vẫn < 0.66 -> (R) dứt khoát
        SetupRescoreScores(s, 1.0 / 3.0, new PageFitVoteTally(1, 3, 3));
        var p = CreatePipeline(s.Vision, s.Scorer, o => o.MinFitScore = 0.66);
        await ReadyPendingRetryAsync();
        await p.ProcessDueAsync();

        await AssertEvalAsync(CampaignMediaEvaluationStatus.Accepted, "accepted");
        await using var db = new AppDbContext(_options);
        var posts = await db.Posts
            .Where(x => db.PostMedias.Any(m => m.PostId == x.Id && m.MediaId == s.MediaId))
            .ToListAsync();
        var post = Assert.Single(posts);
        Assert.Equal(s.Pages[0], post.SocialChannelId);
    }

    [Fact]
    public async Task PostOnly_NoUnknown_BehaviourUnchanged()
    {
        // Không (U): Valid đăng như cũ khi khe trống, ngưỡng không đổi.
        var (campaignId, folderId, pages) = await SeedCampaignAsync(1, times: ["15:00", "16:00"]);
        var mediaId = await SeedMediaAsync(folderId);
        await OccupyAllSlotsAsync(campaignId, pages[0], times: ["15:00", "16:00"]);
        var scorer = new FakeScorer();
        scorer.PassScores[1] = pages.ToDictionary(p => p, _ => 0.95);
        scorer.PassScores[2] = pages.ToDictionary(p => p, _ => 0.96);
        var pipeline = CreatePipeline(new FakeVision(), scorer, o => o.MinFitScore = 0.94);
        await pipeline.ProcessImageAsync(campaignId, mediaId);
        await AssertEvalAsync(CampaignMediaEvaluationStatus.Pending, "no_free_slot");
        await ClearOccupyingPostsAsync(campaignId);
        await ReadyPendingRetryAsync();
        await pipeline.ProcessImageAsync(campaignId, mediaId);
        await AssertEvalAsync(CampaignMediaEvaluationStatus.Accepted, "accepted");
        await using var db = new AppDbContext(_options);
        Assert.Equal(1, await db.Posts.CountAsync(p => db.PostMedias.Any(m => m.PostId == p.Id && m.MediaId == mediaId)));
    }

    [Fact]
    public async Task PostOnly_ScoresNeedRescore_SecondConsecutive_CountsTowardMaxPendingAttempts()
    {
        var s = await SetupWaitingAsync();
        await RunPostOnlyAtLoweredAsync(s);
        await AssertEvalAsync(CampaignMediaEvaluationStatus.Pending, "scores_need_rescore");
        // Dữ liệu bất thường: chấm lại vẫn trả điểm dừng sớm cận trên còn đạt (0,1,3) ở 0.66.
        SetupRescoreScores(s, 0.0, new PageFitVoteTally(0, 1, 3));
        var p = CreatePipeline(s.Vision, s.Scorer, o => { o.MinFitScore = 0.66; o.MaxPendingAttempts = 5; });
        await ReadyPendingRetryAsync();
        await p.ProcessImageAsync(s.CampaignId, s.MediaId);

        await using var db = new AppDbContext(_options);
        var eval = await db.CampaignMediaEvaluations.SingleAsync();
        Assert.Equal(CampaignMediaEvaluationStatus.Pending, eval.Status);
        Assert.Equal("scores_need_rescore", eval.ReasonCode);
        Assert.True(eval.Attempts >= 1, $"Attempts phải tính ở lần 2, hiện {eval.Attempts}");
        Assert.Equal(0, await db.Posts.CountAsync(x => db.PostMedias.Any(m => m.PostId == x.Id && m.MediaId == s.MediaId)));
    }

    [Fact]
    public async Task PostOnly_ScoresNeedRescore_SecondConsecutive_MaxAttempts_BecomesFailed_NotifiesOnce()
    {
        var s = await SetupWaitingAsync();
        await RunPostOnlyAtLoweredAsync(s);
        SetupRescoreScores(s, 0.0, new PageFitVoteTally(0, 1, 3));
        var p = CreatePipeline(s.Vision, s.Scorer, o => { o.MinFitScore = 0.66; o.MaxPendingAttempts = 1; }, withNotifications: true);
        await ReadyPendingRetryAsync();
        await p.ProcessImageAsync(s.CampaignId, s.MediaId);

        await using var db = new AppDbContext(_options);
        var eval = await db.CampaignMediaEvaluations.SingleAsync();
        Assert.Equal(CampaignMediaEvaluationStatus.Failed, eval.Status);
        Assert.Equal(1, await db.AppNotifications.CountAsync(n => n.Title.Contains("Failed")));
        // Chạy thêm không thông báo lần 2 (Failed không được pick)
        await ReadyPendingRetryAsync();
        await p.ProcessDueAsync();
        await using var db2 = new AppDbContext(_options);
        Assert.Equal(1, await db2.AppNotifications.CountAsync(n => n.Title.Contains("Failed")));
    }

    // --- helpers ---

    private async Task AssertEvalAsync(CampaignMediaEvaluationStatus status, string reason)
    {
        await using var db = new AppDbContext(_options);
        var eval = await db.CampaignMediaEvaluations.SingleAsync();
        Assert.Equal(status, eval.Status);
        Assert.Equal(reason, eval.ReasonCode);
    }

    private async Task ReadyPendingRetryAsync()
    {
        _clock.Advance(TimeSpan.FromHours(2));
        await using var db = new AppDbContext(_options);
        var eval = await db.CampaignMediaEvaluations.SingleOrDefaultAsync();
        if (eval is null) return;
        eval.NextAttemptAt = null;
        if (eval.Status is CampaignMediaEvaluationStatus.Scoring or CampaignMediaEvaluationStatus.Verifying)
            eval.Status = CampaignMediaEvaluationStatus.Pending;
        await db.SaveChangesAsync();
    }

    private FullMediaPipelineService CreatePipeline(
        FakeVision vision, FakeScorer scorer, Action<FullMediaPipelineOptions>? configure = null, bool withNotifications = false)
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
        if (withNotifications)
            services.AddScoped(sp => new NotificationService(sp.GetRequiredService<AppDbContext>(), NullLogger<NotificationService>.Instance));
        services.AddScoped<FullMediaPipelineService>();
        var sp = services.BuildServiceProvider();
        _providers.Add(sp);
        var pipeline = sp.GetRequiredService<FullMediaPipelineService>();
        pipeline.CaptionFactory ??= (_, page, _) => Task.FromResult($"ok-{page}");
        pipeline.Random = new Random(42);
        return pipeline;
    }

    private async Task<(Guid CampaignId, Guid FolderId, List<Guid> Pages)> SeedCampaignAsync(int pageCount, string[]? times = null)
    {
        times ??= ["15:00"];
        await using var db = new AppDbContext(_options);
        var pages = new List<Guid>();
        for (var i = 0; i < pageCount; i++)
        {
            var ch = Guid.NewGuid();
            pages.Add(ch);
            db.SocialChannels.Add(new SocialChannelModel { Id = ch, PageName = $"P{i}", ExternalPageId = $"e{i}", Platform = SocialPlatform.Facebook, CreatedAt = DateTime.UtcNow });
            db.PageContexts.Add(new PageContextModel { Id = Guid.NewGuid(), SocialChannelId = ch, BrandName = "B", ToneOfVoice = "tone", Topics = $"topics-{i}", CreatedAt = DateTime.UtcNow });
        }
        var folderId = Guid.NewGuid();
        db.MediaFolders.Add(new MediaFolderModel { Id = folderId, Name = "src", CreatedAt = DateTime.UtcNow });
        var campaignId = Guid.NewGuid();
        db.Campaigns.Add(new CampaignModel
        {
            Id = campaignId, Name = "FM", Mode = CampaignMode.FullMedia, SourceFolderId = folderId,
            Status = CampaignStatus.Running, ScheduleMode = CampaignScheduleMode.AllWeek,
            PublishTimesJson = System.Text.Json.JsonSerializer.Serialize(times), WeekdaysJson = "[]",
            JitterMinutes = 10, StartDate = new DateTime(2026, 10, 1, 0, 0, 0, DateTimeKind.Utc),
            CreatedByUserId = Guid.NewGuid(), CreatedAt = DateTime.UtcNow,
        });
        foreach (var p in pages)
            db.Set<CampaignChannelModel>().Add(new CampaignChannelModel { Id = Guid.NewGuid(), CampaignId = campaignId, SocialChannelId = p, CreatedAt = DateTime.UtcNow });
        await db.SaveChangesAsync();
        return (campaignId, folderId, pages);
    }

    private async Task<Guid> SeedMediaAsync(Guid folderId)
    {
        await using var db = new AppDbContext(_options);
        var id = Guid.NewGuid();
        db.MediaAssets.Add(new MediaAssetModel { Id = id, FolderId = folderId, MimeType = "image/jpeg", StoragePath = $"{id}.jpg", FileName = $"{id}.jpg", CreatedAt = DateTime.UtcNow });
        await db.SaveChangesAsync();
        return id;
    }

    private async Task SeedVisionProfilesAsync(Guid mediaId)
    {
        const string json = """{"topic":"t","audience":"a","tone":"tone","setting":"set","textInImage":"","keywords":["k"],"confidence":0.9,"unclear":false}""";
        await using var db = new AppDbContext(_options);
        db.MediaVisionProfiles.Add(new MediaVisionProfileModel { Id = Guid.NewGuid(), MediaAssetId = mediaId, FileHash = "fh", Pass = VisionPass.A, DescriptionJson = json, Confidence = 0.9, Model = "model-a", CreatedAt = DateTime.UtcNow });
        db.MediaVisionProfiles.Add(new MediaVisionProfileModel { Id = Guid.NewGuid(), MediaAssetId = mediaId, FileHash = "fh", Pass = VisionPass.B, DescriptionJson = json, Confidence = 0.9, Model = "model-b", CreatedAt = DateTime.UtcNow });
        await db.SaveChangesAsync();
    }

    private async Task OccupyAllSlotsAsync(Guid campaignId, Guid channelId, string[] times)
    {
        await using var db = new AppDbContext(_options);
        var campaign = await db.Campaigns.FirstAsync(c => c.Id == campaignId);
        var slots = CampaignGenerationService.BuildSlots(campaign, times.ToList(), [], _clock.GetUtcNow().UtcDateTime);
        foreach (var slot in slots)
            db.Posts.Add(new PostModel { Id = Guid.NewGuid(), Title = "fill", SocialChannelId = channelId, CampaignId = campaignId, CampaignSlotAt = slot, Status = PostStatus.Scheduled, GenerationFlow = GenerationFlow.Recycle, UserId = Guid.NewGuid(), CreatedAt = DateTime.UtcNow });
        await db.SaveChangesAsync();
    }

    private async Task ClearOccupyingPostsAsync(Guid campaignId)
    {
        await using var db = new AppDbContext(_options);
        var posts = await db.Posts.Where(p => p.CampaignId == campaignId).ToListAsync();
        db.Posts.RemoveRange(posts);
        await db.SaveChangesAsync();
    }

    private sealed class FakeVision : IMediaVisionDescriber
    {
        public int CallsA;
        public int CallsB;
        public Task<VisionDescription> DescribeAsync(Guid mediaAssetId, VisionPass pass, CancellationToken ct = default) => Task.FromResult(Desc(pass));
        public Task<VisionOutcome> GetUsableAsync(Guid mediaAssetId, VisionPass pass, CancellationToken ct = default)
        {
            if (pass == VisionPass.A) Interlocked.Increment(ref CallsA); else Interlocked.Increment(ref CallsB);
            return Task.FromResult(new VisionOutcome(VisionUsability.Usable, Desc(pass)));
        }
        private static VisionDescription Desc(VisionPass pass) => new("t", "a", "tone", "set", "", ["k"], 0.9, false, pass == VisionPass.A ? "model-a" : "model-b");
    }

    private sealed class FakeScorer : IPageFitScorer
    {
        public int Calls;
        public List<Guid> ChannelsScored { get; } = [];
        public Dictionary<int, Dictionary<Guid, double>> PassScores { get; } = new();
        public Dictionary<int, Dictionary<Guid, PageFitVoteTally>> PassVotes { get; } = new();
        private int _seq;
        public void ResetTracking() { Calls = 0; ChannelsScored.Clear(); Interlocked.Exchange(ref _seq, 0); }
        public Task<PageFitResult> ScoreAsync(string imageStateText, IReadOnlyList<PageFitCandidate> pages, CancellationToken ct = default)
        {
            Interlocked.Increment(ref Calls);
            foreach (var p in pages) ChannelsScored.Add(p.ChannelId);
            var pass = ((Interlocked.Increment(ref _seq) - 1) % 2) + 1;
            if (!PassScores.TryGetValue(pass, out var map)) map = pages.ToDictionary(p => p.ChannelId, _ => 0.5);
            var scores = pages.ToDictionary(p => p.ChannelId, p => map.TryGetValue(p.ChannelId, out var s) ? s : 0.0);
            Dictionary<Guid, PageFitVoteTally>? votes = null;
            if (PassVotes.TryGetValue(pass, out var vmap))
                votes = pages.Where(p => vmap.ContainsKey(p.ChannelId)).ToDictionary(p => p.ChannelId, p => vmap[p.ChannelId]);
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
