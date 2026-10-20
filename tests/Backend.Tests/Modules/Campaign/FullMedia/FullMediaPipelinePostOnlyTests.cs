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

/// <summary>Review r3: đường IsPostOnly kiểm lại ProfileHash / ngưỡng trước khi đăng.</summary>
public sealed class FullMediaPipelinePostOnlyTests : IAsyncLifetime
{
    private readonly SqliteConnection _connection;
    private readonly DbContextOptions<AppDbContext> _options;
    private readonly AdjustableTimeProvider _clock =
        new(new DateTimeOffset(2026, 10, 7, 3, 0, 0, TimeSpan.Zero));
    private readonly List<ServiceProvider> _providers = [];

    public FullMediaPipelinePostOnlyTests()
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

    [Fact]
    public async Task PostOnly_ProfileUnchanged_PostsAsBefore()
    {
        var (campaignId, folderId, pages) = await SeedCampaignAsync(1, times: ["15:00", "16:00"]);
        var mediaId = await SeedMediaAsync(folderId);
        await OccupyAllSlotsAsync(campaignId, pages[0], times: ["15:00", "16:00"]);
        var pipeline = CreatePipeline(new FakeVision(), AcceptAll(pages), o => o.Enabled = true);
        await pipeline.ProcessImageAsync(campaignId, mediaId);
        await AssertPendingReasonAsync("no_free_slot");

        await ClearOccupyingPostsAsync(campaignId);
        await ReadyPendingRetryAsync();
        await pipeline.ProcessImageAsync(campaignId, mediaId);

        await using var db = new AppDbContext(_options);
        Assert.Equal(CampaignMediaEvaluationStatus.Accepted,
            (await db.CampaignMediaEvaluations.SingleAsync()).Status);
        Assert.Equal(1, await db.Posts.CountAsync(p => p.CampaignId == campaignId
            && db.PostMedias.Any(m => m.PostId == p.Id && m.MediaId == mediaId)));
    }

    [Fact]
    public async Task PostOnly_ProfileChangedWhileWaiting_NoPost_RescoredNextRun_NoVisionCall()
    {
        var (campaignId, folderId, pages) = await SeedCampaignAsync(1, times: ["15:00", "16:00"]);
        var mediaId = await SeedMediaAsync(folderId);
        await OccupyAllSlotsAsync(campaignId, pages[0], times: ["15:00", "16:00"]);
        var vision = new FakeVision();
        var scorer = AcceptAll(pages);
        var pipeline = CreatePipeline(vision, scorer, o => o.Enabled = true);
        await pipeline.ProcessImageAsync(campaignId, mediaId);
        await SeedVisionProfilesAsync(mediaId);

        await using (var db = new AppDbContext(_options))
        {
            var ctx = await db.PageContexts.FirstAsync(p => p.SocialChannelId == pages[0]);
            ctx.Topics = "Đổi hồ sơ lúc chờ khe";
            await db.SaveChangesAsync();
        }

        await ClearOccupyingPostsAsync(campaignId);
        await ReadyPendingRetryAsync();
        await pipeline.ProcessImageAsync(campaignId, mediaId);

        await using (var db = new AppDbContext(_options))
        {
            var eval = await db.CampaignMediaEvaluations.SingleAsync();
            // ProfileHash đổi = nhóm (U) theo Classify -> chấm lại chọn lọc, không còn profile_changed.
            Assert.Equal("scores_need_rescore", eval.ReasonCode);
            Assert.Equal(CampaignMediaEvaluationStatus.Pending, eval.Status);
            Assert.Equal(0, await db.Posts.CountAsync(p =>
                db.PostMedias.Any(m => m.PostId == p.Id && m.MediaId == mediaId)));
        }

        var callsA = vision.CallsA;
        var callsB = vision.CallsB;
        scorer.ResetTracking();
        scorer.PassScores[1] = pages.ToDictionary(p => p, _ => 0.95);
        scorer.PassScores[2] = pages.ToDictionary(p => p, _ => 0.96);
        await ReadyPendingRetryAsync();
        // ProcessImage (không chỉ ProcessDue) để mutation p7 (IsRescore=false) đỏ.
        await pipeline.ProcessImageAsync(campaignId, mediaId);

        Assert.Equal(callsA, vision.CallsA);
        Assert.Equal(callsB, vision.CallsB);
        Assert.True(scorer.Calls > 0);
        await using var db2 = new AppDbContext(_options);
        Assert.Equal(CampaignMediaEvaluationStatus.Accepted,
            (await db2.CampaignMediaEvaluations.SingleAsync()).Status);
    }

    [Fact]
    public async Task PostOnly_OnePageChanged_OnlyUnchangedPagePosted()
    {
        // Hành vi mới (V/R/U): page đổi hồ sơ là (U) -> không đăng page nào ở lượt post-only,
        // chấm lại chọn lọc page (U) ở lượt kế rồi mới đăng cả hai page đạt.
        var (campaignId, folderId, pages) = await SeedCampaignAsync(2, times: ["15:00", "16:00"]);
        var mediaId = await SeedMediaAsync(folderId);
        await OccupyAllSlotsAsync(campaignId, pages[0], times: ["15:00", "16:00"]);
        await OccupyAllSlotsAsync(campaignId, pages[1], times: ["15:00", "16:00"]);
        var vision = new FakeVision();
        var scorer = AcceptAll(pages);
        var pipeline = CreatePipeline(vision, scorer, o => o.Enabled = true);
        await pipeline.ProcessImageAsync(campaignId, mediaId);
        await AssertPendingReasonAsync("no_free_slot");
        await SeedVisionProfilesAsync(mediaId);

        await using (var db = new AppDbContext(_options))
        {
            var ctx = await db.PageContexts.FirstAsync(p => p.SocialChannelId == pages[0]);
            ctx.Topics = "Chỉ page 0 đổi";
            await db.SaveChangesAsync();
        }

        await ClearOccupyingPostsAsync(campaignId);
        await ReadyPendingRetryAsync();
        await pipeline.ProcessImageAsync(campaignId, mediaId);

        await AssertPendingReasonAsync("scores_need_rescore");
        await using (var db0 = new AppDbContext(_options))
            Assert.Equal(0, await db0.Posts.CountAsync(p =>
                db0.PostMedias.Any(m => m.PostId == p.Id && m.MediaId == mediaId)));

        scorer.ResetTracking();
        scorer.PassScores[1] = pages.ToDictionary(p => p, _ => 0.95);
        scorer.PassScores[2] = pages.ToDictionary(p => p, _ => 0.96);
        await ReadyPendingRetryAsync();
        await pipeline.ProcessImageAsync(campaignId, mediaId);

        await using var db2 = new AppDbContext(_options);
        var posts = await db2.Posts
            .Where(p => db2.PostMedias.Any(m => m.PostId == p.Id && m.MediaId == mediaId))
            .ToListAsync();
        Assert.Equal(2, posts.Count);
        Assert.Equal(CampaignMediaEvaluationStatus.Accepted,
            (await db2.CampaignMediaEvaluations.SingleAsync()).Status);
    }

    [Fact]
    public async Task PostOnly_PageBecomesUnscoreable_NotPosted()
    {
        var (campaignId, folderId, pages) = await SeedCampaignAsync(1, times: ["15:00", "16:00"]);
        var mediaId = await SeedMediaAsync(folderId);
        await OccupyAllSlotsAsync(campaignId, pages[0], times: ["15:00", "16:00"]);
        var pipeline = CreatePipeline(new FakeVision(), AcceptAll(pages));
        await pipeline.ProcessImageAsync(campaignId, mediaId);

        await using (var db = new AppDbContext(_options))
        {
            var ctx = await db.PageContexts.FirstAsync();
            ctx.Topics = "";
            ctx.ToneOfVoice = "";
            // Đồng bộ ProfileHash trên điểm với hồ sơ rỗng — chỉ IsScoreable chặn đăng.
            var emptyHash = PageProfileSnapshot.Build(ctx).Hash;
            foreach (var s in db.CampaignMediaScores)
                s.ProfileHash = emptyHash;
            await db.SaveChangesAsync();
        }

        await ClearOccupyingPostsAsync(campaignId);
        await ReadyPendingRetryAsync();
        await pipeline.ProcessImageAsync(campaignId, mediaId);

        await using var db2 = new AppDbContext(_options);
        Assert.Equal("profile_changed_while_waiting",
            (await db2.CampaignMediaEvaluations.SingleAsync()).ReasonCode);
        Assert.Equal(0, await db2.Posts.CountAsync(p =>
            db2.PostMedias.Any(m => m.PostId == p.Id && m.MediaId == mediaId)));
    }

    [Fact]
    public async Task PostOnly_LegacyScoreWithoutProfileHash_NotPosted_Rescored()
    {
        var (campaignId, folderId, pages) = await SeedCampaignAsync(1, times: ["15:00", "16:00"]);
        var mediaId = await SeedMediaAsync(folderId);
        await OccupyAllSlotsAsync(campaignId, pages[0], times: ["15:00", "16:00"]);
        var vision = new FakeVision();
        var scorer = AcceptAll(pages);
        var pipeline = CreatePipeline(vision, scorer, o => o.Enabled = true);
        await pipeline.ProcessImageAsync(campaignId, mediaId);
        await SeedVisionProfilesAsync(mediaId);

        await using (var db = new AppDbContext(_options))
        {
            foreach (var s in db.CampaignMediaScores)
                s.ProfileHash = null;
            await db.SaveChangesAsync();
        }

        await ClearOccupyingPostsAsync(campaignId);
        await ReadyPendingRetryAsync();
        await pipeline.ProcessImageAsync(campaignId, mediaId);
        await AssertPendingReasonAsync("scores_need_rescore");

        var callsA = vision.CallsA;
        scorer.ResetTracking();
        await ReadyPendingRetryAsync();
        await pipeline.ProcessDueAsync();
        Assert.Equal(callsA, vision.CallsA);
        Assert.True(scorer.Calls > 0);
    }

    [Fact]
    public async Task PostOnly_ScoreDroppedBelowThreshold_NotPosted()
    {
        var (campaignId, folderId, pages) = await SeedCampaignAsync(1, times: ["15:00", "16:00"]);
        var mediaId = await SeedMediaAsync(folderId);
        await OccupyAllSlotsAsync(campaignId, pages[0], times: ["15:00", "16:00"]);
        var pipeline = CreatePipeline(new FakeVision(), AcceptAll(pages));
        await pipeline.ProcessImageAsync(campaignId, mediaId);

        await using (var db = new AppDbContext(_options))
        {
            // Giữ Passed=true để còn vào Filter; điểm thấp → FitThreshold loại.
            foreach (var s in db.CampaignMediaScores.Where(x => x.Pass == 2))
                s.Score = 0.5;
            await db.SaveChangesAsync();
        }

        await ClearOccupyingPostsAsync(campaignId);
        await ReadyPendingRetryAsync();
        await pipeline.ProcessImageAsync(campaignId, mediaId);

        await using var db2 = new AppDbContext(_options);
        Assert.Equal(0, await db2.Posts.CountAsync(p =>
            db2.PostMedias.Any(m => m.PostId == p.Id && m.MediaId == mediaId)));
        Assert.Equal("profile_changed_while_waiting",
            (await db2.CampaignMediaEvaluations.SingleAsync()).ReasonCode);
    }

    [Fact]
    public async Task PostOnly_ProfileChanged_DoesNotConsumeMaxPendingAttempts()
    {
        var (campaignId, folderId, pages) = await SeedCampaignAsync(1, times: ["15:00"]);
        var mediaId = await SeedMediaAsync(folderId);
        await OccupyAllSlotsAsync(campaignId, pages[0], times: ["15:00"]);
        var pipeline = CreatePipeline(new FakeVision(), AcceptAll(pages), o =>
        {
            o.Enabled = true;
            o.MaxPendingAttempts = 1;
        });
        await pipeline.ProcessImageAsync(campaignId, mediaId);

        await using (var db = new AppDbContext(_options))
        {
            var ctx = await db.PageContexts.FirstAsync();
            ctx.Topics = "Đổi 1";
            await db.SaveChangesAsync();
        }

        // Khe vẫn đầy — ProcessImage post-only → profile_changed (hash đổi), waiting.
        for (var i = 0; i < 3; i++)
        {
            await using (var db = new AppDbContext(_options))
            {
                var e = await db.CampaignMediaEvaluations.SingleAsync();
                e.Status = CampaignMediaEvaluationStatus.Pending;
                e.ReasonCode = "no_free_slot";
                e.NextAttemptAt = null;
                var ctx = await db.PageContexts.FirstAsync();
                ctx.Topics = $"Đổi lần {i + 2}";
                await db.SaveChangesAsync();
            }

            await ReadyPendingRetryAsync();
            await pipeline.ProcessImageAsync(campaignId, mediaId);
        }

        await using var db2 = new AppDbContext(_options);
        var eval = await db2.CampaignMediaEvaluations.SingleAsync();
        Assert.NotEqual(CampaignMediaEvaluationStatus.Failed, eval.Status);
        Assert.Equal("scores_need_rescore", eval.ReasonCode);
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
            return Task.FromResult(new PageFitResult(scores, 0.01m, pages.Count));
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
