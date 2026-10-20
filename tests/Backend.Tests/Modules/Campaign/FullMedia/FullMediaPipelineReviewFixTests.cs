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

/// <summary>Sửa review FAIL d2d6172 — F1..F4 + test mutation thiếu.</summary>
public sealed class FullMediaPipelineReviewFixTests : IAsyncLifetime
{
    private readonly SqliteConnection _connection;
    private readonly DbContextOptions<AppDbContext> _options;
    private readonly AdjustableTimeProvider _clock =
        new(new DateTimeOffset(2026, 10, 7, 3, 0, 0, TimeSpan.Zero));
    private readonly List<ServiceProvider> _providers = [];

    public FullMediaPipelineReviewFixTests()
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

    // --- F1 ---

    [Fact]
    public async Task TwoImages_SamePage_ConsecutiveFreeSlots()
    {
        var (campaignId, folderId, pages) = await SeedCampaignAsync(pageCount: 1, times: ["15:00", "16:00"]);
        var m1 = await SeedMediaAsync(folderId);
        var m2 = await SeedMediaAsync(folderId);
        var vision = new FakeVision();
        var scorer = AcceptAll(pages);
        var pipeline = CreatePipeline(vision, scorer);

        await pipeline.ProcessImageAsync(campaignId, m1);
        await pipeline.ProcessImageAsync(campaignId, m2);

        await using var db = new AppDbContext(_options);
        var posts = await db.Posts.Where(p => p.CampaignId == campaignId).OrderBy(p => p.CreatedAt).ToListAsync();
        Assert.Equal(2, posts.Count);
        Assert.NotEqual(posts[0].CampaignSlotAt, posts[1].CampaignSlotAt);
        Assert.All(posts, p => Assert.Equal(PostStatus.Scheduled, p.Status));
    }

    [Fact]
    public async Task TwoImages_SamePage_ThreePagesFanOut_EachImageOneSharedSlot()
    {
        var (campaignId, folderId, pages) = await SeedCampaignAsync(pageCount: 3, times: ["15:00", "16:00"]);
        var m1 = await SeedMediaAsync(folderId);
        var m2 = await SeedMediaAsync(folderId);
        var pipeline = CreatePipeline(new FakeVision(), AcceptAll(pages));

        await pipeline.ProcessImageAsync(campaignId, m1);
        await pipeline.ProcessImageAsync(campaignId, m2);

        await using var db = new AppDbContext(_options);
        var g1 = await db.Posts.Where(p => p.CampaignId == campaignId)
            .Join(db.PostMedias.Where(m => m.MediaId == m1), p => p.Id, m => m.PostId, (p, _) => p)
            .ToListAsync();
        var g2 = await db.Posts.Where(p => p.CampaignId == campaignId)
            .Join(db.PostMedias.Where(m => m.MediaId == m2), p => p.Id, m => m.PostId, (p, _) => p)
            .ToListAsync();
        Assert.Equal(3, g1.Count);
        Assert.Equal(3, g2.Count);
        Assert.Single(g1.Select(p => p.CampaignSlotAt).Distinct());
        Assert.Single(g2.Select(p => p.CampaignSlotAt).Distinct());
        Assert.NotEqual(g1[0].CampaignSlotAt, g2[0].CampaignSlotAt);
    }

    [Fact]
    public async Task NoFreeSlot_StaysPending_NoRescoreNoVisionCall()
    {
        var (campaignId, folderId, pages) = await SeedCampaignAsync(pageCount: 1, times: ["15:00"]);
        var mediaId = await SeedMediaAsync(folderId);
        await OccupyAllSlotsAsync(campaignId, pages[0]);

        var vision = new FakeVision();
        var pipeline = CreatePipeline(vision, AcceptAll(pages), o => o.Enabled = true);
        await pipeline.ProcessImageAsync(campaignId, mediaId);

        await using (var db = new AppDbContext(_options))
        {
            var eval = await db.CampaignMediaEvaluations.SingleAsync();
            Assert.Equal(CampaignMediaEvaluationStatus.Pending, eval.Status);
            Assert.Equal("no_free_slot", eval.ReasonCode);
            Assert.True(await db.CampaignMediaScores.AnyAsync(s => s.Pass == 2 && s.Passed));
        }

        var callsA = vision.CallsA;
        var callsB = vision.CallsB;
        // Đến giờ thử lại
        _clock.Advance(TimeSpan.FromHours(2));
        await pipeline.ProcessDueAsync();

        Assert.Equal(callsA, vision.CallsA);
        Assert.Equal(callsB, vision.CallsB);
        await using var db2 = new AppDbContext(_options);
        var eval2 = await db2.CampaignMediaEvaluations.SingleAsync();
        Assert.Equal("no_free_slot", eval2.ReasonCode);
        Assert.Equal(CampaignMediaEvaluationStatus.Pending, eval2.Status);
    }

    [Fact]
    public async Task SlotRace_RetriesNextFreeSlot()
    {
        var (campaignId, folderId, pages) = await SeedCampaignAsync(pageCount: 1, times: ["15:00", "16:00"]);
        var mediaId = await SeedMediaAsync(folderId);
        var pipeline = CreatePipeline(new FakeVision(), AcceptAll(pages));
        // Race thật: khe đầu bị chiếm SAU khi pipeline đã tính khe trống (trong lúc sinh caption,
        // trước khi lưu bài) — pipeline phải bắt unique-violation và thử khe 2.
        var raceInserted = 0;
        pipeline.CaptionFactory = async (_, _, _) =>
        {
            if (Interlocked.Exchange(ref raceInserted, 1) == 0)
            {
                await using var db = new AppDbContext(_options);
                var campaign = await db.Campaigns.FirstAsync(c => c.Id == campaignId);
                var slots = CampaignGenerationService.BuildSlots(
                    campaign, ["15:00", "16:00"], [], _clock.GetUtcNow().UtcDateTime);
                Assert.True(slots.Count >= 2);
                db.Posts.Add(new PostModel
                {
                    Id = Guid.NewGuid(),
                    Title = "occ",
                    SocialChannelId = pages[0],
                    CampaignId = campaignId,
                    CampaignSlotAt = slots[0],
                    Status = PostStatus.Scheduled,
                    GenerationFlow = GenerationFlow.FullMedia,
                    UserId = Guid.NewGuid(),
                    CreatedAt = DateTime.UtcNow,
                });
                await db.SaveChangesAsync();
            }

            return "ok-race";
        };
        await pipeline.ProcessImageAsync(campaignId, mediaId);

        await using var db2 = new AppDbContext(_options);
        var post = await db2.Posts.SingleAsync(p => p.CampaignId == campaignId
            && db2.PostMedias.Any(m => m.PostId == p.Id && m.MediaId == mediaId));
        var campaign2 = await db2.Campaigns.FirstAsync(c => c.Id == campaignId);
        var allSlots = CampaignGenerationService.BuildSlots(
            campaign2, ["15:00", "16:00"], [], _clock.GetUtcNow().UtcDateTime);
        Assert.Equal(allSlots[1], post.CampaignSlotAt);
    }

    [Fact]
    public async Task PartialPagesAlreadyPosted_OnlyMissingPageGetsPost()
    {
        var (campaignId, folderId, pages) = await SeedCampaignAsync(pageCount: 2, times: ["15:00", "16:00"]);
        var mediaId = await SeedMediaAsync(folderId);
        await using (var db = new AppDbContext(_options))
        {
            var campaign = await db.Campaigns.FirstAsync(c => c.Id == campaignId);
            var slots = CampaignGenerationService.BuildSlots(
                campaign, ["15:00", "16:00"], [], _clock.GetUtcNow().UtcDateTime);
            var postId = Guid.NewGuid();
            db.Posts.Add(new PostModel
            {
                Id = postId,
                Title = "existing",
                SocialChannelId = pages[0],
                CampaignId = campaignId,
                CampaignSlotAt = slots[0],
                Status = PostStatus.Scheduled,
                GenerationFlow = GenerationFlow.FullMedia,
                UserId = Guid.NewGuid(),
                CreatedAt = DateTime.UtcNow,
            });
            db.PostMedias.Add(new PostMediaModel
            {
                Id = Guid.NewGuid(),
                PostId = postId,
                MediaId = mediaId,
                CreatedAt = DateTime.UtcNow,
            });
            await db.SaveChangesAsync();
        }

        var pipeline = CreatePipeline(new FakeVision(), AcceptAll(pages));
        await pipeline.ProcessImageAsync(campaignId, mediaId);

        await using var db2 = new AppDbContext(_options);
        var postsForMedia = await db2.Posts
            .Where(p => p.CampaignId == campaignId)
            .Join(db2.PostMedias.Where(m => m.MediaId == mediaId), p => p.Id, m => m.PostId, (p, _) => p)
            .ToListAsync();
        Assert.Equal(2, postsForMedia.Count);
        Assert.Contains(postsForMedia, p => p.SocialChannelId == pages[0]);
        Assert.Contains(postsForMedia, p => p.SocialChannelId == pages[1]);
        // Page mới và page cũ không bắt buộc cùng khe (page cũ đã chiếm khe) — page mới dùng khe trống chung.
        Assert.Equal(1, postsForMedia.Count(p => p.SocialChannelId == pages[1]));
    }

    [Fact]
    public async Task NoRepeat_SamePage_DirectCheck()
    {
        var (campaignId, folderId, pages) = await SeedCampaignAsync(pageCount: 1, times: ["15:00", "16:00", "17:00"]);
        var mediaId = await SeedMediaAsync(folderId);
        var pipeline = CreatePipeline(new FakeVision(), AcceptAll(pages));
        await pipeline.ProcessImageAsync(campaignId, mediaId);

        // Ép evaluation về Pending + scores sẵn để thử tạo lại với khe trống
        await using (var db = new AppDbContext(_options))
        {
            var eval = await db.CampaignMediaEvaluations.SingleAsync();
            eval.Status = CampaignMediaEvaluationStatus.Pending;
            eval.ReasonCode = "no_free_slot";
            eval.NextAttemptAt = null;
            await db.SaveChangesAsync();
        }

        await pipeline.ProcessImageAsync(campaignId, mediaId);

        await using var db2 = new AppDbContext(_options);
        Assert.Equal(1, await db2.Posts.CountAsync(p => p.CampaignId == campaignId
            && db2.PostMedias.Any(m => m.PostId == p.Id && m.MediaId == mediaId && p.SocialChannelId == pages[0])));
    }

    // --- F2 ---

    [Fact]
    public async Task CircuitBreaker_OpensAfterNConsecutiveInfraFailures()
    {
        var (campaignId, folderId, pages) = await SeedCampaignAsync(pageCount: 1);
        var medias = new List<Guid>();
        for (var i = 0; i < 3; i++)
            medias.Add(await SeedMediaAsync(folderId));

        var vision = new FakeVision { ThrowOnA = true };
        var circuit = new FullMediaCircuitBreaker(_clock);
        var pipeline = CreatePipeline(vision, AcceptAll(pages), o =>
        {
            o.Enabled = true;
            o.CircuitBreakerFailures = 3;
            o.CircuitBreakerCooldownSeconds = 60;
            o.MaxImagesPerTick = 10;
        }, circuit);

        await pipeline.ProcessDueAsync();
        Assert.True(circuit.IsOpen);
    }

    [Fact]
    public async Task CircuitBreaker_SuccessResetsCounter()
    {
        var (campaignId, folderId, pages) = await SeedCampaignAsync(pageCount: 1);
        var bad = await SeedMediaAsync(folderId);
        var good = await SeedMediaAsync(folderId);
        var vision = new FakeVision();
        vision.ThrowForMedia.Add(bad);
        var circuit = new FullMediaCircuitBreaker(_clock);
        var pipeline = CreatePipeline(vision, AcceptAll(pages), o =>
        {
            o.Enabled = true;
            o.CircuitBreakerFailures = 3;
            o.MaxImagesPerTick = 10;
        }, circuit);

        await pipeline.ProcessDueAsync();
        Assert.False(circuit.IsOpen);
        Assert.Equal(0, circuit.ConsecutiveInfraFailures);
    }

    [Fact]
    public async Task CircuitBreaker_OpenCallsNoAi_ClosesAfterCooldown()
    {
        var (campaignId, folderId, pages) = await SeedCampaignAsync(pageCount: 1);
        for (var i = 0; i < 2; i++)
            await SeedMediaAsync(folderId);

        var vision = new FakeVision { ThrowOnA = true };
        var circuit = new FullMediaCircuitBreaker(_clock);
        var pipeline = CreatePipeline(vision, AcceptAll(pages), o =>
        {
            o.Enabled = true;
            o.CircuitBreakerFailures = 2;
            o.CircuitBreakerCooldownSeconds = 120;
            o.MaxImagesPerTick = 10;
        }, circuit);

        await pipeline.ProcessDueAsync();
        Assert.True(circuit.IsOpen);
        var calls = vision.CallsA;

        await pipeline.ProcessDueAsync();
        Assert.Equal(calls, vision.CallsA);

        _clock.Advance(TimeSpan.FromSeconds(121));
        Assert.False(circuit.IsOpen);
    }

    // --- F3 ---

    [Fact]
    public async Task Pending_BackoffGrows()
    {
        var (campaignId, folderId, pages) = await SeedCampaignAsync(pageCount: 1);
        var mediaId = await SeedMediaAsync(folderId);
        var vision = new FakeVision { ThrowOnA = true };
        var pipeline = CreatePipeline(vision, AcceptAll(pages), o => o.MaxPendingAttempts = 10);

        // 5 → 15 → 60 phút (không cố định 5) — mutation bỏ PendingBackoffMinutes phải đỏ.
        int[] expectedMinutes = [5, 15, 60];
        for (var i = 0; i < expectedMinutes.Length; i++)
        {
            await using (var db = new AppDbContext(_options))
            {
                var e = await db.CampaignMediaEvaluations.SingleOrDefaultAsync(x => x.MediaAssetId == mediaId);
                if (e is not null)
                {
                    e.Status = CampaignMediaEvaluationStatus.Pending;
                    e.NextAttemptAt = null;
                    await db.SaveChangesAsync();
                }
            }

            var processAt = _clock.GetUtcNow().UtcDateTime;
            await pipeline.ProcessImageAsync(campaignId, mediaId);
            await using var db2 = new AppDbContext(_options);
            var eval = await db2.CampaignMediaEvaluations.SingleAsync();
            Assert.NotNull(eval.NextAttemptAt);
            var mins = (eval.NextAttemptAt!.Value - processAt).TotalMinutes;
            Assert.Equal(expectedMinutes[i], mins);
            _clock.Advance(TimeSpan.FromHours(5));
        }
    }

    [Fact]
    public async Task Pending_MaxAttempts_BecomesFailedAndNotifiesOnce()
    {
        var (campaignId, folderId, pages) = await SeedCampaignAsync(pageCount: 1);
        var mediaId = await SeedMediaAsync(folderId);
        var vision = new FakeVision { ThrowOnA = true };
        var pipeline = CreatePipeline(vision, AcceptAll(pages), o => o.MaxPendingAttempts = 2, withNotifications: true);

        for (var i = 0; i < 5; i++)
        {
            await using (var db = new AppDbContext(_options))
            {
                var e = await db.CampaignMediaEvaluations.SingleOrDefaultAsync(x => x.MediaAssetId == mediaId);
                if (e is null) { /* first */ }
                else if (e.Status == CampaignMediaEvaluationStatus.Failed)
                    break;
                else
                {
                    e.Status = CampaignMediaEvaluationStatus.Pending;
                    e.NextAttemptAt = null;
                    await db.SaveChangesAsync();
                }
            }

            await pipeline.ProcessImageAsync(campaignId, mediaId);
            _clock.Advance(TimeSpan.FromHours(5));
        }

        await using var db2 = new AppDbContext(_options);
        var eval = await db2.CampaignMediaEvaluations.SingleAsync();
        Assert.Equal(CampaignMediaEvaluationStatus.Failed, eval.Status);
        Assert.Equal(1, await db2.AppNotifications.CountAsync(n => n.Title.Contains("Failed")));
    }

    [Fact]
    public async Task WaitingReasons_DoNotConsumeAttempts()
    {
        var (campaignId, folderId, _) = await SeedCampaignAsync(pageCount: 1, emptyProfiles: true);
        var mediaId = await SeedMediaAsync(folderId);
        var pipeline = CreatePipeline(new FakeVision(), new FakeScorer());

        await pipeline.ProcessImageAsync(campaignId, mediaId);
        await using var db = new AppDbContext(_options);
        var eval = await db.CampaignMediaEvaluations.SingleAsync();
        Assert.Equal("no_scoreable_pages", eval.ReasonCode);
        Assert.Equal(0, eval.Attempts);
        Assert.Equal(CampaignMediaEvaluationStatus.Pending, eval.Status);
    }

    // --- F4 ---

    [Fact]
    public async Task DailyCost_CountsOnlyTodaysSpend()
    {
        var (campaignId, folderId, pages) = await SeedCampaignAsync(pageCount: 1);
        var mediaId = await SeedMediaAsync(folderId);
        await using (var db = new AppDbContext(_options))
        {
            // Ảnh cũ: CostUsd tích luỹ lớn + score hôm qua
            var evalId = Guid.NewGuid();
            db.CampaignMediaEvaluations.Add(new CampaignMediaEvaluationModel
            {
                Id = evalId,
                CampaignId = campaignId,
                MediaAssetId = Guid.NewGuid(),
                Status = CampaignMediaEvaluationStatus.Accepted,
                CostUsd = 100m,
                UpdatedAt = _clock.GetUtcNow().UtcDateTime.AddDays(-1),
                EvaluatedAt = _clock.GetUtcNow().UtcDateTime.AddDays(-1),
                CreatedAt = DateTime.UtcNow,
            });
            db.CampaignMediaScores.Add(new CampaignMediaScoreModel
            {
                Id = Guid.NewGuid(),
                CampaignId = campaignId,
                MediaAssetId = Guid.NewGuid(),
                SocialChannelId = pages[0],
                Pass = 1,
                Score = 0.95,
                Passed = true,
                CostUsd = 100m,
                ScoredAt = _clock.GetUtcNow().UtcDateTime.AddDays(-1),
                CreatedAt = DateTime.UtcNow,
            });
            await db.SaveChangesAsync();
        }

        var pipeline = CreatePipeline(new FakeVision(), AcceptAll(pages), o =>
        {
            o.Enabled = true;
            o.MaxDailyCostUsd = 1m;
        });
        Assert.True(await pipeline.CanAffordNextAsync(default));
        await pipeline.ProcessImageAsync(campaignId, mediaId);
        await using var db2 = new AppDbContext(_options);
        Assert.Equal(CampaignMediaEvaluationStatus.Accepted,
            (await db2.CampaignMediaEvaluations.SingleAsync(e => e.MediaAssetId == mediaId)).Status);
    }

    [Fact]
    public async Task DailyCostCap_StopsNextImage()
    {
        var (campaignId, folderId, pages) = await SeedCampaignAsync(pageCount: 1);
        var m1 = await SeedMediaAsync(folderId);
        var m2 = await SeedMediaAsync(folderId);
        await using (var db = new AppDbContext(_options))
        {
            db.CampaignMediaScores.Add(new CampaignMediaScoreModel
            {
                Id = Guid.NewGuid(),
                CampaignId = campaignId,
                MediaAssetId = m1,
                SocialChannelId = pages[0],
                Pass = 1,
                Score = 0.95,
                Passed = true,
                CostUsd = 10m,
                ScoredAt = _clock.GetUtcNow().UtcDateTime,
                CreatedAt = DateTime.UtcNow,
            });
            await db.SaveChangesAsync();
        }

        var vision = new FakeVision();
        var pipeline = CreatePipeline(vision, AcceptAll(pages), o =>
        {
            o.Enabled = true;
            o.MaxDailyCostUsd = 10m;
            o.MaxImagesPerTick = 5;
        });
        var n = await pipeline.ProcessDueAsync();
        Assert.Equal(0, n);
        Assert.Equal(0, vision.CallsA);
    }

    // --- Missing mutation tests ---

    [Fact]
    public async Task RejectedSameInputHash_NotRescored_ViaProcessDue()
    {
        var (campaignId, folderId, pages) = await SeedCampaignAsync(pageCount: 1);
        var mediaId = await SeedMediaAsync(folderId);
        var vision = new FakeVision();
        var scorer = new FakeScorer();
        scorer.PassScores[1] = pages.ToDictionary(p => p, _ => 0.5);
        var pipeline = CreatePipeline(vision, scorer, o => o.Enabled = true);
        await pipeline.ProcessImageAsync(campaignId, mediaId);
        // PickNext cần MediaVisionProfiles để tính InputHash (giống production sau Vision thật).
        await SeedVisionProfilesMatchingFakeAsync(mediaId);
        await using (var db = new AppDbContext(_options))
        {
            var eval = await db.CampaignMediaEvaluations.SingleAsync();
            Assert.Equal(CampaignMediaEvaluationStatus.Rejected, eval.Status);
            Assert.False(string.IsNullOrEmpty(eval.InputHash));
        }

        int attemptsBefore;
        await using (var db = new AppDbContext(_options))
            attemptsBefore = (await db.CampaignMediaEvaluations.SingleAsync()).Attempts;

        var calls = vision.CallsA;
        var scoreCalls = scorer.Calls;
        await pipeline.ProcessDueAsync();
        Assert.Equal(calls, vision.CallsA);
        Assert.Equal(scoreCalls, scorer.Calls);
        await using var db2 = new AppDbContext(_options);
        var eval2 = await db2.CampaignMediaEvaluations.SingleAsync();
        Assert.Equal(CampaignMediaEvaluationStatus.Rejected, eval2.Status);
        // Mutation T12 (bỏ so sánh InputHash) sẽ claim lại dù hash giống → Attempts tăng.
        Assert.Equal(attemptsBefore, eval2.Attempts);
    }

    [Fact]
    public async Task ProcessDue_Disabled_ZeroCalls()
    {
        var (campaignId, folderId, pages) = await SeedCampaignAsync(pageCount: 1);
        await SeedMediaAsync(folderId);
        var vision = new FakeVision();
        var pipeline = CreatePipeline(vision, AcceptAll(pages), o => o.Enabled = false);
        Assert.Equal(0, await pipeline.ProcessDueAsync());
        Assert.Equal(0, vision.CallsA);
    }

    [Fact]
    public async Task PausedCampaign_NotPicked_ByPickNext()
    {
        var (campaignId, folderId, pages) = await SeedCampaignAsync(pageCount: 1);
        await SeedMediaAsync(folderId);
        await using (var db = new AppDbContext(_options))
        {
            var c = await db.Campaigns.FirstAsync(x => x.Id == campaignId);
            c.QualityPausedAt = DateTime.UtcNow;
            await db.SaveChangesAsync();
        }

        var pipeline = CreatePipeline(new FakeVision(), AcceptAll(pages), o => o.Enabled = true);
        Assert.Null(await pipeline.PickNextWorkAsync(default));
    }

    [Fact]
    public async Task PausedAfterClaim_ReleasesPending_CampaignPaused()
    {
        var (campaignId, folderId, pages) = await SeedCampaignAsync(pageCount: 1);
        var mediaId = await SeedMediaAsync(folderId);
        var vision = new FakeVision();
        var pipeline = CreatePipeline(vision, AcceptAll(pages));

        await using (var db = new AppDbContext(_options))
        {
            var c = await db.Campaigns.FirstAsync(x => x.Id == campaignId);
            c.QualityPausedAt = DateTime.UtcNow;
            await db.SaveChangesAsync();
        }

        await pipeline.ProcessImageAsync(campaignId, mediaId);
        await using var db2 = new AppDbContext(_options);
        var eval = await db2.CampaignMediaEvaluations.SingleAsync();
        Assert.Equal(CampaignMediaEvaluationStatus.Pending, eval.Status);
        Assert.Equal("campaign_paused", eval.ReasonCode);
        Assert.Equal(0, vision.CallsA);
    }

    // --- helpers ---

    private FullMediaPipelineService CreatePipeline(
        FakeVision vision,
        FakeScorer scorer,
        Action<FullMediaPipelineOptions>? configure = null,
        FullMediaCircuitBreaker? circuit = null,
        bool withNotifications = false)
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
        circuit ??= new FullMediaCircuitBreaker(_clock);
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<TimeProvider>(_clock);
        services.AddSingleton(Options.Create(opts));
        services.AddSingleton(circuit);
        services.AddScoped(_ => new AppDbContext(_options));
        services.AddScoped<CampaignGenerationService>();
        services.AddScoped<RecycleSourcePicker>();
        services.AddScoped<MediaFolderRepository>();
        services.AddScoped<IUserContext, StubUserContext>();
        services.AddSingleton<IMediaVisionDescriber>(vision);
        services.AddSingleton<IPageFitScorer>(scorer);
        if (withNotifications)
            services.AddScoped(sp => new NotificationService(
                sp.GetRequiredService<AppDbContext>(),
                NullLogger<NotificationService>.Instance));
        services.AddScoped<FullMediaPipelineService>();
        var sp = services.BuildServiceProvider();
        _providers.Add(sp);
        var pipeline = sp.GetRequiredService<FullMediaPipelineService>();
        pipeline.CaptionFactory ??= (_, page, _) => Task.FromResult($"ok-{page}");
        pipeline.Random = new Random(42);
        return pipeline;
    }

    private async Task<(Guid CampaignId, Guid FolderId, List<Guid> Pages)> SeedCampaignAsync(
        int pageCount, bool emptyProfiles = false, string[]? times = null)
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
                BrandName = emptyProfiles ? "" : "B",
                ToneOfVoice = emptyProfiles ? "" : "tone",
                Topics = emptyProfiles ? "" : "topics",
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

    private async Task SeedVisionProfilesMatchingFakeAsync(Guid mediaId)
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

    private async Task OccupyAllSlotsAsync(Guid campaignId, Guid channelId)
    {
        await using var db = new AppDbContext(_options);
        var campaign = await db.Campaigns.FirstAsync(c => c.Id == campaignId);
        var times = System.Text.Json.JsonSerializer.Deserialize<List<string>>(campaign.PublishTimesJson) ?? [];
        var slots = CampaignGenerationService.BuildSlots(
            campaign, times, [], _clock.GetUtcNow().UtcDateTime);
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
        public bool ThrowOnA;
        public int DelayMs;
        public HashSet<Guid> ThrowForMedia { get; } = [];

        public Task<VisionDescription> DescribeAsync(Guid mediaAssetId, VisionPass pass, CancellationToken ct = default)
            => Task.FromResult(Desc(pass));

        public async Task<VisionOutcome> GetUsableAsync(Guid mediaAssetId, VisionPass pass, CancellationToken ct = default)
        {
            if (pass == VisionPass.A) Interlocked.Increment(ref CallsA);
            else Interlocked.Increment(ref CallsB);
            if (DelayMs > 0) await Task.Delay(DelayMs, ct);
            if (ThrowOnA && pass == VisionPass.A)
                return new VisionOutcome(VisionUsability.Failed, null, "vision_failed");
            if (ThrowForMedia.Contains(mediaAssetId) && pass == VisionPass.A)
                return new VisionOutcome(VisionUsability.Failed, null, "vision_failed");
            return new VisionOutcome(VisionUsability.Usable, Desc(pass));
        }

        private static VisionDescription Desc(VisionPass pass)
            => new("t", "a", "tone", "set", "", ["k"], 0.9, false,
                pass == VisionPass.A ? "model-a" : "model-b");
    }

    private sealed class FakeScorer : IPageFitScorer
    {
        public int Calls;
        public Dictionary<int, Dictionary<Guid, double>> PassScores { get; } = new();
        private int _seq;

        public Task<PageFitResult> ScoreAsync(
            string imageStateText, IReadOnlyList<PageFitCandidate> pages, CancellationToken ct = default)
        {
            Interlocked.Increment(ref Calls);
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
