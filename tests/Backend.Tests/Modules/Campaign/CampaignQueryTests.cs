using System.Text.Json;
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
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Backend.Tests.Modules.Campaign;

/// <summary>
/// CAMPAIGN-01 t3: list/detail/page-posts aggregation — mixed data, isolation, pagination.
/// </summary>
public sealed class CampaignQueryTests : IAsyncLifetime
{
    private static readonly DateTimeOffset FixedNow =
        new(2026, 10, 6, 10, 0, 0, TimeSpan.Zero);

    private readonly SqliteConnection _connection;
    private readonly DbContextOptions<AppDbContext> _options;
    private readonly FakeTimeProvider _clock = new(FixedNow);

    public CampaignQueryTests()
    {
        _connection = new SqliteConnection("DataSource=:memory:");
        _connection.Open();
        _options = new DbContextOptionsBuilder<AppDbContext>().UseSqlite(_connection).Options;
        using var db = new AppDbContext(_options);
        db.Database.EnsureCreated();
    }

    public Task InitializeAsync() => Task.CompletedTask;

    public async Task DisposeAsync() => await _connection.DisposeAsync();

    [Fact]
    public async Task ListSummaries_MixedCampaigns_CorrectCounts_AndIsolation()
    {
        var chA = await SeedChannelAsync("Page A");
        var chB = await SeedChannelAsync("Page B");
        var camp1 = await SeedCampaignAsync("Camp-1", [chA, chB]);
        var camp2 = await SeedCampaignAsync("Camp-2", [chA]);

        // camp1: 2 upcoming, 1 published, 1 failed on A; 1 upcoming on B
        await SeedPostAsync(camp1, chA, PostStatus.Scheduled, FixedNow.UtcDateTime.AddHours(2));
        await SeedPostAsync(camp1, chA, PostStatus.Approved, FixedNow.UtcDateTime.AddHours(3));
        await SeedPostAsync(camp1, chA, PostStatus.Published, FixedNow.UtcDateTime.AddHours(-1));
        await SeedPostAsync(camp1, chA, PostStatus.Failed, FixedNow.UtcDateTime.AddHours(-2));
        await SeedPostAsync(camp1, chB, PostStatus.Queued, FixedNow.UtcDateTime.AddHours(4));
        // Noise: cancelled + other campaign must not leak into camp1
        await SeedPostAsync(camp1, chA, PostStatus.Cancelled, FixedNow.UtcDateTime.AddHours(5));
        await SeedPostAsync(camp2, chA, PostStatus.Scheduled, FixedNow.UtcDateTime.AddHours(1));
        await SeedPostAsync(camp2, chA, PostStatus.Published, FixedNow.UtcDateTime.AddHours(-3));

        var summaries = await CreateService().ListSummariesAsync();
        var s1 = Assert.Single(summaries, x => x.Id == camp1);
        Assert.Equal(2, s1.PageCount);
        Assert.Equal(3, s1.UpcomingCount);
        Assert.Equal(1, s1.PublishedCount);
        Assert.Equal(1, s1.FailedCount);

        var s2 = Assert.Single(summaries, x => x.Id == camp2);
        Assert.Equal(1, s2.PageCount);
        Assert.Equal(1, s2.UpcomingCount);
        Assert.Equal(1, s2.PublishedCount);
        Assert.Equal(0, s2.FailedCount);
    }

    [Fact]
    public async Task Detail_PerPageStats_NextPost_Warnings_IsolateOtherCampaign()
    {
        var chA = await SeedChannelAsync("Alpha");
        var chB = await SeedChannelAsync("Beta");
        var camp1 = await SeedCampaignAsync("Detail-1", [chA, chB]);
        var camp2 = await SeedCampaignAsync("Detail-2", [chA]);

        var nextA = FixedNow.UtcDateTime.AddHours(2);
        var laterA = FixedNow.UtcDateTime.AddHours(5);
        await SeedPostAsync(camp1, chA, PostStatus.Scheduled, nextA);
        await SeedPostAsync(camp1, chA, PostStatus.Scheduled, laterA);
        await SeedPostAsync(camp1, chA, PostStatus.Published, FixedNow.UtcDateTime.AddHours(-1));
        await SeedPostAsync(camp1, chA, PostStatus.Failed, FixedNow.UtcDateTime.AddHours(-2));
        await SeedPostAsync(camp1, chB, PostStatus.Approved, FixedNow.UtcDateTime.AddHours(3));
        // Other campaign on same page — must not affect camp1 page A
        await SeedPostAsync(camp2, chA, PostStatus.Scheduled, FixedNow.UtcDateTime.AddHours(1));
        await SeedPostAsync(camp2, chA, PostStatus.Failed, FixedNow.UtcDateTime.AddHours(-4));

        await WriteWarningAsync(camp1, chB, "Không có bài nguồn phù hợp");

        var detail = await CreateService().GetDetailAsync(camp1);
        Assert.NotNull(detail);
        Assert.Equal(camp1, detail!.Campaign.Id);
        Assert.Equal(2, detail.Pages.Count);

        var pageA = Assert.Single(detail.Pages, p => p.SocialChannelId == chA);
        Assert.Equal(2, pageA.ScheduledCount);
        Assert.Equal(1, pageA.PublishedCount);
        Assert.Equal(1, pageA.FailedCount);
        Assert.Equal(nextA, pageA.NextScheduledAt);
        Assert.Null(pageA.Warning);

        var pageB = Assert.Single(detail.Pages, p => p.SocialChannelId == chB);
        Assert.Equal(1, pageB.ScheduledCount);
        Assert.Equal(0, pageB.PublishedCount);
        Assert.Equal(0, pageB.FailedCount);
        Assert.Equal("Không có bài nguồn phù hợp", pageB.Warning);
    }

    [Fact]
    public async Task PagePosts_Pagination_AndIsolation_AndMediaSnippet()
    {
        var ch = await SeedChannelAsync("Posts");
        var otherCh = await SeedChannelAsync("Other");
        var camp = await SeedCampaignAsync("Posts-Camp", [ch, otherCh]);
        var otherCamp = await SeedCampaignAsync("Other-Camp", [ch]);

        var mediaId = await SeedMediaAsync();
        Guid? firstWithMedia = null;
        for (var i = 0; i < 5; i++)
        {
            var id = await SeedPostAsync(
                camp, ch, PostStatus.Scheduled,
                FixedNow.UtcDateTime.AddHours(i + 1),
                content: $"Nội dung bài số {i} đủ dài để kiểm tra rút gọn " + new string('x', 250));
            if (i == 0)
            {
                firstWithMedia = id;
                await AttachMediaAsync(id, mediaId);
            }
        }

        // Noise: other page in same campaign + other campaign same page
        await SeedPostAsync(camp, otherCh, PostStatus.Scheduled, FixedNow.UtcDateTime.AddHours(10));
        await SeedPostAsync(otherCamp, ch, PostStatus.Scheduled, FixedNow.UtcDateTime.AddHours(11));

        var page1 = await CreateService().ListPagePostsAsync(
            camp, ch, new CampaignPagePostsRequest { Index = 1, Size = 2 });
        Assert.NotNull(page1);
        Assert.Equal(5, page1!.Total);
        Assert.Equal(2, page1.Items.Count);
        Assert.Equal(1, page1.Index);
        Assert.Equal(2, page1.Size);

        var page2 = await CreateService().ListPagePostsAsync(
            camp, ch, new CampaignPagePostsRequest { Index = 2, Size = 2 });
        Assert.Equal(2, page2!.Items.Count);
        Assert.Empty(page1.Items.Select(x => x.Id).Intersect(page2.Items.Select(x => x.Id)));

        var page3 = await CreateService().ListPagePostsAsync(
            camp, ch, new CampaignPagePostsRequest { Index = 3, Size = 2 });
        Assert.Single(page3!.Items);

        var withMedia = Assert.Single(page1.Items.Concat(page2.Items).Concat(page3.Items),
            x => x.Id == firstWithMedia);
        Assert.Equal(1, withMedia.MediaCount);
        Assert.Equal(mediaId, withMedia.PrimaryMediaId);
        Assert.Equal(MediaAssetUrls.Preview(mediaId), withMedia.ThumbnailUrl);
        Assert.NotNull(withMedia.ContentSnippet);
        Assert.True(withMedia.ContentSnippet!.Length <= 200);

        // Other page has only its own post
        var otherPage = await CreateService().ListPagePostsAsync(
            camp, otherCh, new CampaignPagePostsRequest { Index = 1, Size = 20 });
        Assert.Equal(1, otherPage!.Total);
    }

    [Fact]
    public async Task Detail_MissingCampaign_ReturnsNull()
    {
        var detail = await CreateService().GetDetailAsync(Guid.NewGuid());
        Assert.Null(detail);
    }

    private CampaignQueryService CreateService()
    {
        var db = new AppDbContext(_options);
        var user = new StubUserContext();
        var repo = new CampaignRepository(db, user);
        return new CampaignQueryService(db, repo, _clock);
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
            ExternalPageId = Guid.NewGuid().ToString("N"),
            AccessToken = "tok",
            CreatedAt = DateTime.UtcNow,
        });
        await db.SaveChangesAsync();
        return id;
    }

    private async Task<Guid> SeedCampaignAsync(string name, List<Guid> channelIds)
    {
        await using var db = new AppDbContext(_options);
        var id = Guid.NewGuid();
        db.Campaigns.Add(new CampaignModel
        {
            Id = id,
            Name = name,
            MediaType = CampaignMediaType.Image,
            ImageStrategy = CampaignImageStrategy.KeepOld,
            ScheduleMode = CampaignScheduleMode.AllWeek,
            WeekdaysJson = "[]",
            PublishTimesJson = JsonSerializer.Serialize(new[] { "09:00" }),
            JitterMinutes = 0,
            StartDate = new DateTime(2026, 10, 1, 0, 0, 0, DateTimeKind.Utc),
            Status = CampaignStatus.Running,
            CreatedAt = DateTime.UtcNow,
        });
        foreach (var ch in channelIds)
        {
            db.Set<CampaignChannelModel>().Add(new CampaignChannelModel
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

    private async Task<Guid> SeedPostAsync(
        Guid campaignId,
        Guid channelId,
        PostStatus status,
        DateTime? scheduledAt,
        string? content = null)
    {
        await using var db = new AppDbContext(_options);
        var id = Guid.NewGuid();
        db.Posts.Add(new PostModel
        {
            Id = id,
            Title = "t",
            Content = content ?? "body",
            SocialChannelId = channelId,
            CampaignId = campaignId,
            CampaignSlotAt = scheduledAt,
            Status = status,
            ScheduledPublishAt = scheduledAt,
            PublishedAt = status == PostStatus.Published ? scheduledAt : null,
            UserId = Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb"),
            GenerationFlow = GenerationFlow.Recycle,
            CreatedAt = DateTime.UtcNow,
        });
        await db.SaveChangesAsync();
        return id;
    }

    private async Task<Guid> SeedMediaAsync()
    {
        await using var db = new AppDbContext(_options);
        var id = Guid.NewGuid();
        db.MediaAssets.Add(new MediaAssetModel
        {
            Id = id,
            FileName = "a.jpg",
            StoragePath = "a.jpg",
            MimeType = "image/jpeg",
            FileSize = 10,
            Source = MediaSource.Upload,
            CreatedAt = DateTime.UtcNow,
        });
        await db.SaveChangesAsync();
        return id;
    }

    private async Task AttachMediaAsync(Guid postId, Guid mediaId)
    {
        await using var db = new AppDbContext(_options);
        db.Set<PostMediaModel>().Add(new PostMediaModel
        {
            Id = Guid.NewGuid(),
            PostId = postId,
            MediaId = mediaId,
            MediaRole = MediaRole.Primary,
            SortOrder = 0,
            CreatedAt = DateTime.UtcNow,
        });
        await db.SaveChangesAsync();
    }

    private async Task WriteWarningAsync(Guid campaignId, Guid channelId, string message)
    {
        await using var db = new AppDbContext(_options);
        var camp = await db.Campaigns.SingleAsync(c => c.Id == campaignId);
        var payload = new { campaignWarnings = new { pages = new[] { new { channelId, message } } } };
        camp.ExtraJson = JsonSerializer.Serialize(payload);
        await db.SaveChangesAsync();
    }

    private sealed class StubUserContext : IUserContext
    {
        public Guid? GetCurrentUserId() => Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb");
        public string? GetCurrentUserName() => "query-tester";
        public IReadOnlyList<string> GetCurrentUserRoles() => ["Admin"];
    }

    private sealed class FakeTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }
}
