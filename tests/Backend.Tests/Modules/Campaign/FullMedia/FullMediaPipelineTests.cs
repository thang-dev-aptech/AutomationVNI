using System.Collections.Concurrent;
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
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;

namespace Backend.Tests.Modules.Campaign.FullMedia;

/// <summary>
/// FULLMEDIA t4 — pipeline + worker + ngưỡng/đồng thuận/fan-out/cô lập.
/// Mutation T1–T27: mỗi nhóm có test đỏ khi hoàn nguyên hành vi.
/// </summary>
public sealed class FullMediaPipelineTests : IAsyncLifetime
{
    private readonly SqliteConnection _connection;
    private readonly DbContextOptions<AppDbContext> _options;
    private readonly FixedTimeProvider _clock =
        new(new DateTimeOffset(2026, 10, 7, 3, 0, 0, TimeSpan.Zero));

    public FullMediaPipelineTests()
    {
        _connection = SqliteTestDatabase.OpenInMemory();
        _options = new DbContextOptionsBuilder<AppDbContext>().UseSqlite(_connection).Options;
        using var db = new AppDbContext(_options);
        db.Database.EnsureCreated();
    }

    public Task InitializeAsync() => Task.CompletedTask;
    public async Task DisposeAsync() => await _connection.DisposeAsync();

    // --- FitThreshold (T1, T2) ---

    [Fact]
    public void ThresholdBoundary_0939Rejects_094Accepts()
    {
        // T1: >= ; T2: ngưỡng mặc định 0.94 (không 0.90); không làm tròn 0.9399
        Assert.Equal(0.94, FitThreshold.DefaultMinFitScore);
        Assert.Equal(0.94, new FullMediaPipelineOptions().MinFitScore);
        Assert.False(FitThreshold.Passes(0.939, FitThreshold.DefaultMinFitScore));
        Assert.True(FitThreshold.Passes(0.94, FitThreshold.DefaultMinFitScore));
        Assert.False(FitThreshold.Passes(0.9399, FitThreshold.DefaultMinFitScore));
        Assert.False(FitThreshold.Passes(0.90, FitThreshold.DefaultMinFitScore));
    }

    [Fact]
    public void FitThreshold_SingleComparisonSite_UsesGreaterOrEqual()
    {
        // T1: nguồn duy nhất chứa file FitThreshold.cs phải chứa ">="
        var path = Path.GetFullPath(Path.Combine(
            AppContext.BaseDirectory, "..", "..", "..", "..", "..",
            "backend", "Modules", "Campaign", "FullMedia", "FitThreshold.cs"));
        var text = File.ReadAllText(path);
        Assert.Contains("score >= minFitScore", text);
        Assert.DoesNotContain("score > minFitScore", text);
    }

    // --- Two-pass / Vision B (T3, T4) ---

    [Fact]
    public async Task TwoPassConsensus_Pass1OnlyNotAccepted_VisionBNotCalledWhenRejected()
    {
        var (campaignId, mediaId, pages) = await SeedFullMediaAsync(pageCount: 1);
        var vision = new FakeVision();
        var scorer = new FakeScorer();
        scorer.PassScores[1] = pages.ToDictionary(p => p, _ => 0.50);
        // pass 2 would accept — nhưng không được gọi
        scorer.PassScores[2] = pages.ToDictionary(p => p, _ => 0.99);

        var pipeline = CreatePipeline(vision, scorer);
        await pipeline.ProcessImageAsync(campaignId, mediaId);

        Assert.Equal(1, vision.CallsA);
        Assert.Equal(0, vision.CallsB); // T4
        await using var db = new AppDbContext(_options);
        var eval = await db.CampaignMediaEvaluations.SingleAsync(e => e.MediaAssetId == mediaId);
        Assert.Equal(CampaignMediaEvaluationStatus.Rejected, eval.Status);
        Assert.Empty(await db.Posts.Where(p => p.CampaignId == campaignId).ToListAsync());
    }

    [Fact]
    public async Task TwoPassConsensus_Pass1OkPass2Fail_Rejected()
    {
        var (campaignId, mediaId, pages) = await SeedFullMediaAsync(pageCount: 1);
        var vision = new FakeVision();
        var scorer = new FakeScorer();
        scorer.PassScores[1] = pages.ToDictionary(p => p, _ => 0.95);
        scorer.PassScores[2] = pages.ToDictionary(p => p, _ => 0.50);
        var pipeline = CreatePipeline(vision, scorer);
        await pipeline.ProcessImageAsync(campaignId, mediaId);

        Assert.Equal(1, vision.CallsB);
        await using var db = new AppDbContext(_options);
        var eval = await db.CampaignMediaEvaluations.SingleAsync();
        Assert.Equal(CampaignMediaEvaluationStatus.Rejected, eval.Status);
        Assert.Equal(0, await db.Posts.CountAsync(p => p.CampaignId == campaignId));
    }

    // --- Fan-out / slots (T5, T6, T27) ---

    [Fact]
    public async Task FanOut_ThreePages_ThreeScheduledPostsSameSlotJitter()
    {
        var (campaignId, mediaId, pages) = await SeedFullMediaAsync(pageCount: 3);
        var vision = new FakeVision();
        var scorer = AcceptAllScorer(pages);
        var pipeline = CreatePipeline(vision, scorer);
        pipeline.Random = new Random(1);
        await pipeline.ProcessImageAsync(campaignId, mediaId);

        await using var db = new AppDbContext(_options);
        var posts = await db.Posts.Where(p => p.CampaignId == campaignId).ToListAsync();
        Assert.Equal(3, posts.Count); // T5: mọi page đạt, không chỉ cao nhất
        Assert.All(posts, p =>
        {
            Assert.Equal(PostStatus.Scheduled, p.Status);
            Assert.Equal(GenerationFlow.FullMedia, p.GenerationFlow); // T27
            Assert.Equal("campaign-worker", p.ApprovedBy);
            Assert.NotNull(p.ApprovedAt);
        });
        var slot = posts[0].CampaignSlotAt;
        Assert.All(posts, p => Assert.Equal(slot, p.CampaignSlotAt)); // T6: cùng khe
        var scheduled = posts.Select(p => p.ScheduledPublishAt).Distinct().ToList();
        Assert.Single(scheduled); // cùng jitter seed → cùng ScheduledPublishAt
    }

    // --- No repeat (T7) ---

    [Fact]
    public async Task NoRepeatOnSamePage()
    {
        var (campaignId, mediaId, pages) = await SeedFullMediaAsync(pageCount: 1);
        var vision = new FakeVision();
        var scorer = AcceptAllScorer(pages);
        var pipeline = CreatePipeline(vision, scorer);
        await pipeline.ProcessImageAsync(campaignId, mediaId);
        await pipeline.ProcessImageAsync(campaignId, mediaId);

        await using var db = new AppDbContext(_options);
        Assert.Equal(1, await db.Posts.CountAsync(p => p.CampaignId == campaignId));
    }

    // --- Caption isolation (T8) ---

    [Fact]
    public async Task CaptionFailureIsolated()
    {
        var (campaignId, mediaId, pages) = await SeedFullMediaAsync(pageCount: 2);
        var vision = new FakeVision();
        var scorer = AcceptAllScorer(pages);
        var pipeline = CreatePipeline(vision, scorer);
        var failPage = pages[0];
        pipeline.CaptionFactory = (m, page, ct) =>
            page == failPage
                ? throw new InvalidOperationException("caption boom")
                : Task.FromResult($"caption-{page}");
        await pipeline.ProcessImageAsync(campaignId, mediaId);

        await using var db = new AppDbContext(_options);
        var posts = await db.Posts.Where(p => p.CampaignId == campaignId).ToListAsync();
        Assert.Single(posts);
        Assert.Equal(pages[1], posts[0].SocialChannelId);
    }

    // --- Pending vs Rejected (T9, T10, T11) ---

    [Fact]
    public async Task AllPagesEmptyProfile_StaysPending()
    {
        var (campaignId, mediaId, _) = await SeedFullMediaAsync(pageCount: 2, emptyProfiles: true);
        var vision = new FakeVision();
        var scorer = new FakeScorer();
        var pipeline = CreatePipeline(vision, scorer);
        await pipeline.ProcessImageAsync(campaignId, mediaId);

        Assert.Equal(0, vision.CallsA); // T9: không gửi chấm
        Assert.Equal(0, scorer.Calls);
        await using var db = new AppDbContext(_options);
        var eval = await db.CampaignMediaEvaluations.SingleAsync();
        Assert.Equal(CampaignMediaEvaluationStatus.Pending, eval.Status); // T10
        Assert.Equal("no_scoreable_pages", eval.ReasonCode);
    }

    [Fact]
    public async Task SomeEmptyProfile_SkippedOthersScored()
    {
        var (campaignId, mediaId, pages) = await SeedFullMediaAsync(pageCount: 2, emptySecond: true);
        var vision = new FakeVision();
        var scorer = AcceptAllScorer([pages[0]]);
        var pipeline = CreatePipeline(vision, scorer);
        await pipeline.ProcessImageAsync(campaignId, mediaId);

        await using var db = new AppDbContext(_options);
        Assert.Equal(1, await db.Posts.CountAsync(p => p.CampaignId == campaignId));
        Assert.Equal(pages[0], (await db.Posts.SingleAsync()).SocialChannelId);
    }

    [Fact]
    public async Task ScorerPermanentFailure_StaysPending_NoPost()
    {
        var (campaignId, mediaId, pages) = await SeedFullMediaAsync(pageCount: 1);
        var vision = new FakeVision();
        var scorer = new FakeScorer { ThrowPermanent = true };
        var pipeline = CreatePipeline(vision, scorer);
        await pipeline.ProcessImageAsync(campaignId, mediaId);

        await using var db = new AppDbContext(_options);
        var eval = await db.CampaignMediaEvaluations.SingleAsync();
        Assert.Equal(CampaignMediaEvaluationStatus.Pending, eval.Status); // T11
        Assert.Equal(0, await db.Posts.CountAsync());
    }

    // --- Rescore (T12, T13, T14) ---

    [Fact]
    public async Task RejectedNotRescoredSameInputHash()
    {
        var (campaignId, mediaId, pages) = await SeedFullMediaAsync(pageCount: 1);
        var vision = new FakeVision();
        var scorer = new FakeScorer();
        scorer.PassScores[1] = pages.ToDictionary(p => p, _ => 0.5);
        var pipeline = CreatePipeline(vision, scorer, o =>
        {
            o.Enabled = true;
            o.MaxRescores = 3;
        });
        await pipeline.ProcessImageAsync(campaignId, mediaId);
        // Seed profile khớp FakeVision để PickNext tính được InputHash (T12).
        await using (var dbSeed = new AppDbContext(_options))
        {
            const string json =
                """{"topic":"t","audience":"a","tone":"tone","setting":"set","textInImage":"","keywords":["k"],"confidence":0.9,"unclear":false}""";
            dbSeed.MediaVisionProfiles.Add(new MediaVisionProfileModel
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
            await dbSeed.SaveChangesAsync();
        }

        var callsA = vision.CallsA;
        var scoreCalls = scorer.Calls;
        int attemptsBefore;
        await using (var dbA = new AppDbContext(_options))
            attemptsBefore = (await dbA.CampaignMediaEvaluations.SingleAsync()).Attempts;

        // ProcessDue không nhận lại Rejected cùng hash (T12)
        await pipeline.ProcessDueAsync();
        Assert.Equal(callsA, vision.CallsA);
        Assert.Equal(scoreCalls, scorer.Calls);
        await using var db = new AppDbContext(_options);
        var eval = await db.CampaignMediaEvaluations.SingleAsync();
        Assert.Equal(CampaignMediaEvaluationStatus.Rejected, eval.Status);
        Assert.Equal(attemptsBefore, eval.Attempts);
    }

    [Fact]
    public async Task RescoredOnProfileHashChange_NoVisionCall()
    {
        var (campaignId, mediaId, pages) = await SeedFullMediaAsync(pageCount: 1);
        var vision = new FakeVision();
        var scorer = new FakeScorer();
        scorer.PassScores[1] = pages.ToDictionary(p => p, _ => 0.5);
        var pipeline = CreatePipeline(vision, scorer);
        await pipeline.ProcessImageAsync(campaignId, mediaId);

        await using (var db = new AppDbContext(_options))
        {
            const string json =
                """{"topic":"t","audience":"a","tone":"tone","setting":"set","textInImage":"","keywords":["k"],"confidence":0.9,"unclear":false}""";
            db.MediaVisionProfiles.Add(new MediaVisionProfileModel
            {
                Id = Guid.NewGuid(),
                MediaAssetId = mediaId,
                FileHash = "abc",
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
                FileHash = "abc",
                Pass = VisionPass.B,
                DescriptionJson = json,
                Confidence = 0.9,
                Model = "model-b",
                CreatedAt = DateTime.UtcNow,
            });
            var ctx = await db.PageContexts.FirstAsync(p => p.SocialChannelId == pages[0]);
            ctx.Topics = "Chủ đề mới hoàn toàn để đổi hash";
            await db.SaveChangesAsync();
        }

        var callsABefore = vision.CallsA;
        var callsBBefore = vision.CallsB;
        scorer.PassScores[1] = pages.ToDictionary(p => p, _ => 0.95);
        scorer.PassScores[2] = pages.ToDictionary(p => p, _ => 0.95);
        scorer.ResetSequence();
        await pipeline.ProcessDueAsync();

        await using var db2 = new AppDbContext(_options);
        var eval = await db2.CampaignMediaEvaluations.SingleAsync();
        Assert.True(eval.Attempts >= 2, $"Attempts={eval.Attempts} status={eval.Status} reason={eval.ReasonCode}");
        // T13: không gọi lại Vision
        Assert.Equal(callsABefore, vision.CallsA);
        Assert.Equal(callsBBefore, vision.CallsB);
    }

    [Fact]
    public void MaxRescores_DefaultPositive_AndOptionsBound()
    {
        // T14
        Assert.True(new FullMediaPipelineOptions().MaxRescores > 0);
        var path = Path.GetFullPath(Path.Combine(
            AppContext.BaseDirectory, "..", "..", "..", "..", "..",
            "backend", "Modules", "Campaign", "FullMedia", "FullMediaPipelineService.cs"));
        Assert.Contains("MaxRescores", File.ReadAllText(path));
    }

    // --- N2 same model ---

    [Fact]
    public async Task VisionABSameModel_NeverAccepted_StaysPending()
    {
        var (campaignId, mediaId, pages) = await SeedFullMediaAsync(pageCount: 1);
        var vision = new FakeVision { SameModelForAB = true };
        var scorer = AcceptAllScorer(pages);
        var pipeline = CreatePipeline(vision, scorer);
        await pipeline.ProcessImageAsync(campaignId, mediaId);

        await using var db = new AppDbContext(_options);
        var eval = await db.CampaignMediaEvaluations.SingleAsync();
        Assert.Equal(CampaignMediaEvaluationStatus.Pending, eval.Status);
        Assert.Equal("vision_not_independent", eval.ReasonCode);
        Assert.Equal(0, await db.Posts.CountAsync());
    }

    // --- Worker (T15–T23) ---

    [Fact]
    public async Task WorkerDisabled_ZeroCalls()
    {
        var counter = new CountingScopeFactory();
        var opts = Options.Create(new FullMediaPipelineOptions { Enabled = false });
        var clock = new FixedTimeProvider(DateTimeOffset.UtcNow);
        var worker = new CampaignFullMediaWorker(
            counter, opts, clock, new FullMediaCircuitBreaker(clock),
            NullLogger<CampaignFullMediaWorker>.Instance);
        using var cts = new CancellationTokenSource();
        await worker.StartAsync(cts.Token);
        await Task.Delay(50);
        await worker.StopAsync(CancellationToken.None);
        Assert.Equal(0, counter.CreateCount); // T15: không mở scope
    }

    [Fact]
    public async Task WorkerEnabled_DefaultFalse_InOptionsAndAppsettings()
    {
        Assert.False(new FullMediaPipelineOptions().Enabled);
        var appsettings = Path.GetFullPath(Path.Combine(
            AppContext.BaseDirectory, "..", "..", "..", "..", "..", "backend", "appsettings.json"));
        var json = File.ReadAllText(appsettings);
        Assert.Contains("\"FullMediaPipeline\"", json);
        Assert.Matches(@"""Enabled""\s*:\s*false", json);
    }

    [Fact]
    public async Task ImageThrows_LoopSurvives()
    {
        var (campaignId, mediaId, pages) = await SeedFullMediaAsync(pageCount: 1);
        var vision = new FakeVision { ThrowOnA = true };
        var scorer = AcceptAllScorer(pages);
        var pipeline = CreatePipeline(vision, scorer, o => { o.Enabled = true; o.MaxImagesPerTick = 2; });
        // không ném ra ngoài
        await pipeline.ProcessDueAsync();
        await using var db = new AppDbContext(_options);
        var eval = await db.CampaignMediaEvaluations.SingleOrDefaultAsync(e => e.MediaAssetId == mediaId);
        Assert.NotNull(eval);
    }

    [Fact]
    [Trait("Category", "Slow")]
    public async Task HungProvider_TimesOut()
    {
        var (campaignId, mediaId, pages) = await SeedFullMediaAsync(pageCount: 1);
        var vision = new FakeVision { HangA = true };
        var scorer = AcceptAllScorer(pages);
        var pipeline = CreatePipeline(vision, scorer, o => o.StageTimeoutSeconds = 1);
        var sw = System.Diagnostics.Stopwatch.StartNew();
        await pipeline.ProcessImageAsync(campaignId, mediaId);
        sw.Stop();
        Assert.True(sw.Elapsed < TimeSpan.FromSeconds(8), $"timeout quá chậm: {sw.Elapsed}"); // T18
    }

    [Fact]
    public void CircuitBreaker_Configured_AndWorkerChecks()
    {
        // T19 / F2: cầu chì trong service + FullMediaCircuitBreaker
        Assert.True(new FullMediaPipelineOptions().CircuitBreakerFailures > 0);
        var root = Path.GetFullPath(Path.Combine(
            AppContext.BaseDirectory, "..", "..", "..", "..", "..",
            "backend", "Modules", "Campaign", "FullMedia"));
        var service = File.ReadAllText(Path.Combine(root, "FullMediaPipelineService.cs"));
        Assert.Contains("CircuitBreakerFailures", service);
        Assert.Contains("RecordInfraFailure", service);
        var breaker = File.ReadAllText(Path.Combine(root, "FullMediaCircuitBreaker.cs"));
        Assert.Contains("_openUntil", breaker);
    }

    [Fact]
    public async Task StoppingToken_StopsFast()
    {
        var opts = Options.Create(new FullMediaPipelineOptions
        {
            Enabled = true,
            StartupDelaySeconds = 30,
            IntervalSeconds = 60,
        });
        var clock = new FixedTimeProvider(DateTimeOffset.UtcNow);
        var worker = new CampaignFullMediaWorker(
            new CountingScopeFactory(), opts, clock, new FullMediaCircuitBreaker(clock),
            NullLogger<CampaignFullMediaWorker>.Instance);
        using var cts = new CancellationTokenSource();
        await worker.StartAsync(cts.Token);
        var sw = System.Diagnostics.Stopwatch.StartNew();
        cts.Cancel();
        await worker.StopAsync(CancellationToken.None);
        sw.Stop();
        Assert.True(sw.Elapsed < TimeSpan.FromSeconds(1));
    }

    [Fact]
    public async Task StuckRunning_ResetOnStart()
    {
        var (campaignId, mediaId, _) = await SeedFullMediaAsync(pageCount: 1);
        await using (var db = new AppDbContext(_options))
        {
            db.CampaignMediaEvaluations.Add(new CampaignMediaEvaluationModel
            {
                Id = Guid.NewGuid(),
                CampaignId = campaignId,
                MediaAssetId = mediaId,
                Status = CampaignMediaEvaluationStatus.Scoring,
                CreatedAt = DateTime.UtcNow,
            });
            await db.SaveChangesAsync();
        }

        var pipeline = CreatePipeline(new FakeVision(), new FakeScorer());
        await pipeline.ResetStuckAsync();

        await using var db2 = new AppDbContext(_options);
        Assert.Equal(CampaignMediaEvaluationStatus.Pending,
            (await db2.CampaignMediaEvaluations.SingleAsync()).Status);
    }

    [Fact]
    public async Task QualityPaused_Skipped()
    {
        var (campaignId, mediaId, pages) = await SeedFullMediaAsync(pageCount: 1);
        await using (var db = new AppDbContext(_options))
        {
            var c = await db.Campaigns.FirstAsync(x => x.Id == campaignId);
            c.QualityPausedAt = DateTime.UtcNow;
            await db.SaveChangesAsync();
        }

        var vision = new FakeVision();
        var pipeline = CreatePipeline(vision, AcceptAllScorer(pages), o => o.Enabled = true);
        await pipeline.ProcessDueAsync();
        Assert.Equal(0, vision.CallsA); // T23
    }

    [Fact]
    public async Task Caps_PerTickPerDayCost()
    {
        // T20
        var opts = new FullMediaPipelineOptions();
        Assert.True(opts.MaxImagesPerTick > 0);
        Assert.True(opts.MaxImagesPerDay > 0);
        Assert.True(opts.MaxDailyCostUsd > 0);
        var path = Path.GetFullPath(Path.Combine(
            AppContext.BaseDirectory, "..", "..", "..", "..", "..",
            "backend", "Modules", "Campaign", "FullMedia", "FullMediaPipelineService.cs"));
        var text = File.ReadAllText(path);
        Assert.Contains("MaxImagesPerTick", text);
        Assert.Contains("MaxImagesPerDay", text);
        Assert.Contains("MaxDailyCostUsd", text);
        Assert.Contains("CampaignMediaScores", text); // T26: cost theo ScoredAt hôm nay
    }

    [Fact]
    public void SequentialOnly_NoTaskRunInPipeline()
    {
        // T21
        var path = Path.GetFullPath(Path.Combine(
            AppContext.BaseDirectory, "..", "..", "..", "..", "..",
            "backend", "Modules", "Campaign", "FullMedia", "FullMediaPipelineService.cs"));
        var text = File.ReadAllText(path);
        Assert.DoesNotContain("Task.Run", text);
        Assert.DoesNotContain("Parallel.", text);
    }

    [Fact]
    public async Task TwoWorkers_ClaimOnce()
    {
        var (campaignId, mediaId, pages) = await SeedFullMediaAsync(pageCount: 1);
        var vision = new FakeVision { DelayMs = 200 };
        var p1 = CreatePipeline(vision, AcceptAllScorer(pages));
        var p2 = CreatePipeline(vision, AcceptAllScorer(pages));
        await Task.WhenAll(
            p1.ProcessImageAsync(campaignId, mediaId),
            p2.ProcessImageAsync(campaignId, mediaId));

        await using var db = new AppDbContext(_options);
        var eval = await db.CampaignMediaEvaluations.SingleAsync();
        Assert.True(eval.Attempts >= 1);
        // Chỉ một lần Accepted (hoặc một claim thắng)
        Assert.Equal(1, await db.Posts.CountAsync(p => p.CampaignId == campaignId));
    }

    [Fact]
    public async Task UserWriteDuringHeldAiCall_NoSqliteBusy()
    {
        // T16: file SQLite thật — pipeline không giữ kết nối khi AI treo
        var path = Path.Combine(Path.GetTempPath(), $"fm-iso-{Guid.NewGuid():N}.db");
        try
        {
            await using var conn = new SqliteConnection($"Data Source={path}");
            await conn.OpenAsync();
            var options = new DbContextOptionsBuilder<AppDbContext>().UseSqlite(conn).Options;
            await using (var db = new AppDbContext(options))
                await db.Database.EnsureCreatedAsync();

            Guid campaignId, mediaId;
            List<Guid> pages;
            await using (var db = new AppDbContext(options))
            {
                (campaignId, mediaId, pages) = await SeedIntoAsync(db, pageCount: 1);
            }

            var hang = new TaskCompletionSource();
            var vision = new FakeVision { HangGate = hang };
            var scorer = AcceptAllScorer(pages);
            var services = BuildServices(options, vision, scorer, new FullMediaPipelineOptions
            {
                Enabled = true,
                MinFitScore = 0.94,
                StageTimeoutSeconds = 30,
            }, _clock);
            var sp = services.BuildServiceProvider();
            var pipeline = sp.GetRequiredService<FullMediaPipelineService>();

            var processTask = pipeline.ProcessImageAsync(campaignId, mediaId);
            await Task.Delay(100); // chờ AI treo

            var sw = System.Diagnostics.Stopwatch.StartNew();
            await using (var db = new AppDbContext(options))
            {
                var c = await db.Campaigns.FirstAsync(x => x.Id == campaignId);
                c.Name = "renamed-during-ai";
                await db.SaveChangesAsync();
            }

            sw.Stop();
            hang.TrySetResult();
            await processTask;
            Assert.True(sw.Elapsed < TimeSpan.FromSeconds(2), $"user write chậm/busy: {sw.Elapsed}");
        }
        finally
        {
            try { File.Delete(path); } catch { /* ignore */ }
        }
    }

    [Fact]
    public void ClaimUses_ExecuteUpdateAsync()
    {
        // T17
        var path = Path.GetFullPath(Path.Combine(
            AppContext.BaseDirectory, "..", "..", "..", "..", "..",
            "backend", "Modules", "Campaign", "FullMedia", "FullMediaPipelineService.cs"));
        Assert.Contains("ExecuteUpdateAsync", File.ReadAllText(path));
    }

    // --- N1 DI ---

    [Fact]
    public void AddFullMediaPipeline_ResolvesPipelineAndWorker()
    {
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["FullMediaPipeline:Enabled"] = "false",
                ["PageFit:TimeoutSeconds"] = "30",
            })
            .Build();
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<IConfiguration>(config);
        services.AddDbContext<AppDbContext>(o => o.UseSqlite("DataSource=:memory:"));
        services.AddSingleton(TimeProvider.System);
        services.AddScoped<CampaignGenerationService>();
        services.AddScoped<RecycleSourcePicker>();
        services.AddScoped<MediaFolderRepository>();
        services.AddScoped<IUserContext, StubUserContext>();
        services.AddFullMediaPipeline(config);
        using var sp = services.BuildServiceProvider();
        Assert.NotNull(sp.GetService<FullMediaPipelineService>());
        Assert.Contains(services, d => d.ImplementationType == typeof(CampaignFullMediaWorker));
    }

    [Fact]
    public void Program_Calls_AddFullMediaPipeline()
    {
        // T25 — phải là lời gọi thật, không chỉ chuỗi trong comment.
        var programPath = Path.GetFullPath(Path.Combine(
            AppContext.BaseDirectory, "..", "..", "..", "..", "..", "backend", "Program.cs"));
        var text = File.ReadAllText(programPath);
        Assert.Contains("builder.Services.AddFullMediaPipeline(builder.Configuration);", text);
        Assert.DoesNotContain("AddScoped<Backend.Modules.Campaign.FullMedia.MediaVisionService>()", text);
    }

    [Fact]
    public void GenerationFlow_FullMedia_IsNine()
    {
        Assert.Equal(9, (int)GenerationFlow.FullMedia);
    }

    [Fact]
    public void Migration_ModeDefault_IsRecycle()
    {
        var path = Directory.GetFiles(
                Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..",
                    "backend", "Migrations")),
                "*AddFullMediaPipeline.cs")
            .Single(f => !f.Contains("Designer"));
        var text = File.ReadAllText(path);
        Assert.Contains("defaultValue: 1", text);
        Assert.DoesNotContain("defaultValue: 0", text);
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
            StageTimeoutSeconds = 5,
        };
        configure?.Invoke(opts);
        var services = BuildServices(_options, vision, scorer, opts, _clock);
        var sp = services.BuildServiceProvider();
        // giữ provider sống qua pipeline (tests ngắn)
        _providers.Add(sp);
        var pipeline = sp.GetRequiredService<FullMediaPipelineService>();
        pipeline.CaptionFactory ??= (_, page, _) => Task.FromResult($"ok-{page}");
        pipeline.Random = new Random(42);
        return pipeline;
    }

    private readonly List<ServiceProvider> _providers = [];

    private static ServiceCollection BuildServices(
        DbContextOptions<AppDbContext> dbOptions,
        FakeVision vision,
        FakeScorer scorer,
        FullMediaPipelineOptions opts,
        TimeProvider clock)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton(clock);
        services.AddSingleton(Options.Create(opts));
        services.AddSingleton(new FullMediaCircuitBreaker(clock));
        services.AddScoped(_ => new AppDbContext(dbOptions));
        services.AddScoped<CampaignGenerationService>();
        services.AddScoped<RecycleSourcePicker>();
        services.AddScoped<MediaFolderRepository>();
        services.AddScoped<IUserContext, StubUserContext>();
        services.AddSingleton<IMediaVisionDescriber>(vision);
        services.AddSingleton<IPageFitScorer>(scorer);
        services.AddScoped<FullMediaPipelineService>();
        return services;
    }

    private static FakeScorer AcceptAllScorer(IReadOnlyList<Guid> pages)
    {
        var s = new FakeScorer();
        s.PassScores[1] = pages.ToDictionary(p => p, _ => 0.95);
        s.PassScores[2] = pages.ToDictionary(p => p, _ => 0.96);
        return s;
    }

    private async Task<(Guid CampaignId, Guid MediaId, List<Guid> Pages)> SeedFullMediaAsync(
        int pageCount, bool emptyProfiles = false, bool emptySecond = false)
    {
        await using var db = new AppDbContext(_options);
        return await SeedIntoAsync(db, pageCount, emptyProfiles, emptySecond);
    }

    private static async Task<(Guid CampaignId, Guid MediaId, List<Guid> Pages)> SeedIntoAsync(
        AppDbContext db, int pageCount, bool emptyProfiles = false, bool emptySecond = false)
    {
        var pages = new List<Guid>();
        for (var i = 0; i < pageCount; i++)
        {
            var ch = Guid.NewGuid();
            pages.Add(ch);
            db.SocialChannels.Add(new SocialChannelModel
            {
                Id = ch,
                PageName = $"Page{i}",
                ExternalPageId = $"ext-{ch:N}",
                Platform = SocialPlatform.Facebook,
                CreatedAt = DateTime.UtcNow,
            });
            var empty = emptyProfiles || (emptySecond && i == 1);
            db.PageContexts.Add(new PageContextModel
            {
                Id = Guid.NewGuid(),
                SocialChannelId = ch,
                BrandName = empty ? "" : $"Brand{i}",
                ToneOfVoice = empty ? "" : "thân thiện",
                Topics = empty ? "" : "du học",
                CtaText = "Liên hệ",
                CreatedAt = DateTime.UtcNow,
            });
        }

        var folderId = Guid.NewGuid();
        db.MediaFolders.Add(new MediaFolderModel
        {
            Id = folderId,
            Name = "DriveSrc",
            CreatedAt = DateTime.UtcNow,
        });
        var mediaId = Guid.NewGuid();
        db.MediaAssets.Add(new MediaAssetModel
        {
            Id = mediaId,
            FolderId = folderId,
            MimeType = "image/jpeg",
            StoragePath = "test.jpg",
            FileName = "test.jpg",
            CreatedAt = DateTime.UtcNow,
        });

        var campaignId = Guid.NewGuid();
        db.Campaigns.Add(new CampaignModel
        {
            Id = campaignId,
            Name = "FM",
            Mode = CampaignMode.FullMedia,
            SourceFolderId = folderId,
            Status = CampaignStatus.Running,
            ScheduleMode = CampaignScheduleMode.AllWeek,
            PublishTimesJson = """["15:00"]""",
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
        return (campaignId, mediaId, pages);
    }

    private sealed class FakeVision : IMediaVisionDescriber
    {
        public int CallsA;
        public int CallsB;
        public bool SameModelForAB;
        public bool ThrowOnA;
        public bool HangA;
        public int DelayMs;
        public TaskCompletionSource? HangGate;

        public Task<VisionDescription> DescribeAsync(Guid mediaAssetId, VisionPass pass, CancellationToken ct = default)
            => Task.FromResult(Desc(pass));

        public async Task<VisionOutcome> GetUsableAsync(Guid mediaAssetId, VisionPass pass, CancellationToken ct = default)
        {
            if (pass == VisionPass.A) Interlocked.Increment(ref CallsA);
            else Interlocked.Increment(ref CallsB);

            if (ThrowOnA && pass == VisionPass.A)
                throw new InvalidOperationException("vision boom");

            if (HangA && pass == VisionPass.A)
            {
                if (HangGate is not null)
                    await HangGate.Task.WaitAsync(ct);
                else
                    await Task.Delay(Timeout.Infinite, ct);
            }

            if (DelayMs > 0)
                await Task.Delay(DelayMs, ct);

            return new VisionOutcome(VisionUsability.Usable, Desc(pass));
        }

        private VisionDescription Desc(VisionPass pass)
        {
            var model = SameModelForAB ? "same-model" : (pass == VisionPass.A ? "model-a" : "model-b");
            return new VisionDescription("t", "a", "tone", "set", "", ["k"], 0.9, false, model);
        }
    }

    private sealed class FakeScorer : IPageFitScorer
    {
        public int Calls;
        public bool ThrowPermanent;
        public Dictionary<int, Dictionary<Guid, double>> PassScores { get; } = new();
        private int _seq;

        public void ResetSequence() => Interlocked.Exchange(ref _seq, 0);

        public Task<PageFitResult> ScoreAsync(
            string imageStateText,
            IReadOnlyList<PageFitCandidate> pages,
            CancellationToken ct = default)
        {
            Interlocked.Increment(ref Calls);
            if (ThrowPermanent)
                throw new PageFitPermanentException("no_logprobs", "Provider không trả logprobs");

            // Lượt 1 / lượt 2 xen kẽ theo thứ tự gọi trên scorer này (mỗi pipeline một instance).
            var pass = ((Interlocked.Increment(ref _seq) - 1) % 2) + 1;
            if (!PassScores.TryGetValue(pass, out var map)
                && !PassScores.TryGetValue(1, out map))
            {
                map = pages.ToDictionary(p => p.ChannelId, _ => 0.5);
            }

            var scores = pages.ToDictionary(
                p => p.ChannelId,
                p => map.TryGetValue(p.ChannelId, out var s) ? s : 0.0);
            return Task.FromResult(new PageFitResult(scores, 0.01m, pages.Count));
        }
    }

    private sealed class CountingScopeFactory : IServiceScopeFactory
    {
        public int CreateCount;
        public IServiceScope CreateScope()
        {
            Interlocked.Increment(ref CreateCount);
            return new EmptyScope();
        }

        private sealed class EmptyScope : IServiceScope
        {
            public IServiceProvider ServiceProvider { get; } = new ServiceCollection().BuildServiceProvider();
            public void Dispose() { }
        }
    }

    private sealed class StubUserContext : IUserContext
    {
        public Guid? GetCurrentUserId() => Guid.Empty;
        public string? GetCurrentUserName() => "test";
        public IReadOnlyList<string> GetCurrentUserRoles() => ["Admin"];
    }

    private sealed class FixedTimeProvider(DateTimeOffset utcNow) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => utcNow;
    }
}
