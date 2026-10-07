using Backend.Data;
using Backend.Modules.Campaign;
using Backend.Modules.Campaign.Enums;
using Backend.Modules.MediaAsset;
using Backend.Modules.MediaAsset.Enums;
using Backend.Modules.Post;
using Backend.Modules.Post.Enums;
using Backend.Modules.SocialChannel;
using Backend.Modules.SocialChannel.Enums;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Xunit;

namespace Backend.Tests.Modules.Campaign;

/// <summary>
/// AC campaign-missing-author-log-once-test (628274a5): warning thiếu CreatedByUserId
/// chỉ 1 lần / chiến dịch / lượt GenerateDueAsync.
/// </summary>
public sealed class CampaignMissingAuthorLogOnceTests : IAsyncLifetime
{
    private static readonly Guid AuthorA = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");

    private readonly SqliteConnection _connection;
    private readonly DbContextOptions<AppDbContext> _options;
    private readonly FixedTimeProvider _clock;

    public CampaignMissingAuthorLogOnceTests()
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
    public async Task A_NullAuthor_ThreePages_LogsWarningOnce_PostsUseEmptyUserId()
    {
        var pages = new List<Guid>();
        for (var i = 0; i < 3; i++)
        {
            var ch = await SeedChannelAsync($"P{i}");
            await SeedPublishedWithMediaAsync(ch, $"src-{i}");
            pages.Add(ch);
        }

        var campId = await SeedCampaignAsync("NoAuthor3", pages, createdByUserId: null);
        var logger = new RecordingLogger();
        var result = await CreateService(logger).GenerateDueAsync();

        Assert.True(result.Created > 0);
        Assert.Equal(1, logger.CountMissingAuthorWarnings());
        Assert.Equal(1, logger.CountMissingAuthorWarningsFor(campId));

        await using var db = new AppDbContext(_options);
        var posts = await db.Posts.Where(p => p.CampaignId == campId && !p.IsDeleted).ToListAsync();
        Assert.NotEmpty(posts);
        Assert.All(posts, p => Assert.Equal(Guid.Empty, p.UserId));
    }

    [Fact]
    public async Task B_TwoCampaignsMissingAuthor_TwoWarnings()
    {
        var ch1 = await SeedChannelAsync("C1");
        var ch2 = await SeedChannelAsync("C2");
        await SeedPublishedWithMediaAsync(ch1, "s1");
        await SeedPublishedWithMediaAsync(ch2, "s2");

        var id1 = await SeedCampaignAsync("MissA", [ch1], createdByUserId: null);
        var id2 = await SeedCampaignAsync("MissB", [ch2], createdByUserId: null);

        var logger = new RecordingLogger();
        await CreateService(logger).GenerateDueAsync();

        Assert.Equal(2, logger.CountMissingAuthorWarnings());
        Assert.Equal(1, logger.CountMissingAuthorWarningsFor(id1));
        Assert.Equal(1, logger.CountMissingAuthorWarningsFor(id2));
    }

    [Fact]
    public async Task C_CampaignWithAuthor_NoMissingAuthorWarning()
    {
        var ch = await SeedChannelAsync("WithAuthor");
        await SeedPublishedWithMediaAsync(ch, "src");
        await SeedCampaignAsync("HasAuthor", [ch], createdByUserId: AuthorA);

        var logger = new RecordingLogger();
        var result = await CreateService(logger).GenerateDueAsync();

        Assert.True(result.Created > 0);
        Assert.Equal(0, logger.CountMissingAuthorWarnings());

        await using var db = new AppDbContext(_options);
        var posts = await db.Posts.Where(p => !p.IsDeleted && p.CampaignId != null).ToListAsync();
        Assert.All(posts, p => Assert.Equal(AuthorA, p.UserId));
    }

    private CampaignGenerationService CreateService(RecordingLogger logger)
    {
        var db = new AppDbContext(_options);
        return new CampaignGenerationService(
            db,
            new RecycleSourcePicker(db),
            _clock,
            logger);
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

    private async Task SeedPublishedWithMediaAsync(Guid channelId, string title)
    {
        await using var db = new AppDbContext(_options);
        var postId = Guid.NewGuid();
        var mediaId = Guid.NewGuid();
        db.Posts.Add(new PostModel
        {
            Id = postId,
            Title = title,
            Content = "body",
            SocialChannelId = channelId,
            Status = PostStatus.Published,
            GenerationFlow = GenerationFlow.FullAI,
            UserId = Guid.NewGuid(),
            PublishedAt = DateTime.UtcNow.AddDays(-2),
            CreatedAt = DateTime.UtcNow.AddDays(-3),
        });
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
        await db.SaveChangesAsync();
    }

    private async Task<Guid> SeedCampaignAsync(
        string name, List<Guid> channelIds, Guid? createdByUserId)
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
            PublishTimesJson = "[\"15:00\"]",
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

    private sealed class FixedTimeProvider(DateTimeOffset utcNow) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => utcNow;
    }

    private sealed class RecordingLogger : ILogger<CampaignGenerationService>
    {
        private readonly List<(LogLevel Level, string Message)> _entries = [];

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            _entries.Add((logLevel, formatter(state, exception)));
        }

        public int CountMissingAuthorWarnings() =>
            _entries.Count(e =>
                e.Level == LogLevel.Warning
                && e.Message.Contains("thiếu CreatedByUserId", StringComparison.Ordinal));

        public int CountMissingAuthorWarningsFor(Guid campaignId) =>
            _entries.Count(e =>
                e.Level == LogLevel.Warning
                && e.Message.Contains("thiếu CreatedByUserId", StringComparison.Ordinal)
                && e.Message.Contains(campaignId.ToString(), StringComparison.OrdinalIgnoreCase));
    }
}
