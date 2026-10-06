using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text.Encodings.Web;
using System.Text.Json;
using Backend.Data;
using Backend.Modules.Category;
using Backend.Modules.ChannelGroup;
using Backend.Modules.ContentCrawl;
using Backend.Modules.GenerationJob;
using Backend.Modules.MediaAsset;
using Backend.Modules.MediaAsset.Enums;
using Backend.Modules.MediaEmbedding;
using Backend.Modules.MediaFolder;
using Backend.Modules.PageContext;
using Backend.Modules.Post;
using Backend.Modules.Post.Enums;
using Backend.Modules.PromptTemplate;
using Backend.Modules.PublishLog;
using Backend.Modules.SocialChannel;
using Backend.Modules.SocialChannel.Enums;
using Backend.Shared;
using Backend.Shared.Ai;
using Backend.Shared.Repositories;
using Backend.Shared.SocialPublish;
using Backend.Shared.Storage;
using Backend.Tests.Modules.MediaAsset;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Xunit;

namespace Backend.Tests.Modules.Post;

/// <summary>
/// R-033 AC calendar-filters-backend-test (cda656fa): SQLite + HTTP cho calendar / list / facets.
/// </summary>
public sealed class PostCalendarFiltersTests : IAsyncLifetime
{
    private static readonly Guid AuthorA = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
    private static readonly Guid AuthorB = Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb");
    private static readonly Guid ChannelFb = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid ChannelTt = Guid.Parse("22222222-2222-2222-2222-222222222222");
    private static readonly Guid ChannelLi = Guid.Parse("33333333-3333-3333-3333-333333333333");
    private static readonly Guid CatSports = Guid.Parse("44444444-4444-4444-4444-444444444444");
    private static readonly Guid CatNews = Guid.Parse("55555555-5555-5555-5555-555555555555");
    private static readonly Guid GroupActive = Guid.Parse("66666666-6666-6666-6666-666666666666");
    private static readonly Guid GroupDeleted = Guid.Parse("77777777-7777-7777-7777-777777777777");

    private static readonly DateTime FromUtc = new(2026, 10, 1, 0, 0, 0, DateTimeKind.Utc);
    private static readonly DateTime ToUtc = new(2026, 10, 8, 0, 0, 0, DateTimeKind.Utc);

    private readonly SqliteConnection _connection;
    private readonly DbContextOptions<AppDbContext> _options;
    private readonly IHost _host;
    private readonly HttpClient _client;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    public PostCalendarFiltersTests()
    {
        _connection = new SqliteConnection("DataSource=:memory:");
        _connection.Open();
        _options = new DbContextOptionsBuilder<AppDbContext>().UseSqlite(_connection).Options;
        using (var seed = new AppDbContext(_options))
            seed.Database.EnsureCreated();

        var options = _options;
        _host = new HostBuilder()
            .ConfigureWebHost(web =>
            {
                web.UseTestServer();
                web.ConfigureServices(services =>
                {
                    services.AddSingleton(options);
                    services.AddScoped(_ => new AppDbContext(options));
                    services.AddHttpContextAccessor();
                    services.AddScoped<IUserContext, HttpUserContext>();
                    services.AddScoped<MediaFolderRepository>();
                    services.AddScoped<MediaAssetRepository>();
                    services.AddScoped<PageContextRepository>();
                    services.AddScoped<PromptTemplateRepository>();
                    services.AddScoped<SocialChannelRepository>();
                    services.AddScoped<ChannelGroupRepository>();
                    services.AddScoped<MediaEmbeddingRepository>();
                    services.AddScoped<GenerationJobRepository>();
                    services.AddScoped<PublishLogRepository>();
                    services.AddScoped<PostRepository>();
                    services.AddScoped<PostMediaRepository>();
                    services.AddScoped<PostWorkflowService>();
                    services.AddScoped<PostFromMediaService>();
                    services.AddScoped<PostRecycleService>();
                    services.AddScoped<AiImageFolderService>();
                    services.AddScoped<MediaIntelligenceService>();
                    services.AddScoped<GenerationJobPipelineService>();
                    services.AddScoped<PublishPipelineService>();
                    services.AddScoped<IPublishPipelineService>(sp =>
                        sp.GetRequiredService<PublishPipelineService>());
                    services.AddSingleton<IFileStorageService, InMemoryImageStorage>();
                    services.AddSingleton<IImageOverlayService, IdleOverlayService>();
                    services.AddSingleton<IAiTextGenerationService>(new CountingTextService(new AiProbe()));
                    services.AddSingleton<IAiImageGenerationService>(new CountingImageService(new AiProbe()));
                    services.AddSingleton<ISocialPublishService, IdlePublishService>();
                    services.AddSingleton<IOptions<ContentCrawlOptions>>(
                        Options.Create(new ContentCrawlOptions()));
                    services.AddSingleton<IOptions<ReelsOptions>>(Options.Create(new ReelsOptions()));
                    services.AddSingleton<IOptions<AiProvidersOptions>>(
                        Options.Create(new AiProvidersOptions()));
                    services.AddSingleton(new HttpClient());
                    services.AddSingleton<SlideshowVideoRenderService>();
                    services.AddLogging();
                    services.AddAuthentication(CalAuthHandler.SchemeName)
                        .AddScheme<AuthenticationSchemeOptions, CalAuthHandler>(
                            CalAuthHandler.SchemeName, _ => { });
                    services.AddAuthorization();
                    services.AddControllers()
                        .AddApplicationPart(typeof(PostController).Assembly);
                });
                web.Configure(app =>
                {
                    app.UseRouting();
                    app.UseAuthentication();
                    app.UseAuthorization();
                    app.UseEndpoints(endpoints => endpoints.MapControllers());
                });
            })
            .Start();
        _client = _host.GetTestClient();
    }

    public async Task InitializeAsync() => await SeedFixtureAsync();

    public async Task DisposeAsync()
    {
        _client.Dispose();
        await _host.StopAsync();
        _host.Dispose();
        await _connection.DisposeAsync();
    }

    [Fact]
    public async Task StatusFilter_ReturnsOnlyMatching()
    {
        var items = await PostCalendarAsync(new PostCalendarRequest
        {
            FromUtc = FromUtc,
            ToUtc = ToUtc,
            Statuses = [PostStatus.Scheduled]
        });
        Assert.All(items, i => Assert.Equal(PostStatus.Scheduled, i.Status));
        Assert.Contains(items, i => i.Title == "sched-fb-a");
    }

    [Fact]
    public async Task ChannelFilter_OrWithinGroup()
    {
        var items = await PostCalendarAsync(new PostCalendarRequest
        {
            FromUtc = FromUtc,
            ToUtc = ToUtc,
            SocialChannelIds = [ChannelFb, ChannelTt]
        });
        Assert.All(items, i =>
            Assert.True(i.SocialChannelId == ChannelFb || i.SocialChannelId == ChannelTt));
        Assert.DoesNotContain(items, i => i.SocialChannelId == ChannelLi);
    }

    [Fact]
    public async Task ChannelGroup_ResolvesActiveMembers_DeletedGroupEmpty()
    {
        var active = await PostCalendarAsync(new PostCalendarRequest
        {
            FromUtc = FromUtc,
            ToUtc = ToUtc,
            ChannelGroupIds = [GroupActive]
        });
        Assert.NotEmpty(active);
        Assert.All(active, i =>
            Assert.True(i.SocialChannelId == ChannelFb || i.SocialChannelId == ChannelTt));

        var deleted = await PostCalendarAsync(new PostCalendarRequest
        {
            FromUtc = FromUtc,
            ToUtc = ToUtc,
            ChannelGroupIds = [GroupDeleted]
        });
        Assert.Empty(deleted);
    }

    [Fact]
    public async Task AndAcrossFilterGroups_OrWithin()
    {
        var items = await PostCalendarAsync(new PostCalendarRequest
        {
            FromUtc = FromUtc,
            ToUtc = ToUtc,
            SocialChannelIds = [ChannelFb, ChannelLi],
            Statuses = [PostStatus.Published],
            CategoryIds = [CatSports]
        });
        Assert.All(items, i =>
        {
            Assert.True(i.SocialChannelId == ChannelFb || i.SocialChannelId == ChannelLi);
            Assert.Equal(PostStatus.Published, i.Status);
            Assert.Equal(CatSports, i.CategoryId);
        });
    }

    [Fact]
    public async Task SoftDeleted_AndOutsideRange_Excluded_HalfOpenBounds()
    {
        var items = await PostCalendarAsync(new PostCalendarRequest
        {
            FromUtc = FromUtc,
            ToUtc = ToUtc
        });

        Assert.DoesNotContain(items, i => i.Title == "deleted");
        Assert.DoesNotContain(items, i => i.Title == "before-range");
        Assert.DoesNotContain(items, i => i.Title == "at-to-boundary");
        Assert.Contains(items, i => i.Title == "at-from-boundary");
    }

    [Fact]
    public async Task List_Pagination_Order_Keyword()
    {
        var page1 = await PostCalendarListAsync(new PostCalendarRequest
        {
            FromUtc = FromUtc,
            ToUtc = ToUtc,
            Index = 1,
            Size = 2
        });
        Assert.True(page1.Total >= 4);
        Assert.Equal(2, page1.Items.Count);
        Assert.Equal(1, page1.Index);
        Assert.Equal(2, page1.Size);

        var page2 = await PostCalendarListAsync(new PostCalendarRequest
        {
            FromUtc = FromUtc,
            ToUtc = ToUtc,
            Index = 2,
            Size = 2
        });
        Assert.Equal(page1.Total, page2.Total);
        Assert.Equal(2, page2.Items.Count);
        Assert.Empty(page1.Items.Select(i => i.Id).Intersect(page2.Items.Select(i => i.Id)));

        // Sắp theo giờ đăng tăng dần
        var all = await PostCalendarListAsync(new PostCalendarRequest
        {
            FromUtc = FromUtc,
            ToUtc = ToUtc,
            Index = 1,
            Size = 100
        });
        var times = all.Items
            .Select(i => i.ScheduledPublishAt ?? i.PublishedAt)
            .ToList();
        Assert.Equal(times.OrderBy(t => t).ToList(), times);

        var search = await PostCalendarListAsync(new PostCalendarRequest
        {
            FromUtc = FromUtc,
            ToUtc = ToUtc,
            Keyword = "UNICORN",
            Index = 1,
            Size = 20
        });
        Assert.Contains(search.Items, i => i.Title.Contains("unicorn", StringComparison.OrdinalIgnoreCase));
        Assert.All(search.Items, i =>
            Assert.True(
                i.Title.Contains("unicorn", StringComparison.OrdinalIgnoreCase)
                || (i.Content?.Contains("unicorn", StringComparison.OrdinalIgnoreCase) ?? false)));
    }

    [Fact]
    public async Task Facets_ReturnAuthorsAndCategoriesInRange()
    {
        var facets = await PostCalendarFacetsAsync(new PostCalendarRequest
        {
            FromUtc = FromUtc,
            ToUtc = ToUtc
        });

        Assert.Contains(facets.Authors, a => a.UserId == AuthorA && a.Name == "alice");
        Assert.Contains(facets.Authors, a => a.UserId == AuthorB && a.Name == "bob");
        Assert.Contains(facets.Categories, c => c.CategoryId == CatSports && c.Name == "Thể thao");
        Assert.Contains(facets.Categories, c => c.CategoryId == CatNews && c.Name == "Tin tức");
    }

    [Fact]
    public async Task Response_HasEnrichmentFields()
    {
        var items = await PostCalendarAsync(new PostCalendarRequest
        {
            FromUtc = FromUtc,
            ToUtc = ToUtc,
            Keyword = "enrich-me"
        });
        var item = Assert.Single(items);
        Assert.Equal("FB Page", item.ChannelName);
        Assert.Equal(SocialPlatform.Facebook, item.Platform);
        Assert.Equal("Thể thao", item.CategoryName);
        Assert.Equal("alice", item.AuthorName);
        Assert.True(item.MediaCount >= 1);
        Assert.Equal("https://cdn.example/thumb.jpg", item.ThumbnailUrl);
        Assert.Equal(PostTypeClassifier.Image, item.PostType);
    }

    [Fact]
    public async Task PostTypeFilter_MatchesClassifier()
    {
        var shorts = await PostCalendarAsync(new PostCalendarRequest
        {
            FromUtc = FromUtc,
            ToUtc = ToUtc,
            PostTypes = [PostTypeClassifier.ShortVideo]
        });
        Assert.NotEmpty(shorts);
        Assert.All(shorts, i => Assert.Equal(PostTypeClassifier.ShortVideo, i.PostType));

        var news = await PostCalendarAsync(new PostCalendarRequest
        {
            FromUtc = FromUtc,
            ToUtc = ToUtc,
            PostTypes = [PostTypeClassifier.News]
        });
        Assert.NotEmpty(news);
        Assert.All(news, i => Assert.Equal(PostTypeClassifier.News, i.PostType));
    }

    [Fact]
    public async Task NoNewFilters_SameIdsAsLegacyTimeChannelStatus()
    {
        // Hồi quy: chỉ From/To/Statuses/SocialChannelIds → cùng tập id như lọc thời gian+status+kênh.
        var req = new PostCalendarRequest
        {
            FromUtc = FromUtc,
            ToUtc = ToUtc,
            Statuses = [PostStatus.Scheduled, PostStatus.Published],
            SocialChannelIds = [ChannelFb]
        };
        var viaApi = await PostCalendarAsync(req);

        await using var scope = _host.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var legacyIds = await db.Set<PostModel>()
            .Where(x => !x.IsDeleted)
            .Where(x =>
                (x.ScheduledPublishAt != null
                    && x.ScheduledPublishAt >= FromUtc && x.ScheduledPublishAt < ToUtc)
                || (x.PublishedAt != null
                    && x.PublishedAt >= FromUtc && x.PublishedAt < ToUtc))
            .Where(x => x.Status == PostStatus.Scheduled || x.Status == PostStatus.Published)
            .Where(x => x.SocialChannelId == ChannelFb)
            .Select(x => x.Id)
            .ToListAsync();

        Assert.Equal(
            legacyIds.OrderBy(x => x).ToList(),
            viaApi.Select(x => x.Id).OrderBy(x => x).ToList());
    }

    // --- helpers ---

    private async Task SeedFixtureAsync()
    {
        await using var db = new AppDbContext(_options);

        db.Users.AddRange(
            new ApplicationUser { Id = AuthorA, UserName = "alice", NormalizedUserName = "ALICE", Email = "a@x.com", NormalizedEmail = "A@X.COM", EmailConfirmed = true, SecurityStamp = "s1" },
            new ApplicationUser { Id = AuthorB, UserName = "bob", NormalizedUserName = "BOB", Email = "b@x.com", NormalizedEmail = "B@X.COM", EmailConfirmed = true, SecurityStamp = "s2" });

        db.Set<SocialChannelModel>().AddRange(
            new SocialChannelModel { Id = ChannelFb, PageName = "FB Page", Platform = SocialPlatform.Facebook, ExternalPageId = "fb1", AccessToken = "t", CreatedAt = DateTime.UtcNow },
            new SocialChannelModel { Id = ChannelTt, PageName = "TT Page", Platform = SocialPlatform.TikTok, ExternalPageId = "tt1", AccessToken = "t", CreatedAt = DateTime.UtcNow },
            new SocialChannelModel { Id = ChannelLi, PageName = "LI Page", Platform = SocialPlatform.LinkedIn, ExternalPageId = "li1", AccessToken = "t", CreatedAt = DateTime.UtcNow });

        db.Set<CategoryModel>().AddRange(
            new CategoryModel { Id = CatSports, Name = "Thể thao", Slug = "the-thao", CreatedAt = DateTime.UtcNow },
            new CategoryModel { Id = CatNews, Name = "Tin tức", Slug = "tin-tuc", CreatedAt = DateTime.UtcNow });

        db.Set<ChannelGroupModel>().AddRange(
            new ChannelGroupModel { Id = GroupActive, Name = "Active", CreatedAt = DateTime.UtcNow },
            new ChannelGroupModel { Id = GroupDeleted, Name = "Deleted", IsDeleted = true, DeletedAt = DateTime.UtcNow, CreatedAt = DateTime.UtcNow });

        db.Set<ChannelGroupMemberModel>().AddRange(
            new ChannelGroupMemberModel { Id = Guid.NewGuid(), ChannelGroupId = GroupActive, SocialChannelId = ChannelFb, CreatedAt = DateTime.UtcNow },
            new ChannelGroupMemberModel { Id = Guid.NewGuid(), ChannelGroupId = GroupActive, SocialChannelId = ChannelTt, CreatedAt = DateTime.UtcNow },
            new ChannelGroupMemberModel { Id = Guid.NewGuid(), ChannelGroupId = GroupDeleted, SocialChannelId = ChannelLi, CreatedAt = DateTime.UtcNow });

        // Posts across channels/statuses/authors/categories/types
        AddPost(db, "sched-fb-a", AuthorA, ChannelFb, PostStatus.Scheduled, CatSports,
            FromUtc.AddHours(2), null, null);
        AddPost(db, "pub-tt-b", AuthorB, ChannelTt, PostStatus.Published, CatNews,
            null, FromUtc.AddHours(5), null);
        AddPost(db, "pub-li-sports", AuthorA, ChannelLi, PostStatus.Published, CatSports,
            null, FromUtc.AddHours(6), null);
        AddPost(db, "fail-fb", AuthorB, ChannelFb, PostStatus.Failed, CatNews,
            FromUtc.AddHours(7), null, null);
        AddPost(db, "at-from-boundary", AuthorA, ChannelFb, PostStatus.Scheduled, null,
            FromUtc, null, null);
        AddPost(db, "at-to-boundary", AuthorA, ChannelFb, PostStatus.Scheduled, null,
            ToUtc, null, null); // excluded (half-open)
        AddPost(db, "before-range", AuthorA, ChannelFb, PostStatus.Scheduled, null,
            FromUtc.AddDays(-1), null, null);
        AddPost(db, "deleted", AuthorA, ChannelFb, PostStatus.Scheduled, null,
            FromUtc.AddHours(3), null, null, isDeleted: true);
        AddPost(db, "Find Unicorn Title", AuthorA, ChannelLi, PostStatus.Scheduled, CatSports,
            FromUtc.AddHours(8), null, null);
        AddPost(db, "enrich-me", AuthorA, ChannelFb, PostStatus.Scheduled, CatSports,
            FromUtc.AddHours(4), null, null);
        AddPost(db, "news-post", AuthorB, ChannelFb, PostStatus.Published, CatNews,
            null, FromUtc.AddHours(9), Guid.Parse("99999999-9999-9999-9999-999999999999"));
        AddPost(db, "tt-short", AuthorB, ChannelTt, PostStatus.Scheduled, null,
            FromUtc.AddHours(10), null, null);
        AddPost(db, "li-long-video", AuthorA, ChannelLi, PostStatus.Scheduled, null,
            FromUtc.AddHours(11), null, null);

        await db.SaveChangesAsync();

        // Media for enrichment + type classification
        var enrichPost = await db.Set<PostModel>().SingleAsync(p => p.Title == "enrich-me");
        var ttShort = await db.Set<PostModel>().SingleAsync(p => p.Title == "tt-short");
        var liVideo = await db.Set<PostModel>().SingleAsync(p => p.Title == "li-long-video");

        var img = new MediaAssetModel
        {
            Id = Guid.NewGuid(),
            FileName = "thumb.jpg",
            OriginalFileName = "thumb.jpg",
            StoragePath = "/t.jpg",
            PublicUrl = "https://cdn.example/thumb.jpg",
            MimeType = "image/jpeg",
            Source = MediaSource.Upload,
            CreatedAt = DateTime.UtcNow
        };
        var vidTt = new MediaAssetModel
        {
            Id = Guid.NewGuid(),
            FileName = "tt.mp4",
            OriginalFileName = "tt.mp4",
            StoragePath = "/tt.mp4",
            PublicUrl = "https://cdn.example/tt.mp4",
            MimeType = "video/mp4",
            Source = MediaSource.Upload,
            CreatedAt = DateTime.UtcNow
        };
        var vidLi = new MediaAssetModel
        {
            Id = Guid.NewGuid(),
            FileName = "li.mp4",
            OriginalFileName = "li.mp4",
            StoragePath = "/li.mp4",
            PublicUrl = "https://cdn.example/li.mp4",
            MimeType = "video/mp4",
            Source = MediaSource.Upload,
            CreatedAt = DateTime.UtcNow
        };
        db.Set<MediaAssetModel>().AddRange(img, vidTt, vidLi);
        db.Set<PostMediaModel>().AddRange(
            new PostMediaModel
            {
                Id = Guid.NewGuid(),
                PostId = enrichPost.Id,
                MediaId = img.Id,
                MediaRole = MediaRole.Cover,
                SortOrder = 0,
                CreatedAt = DateTime.UtcNow
            },
            new PostMediaModel
            {
                Id = Guid.NewGuid(),
                PostId = ttShort.Id,
                MediaId = vidTt.Id,
                MediaRole = MediaRole.Cover,
                SortOrder = 0,
                CreatedAt = DateTime.UtcNow
            },
            new PostMediaModel
            {
                Id = Guid.NewGuid(),
                PostId = liVideo.Id,
                MediaId = vidLi.Id,
                MediaRole = MediaRole.Cover,
                SortOrder = 0,
                CreatedAt = DateTime.UtcNow
            });
        await db.SaveChangesAsync();
    }

    private static void AddPost(
        AppDbContext db,
        string title,
        Guid userId,
        Guid channelId,
        PostStatus status,
        Guid? categoryId,
        DateTime? scheduled,
        DateTime? published,
        Guid? newsArticleId,
        bool isDeleted = false)
    {
        db.Set<PostModel>().Add(new PostModel
        {
            Id = Guid.NewGuid(),
            Title = title,
            Content = title.Contains("Unicorn", StringComparison.OrdinalIgnoreCase)
                ? "body with unicorn keyword"
                : "body",
            SocialChannelId = channelId,
            CategoryId = categoryId,
            Status = status,
            UserId = userId,
            ScheduledPublishAt = scheduled,
            PublishedAt = published,
            NewsArticleId = newsArticleId,
            IsDeleted = isDeleted,
            DeletedAt = isDeleted ? DateTime.UtcNow : null,
            CreatedAt = DateTime.UtcNow,
            CreatedBy = "seed"
        });
    }

    private async Task<List<PostResponse>> PostCalendarAsync(PostCalendarRequest request)
    {
        using var response = await SendAsync("api/Post/calendar", request);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<ApiResponse<List<PostResponse>>>(JsonOptions);
        Assert.NotNull(body);
        Assert.True(body.Success);
        return body.Data ?? [];
    }

    private async Task<PagedResult<PostResponse>> PostCalendarListAsync(PostCalendarRequest request)
    {
        using var response = await SendAsync("api/Post/calendar/list", request);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content
            .ReadFromJsonAsync<ApiResponse<PagedResult<PostResponse>>>(JsonOptions);
        Assert.NotNull(body);
        Assert.True(body.Success);
        Assert.NotNull(body.Data);
        return body.Data!;
    }

    private async Task<PostCalendarFacetsResponse> PostCalendarFacetsAsync(PostCalendarRequest request)
    {
        using var response = await SendAsync("api/Post/calendar/facets", request);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content
            .ReadFromJsonAsync<ApiResponse<PostCalendarFacetsResponse>>(JsonOptions);
        Assert.NotNull(body);
        Assert.True(body.Success);
        Assert.NotNull(body.Data);
        return body.Data!;
    }

    private async Task<HttpResponseMessage> SendAsync(string url, PostCalendarRequest request)
    {
        using var msg = new HttpRequestMessage(HttpMethod.Post, url);
        msg.Headers.Authorization = new AuthenticationHeaderValue(
            CalAuthHandler.SchemeName, $"Admin:{AuthorA:D}");
        msg.Content = JsonContent.Create(request);
        return await _client.SendAsync(msg);
    }

    private sealed class CalAuthHandler : AuthenticationHandler<AuthenticationSchemeOptions>
    {
        public const string SchemeName = "TestPostCalendar";

        public CalAuthHandler(
            IOptionsMonitor<AuthenticationSchemeOptions> options,
            ILoggerFactory logger,
            UrlEncoder encoder) : base(options, logger, encoder) { }

        protected override Task<AuthenticateResult> HandleAuthenticateAsync()
        {
            var header = Request.Headers.Authorization.ToString();
            if (string.IsNullOrWhiteSpace(header))
                return Task.FromResult(AuthenticateResult.Fail("Missing Authorization header."));

            var value = header.StartsWith(SchemeName + " ", StringComparison.OrdinalIgnoreCase)
                ? header[(SchemeName.Length + 1)..].Trim()
                : header.Trim();
            var parts = value.Split(':', 2);
            if (parts.Length != 2 || !Guid.TryParse(parts[1], out var userId))
                return Task.FromResult(AuthenticateResult.Fail("Invalid test auth payload."));

            var claims = new[]
            {
                new Claim(ClaimTypes.Name, "actor"),
                new Claim(ClaimTypes.NameIdentifier, userId.ToString("D")),
                new Claim(ClaimTypes.Role, parts[0]),
            };
            return Task.FromResult(AuthenticateResult.Success(
                new AuthenticationTicket(
                    new ClaimsPrincipal(new ClaimsIdentity(claims, SchemeName)), SchemeName)));
        }
    }
}
