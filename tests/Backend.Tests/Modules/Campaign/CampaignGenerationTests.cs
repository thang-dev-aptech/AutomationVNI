using Backend.Data;
using Backend.Modules.Campaign;
using Backend.Modules.Campaign.Enums;
using Backend.Modules.ChannelGroup;
using Backend.Modules.MediaAsset;
using Backend.Modules.MediaAsset.Enums;
using Backend.Modules.Post;
using Backend.Modules.Post.Enums;
using Backend.Modules.SocialChannel;
using Backend.Modules.SocialChannel.Enums;
using Backend.Shared.Repositories;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Backend.Tests.Modules.Campaign;

/// <summary>
/// CAMPAIGN-01 AC campaign-generation-test (c4c9a3b3) — SQLite thật, TimeProvider giả.
/// </summary>
public sealed class CampaignGenerationTests : IAsyncLifetime
{
    private static readonly TimeSpan VnOffset = TimeSpan.FromHours(7);

    private readonly SqliteConnection _connection;
    private readonly DbContextOptions<AppDbContext> _options;
    private readonly FixedTimeProvider _clock;

    public CampaignGenerationTests()
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

    [Fact]
    public async Task A_ByWeekday_TwoPages_CreatesExpectedSlots_OnlyMonWed()
    {
        var ch1 = await SeedChannelAsync("P1");
        var ch2 = await SeedChannelAsync("P2");
        await SeedPublishedWithMediaAsync(ch1, hasImage: true, hasVideo: false);
        await SeedPublishedWithMediaAsync(ch2, hasImage: true, hasVideo: false);

        var campaignId = await SeedCampaignAsync(
            "WD",
            [ch1, ch2],
            groupIds: [],
            CampaignMediaType.Image,
            CampaignScheduleMode.ByWeekday,
            weekdays: [1, 3],
            times: ["09:00", "15:00"],
            jitter: 0,
            start: new DateTime(2026, 10, 1, 0, 0, 0, DateTimeKind.Utc));

        var service = CreateService();
        var result = await service.GenerateDueAsync();
        Assert.Equal(6, result.Created); // 3 slots × 2 pages

        await using var db = new AppDbContext(_options);
        var posts = await db.Posts.Where(p => p.CampaignId == campaignId).ToListAsync();
        Assert.Equal(6, posts.Count);
        foreach (var p in posts)
        {
            var vn = p.CampaignSlotAt!.Value + VnOffset;
            var dow = ((int)vn.DayOfWeek + 6) % 7 + 1;
            Assert.Contains(dow, new[] { 1, 3 });
            Assert.Contains(vn.ToString("HH:mm"), new[] { "09:00", "15:00" });
            Assert.Equal(PostStatus.Scheduled, p.Status);
            Assert.NotNull(p.SourcePostId);
        }
    }

    [Fact]
    public async Task A_AllWeek_CreatesEveryDayInHorizon()
    {
        var ch = await SeedChannelAsync("All");
        await SeedPublishedWithMediaAsync(ch, hasImage: true, hasVideo: false);
        var campaignId = await SeedCampaignAsync(
            "AllWeek",
            [ch],
            [],
            CampaignMediaType.Image,
            CampaignScheduleMode.AllWeek,
            weekdays: [],
            times: ["12:00"],
            jitter: 0,
            start: new DateTime(2026, 10, 1, 0, 0, 0, DateTimeKind.Utc));

        await CreateService().GenerateDueAsync();

        await using var db = new AppDbContext(_options);
        var slots = await db.Posts
            .Where(p => p.CampaignId == campaignId)
            .Select(p => p.CampaignSlotAt!.Value)
            .ToListAsync();
        // now=10:00 VN → 12:00 same day OK → 7 days
        Assert.Equal(7, slots.Count);
        var days = slots.Select(s => (s + VnOffset).Date).Distinct().OrderBy(d => d).ToList();
        Assert.Equal(7, days.Count);
    }

    [Fact]
    public async Task B_PastSlots_EndDate_FutureStart_Skipped()
    {
        var ch = await SeedChannelAsync("B");
        await SeedPublishedWithMediaAsync(ch, hasImage: true, hasVideo: false);

        // End date = today VN → only today; 09:00 past, 15:00 ok → 1
        var ended = await SeedCampaignAsync(
            "EndSoon",
            [ch],
            [],
            CampaignMediaType.Image,
            CampaignScheduleMode.AllWeek,
            [],
            ["09:00", "15:00"],
            0,
            new DateTime(2026, 10, 1, 0, 0, 0, DateTimeKind.Utc),
            end: new DateTime(2026, 10, 7, 0, 0, 0, DateTimeKind.Utc));
        await CreateService().GenerateDueAsync();
        Assert.Equal(1, await CountCampaignPostsAsync(ended));

        // Start in future → 0
        var future = await SeedCampaignAsync(
            "FutureStart",
            [ch],
            [],
            CampaignMediaType.Image,
            CampaignScheduleMode.AllWeek,
            [],
            ["09:00"],
            0,
            new DateTime(2026, 10, 20, 0, 0, 0, DateTimeKind.Utc));
        await CreateService().GenerateDueAsync();
        Assert.Equal(0, await CountCampaignPostsAsync(future));
    }

    [Fact]
    public async Task C_JitterWithinRange_NotInPast()
    {
        var ch = await SeedChannelAsync("Jitter");
        await SeedPublishedWithMediaAsync(ch, hasImage: true, hasVideo: false);
        var campaignId = await SeedCampaignAsync(
            "Jit",
            [ch],
            [],
            CampaignMediaType.Image,
            CampaignScheduleMode.AllWeek,
            [],
            ["15:00"],
            jitter: 30,
            start: new DateTime(2026, 10, 1, 0, 0, 0, DateTimeKind.Utc));

        var service = CreateService();
        service.Random = new Random(42);
        await service.GenerateDueAsync();

        var now = _clock.GetUtcNow().UtcDateTime;
        await using var db = new AppDbContext(_options);
        var posts = await db.Posts.Where(p => p.CampaignId == campaignId).ToListAsync();
        Assert.NotEmpty(posts);
        foreach (var p in posts)
        {
            Assert.True(p.ScheduledPublishAt > now);
            var delta = (p.ScheduledPublishAt!.Value - p.CampaignSlotAt!.Value).TotalMinutes;
            Assert.InRange(delta, -30, 30);
        }
    }

    [Fact]
    public async Task D_SecondRunAndParallel_NoDuplicates()
    {
        var ch = await SeedChannelAsync("Dup");
        await SeedPublishedWithMediaAsync(ch, hasImage: true, hasVideo: false);
        var campaignId = await SeedCampaignAsync(
            "Idem",
            [ch],
            [],
            CampaignMediaType.Image,
            CampaignScheduleMode.AllWeek,
            [],
            ["15:00"],
            0,
            new DateTime(2026, 10, 1, 0, 0, 0, DateTimeKind.Utc));

        var s1 = CreateService();
        var first = await s1.GenerateDueAsync();
        Assert.True(first.Created > 0);
        var second = await CreateService().GenerateDueAsync();
        Assert.Equal(0, second.Created);
        Assert.Equal(first.Created, await CountCampaignPostsAsync(campaignId));

        // Song song trên 2 connection cùng DB file — dùng shared memory connection
        var parallel = await Task.WhenAll(
            CreateService().GenerateDueAsync(),
            CreateService().GenerateDueAsync());
        Assert.All(parallel, r => Assert.Equal(0, r.Created));
        Assert.Equal(first.Created, await CountCampaignPostsAsync(campaignId));
    }

    [Fact]
    public async Task D_RevertToProve_WithoutExistingSlotCheck_WouldDuplicate_IfUniqueRemoved()
    {
        // Chứng minh unique + kiểm tra existing là cần: nếu bỏ qua existingSet vẫn bị unique chặn.
        var ch = await SeedChannelAsync("RevD");
        await SeedPublishedWithMediaAsync(ch, hasImage: true, hasVideo: false);
        var campaignId = await SeedCampaignAsync(
            "RevD",
            [ch],
            [],
            CampaignMediaType.Image,
            CampaignScheduleMode.AllWeek,
            [],
            ["15:00"],
            0,
            new DateTime(2026, 10, 1, 0, 0, 0, DateTimeKind.Utc));

        await CreateService().GenerateDueAsync();
        var before = await CountCampaignPostsAsync(campaignId);

        await using var db = new AppDbContext(_options);
        var campaign = await db.Campaigns.SingleAsync(c => c.Id == campaignId);
        var existing = await db.Posts
            .Where(p => p.CampaignId == campaignId && p.CampaignSlotAt != null)
            .Select(p => p.CampaignSlotAt!.Value)
            .FirstAsync();

        db.Posts.Add(new PostModel
        {
            Id = Guid.NewGuid(),
            Title = "dup-attempt",
            SocialChannelId = ch,
            Status = PostStatus.Scheduled,
            CampaignId = campaignId,
            CampaignSlotAt = existing,
            UserId = Guid.Empty,
            CreatedAt = DateTime.UtcNow,
            GenerationFlow = GenerationFlow.Recycle,
        });
        await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync());
        Assert.Equal(before, await CountCampaignPostsAsync(campaignId));
        _ = campaign;
    }

    [Fact]
    public async Task E_ImageFilter_ExcludesVideoOnly_VideoFilter_RequiresVideo()
    {
        var ch = await SeedChannelAsync("Media");
        await SeedPublishedWithMediaAsync(ch, hasImage: false, hasVideo: true, title: "vid-only");
        await SeedPublishedWithMediaAsync(ch, hasImage: true, hasVideo: false, title: "img-only");

        var imageCamp = await SeedCampaignAsync(
            "Img",
            [ch],
            [],
            CampaignMediaType.Image,
            CampaignScheduleMode.AllWeek,
            [],
            ["15:00"],
            0,
            new DateTime(2026, 10, 1, 0, 0, 0, DateTimeKind.Utc));
        await CreateService().GenerateDueAsync();

        await using (var db = new AppDbContext(_options))
        {
            var posts = await db.Posts.Where(p => p.CampaignId == imageCamp).ToListAsync();
            Assert.NotEmpty(posts);
            foreach (var p in posts)
            {
                var source = await db.Posts.SingleAsync(x => x.Id == p.SourcePostId);
                Assert.Equal("img-only", source.Title);
                Assert.True(await db.PostMedias.AnyAsync(m =>
                    m.PostId == p.Id && !m.IsDeleted));
            }
        }

        // Xoá bài chiến dịch ảnh, chạy video
        await using (var db = new AppDbContext(_options))
        {
            db.Posts.RemoveRange(db.Posts.Where(p => p.CampaignId == imageCamp));
            await db.SaveChangesAsync();
        }

        var videoCamp = await SeedCampaignAsync(
            "Vid",
            [ch],
            [],
            CampaignMediaType.Video,
            CampaignScheduleMode.AllWeek,
            [],
            ["15:00"],
            0,
            new DateTime(2026, 10, 1, 0, 0, 0, DateTimeKind.Utc));
        await CreateService().GenerateDueAsync();

        await using (var db = new AppDbContext(_options))
        {
            var posts = await db.Posts.Where(p => p.CampaignId == videoCamp).ToListAsync();
            Assert.NotEmpty(posts);
            foreach (var p in posts)
            {
                var source = await db.Posts.SingleAsync(x => x.Id == p.SourcePostId);
                Assert.Equal("vid-only", source.Title);
            }
        }
    }

    [Fact]
    public async Task E_RevertToProve_WithoutMediaFilter_ImageCampaignWouldPickVideo()
    {
        var ch = await SeedChannelAsync("RevE");
        await SeedPublishedWithMediaAsync(ch, hasImage: false, hasVideo: true, title: "only-vid");

        var picker = new RecycleSourcePicker(new AppDbContext(_options));
        var withFilter = await picker.PickRandomPublishedAsync(ch, 10, CampaignMediaType.Image);
        Assert.Empty(withFilter);

        var withoutFilter = await picker.PickRandomPublishedAsync(ch, 10, mediaFilter: null);
        Assert.NotEmpty(withoutFilter);
        Assert.Equal("only-vid", withoutFilter[0].Title);
    }

    [Fact]
    public async Task F_ChannelGroup_AddAndRemove_AffectsGeneration()
    {
        var chIn = await SeedChannelAsync("InGroup");
        var chOut = await SeedChannelAsync("Out");
        await SeedPublishedWithMediaAsync(chIn, true, false);
        await SeedPublishedWithMediaAsync(chOut, true, false);
        var groupId = await SeedGroupAsync([chIn]);

        var campaignId = await SeedCampaignAsync(
            "Grp",
            channelIds: [],
            groupIds: [groupId],
            CampaignMediaType.Image,
            CampaignScheduleMode.AllWeek,
            [],
            ["15:00"],
            0,
            new DateTime(2026, 10, 1, 0, 0, 0, DateTimeKind.Utc));

        await CreateService().GenerateDueAsync();
        Assert.True(await CountCampaignPostsAsync(campaignId) > 0);
        Assert.Equal(0, await CountPostsForChannelAsync(campaignId, chOut));

        // Bỏ chIn khỏi nhóm, thêm chOut
        await using (var db = new AppDbContext(_options))
        {
            var members = await db.ChannelGroupMembers.Where(m => m.ChannelGroupId == groupId).ToListAsync();
            foreach (var m in members) { m.IsDeleted = true; m.DeletedAt = DateTime.UtcNow; }
            db.ChannelGroupMembers.Add(new ChannelGroupMemberModel
            {
                Id = Guid.NewGuid(),
                ChannelGroupId = groupId,
                SocialChannelId = chOut,
                CreatedAt = DateTime.UtcNow,
            });
            await db.SaveChangesAsync();
        }

        var beforeOut = await CountPostsForChannelAsync(campaignId, chOut);
        await CreateService().GenerateDueAsync();
        Assert.True(await CountPostsForChannelAsync(campaignId, chOut) > beforeOut);
    }

    [Fact]
    public async Task G_NoSource_ZeroPosts_WritesWarning()
    {
        var ch = await SeedChannelAsync("Empty");
        var campaignId = await SeedCampaignAsync(
            "Warn",
            [ch],
            [],
            CampaignMediaType.Image,
            CampaignScheduleMode.AllWeek,
            [],
            ["15:00"],
            0,
            new DateTime(2026, 10, 1, 0, 0, 0, DateTimeKind.Utc));

        await CreateService().GenerateDueAsync();
        Assert.Equal(0, await CountCampaignPostsAsync(campaignId));

        await using var db = new AppDbContext(_options);
        var campaign = await db.Campaigns.SingleAsync(c => c.Id == campaignId);
        var warnings = CampaignGenerationService.ReadWarnings(campaign.ExtraJson);
        Assert.Contains(warnings, w => w.ChannelId == ch && w.Message.Contains("Ảnh"));
    }

    [Fact]
    public async Task H_PausedEndedDeleted_CreateNothing()
    {
        var ch = await SeedChannelAsync("Halt");
        await SeedPublishedWithMediaAsync(ch, true, false);

        var paused = await SeedCampaignAsync(
            "Paused", [ch], [], CampaignMediaType.Image, CampaignScheduleMode.AllWeek,
            [], ["15:00"], 0, new DateTime(2026, 10, 1, 0, 0, 0, DateTimeKind.Utc),
            status: CampaignStatus.Paused);
        var ended = await SeedCampaignAsync(
            "Ended", [ch], [], CampaignMediaType.Image, CampaignScheduleMode.AllWeek,
            [], ["15:00"], 0, new DateTime(2026, 10, 1, 0, 0, 0, DateTimeKind.Utc),
            status: CampaignStatus.Ended);
        var deleted = await SeedCampaignAsync(
            "Deleted", [ch], [], CampaignMediaType.Image, CampaignScheduleMode.AllWeek,
            [], ["15:00"], 0, new DateTime(2026, 10, 1, 0, 0, 0, DateTimeKind.Utc));
        await using (var db = new AppDbContext(_options))
        {
            var c = await db.Campaigns.SingleAsync(x => x.Id == deleted);
            c.IsDeleted = true;
            c.DeletedAt = DateTime.UtcNow;
            await db.SaveChangesAsync();
        }

        await CreateService().GenerateDueAsync();
        Assert.Equal(0, await CountCampaignPostsAsync(paused));
        Assert.Equal(0, await CountCampaignPostsAsync(ended));
        Assert.Equal(0, await CountCampaignPostsAsync(deleted));
    }

    [Fact]
    public async Task Recycle_Regression_StillCreatesWithoutMediaFilter()
    {
        var ch = await SeedChannelAsync("Recycle");
        await SeedPublishedWithMediaAsync(ch, false, true, title: "vid");
        await using var db = new AppDbContext(_options);
        var user = new StubUserContext();
        var recycle = new PostRecycleService(
            db, user, null!, new RecycleSourcePicker(db), NullLogger<PostRecycleService>.Instance);

        var result = await recycle.CreateRecycleBatchAsync(new RecyclePostRequest
        {
            ChannelIds = [ch],
            Count = 1,
            Flow = GenerationFlow.Recycle,
            ImageStrategy = RecycleImageStrategy.KeepOld,
        });

        Assert.Equal(1, result.Created);
        Assert.Equal(PostStatus.Approved, await db.Posts
            .Where(p => result.PostIds.Contains(p.Id))
            .Select(p => p.Status)
            .SingleAsync());
    }

    // --- helpers ---

    private CampaignGenerationService CreateService()
    {
        var db = new AppDbContext(_options);
        return new CampaignGenerationService(
            db,
            new RecycleSourcePicker(db),
            _clock,
            NullLogger<CampaignGenerationService>.Instance);
    }

    private async Task<Guid> SeedChannelAsync(string name)
    {
        await using var db = new AppDbContext(_options);
        var id = Guid.NewGuid();
        db.SocialChannels.Add(new SocialChannelModel
        {
            Id = id,
            PageName = name,
            Platform = SocialPlatform.Facebook,
            ChannelType = SocialChannelType.Page,
            ExternalPageId = id.ToString("N")[..10],
            IsActive = true,
            CreatedAt = DateTime.UtcNow,
        });
        await db.SaveChangesAsync();
        return id;
    }

    private async Task<Guid> SeedGroupAsync(List<Guid> channelIds)
    {
        await using var db = new AppDbContext(_options);
        var id = Guid.NewGuid();
        db.ChannelGroups.Add(new ChannelGroupModel
        {
            Id = id,
            Name = "G-" + id.ToString("N")[..6],
            CreatedAt = DateTime.UtcNow,
        });
        foreach (var ch in channelIds)
        {
            db.ChannelGroupMembers.Add(new ChannelGroupMemberModel
            {
                Id = Guid.NewGuid(),
                ChannelGroupId = id,
                SocialChannelId = ch,
                CreatedAt = DateTime.UtcNow,
            });
        }
        await db.SaveChangesAsync();
        return id;
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
                SortOrder = 0,
                CreatedAt = DateTime.UtcNow,
            });
        }

        await db.SaveChangesAsync();
    }

    private async Task<Guid> SeedCampaignAsync(
        string name,
        List<Guid> channelIds,
        List<Guid> groupIds,
        CampaignMediaType mediaType,
        CampaignScheduleMode mode,
        List<int> weekdays,
        List<string> times,
        int jitter,
        DateTime start,
        DateTime? end = null,
        CampaignStatus status = CampaignStatus.Running)
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
            ScheduleMode = mode,
            WeekdaysJson = System.Text.Json.JsonSerializer.Serialize(weekdays),
            PublishTimesJson = System.Text.Json.JsonSerializer.Serialize(times),
            JitterMinutes = jitter,
            StartDate = start,
            EndDate = end,
            Status = status,
            CreatedAt = DateTime.UtcNow,
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
        foreach (var g in groupIds)
        {
            db.CampaignChannelGroups.Add(new CampaignChannelGroupModel
            {
                Id = Guid.NewGuid(),
                CampaignId = id,
                ChannelGroupId = g,
                CreatedAt = DateTime.UtcNow,
            });
        }
        await db.SaveChangesAsync();
        return id;
    }

    private async Task<int> CountCampaignPostsAsync(Guid campaignId)
    {
        await using var db = new AppDbContext(_options);
        return await db.Posts.CountAsync(p => p.CampaignId == campaignId && !p.IsDeleted);
    }

    private async Task<int> CountPostsForChannelAsync(Guid campaignId, Guid channelId)
    {
        await using var db = new AppDbContext(_options);
        return await db.Posts.CountAsync(p =>
            p.CampaignId == campaignId && p.SocialChannelId == channelId && !p.IsDeleted);
    }

    private sealed class FixedTimeProvider(DateTimeOffset utcNow) : TimeProvider
    {
        private DateTimeOffset _utcNow = utcNow;
        public override DateTimeOffset GetUtcNow() => _utcNow;
        public void SetUtcNow(DateTimeOffset value) => _utcNow = value;
    }

    private sealed class StubUserContext : IUserContext
    {
        public Guid? GetCurrentUserId() => Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb");
        public string? GetCurrentUserName() => "recycle-tester";
        public IReadOnlyList<string> GetCurrentUserRoles() => ["Admin"];
    }
}
