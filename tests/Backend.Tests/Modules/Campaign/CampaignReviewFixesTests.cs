using Backend.Data;
using Backend.Modules.Campaign;
using Backend.Modules.Campaign.Enums;
using Backend.Modules.MediaAsset;
using Backend.Modules.MediaAsset.Enums;
using Backend.Modules.Post;
using Backend.Modules.Post.Enums;
using Backend.Modules.SocialChannel;
using Backend.Modules.SocialChannel.Enums;
using Backend.Shared.Repositories;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Backend.Tests.Modules.Campaign;

/// <summary>
/// CAMPAIGN-01 review fixes: B1 (af4cf5cc), NB1/NB3 (48daab24), NB2 (34550662).
/// </summary>
public sealed class CampaignReviewFixesTests : IAsyncLifetime
{
    private static readonly TimeSpan VnOffset = TimeSpan.FromHours(7);
    private static readonly Guid AuthorA = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
    private static readonly Guid AuthorB = Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb");

    private readonly SqliteConnection _connection;
    private readonly DbContextOptions<AppDbContext> _options;
    private readonly FixedTimeProvider _clock;

    public CampaignReviewFixesTests()
    {
        _connection = new SqliteConnection("DataSource=:memory:");
        _connection.Open();
        _options = new DbContextOptionsBuilder<AppDbContext>().UseSqlite(_connection).Options;
        using (var db = new AppDbContext(_options))
            db.Database.EnsureCreated();

        // Wednesday 2026-10-07 10:00 VN = 03:00 UTC
        _clock = new FixedTimeProvider(new DateTimeOffset(2026, 10, 7, 3, 0, 0, TimeSpan.Zero));
    }

    public Task InitializeAsync() => Task.CompletedTask;
    public async Task DisposeAsync() => await _connection.DisposeAsync();

    // --- News source exclusion (user request 2026-10-07) ---

    [Fact]
    public async Task PickRandom_ExcludesPostsCreatedFromNews_RevertToProve()
    {
        var ch = await SeedChannelAsync("NewsSrc");
        await SeedPublishedWithMediaAsync(ch, hasImage: true, hasVideo: false, title: "organic");
        await SeedPublishedFromNewsAsync(ch, title: "from-news");

        await using (var db = new AppDbContext(_options))
        {
            var picker = new RecycleSourcePicker(db);
            var picked = await picker.PickRandomPublishedAsync(ch, 50, CampaignMediaType.Image);
            Assert.NotEmpty(picked);
            Assert.All(picked, p => Assert.Equal("organic", p.Title));
            Assert.DoesNotContain(picked, p => p.Title == "from-news");
        }

        // Revert-to-prove: bỏ lọc NewsArticleId == null → bài from-news lọt vào.
        await using (var db = new AppDbContext(_options))
        {
            var withoutNewsFilter = await db.Posts
                .Where(p => !p.IsDeleted
                    && p.Status == PostStatus.Published
                    && p.SocialChannelId == ch
                    && p.GenerationFlow != GenerationFlow.TextOnly
                    && db.PostMedias.Any(m =>
                        !m.IsDeleted
                        && m.PostId == p.Id
                        && db.MediaAssets.Any(a =>
                            !a.IsDeleted
                            && a.Id == m.MediaId
                            && a.MimeType.StartsWith("image/")))
                    && !db.PostMedias.Any(m =>
                        !m.IsDeleted
                        && m.PostId == p.Id
                        && db.MediaAssets.Any(a =>
                            !a.IsDeleted
                            && a.Id == m.MediaId
                            && a.MimeType.StartsWith("video/"))))
                .ToListAsync();
            Assert.Contains(withoutNewsFilter, p => p.Title == "from-news" && p.NewsArticleId != null);
            Assert.Contains(withoutNewsFilter, p => p.Title == "organic" && p.NewsArticleId == null);
        }
    }

    // --- B1 / af4cf5cc ---

    [Fact]
    public async Task B1_ImageExcludesMixed_VideoAllowsMixed_RepeatAndRevertToProve()
    {
        var ch = await SeedChannelAsync("MixedSrc");
        await SeedPublishedWithMediaAsync(ch, hasImage: true, hasVideo: false, title: "img-only");
        await SeedPublishedWithMediaAsync(ch, hasImage: false, hasVideo: true, title: "vid-only");
        await SeedPublishedWithMediaAsync(ch, hasImage: true, hasVideo: true, title: "mixed");
        await SeedTextOnlyWithImageAsync(ch, "text-only");
        await SeedPublishedNoMediaAsync(ch, "no-media");
        await SeedDraftWithImageAsync(ch, "draft");

        for (var i = 0; i < 25; i++)
        {
            await using var db = new AppDbContext(_options);
            var picker = new RecycleSourcePicker(db);

            var images = await picker.PickRandomPublishedAsync(ch, 50, CampaignMediaType.Image);
            Assert.NotEmpty(images);
            Assert.All(images, p => Assert.Equal("img-only", p.Title));

            var videos = await picker.PickRandomPublishedAsync(ch, 50, CampaignMediaType.Video);
            Assert.NotEmpty(videos);
            Assert.All(videos, p => Assert.True(p.Title is "vid-only" or "mixed"));
            Assert.Contains(videos, p => p.Title == "vid-only");
            Assert.Contains(videos, p => p.Title == "mixed");
        }

        // Generation path — chiến dịch Ảnh không lấy mixed
        var imgCamp = await SeedCampaignAsync(
            "ImgMixed", [ch], CampaignMediaType.Image, ["15:00"], createdByUserId: AuthorA);
        await CreateService().GenerateDueAsync();
        await using (var db = new AppDbContext(_options))
        {
            var posts = await db.Posts.Where(p => p.CampaignId == imgCamp).ToListAsync();
            Assert.NotEmpty(posts);
            foreach (var p in posts)
            {
                var src = await db.Posts.SingleAsync(x => x.Id == p.SourcePostId);
                Assert.Equal("img-only", src.Title);
            }
        }

        // Revert-to-prove: bỏ vế loại trừ video → mixed sẽ vào tập ảnh
        await using (var db = new AppDbContext(_options))
        {
            var brokenImageFilter = await db.Posts
                .Where(p => !p.IsDeleted
                    && p.Status == PostStatus.Published
                    && p.SocialChannelId == ch
                    && p.GenerationFlow != GenerationFlow.TextOnly
                    && db.PostMedias.Any(m =>
                        !m.IsDeleted
                        && m.PostId == p.Id
                        && db.MediaAssets.Any(a =>
                            !a.IsDeleted
                            && a.Id == m.MediaId
                            && a.MimeType.StartsWith("image/"))))
                .Select(p => p.Title)
                .ToListAsync();
            Assert.Contains("img-only", brokenImageFilter);
            Assert.Contains("mixed", brokenImageFilter);
        }
    }

    // --- NB1 + NB3 / 48daab24 ---

    [Fact]
    public async Task NB1_UniqueConflict_KeepsSiblingWarnings_AndContinuesSlots()
    {
        var chBusy = await SeedChannelAsync("Busy");
        var chWarn1 = await SeedChannelAsync("Warn1");
        var chWarn2 = await SeedChannelAsync("Warn2");
        await SeedPublishedWithMediaAsync(chBusy, true, false, "src");

        var camp1 = await SeedCampaignAsync(
            "Camp1", [chBusy, chWarn1], CampaignMediaType.Image, ["15:00", "16:00"],
            createdByUserId: AuthorA);
        var camp2 = await SeedCampaignAsync(
            "Camp2", [chWarn2], CampaignMediaType.Image, ["15:00"],
            createdByUserId: AuthorA);

        // Chiếm 1 khe bằng bài Cancelled — existingSet bỏ qua nhưng unique vẫn chặn.
        DateTime occupiedSlot;
        await using (var db = new AppDbContext(_options))
        {
            var campaign = await db.Campaigns.SingleAsync(c => c.Id == camp1);
            var slots = CreateService().BuildSlots(
                campaign,
                ["15:00", "16:00"],
                [],
                _clock.GetUtcNow().UtcDateTime);
            Assert.True(slots.Count >= 2);
            occupiedSlot = slots[0];
            db.Posts.Add(new PostModel
            {
                Id = Guid.NewGuid(),
                Title = "cancelled-occupier",
                SocialChannelId = chBusy,
                Status = PostStatus.Cancelled,
                CampaignId = camp1,
                CampaignSlotAt = occupiedSlot,
                UserId = AuthorA,
                CreatedAt = DateTime.UtcNow,
                GenerationFlow = GenerationFlow.Recycle,
            });
            await db.SaveChangesAsync();
        }

        var result = await CreateService().GenerateDueAsync();
        Assert.True(result.Created > 0);

        await using (var db = new AppDbContext(_options))
        {
            var c1 = await db.Campaigns.SingleAsync(c => c.Id == camp1);
            var c2 = await db.Campaigns.SingleAsync(c => c.Id == camp2);
            var w1 = CampaignGenerationService.ReadWarnings(c1.ExtraJson);
            var w2 = CampaignGenerationService.ReadWarnings(c2.ExtraJson);
            Assert.Contains(w1, w => w.ChannelId == chWarn1);
            Assert.Contains(w2, w => w.ChannelId == chWarn2);

            // (b) khe còn lại của chBusy vẫn sinh
            var busyPosts = await db.Posts
                .Where(p => p.CampaignId == camp1
                    && p.SocialChannelId == chBusy
                    && p.Status != PostStatus.Cancelled
                    && !p.IsDeleted)
                .ToListAsync();
            Assert.NotEmpty(busyPosts);
            Assert.DoesNotContain(busyPosts, p => p.CampaignSlotAt == occupiedSlot);
        }
    }

    [Fact]
    public async Task NB1_RevertToProve_ChangeTrackerClear_DropsCampaignTracking()
    {
        var ch = await SeedChannelAsync("ClearProve");
        var campaignId = await SeedCampaignAsync(
            "ClearCamp", [ch], CampaignMediaType.Image, ["15:00"]);

        await using var db = new AppDbContext(_options);
        var campaign = await db.Campaigns.SingleAsync(c => c.Id == campaignId);
        Assert.Equal(EntityState.Unchanged, db.Entry(campaign).State);

        db.ChangeTracker.Clear();
        Assert.Empty(db.ChangeTracker.Entries());

        campaign.ExtraJson = """{"campaignWarnings":{"pages":[{"channelId":"00000000-0000-0000-0000-000000000001","message":"x"}]}}""";
        await db.SaveChangesAsync(); // detached → không ghi

        await using var check = new AppDbContext(_options);
        var reloaded = await check.Campaigns.SingleAsync(c => c.Id == campaignId);
        Assert.True(
            string.IsNullOrWhiteSpace(reloaded.ExtraJson)
            || !reloaded.ExtraJson.Contains("campaignWarnings", StringComparison.Ordinal));
    }

    [Fact]
    public void NB3_IsUniqueViolation_OnlyExtendedUniqueOrPrimaryKey()
    {
        Assert.True(WrapUnique(2067));
        Assert.True(WrapUnique(1555));
        Assert.False(WrapUnique(1299)); // SQLITE_CONSTRAINT_NOTNULL
        Assert.False(WrapUnique(787));  // SQLITE_CONSTRAINT_FOREIGNKEY
        Assert.False(WrapUnique(19));   // bare CONSTRAINT without UNIQUE/PK extended

        var messageOnly = new DbUpdateException(
            "fail",
            new InvalidOperationException("UNIQUE constraint failed: Posts.CampaignSlotAt"));
        Assert.False(CampaignGenerationService.IsUniqueViolation(messageOnly));
    }

    [Fact]
    public async Task NB3_NotNullConstraint_IsNotSwallowedAsSkipped()
    {
        var ch = await SeedChannelAsync("NotNull");
        await SeedPublishedWithMediaAsync(ch, true, false, "src");
        var campId = await SeedCampaignAsync(
            "NotNullCamp", [ch], CampaignMediaType.Image, ["15:00"], createdByUserId: AuthorA);

        // Trigger NOT NULL giả trên Title khi insert bài chiến dịch.
        await using (var db = new AppDbContext(_options))
        {
            await db.Database.ExecuteSqlRawAsync("""
                CREATE TRIGGER fail_campaign_post_notnull
                BEFORE INSERT ON Posts
                WHEN NEW.CampaignId IS NOT NULL
                BEGIN
                    SELECT RAISE(ABORT, 'NOT NULL constraint failed: Posts.Title');
                END;
                """);
        }

        var ex = await Assert.ThrowsAsync<DbUpdateException>(() => CreateService().GenerateDueAsync());
        Assert.False(CampaignGenerationService.IsUniqueViolation(ex));
        Assert.Equal(0, await CountCampaignPostsAsync(campId));
    }

    // --- NB2 / 34550662 ---

    [Fact]
    public async Task NB2_CreateSetsCreatedByUserId_WorkerCopiesToPost_UpdateKeepsAuthor()
    {
        var ch = await SeedChannelAsync("AuthorCh");
        await SeedPublishedWithMediaAsync(ch, true, false, "src");
        await SeedUserAsync(AuthorA, "author-a");
        await SeedUserAsync(AuthorB, "author-b");

        // (a) Create qua repository (giống API Create)
        await using (var db = new AppDbContext(_options))
        {
            var repo = new CampaignRepository(db, new StubUserContext(AuthorA, "author-a"));
            var created = await repo.CreateAsync(new CreateCampaignRequest
            {
                Name = "AuthorCamp",
                MediaType = CampaignMediaType.Image,
                ImageStrategy = CampaignImageStrategy.KeepOld,
                ChannelIds = [ch],
                ScheduleMode = CampaignScheduleMode.AllWeek,
                PublishTimes = ["15:00"],
                JitterMinutes = 0,
                StartDate = new DateTime(2026, 10, 1, 0, 0, 0, DateTimeKind.Utc),
            });
            Assert.Equal(AuthorA, created.CreatedByUserId);

            // (c) B sửa — CreatedByUserId không đổi
            var updated = await new CampaignRepository(
                new AppDbContext(_options),
                new StubUserContext(AuthorB, "author-b"))
                .UpdateAsync(created.Id, new UpdateCampaignRequest { Name = "RenamedByB" });
            Assert.NotNull(updated);
            Assert.Equal(AuthorA, updated!.CreatedByUserId);
            Assert.Equal("RenamedByB", updated.Name);
        }

        Guid campaignId;
        await using (var db = new AppDbContext(_options))
        {
            campaignId = await db.Campaigns.Where(c => c.Name == "RenamedByB").Select(c => c.Id).SingleAsync();
        }

        await CreateService().GenerateDueAsync();

        await using (var db = new AppDbContext(_options))
        {
            var posts = await db.Posts.Where(p => p.CampaignId == campaignId && !p.IsDeleted).ToListAsync();
            Assert.NotEmpty(posts);
            Assert.All(posts, p => Assert.Equal(AuthorA, p.UserId));
            Assert.DoesNotContain(posts, p => p.UserId == Guid.Empty);

            // Calendar AuthorName + filter Authors
            var postRepo = new PostRepository(db, new StubUserContext(AuthorA, "author-a"));
            var from = _clock.GetUtcNow().UtcDateTime.Date;
            var to = from.AddDays(10);
            var calendar = await postRepo.GetCalendarAsync(new PostCalendarRequest
            {
                FromUtc = from,
                ToUtc = to,
                Authors = [AuthorA],
            });
            Assert.Contains(calendar, x => x.AuthorName == "author-a" && x.UserId == AuthorA);

            var facets = await postRepo.GetCalendarFacetsAsync(new PostCalendarRequest
            {
                FromUtc = from,
                ToUtc = to,
            });
            Assert.Contains(facets.Authors, a => a.UserId == AuthorA && a.Name == "author-a");
        }
    }

    [Fact]
    public async Task NB2_NullCreatedByUserId_UsesEmpty_AndDoesNotCrash()
    {
        var ch = await SeedChannelAsync("Legacy");
        await SeedPublishedWithMediaAsync(ch, true, false, "src");
        var campId = await SeedCampaignAsync(
            "LegacyCamp", [ch], CampaignMediaType.Image, ["15:00"], createdByUserId: null);

        var result = await CreateService().GenerateDueAsync();
        Assert.True(result.Created > 0);

        await using var db = new AppDbContext(_options);
        var posts = await db.Posts.Where(p => p.CampaignId == campId).ToListAsync();
        Assert.All(posts, p => Assert.Equal(Guid.Empty, p.UserId));
    }

    [Fact]
    public async Task NB2_BackfillSql_MatchesAspNetUsersUserName()
    {
        var userId = Guid.Parse("cccccccc-cccc-cccc-cccc-cccccccccccc");
        await SeedUserAsync(userId, "legacy-creator");

        Guid campId;
        await using (var db = new AppDbContext(_options))
        {
            campId = Guid.NewGuid();
            db.Campaigns.Add(new CampaignModel
            {
                Id = campId,
                Name = "PreMigration",
                MediaType = CampaignMediaType.Image,
                ImageStrategy = CampaignImageStrategy.KeepOld,
                ScheduleMode = CampaignScheduleMode.AllWeek,
                WeekdaysJson = "[]",
                PublishTimesJson = "[\"15:00\"]",
                StartDate = new DateTime(2026, 10, 1, 0, 0, 0, DateTimeKind.Utc),
                Status = CampaignStatus.Running,
                CreatedAt = DateTime.UtcNow,
                CreatedBy = "legacy-creator",
                CreatedByUserId = null,
            });
            await db.SaveChangesAsync();

            // Cùng SQL trong migration AddCampaignCreatedByUserId
            await db.Database.ExecuteSqlRawAsync("""
                UPDATE Campaigns
                SET CreatedByUserId = (
                    SELECT u.Id
                    FROM AspNetUsers AS u
                    WHERE u.UserName = Campaigns.CreatedBy
                    LIMIT 1
                )
                WHERE CreatedByUserId IS NULL
                  AND CreatedBy IS NOT NULL
                  AND TRIM(CreatedBy) <> '';
                """);
        }

        await using var check = new AppDbContext(_options);
        var camp = await check.Campaigns.SingleAsync(c => c.Id == campId);
        Assert.Equal(userId, camp.CreatedByUserId);
    }

    // --- helpers ---

    private static bool WrapUnique(int extendedErrorCode)
    {
        var sqlite = new SqliteException("constraint failed", 19, extendedErrorCode);
        return CampaignGenerationService.IsUniqueViolation(new DbUpdateException("save", sqlite));
    }

    private CampaignGenerationService CreateService()
    {
        var db = new AppDbContext(_options);
        return new CampaignGenerationService(
            db,
            new RecycleSourcePicker(db),
            _clock,
            NullLogger<CampaignGenerationService>.Instance);
    }

    private async Task<Guid> SeedChannelAsync(string pageName)
    {
        await using var db = new AppDbContext(_options);
        var id = Guid.NewGuid();
        db.SocialChannels.Add(new SocialChannelModel
        {
            Id = id,
            PageName = pageName,
            Platform = SocialPlatform.Facebook,
            CreatedAt = DateTime.UtcNow,
        });
        await db.SaveChangesAsync();
        return id;
    }

    private async Task SeedUserAsync(Guid id, string userName)
    {
        await using var db = new AppDbContext(_options);
        if (await db.Users.AnyAsync(u => u.Id == id)) return;
        db.Users.Add(new ApplicationUser
        {
            Id = id,
            UserName = userName,
            NormalizedUserName = userName.ToUpperInvariant(),
            Email = $"{userName}@test.local",
            NormalizedEmail = $"{userName}@test.local".ToUpperInvariant(),
            EmailConfirmed = true,
            SecurityStamp = Guid.NewGuid().ToString(),
        });
        await db.SaveChangesAsync();
    }

    private async Task SeedPublishedWithMediaAsync(
        Guid channelId, bool hasImage, bool hasVideo, string title = "src")
    {
        await using var db = new AppDbContext(_options);
        var postId = Guid.NewGuid();
        db.Posts.Add(new PostModel
        {
            Id = postId,
            Title = title,
            Content = "body-" + title,
            SocialChannelId = channelId,
            Status = PostStatus.Published,
            GenerationFlow = GenerationFlow.FullAI,
            UserId = Guid.NewGuid(),
            PublishedAt = DateTime.UtcNow.AddDays(-2),
            CreatedAt = DateTime.UtcNow.AddDays(-3),
        });
        await AddMediaAsync(db, postId, hasImage, hasVideo);
        await db.SaveChangesAsync();
    }

    private async Task SeedPublishedFromNewsAsync(Guid channelId, string title)
    {
        await using var db = new AppDbContext(_options);
        var postId = Guid.NewGuid();
        db.Posts.Add(new PostModel
        {
            Id = postId,
            Title = title,
            Content = "news-body",
            SocialChannelId = channelId,
            Status = PostStatus.Published,
            GenerationFlow = GenerationFlow.FullAI,
            NewsArticleId = Guid.NewGuid(),
            UserId = Guid.NewGuid(),
            PublishedAt = DateTime.UtcNow.AddDays(-1),
            CreatedAt = DateTime.UtcNow.AddDays(-2),
        });
        await AddMediaAsync(db, postId, hasImage: true, hasVideo: false);
        await db.SaveChangesAsync();
    }

    private async Task SeedTextOnlyWithImageAsync(Guid channelId, string title)
    {
        await using var db = new AppDbContext(_options);
        var postId = Guid.NewGuid();
        db.Posts.Add(new PostModel
        {
            Id = postId,
            Title = title,
            Content = "text",
            SocialChannelId = channelId,
            Status = PostStatus.Published,
            GenerationFlow = GenerationFlow.TextOnly,
            UserId = Guid.NewGuid(),
            PublishedAt = DateTime.UtcNow.AddDays(-1),
            CreatedAt = DateTime.UtcNow.AddDays(-2),
        });
        await AddMediaAsync(db, postId, hasImage: true, hasVideo: false);
        await db.SaveChangesAsync();
    }

    private async Task SeedPublishedNoMediaAsync(Guid channelId, string title)
    {
        await using var db = new AppDbContext(_options);
        db.Posts.Add(new PostModel
        {
            Id = Guid.NewGuid(),
            Title = title,
            Content = "none",
            SocialChannelId = channelId,
            Status = PostStatus.Published,
            GenerationFlow = GenerationFlow.FullAI,
            UserId = Guid.NewGuid(),
            PublishedAt = DateTime.UtcNow.AddDays(-1),
            CreatedAt = DateTime.UtcNow.AddDays(-2),
        });
        await db.SaveChangesAsync();
    }

    private async Task SeedDraftWithImageAsync(Guid channelId, string title)
    {
        await using var db = new AppDbContext(_options);
        var postId = Guid.NewGuid();
        db.Posts.Add(new PostModel
        {
            Id = postId,
            Title = title,
            Content = "draft",
            SocialChannelId = channelId,
            Status = PostStatus.Draft,
            GenerationFlow = GenerationFlow.FullAI,
            UserId = Guid.NewGuid(),
            CreatedAt = DateTime.UtcNow.AddDays(-1),
        });
        await AddMediaAsync(db, postId, hasImage: true, hasVideo: false);
        await db.SaveChangesAsync();
    }

    private static async Task AddMediaAsync(
        AppDbContext db, Guid postId, bool hasImage, bool hasVideo)
    {
        if (hasImage)
        {
            var mediaId = Guid.NewGuid();
            db.MediaAssets.Add(new MediaAssetModel
            {
                Id = mediaId,
                FileName = "a.jpg",
                OriginalFileName = "a.jpg",
                StoragePath = "x/a.jpg",
                MimeType = "image/jpeg",
                Source = MediaSource.Upload,
                CreatedAt = DateTime.UtcNow,
            });
            db.PostMedias.Add(new PostMediaModel
            {
                Id = Guid.NewGuid(),
                PostId = postId,
                MediaId = mediaId,
                MediaRole = MediaRole.Cover,
                SortOrder = 0,
                CreatedAt = DateTime.UtcNow,
            });
        }

        if (hasVideo)
        {
            var mediaId = Guid.NewGuid();
            db.MediaAssets.Add(new MediaAssetModel
            {
                Id = mediaId,
                FileName = "v.mp4",
                OriginalFileName = "v.mp4",
                StoragePath = "x/v.mp4",
                MimeType = "video/mp4",
                Source = MediaSource.Upload,
                CreatedAt = DateTime.UtcNow,
            });
            db.PostMedias.Add(new PostMediaModel
            {
                Id = Guid.NewGuid(),
                PostId = postId,
                MediaId = mediaId,
                MediaRole = MediaRole.Cover,
                SortOrder = 1,
                CreatedAt = DateTime.UtcNow,
            });
        }

        await Task.CompletedTask;
    }

    private async Task<Guid> SeedCampaignAsync(
        string name,
        List<Guid> channelIds,
        CampaignMediaType mediaType,
        List<string> times,
        Guid? createdByUserId = null)
    {
        await using var db = new AppDbContext(_options);
        var id = Guid.NewGuid();
        db.Campaigns.Add(new CampaignModel
        {
            Id = id,
            Name = name,
            MediaType = mediaType,
            ImageStrategy = mediaType == CampaignMediaType.Image
                ? CampaignImageStrategy.KeepOld
                : null,
            ScheduleMode = CampaignScheduleMode.AllWeek,
            WeekdaysJson = "[]",
            PublishTimesJson = System.Text.Json.JsonSerializer.Serialize(times),
            JitterMinutes = 0,
            StartDate = new DateTime(2026, 10, 1, 0, 0, 0, DateTimeKind.Utc),
            Status = CampaignStatus.Running,
            CreatedAt = DateTime.UtcNow,
            CreatedByUserId = createdByUserId,
        });
        foreach (var ch in channelIds)
        {
            db.CampaignChannels.Add(new CampaignChannelModel
            {
                Id = Guid.NewGuid(),
                CampaignId = id,
                SocialChannelId = ch,
                CreatedAt = DateTime.UtcNow,
            });
        }
        await db.SaveChangesAsync();
        return id;
    }

    private async Task<int> CountCampaignPostsAsync(Guid campaignId)
    {
        await using var db = new AppDbContext(_options);
        return await db.Posts.CountAsync(p =>
            p.CampaignId == campaignId && !p.IsDeleted && p.Status != PostStatus.Cancelled);
    }

    private sealed class FixedTimeProvider(DateTimeOffset utcNow) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => utcNow;
    }

    private sealed class StubUserContext(Guid userId, string userName) : IUserContext
    {
        public Guid? GetCurrentUserId() => userId;
        public string? GetCurrentUserName() => userName;
        public IReadOnlyList<string> GetCurrentUserRoles() => ["Admin"];
    }
}
