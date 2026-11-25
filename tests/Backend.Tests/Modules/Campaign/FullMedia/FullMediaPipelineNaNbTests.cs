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
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;

namespace Backend.Tests.Modules.Campaign.FullMedia;

/// <summary>Review r2 notes N-A (ResetFailed) + N-B (rescore theo ProfileHash).</summary>
public sealed class FullMediaPipelineNaNbTests : IAsyncLifetime
{
    private readonly SqliteConnection _connection;
    private readonly DbContextOptions<AppDbContext> _options;
    private readonly AdjustableTimeProvider _clock =
        new(new DateTimeOffset(2026, 10, 7, 3, 0, 0, TimeSpan.Zero));
    private readonly List<ServiceProvider> _providers = [];

    public FullMediaPipelineNaNbTests()
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

    // --- N-A ---

    [Fact]
    public async Task ResetFailed_FailedBecomesPending_ClearsAttemptsAndReason()
    {
        var (campaignId, folderId, _) = await SeedCampaignAsync(1);
        var mediaId = await SeedMediaAsync(folderId);
        await using (var db = new AppDbContext(_options))
        {
            db.CampaignMediaEvaluations.Add(new CampaignMediaEvaluationModel
            {
                Id = Guid.NewGuid(),
                CampaignId = campaignId,
                MediaAssetId = mediaId,
                Status = CampaignMediaEvaluationStatus.Failed,
                ReasonCode = "vision_failed",
                Attempts = 5,
                NextAttemptAt = DateTime.UtcNow.AddHours(1),
                CreatedAt = DateTime.UtcNow,
            });
            await db.SaveChangesAsync();
        }

        var pipeline = CreatePipeline(new FakeVision(), new FakeScorer());
        Assert.True(await pipeline.ResetFailedToPendingAsync(campaignId, mediaId));

        await using var db2 = new AppDbContext(_options);
        var eval = await db2.CampaignMediaEvaluations.SingleAsync();
        Assert.Equal(CampaignMediaEvaluationStatus.Pending, eval.Status);
        Assert.Null(eval.ReasonCode);
        Assert.Null(eval.NextAttemptAt);
        Assert.Equal(0, eval.Attempts);
        Assert.Equal(1, (int)CampaignMediaEvaluationStatus.Pending);
        Assert.Equal(7, (int)CampaignMediaEvaluationStatus.Failed);
    }

    [Fact]
    public async Task ResetFailed_NonFailedUnchanged_ReturnsFalse()
    {
        var (campaignId, folderId, pages) = await SeedCampaignAsync(1);
        var mediaId = await SeedMediaAsync(folderId);
        var pipeline = CreatePipeline(new FakeVision(), AcceptAll(pages));
        await pipeline.ProcessImageAsync(campaignId, mediaId);

        Assert.False(await pipeline.ResetFailedToPendingAsync(campaignId, mediaId));
        await using var db = new AppDbContext(_options);
        var eval = await db.CampaignMediaEvaluations.SingleAsync();
        Assert.NotEqual(CampaignMediaEvaluationStatus.Pending, eval.Status);
        Assert.NotEqual(0, eval.Attempts);
    }

    [Fact]
    public async Task ResetFailed_KeepsStoredScores()
    {
        var (campaignId, folderId, pages) = await SeedCampaignAsync(1);
        var mediaId = await SeedMediaAsync(folderId);
        var scorer = new FakeScorer();
        scorer.PassScores[1] = pages.ToDictionary(p => p, _ => 0.5);
        var pipeline = CreatePipeline(new FakeVision(), scorer);
        await pipeline.ProcessImageAsync(campaignId, mediaId);

        await using (var db = new AppDbContext(_options))
        {
            var eval = await db.CampaignMediaEvaluations.SingleAsync();
            eval.Status = CampaignMediaEvaluationStatus.Failed;
            eval.ReasonCode = "vision_failed";
            await db.SaveChangesAsync();
        }

        var scoreCount = await new AppDbContext(_options).CampaignMediaScores.CountAsync();
        Assert.True(scoreCount > 0);
        Assert.True(await pipeline.ResetFailedToPendingAsync(campaignId, mediaId));
        Assert.Equal(scoreCount, await new AppDbContext(_options).CampaignMediaScores.CountAsync());
    }

    [Fact]
    public async Task ResetFailed_ThenPickNext_ProcessesAgain()
    {
        var (campaignId, folderId, pages) = await SeedCampaignAsync(1);
        var mediaId = await SeedMediaAsync(folderId);
        await using (var db = new AppDbContext(_options))
        {
            db.CampaignMediaEvaluations.Add(new CampaignMediaEvaluationModel
            {
                Id = Guid.NewGuid(),
                CampaignId = campaignId,
                MediaAssetId = mediaId,
                Status = CampaignMediaEvaluationStatus.Failed,
                ReasonCode = "pipeline_error",
                Attempts = 9,
                CreatedAt = DateTime.UtcNow,
            });
            await db.SaveChangesAsync();
        }

        var vision = new FakeVision();
        var pipeline = CreatePipeline(vision, AcceptAll(pages), o => o.Enabled = true);
        Assert.Null(await pipeline.PickNextWorkAsync(default));
        Assert.True(await pipeline.ResetFailedToPendingAsync(campaignId, mediaId));
        Assert.NotNull(await pipeline.PickNextWorkAsync(default));
    }

    // --- N-B ---

    [Fact]
    public async Task Rescore_OnlyChangedPageIsScored()
    {
        var (campaignId, folderId, pages) = await SeedCampaignAsync(2);
        var mediaId = await SeedMediaAsync(folderId);
        var vision = new FakeVision();
        var scorer = new FakeScorer();
        scorer.PassScores[1] = pages.ToDictionary(p => p, _ => 0.5);
        var pipeline = CreatePipeline(vision, scorer, o => o.Enabled = true);
        await pipeline.ProcessImageAsync(campaignId, mediaId);
        await SeedVisionProfilesAsync(mediaId);

        await using (var db = new AppDbContext(_options))
        {
            var ctx = await db.PageContexts.FirstAsync(p => p.SocialChannelId == pages[0]);
            ctx.Topics = "Đổi chủ đề page 0 để đổi ProfileHash";
            await db.SaveChangesAsync();
        }

        scorer.ResetTracking();
        scorer.PassScores[1] = pages.ToDictionary(p => p, _ => 0.5);
        await pipeline.ProcessDueAsync();

        Assert.Contains(pages[0], scorer.ChannelsScored);
        Assert.DoesNotContain(pages[1], scorer.ChannelsScored);
    }

    [Fact]
    public async Task Rescore_NewPage_Scored_OldPagesNotCalled()
    {
        var (campaignId, folderId, pages) = await SeedCampaignAsync(1);
        var mediaId = await SeedMediaAsync(folderId);
        var scorer = new FakeScorer();
        scorer.PassScores[1] = pages.ToDictionary(p => p, _ => 0.5);
        var pipeline = CreatePipeline(new FakeVision(), scorer, o => o.Enabled = true);
        await pipeline.ProcessImageAsync(campaignId, mediaId);
        await SeedVisionProfilesAsync(mediaId);

        var newPage = await AddPageToCampaignAsync(campaignId, "P-new");
        scorer.ResetTracking();
        scorer.PassScores[1] = new Dictionary<Guid, double>
        {
            [pages[0]] = 0.5,
            [newPage] = 0.5,
        };
        await pipeline.ProcessDueAsync();

        Assert.Contains(newPage, scorer.ChannelsScored);
        Assert.DoesNotContain(pages[0], scorer.ChannelsScored);
    }

    [Fact]
    public async Task Rescore_AllHashesUnchanged_NoScorerCall_StaysRejected()
    {
        var (campaignId, folderId, pages) = await SeedCampaignAsync(1);
        var mediaId = await SeedMediaAsync(folderId);
        var scorer = new FakeScorer();
        scorer.PassScores[1] = pages.ToDictionary(p => p, _ => 0.5);
        var pipeline = CreatePipeline(new FakeVision(), scorer, o => o.Enabled = true);
        await pipeline.ProcessImageAsync(campaignId, mediaId);
        await SeedVisionProfilesAsync(mediaId);

        await using (var db = new AppDbContext(_options))
        {
            var eval = await db.CampaignMediaEvaluations.SingleAsync();
            eval.InputHash = "stale-hash-force-rescore";
            await db.SaveChangesAsync();
        }

        scorer.ResetTracking();
        await pipeline.ProcessDueAsync();

        Assert.Equal(0, scorer.Calls);
        await using var db2 = new AppDbContext(_options);
        var eval2 = await db2.CampaignMediaEvaluations.SingleAsync();
        Assert.Equal(CampaignMediaEvaluationStatus.Rejected, eval2.Status);
        Assert.NotEqual("stale-hash-force-rescore", eval2.InputHash);
    }

    [Fact]
    public async Task Rescore_LegacyScoreWithoutProfileHash_Rescored()
    {
        var (campaignId, folderId, pages) = await SeedCampaignAsync(1);
        var mediaId = await SeedMediaAsync(folderId);
        var scorer = new FakeScorer();
        scorer.PassScores[1] = pages.ToDictionary(p => p, _ => 0.5);
        var pipeline = CreatePipeline(new FakeVision(), scorer, o => o.Enabled = true);
        await pipeline.ProcessImageAsync(campaignId, mediaId);
        await SeedVisionProfilesAsync(mediaId);

        await using (var db = new AppDbContext(_options))
        {
            foreach (var s in db.CampaignMediaScores)
                s.ProfileHash = null;
            var ctx = await db.PageContexts.FirstAsync(p => p.SocialChannelId == pages[0]);
            ctx.Topics = "Đổi để kích hoạt rescore";
            await db.SaveChangesAsync();
        }

        scorer.ResetTracking();
        scorer.PassScores[1] = pages.ToDictionary(p => p, _ => 0.5);
        await pipeline.ProcessDueAsync();
        Assert.Contains(pages[0], scorer.ChannelsScored);
    }

    [Fact]
    public async Task Rescore_ChangedPagePasses_BothPassesRequired_CreatesPostOnlyForThatPage()
    {
        var (campaignId, folderId, pages) = await SeedCampaignAsync(2, times: ["15:00", "16:00"]);
        var mediaId = await SeedMediaAsync(folderId);
        var scorer = new FakeScorer();
        scorer.PassScores[1] = pages.ToDictionary(p => p, _ => 0.5);
        var pipeline = CreatePipeline(new FakeVision(), scorer, o => o.Enabled = true);
        await pipeline.ProcessImageAsync(campaignId, mediaId);
        await SeedVisionProfilesAsync(mediaId);

        await using (var db = new AppDbContext(_options))
        {
            var ctx = await db.PageContexts.FirstAsync(p => p.SocialChannelId == pages[0]);
            ctx.Topics = "Page 0 đủ điểm sau khi đổi";
            await db.SaveChangesAsync();
        }

        scorer.ResetTracking();
        scorer.PassScores[1] = new Dictionary<Guid, double>
        {
            [pages[0]] = 0.95,
            [pages[1]] = 0.5,
        };
        scorer.PassScores[2] = new Dictionary<Guid, double>
        {
            [pages[0]] = 0.96,
            [pages[1]] = 0.5,
        };
        await pipeline.ProcessDueAsync();

        await using var db2 = new AppDbContext(_options);
        var posts = await db2.Posts.Where(p => p.CampaignId == campaignId).ToListAsync();
        Assert.Single(posts);
        Assert.Equal(pages[0], posts[0].SocialChannelId);
        Assert.DoesNotContain(pages[1], scorer.ChannelsScored);
    }

    [Fact]
    public async Task Rescore_CostAccumulatesNotOverwritten()
    {
        var (campaignId, folderId, pages) = await SeedCampaignAsync(1);
        var mediaId = await SeedMediaAsync(folderId);
        var scorer = new FakeScorer { CostPerCall = 0.01m };
        scorer.PassScores[1] = pages.ToDictionary(p => p, _ => 0.5);
        var pipeline = CreatePipeline(new FakeVision(), scorer, o => o.Enabled = true);
        await pipeline.ProcessImageAsync(campaignId, mediaId);
        await SeedVisionProfilesAsync(mediaId);

        decimal costBefore;
        await using (var db = new AppDbContext(_options))
        {
            costBefore = await db.CampaignMediaScores.SumAsync(s => s.CostUsd);
            Assert.True(costBefore > 0);
            var ctx = await db.PageContexts.FirstAsync();
            ctx.Topics = "Đổi để chấm lại và cộng dồn cost";
            await db.SaveChangesAsync();
        }

        scorer.PassScores[1] = pages.ToDictionary(p => p, _ => 0.5);
        await pipeline.ProcessDueAsync();

        await using var db2 = new AppDbContext(_options);
        var costAfter = await db2.CampaignMediaScores.SumAsync(s => s.CostUsd);
        Assert.True(costAfter > costBefore, $"before={costBefore} after={costAfter}");
    }

    [Fact]
    public async Task Rescore_StillNoVisionCall()
    {
        var (campaignId, folderId, pages) = await SeedCampaignAsync(1);
        var mediaId = await SeedMediaAsync(folderId);
        var vision = new FakeVision();
        var scorer = new FakeScorer();
        scorer.PassScores[1] = pages.ToDictionary(p => p, _ => 0.5);
        var pipeline = CreatePipeline(vision, scorer, o => o.Enabled = true);
        await pipeline.ProcessImageAsync(campaignId, mediaId);
        await SeedVisionProfilesAsync(mediaId);
        var callsA = vision.CallsA;
        var callsB = vision.CallsB;

        await using (var db = new AppDbContext(_options))
        {
            var ctx = await db.PageContexts.FirstAsync();
            ctx.Topics = "Đổi hash";
            await db.SaveChangesAsync();
        }

        scorer.PassScores[1] = pages.ToDictionary(p => p, _ => 0.95);
        scorer.PassScores[2] = pages.ToDictionary(p => p, _ => 0.95);
        await pipeline.ProcessDueAsync();
        Assert.Equal(callsA, vision.CallsA);
        Assert.Equal(callsB, vision.CallsB);
    }

    [Fact]
    public async Task Rescore_MaxRescores_BlocksFurtherRescore()
    {
        var (campaignId, folderId, pages) = await SeedCampaignAsync(1);
        var mediaId = await SeedMediaAsync(folderId);
        var scorer = new FakeScorer();
        scorer.PassScores[1] = pages.ToDictionary(p => p, _ => 0.5);
        var pipeline = CreatePipeline(new FakeVision(), scorer, o =>
        {
            o.Enabled = true;
            o.MaxRescores = 1;
        });
        await pipeline.ProcessImageAsync(campaignId, mediaId);
        await SeedVisionProfilesAsync(mediaId);

        await using (var db = new AppDbContext(_options))
        {
            var eval = await db.CampaignMediaEvaluations.SingleAsync();
            // Attempts đã >= 1 sau lần đầu; MaxRescores=1 → không chấm lại thêm.
            Assert.True(eval.Attempts >= 1);
            var ctx = await db.PageContexts.FirstAsync();
            ctx.Topics = "Đổi nhưng đã hết MaxRescores";
            await db.SaveChangesAsync();
        }

        scorer.ResetTracking();
        await pipeline.ProcessDueAsync();
        Assert.Equal(0, scorer.Calls);
    }

    [Fact]
    public void Migration_AddFullMediaScoreProfileHash_Exists()
    {
        var dir = Path.GetFullPath(Path.Combine(
            AppContext.BaseDirectory, "..", "..", "..", "..", "..", "backend", "Migrations"));
        Assert.Contains(Directory.GetFiles(dir, "*AddFullMediaScoreProfileHash.cs"),
            f => !f.EndsWith(".Designer.cs", StringComparison.Ordinal));
        Assert.Contains(Directory.GetFiles(dir, "*AddFullMediaScoreProfileHash.Designer.cs"), _ => true);
    }

    // --- helpers ---

    private FullMediaPipelineService CreatePipeline(
        FakeVision vision,
        FakeScorer scorer,
        Action<FullMediaPipelineOptions>? configure = null)
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

    private async Task<Guid> AddPageToCampaignAsync(Guid campaignId, string name)
    {
        await using var db = new AppDbContext(_options);
        var ch = Guid.NewGuid();
        db.SocialChannels.Add(new SocialChannelModel
        {
            Id = ch,
            PageName = name,
            ExternalPageId = name,
            Platform = SocialPlatform.Facebook,
            CreatedAt = DateTime.UtcNow,
        });
        db.PageContexts.Add(new PageContextModel
        {
            Id = Guid.NewGuid(),
            SocialChannelId = ch,
            BrandName = "B",
            ToneOfVoice = "tone",
            Topics = "new-page-topics",
            CreatedAt = DateTime.UtcNow,
        });
        db.Set<CampaignChannelModel>().Add(new CampaignChannelModel
        {
            Id = Guid.NewGuid(),
            CampaignId = campaignId,
            SocialChannelId = ch,
            CreatedAt = DateTime.UtcNow,
        });
        await db.SaveChangesAsync();
        return ch;
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
        public decimal CostPerCall = 0.01m;
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
            foreach (var p in pages)
                ChannelsScored.Add(p.ChannelId);
            var pass = ((Interlocked.Increment(ref _seq) - 1) % 2) + 1;
            if (!PassScores.TryGetValue(pass, out var map))
                map = pages.ToDictionary(p => p.ChannelId, _ => 0.5);
            var scores = pages.ToDictionary(
                p => p.ChannelId,
                p => map.TryGetValue(p.ChannelId, out var s) ? s : 0.0);
            return Task.FromResult(new PageFitResult(scores, CostPerCall, pages.Count));
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
