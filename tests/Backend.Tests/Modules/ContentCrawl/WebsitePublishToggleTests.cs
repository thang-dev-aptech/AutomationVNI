using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Runtime.CompilerServices;
using System.Security.Claims;
using System.Text.Encodings.Web;
using System.Text.Json;
using Backend.Data;
using Backend.Modules.ContentCrawl;
using Backend.Modules.ContentCrawl.Enums;
using Backend.Modules.NewsSite;
using Backend.Shared.Ai;
using Backend.Shared.Notification;
using Backend.Shared.Repositories;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;

namespace Backend.Tests.Modules.ContentCrawl;

/// <summary>
/// NEWS-PUBLISH-OFF-01: ContentCrawl:WebsitePublishEnabled=false chặn MỌI đường đưa tin cào lên
/// trang tin tức (tự duyệt, quét tồn đọng, Duyệt, /dang, NewsSite/compose, worker viết bài) nhưng
/// giữ nguyên cào + chấm điểm. Bật (mặc định) thì hành vi y như cũ.
/// </summary>
public sealed class WebsitePublishToggleTests : IAsyncLifetime
{
    private readonly SqliteConnection _connection = new("DataSource=:memory:");
    private DbContextOptions<AppDbContext> _dbOptions = null!;

    public async Task InitializeAsync()
    {
        await _connection.OpenAsync();
        _dbOptions = new DbContextOptionsBuilder<AppDbContext>().UseSqlite(_connection).Options;
        await using var db = new AppDbContext(_dbOptions);
        await db.Database.EnsureCreatedAsync();
    }

    public async Task DisposeAsync() => await _connection.DisposeAsync();

    private static ContentCrawlOptions Opt(bool websitePublish, int autoApproveMinScore = 80) => new()
    {
        WebsitePublishEnabled = websitePublish,
        TwoGateFlow = true,
        AutoApproveMinScore = autoApproveMinScore,
        MinQualityScore = 60,
    };

    // ── Tự duyệt theo điểm (ProcessPendingAsync) ────────────────────────────

    [Fact]
    public async Task ProcessPending_Off_ScoresButNeverAutoApprovesToWebsite()
    {
        var id = await SeedArticleAsync(CrawledArticleStatus.New);
        var ai = new FakeAi(score: 90);

        await using (var db = NewDb())
            await CreatePipeline(db, Opt(false), ai).ProcessPendingAsync();

        await using var verify = NewDb();
        var article = await verify.CrawledArticles.SingleAsync(x => x.Id == id);
        Assert.Equal(CrawledArticleStatus.Pending, article.Status);
        Assert.Equal(90, article.QualityScore);
        Assert.Null(article.ReviewedBy);
        Assert.Empty(await verify.NewsArticles.ToListAsync());
        Assert.True(ai.Calls > 0); // vẫn chấm điểm
    }

    [Fact]
    public async Task ProcessPending_On_AutoApprovesHighScoreToWebsiteQueue_AsBefore()
    {
        var id = await SeedArticleAsync(CrawledArticleStatus.New);

        await using (var db = NewDb())
            await CreatePipeline(db, Opt(true), new FakeAi(score: 90)).ProcessPendingAsync();

        await using var verify = NewDb();
        var article = await verify.CrawledArticles.SingleAsync(x => x.Id == id);
        Assert.Equal(CrawledArticleStatus.Approved, article.Status);
        var news = Assert.Single(await verify.NewsArticles.ToListAsync());
        Assert.Equal(id, news.CrawledArticleId);
        Assert.Equal(NewsArticleStatus.Composing, news.Status);
    }

    [Fact]
    public async Task ProcessPending_Off_LowScoreIsStillFiltered()
    {
        var id = await SeedArticleAsync(CrawledArticleStatus.New);

        await using (var db = NewDb())
            await CreatePipeline(db, Opt(false), new FakeAi(score: 30)).ProcessPendingAsync();

        await using var verify = NewDb();
        var article = await verify.CrawledArticles.SingleAsync(x => x.Id == id);
        Assert.Equal(CrawledArticleStatus.Filtered, article.Status);
        Assert.Equal(30, article.QualityScore);
    }

    // ── Duyệt tay + quét tồn đọng ───────────────────────────────────────────

    [Fact]
    public async Task Approve_Off_RejectsBeforeTouchingTheArticle()
    {
        var id = await SeedArticleAsync(CrawledArticleStatus.Pending, score: 95);

        await using (var db = NewDb())
        {
            var ex = await Assert.ThrowsAsync<ArgumentException>(() =>
                CreatePipeline(db, Opt(false), new FakeAi(90)).ApproveAsync(id, new ApproveCrawledArticleRequest()));
            Assert.Contains("đang tắt", ex.Message);
        }

        await using var verify = NewDb();
        var article = await verify.CrawledArticles.SingleAsync(x => x.Id == id);
        Assert.Equal(CrawledArticleStatus.Pending, article.Status);
        Assert.Null(article.ReviewedBy);
        Assert.Null(article.ReviewedAt);
        Assert.Empty(await verify.NewsArticles.ToListAsync());
    }

    [Fact]
    public async Task Approve_On_QueuesWebsiteArticle_AsBefore()
    {
        var id = await SeedArticleAsync(CrawledArticleStatus.Pending, score: 95);

        await using (var db = NewDb())
        {
            var result = await CreatePipeline(db, Opt(true), new FakeAi(90))
                .ApproveAsync(id, new ApproveCrawledArticleRequest());
            Assert.NotNull(result.NewsArticleId);
        }

        await using var verify = NewDb();
        Assert.Equal(CrawledArticleStatus.Approved,
            (await verify.CrawledArticles.SingleAsync(x => x.Id == id)).Status);
        Assert.Single(await verify.NewsArticles.ToListAsync());
    }

    [Fact]
    public async Task Sweep_Off_RejectsAndApprovesNothing()
    {
        var id = await SeedArticleAsync(CrawledArticleStatus.Pending, score: 95);

        await using (var db = NewDb())
        {
            var ex = await Assert.ThrowsAsync<ArgumentException>(() =>
                CreatePipeline(db, Opt(false), new FakeAi(90)).SweepAutoApproveBacklogAsync());
            Assert.Contains("đang tắt", ex.Message);
        }

        await using var verify = NewDb();
        Assert.Equal(CrawledArticleStatus.Pending,
            (await verify.CrawledArticles.SingleAsync(x => x.Id == id)).Status);
        Assert.Empty(await verify.NewsArticles.ToListAsync());
    }

    // ── Hàng đợi viết bài ───────────────────────────────────────────────────

    [Fact]
    public async Task ComposeQueued_Off_LeavesQueuedArticleUntouchedAndCallsNoAi()
    {
        var crawledId = await SeedArticleAsync(CrawledArticleStatus.Approved);
        var newsId = await SeedQueuedNewsAsync(crawledId);
        var ai = new FakeAi(90);

        // NewsPublisher thật nhưng không có NewsComposeService — lọt tới bước viết bài là NRE, test đỏ.
        await using (var db = NewDb())
            await CreatePipeline(db, Opt(false), ai).ComposeQueuedAsync(newsId);

        await using var verify = NewDb();
        var news = await verify.NewsArticles.SingleAsync(x => x.Id == newsId);
        Assert.Equal(NewsArticleStatus.Composing, news.Status);
        Assert.Equal(0, news.ComposeAttemptCount);
        Assert.Equal(0, ai.Calls);
    }

    [Fact]
    public async Task Worker_Off_StillCrawlsAndScores_ButSkipsComposing()
    {
        await using var services = BuildWorkerServices();
        var worker = new SpyWorker(services.GetRequiredService<IServiceScopeFactory>());

        await worker.TickAsync(Opt(false));
        Assert.Equal((1, 1, 0), (worker.Fetch, worker.Process, worker.Compose));

        await worker.TickAsync(Opt(true));
        Assert.Equal((2, 2, 1), (worker.Fetch, worker.Process, worker.Compose));
    }

    [Fact]
    public async Task NewsPublisher_Off_RefusesQueueAndPublish()
    {
        var crawledId = await SeedArticleAsync(CrawledArticleStatus.Approved);
        await using var db = NewDb();
        var crawled = await db.CrawledArticles.SingleAsync(x => x.Id == crawledId);
        var publisher = CreatePublisher(db, Opt(false));

        var queue = await Assert.ThrowsAsync<InvalidOperationException>(() => publisher.QueueAsync(crawled));
        var publish = await Assert.ThrowsAsync<InvalidOperationException>(() => publisher.PublishAsync(crawled));
        Assert.Contains("đang tắt", queue.Message);
        Assert.Contains("đang tắt", publish.Message);
        Assert.Empty(await db.NewsArticles.ToListAsync());
    }

    [Fact]
    public void Defaults_MissingKeyKeepsOldBehavior_ShippedConfigTurnsItOff()
    {
        Assert.True(new ContentCrawlOptions().WebsitePublishEnabled);

        var appsettings = Path.Combine(FindRepoRoot(), "backend", "appsettings.json");
        using var doc = JsonDocument.Parse(File.ReadAllText(appsettings));
        Assert.False(doc.RootElement.GetProperty("ContentCrawl").GetProperty("WebsitePublishEnabled").GetBoolean());
    }

    // ── Telegram ────────────────────────────────────────────────────────────

    [Theory]
    [InlineData("/dang 7")]
    [InlineData("/duyet 7")]
    public async Task TelegramApprove_Off_RepliesDisabledAndLeavesArticlePending(string command)
    {
        var id = await SeedArticleAsync(CrawledArticleStatus.Pending, score: 95, shortCode: 7);
        await using var db = NewDb();
        var telegram = new CrawlTelegramService(
            db, null!, null!, null!, null!, null!, null!, null!,
            Options.Create(new TelegramOptions()), Options.Create(Opt(false)),
            NullLogger<CrawlTelegramService>.Instance);

        var reply = await telegram.HandleCommandAsync(1, command);
        var button = await telegram.HandleCallbackAsync(1, "ok:7");

        Assert.Contains("đang tắt", reply.Text);
        Assert.Contains("đang tắt", button);
        await using var verify = NewDb();
        Assert.Equal(CrawledArticleStatus.Pending,
            (await verify.CrawledArticles.SingleAsync(x => x.Id == id)).Status);
    }

    // ── HTTP ────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Http_Off_ApproveSweepAndComposeReturn400_SummaryReportsFlag()
    {
        var id = await SeedArticleAsync(CrawledArticleStatus.Pending, score: 95);
        var ai = new FakeAi(90);
        using var host = CreateApiHost(Opt(false), ai);
        using var client = host.GetTestClient();

        using (var approve = await client.SendAsync(Post($"/api/ContentCrawl/articles/{id}/approve", "Admin", new { })))
        {
            Assert.Equal(HttpStatusCode.BadRequest, approve.StatusCode);
            Assert.Contains("đang tắt", await approve.Content.ReadAsStringAsync());
        }
        using (var sweep = await client.SendAsync(Post("/api/ContentCrawl/articles/sweep-auto-approve", "Admin")))
            Assert.Equal(HttpStatusCode.BadRequest, sweep.StatusCode);
        using (var compose = await client.SendAsync(Post($"/api/NewsSite/compose/{id}", "Admin")))
        {
            Assert.Equal(HttpStatusCode.BadRequest, compose.StatusCode);
            Assert.Contains("WEBSITE_PUBLISH_DISABLED", await compose.Content.ReadAsStringAsync());
        }

        Assert.False(await ReadSummaryFlagAsync(client));
        Assert.Equal(0, ai.Calls);

        await using var verify = NewDb();
        Assert.Equal(CrawledArticleStatus.Pending,
            (await verify.CrawledArticles.SingleAsync(x => x.Id == id)).Status);
        Assert.Empty(await verify.NewsArticles.ToListAsync());
    }

    [Fact]
    public async Task Http_On_SummaryReportsFlagTrue()
    {
        using var host = CreateApiHost(Opt(true), new FakeAi(90));
        using var client = host.GetTestClient();

        Assert.True(await ReadSummaryFlagAsync(client));
    }

    // ── Hạ tầng ─────────────────────────────────────────────────────────────

    private AppDbContext NewDb() => new(_dbOptions);

    private async Task<Guid> SeedArticleAsync(
        CrawledArticleStatus status, int? score = null, int? shortCode = null)
    {
        await using var db = NewDb();
        var id = Guid.NewGuid();
        db.CrawledArticles.Add(new CrawledArticleModel
        {
            Id = id,
            CrawlSourceId = Guid.NewGuid(),
            Title = $"Bộ GD&ĐT công bố phương án tuyển sinh {id:N}",
            Summary = "Tóm tắt đủ dài để qua các ngưỡng kiểm tra độ dài nội dung của pipeline.",
            Content = string.Join(' ', Enumerable.Repeat("Nội dung chính sách giáo dục chi tiết.", 40)),
            SourceUrl = $"https://example.edu.vn/tin-{id:N}.html",
            FetchedAt = DateTime.UtcNow,
            ContentHash = id.ToString("N"),
            Status = status,
            QualityScore = score,
            ShortCode = shortCode,
        });
        await db.SaveChangesAsync();
        return id;
    }

    private async Task<Guid> SeedQueuedNewsAsync(Guid crawledId)
    {
        await using var db = NewDb();
        var id = Guid.NewGuid();
        db.NewsArticles.Add(new NewsArticleModel
        {
            Id = id,
            CrawledArticleId = crawledId,
            Status = NewsArticleStatus.Composing,
            Slug = $"cho-{id:N}"[..24],
            Title = "Đang chờ viết",
            CreatedAt = DateTime.UtcNow,
        });
        await db.SaveChangesAsync();
        return id;
    }

    private static NewsPublisher CreatePublisher(AppDbContext db, ContentCrawlOptions opt) => new(
        new NewsSiteRepository(db, Options.Create(new NewsSiteOptions())),
        compose: null!, dedup: null!, builder: null!,
        Options.Create(new NewsSiteOptions()), Options.Create(opt),
        NullLogger<NewsPublisher>.Instance);

    private static ContentCrawlPipelineService CreatePipeline(AppDbContext db, ContentCrawlOptions opt, FakeAi ai)
    {
        var options = Options.Create(opt);
        var user = new StubUser();
        return new ContentCrawlPipelineService(
            db,
            new ContentCrawlRepository(db, user, options),
            postRepository: null!,
            pageContextRepository: null!,
            new ContentDedupService(db, ai, options, NullLogger<ContentDedupService>.Instance),
            webCrawler: null!,
            httpFetcher: null!,
            feedFetcher: null!,
            new CrawlScreenService(ai, options, NullLogger<CrawlScreenService>.Instance),
            CreatePublisher(db, opt),
            new NewsSiteRepository(db, Options.Create(new NewsSiteOptions())),
            notifications: null!,
            telegram: null!,
            user,
            options,
            Options.Create(new NewsSiteOptions()),
            NullLogger<ContentCrawlPipelineService>.Instance);
    }

    private ServiceProvider BuildWorkerServices() => new ServiceCollection()
        .AddScoped(_ => NewDb())
        .AddScoped<IUserContext, StubUser>()
        .AddSingleton<IOptions<ContentCrawlOptions>>(Options.Create(new ContentCrawlOptions()))
        .AddScoped<ContentCrawlRepository>()
        .BuildServiceProvider();

    private IHost CreateApiHost(ContentCrawlOptions opt, FakeAi ai) =>
        new HostBuilder()
            .ConfigureWebHost(web =>
            {
                web.UseTestServer();
                web.ConfigureServices(services =>
                {
                    services.AddScoped(_ => NewDb());
                    services.AddHttpContextAccessor();
                    services.AddScoped<IUserContext, HttpUserContext>();
                    services.AddSingleton<IOptions<ContentCrawlOptions>>(Options.Create(opt));
                    services.AddSingleton<IOptions<NewsSiteOptions>>(Options.Create(new NewsSiteOptions()));
                    services.AddScoped<ContentCrawlRepository>();
                    services.AddScoped(sp => CreatePipeline(sp.GetRequiredService<AppDbContext>(), opt, ai));
                    services.AddScoped(sp => new NewsSiteRepository(
                        sp.GetRequiredService<AppDbContext>(), Options.Create(new NewsSiteOptions())));
                    services.AddScoped(sp => CreatePublisher(sp.GetRequiredService<AppDbContext>(), opt));
                    services.AddSingleton(Uninitialized<NewsFanpageService>());
                    services.AddSingleton(Uninitialized<NewsDedupService>());
                    services.AddSingleton(Uninitialized<NewsSiteBuilder>());
                    services.AddSingleton(Uninitialized<HttpArticleFetcher>());
                    services.AddSingleton(Uninitialized<FeedSourceFetcher>());
                    services.AddSingleton(Uninitialized<FeedDiscoveryService>());
                    services.AddSingleton(Uninitialized<CrawlSourcePortability>());
                    services.AddSingleton(Uninitialized<Backend.Shared.OpenClaw.IOpenClawBrowserClient>());
                    services.AddLogging();
                    services.AddAuthentication(ToggleTestAuth.Scheme)
                        .AddScheme<AuthenticationSchemeOptions, ToggleTestAuth>(ToggleTestAuth.Scheme, _ => { });
                    services.AddAuthorization();
                    services.AddControllers().AddApplicationPart(typeof(ContentCrawlController).Assembly);
                });
                web.Configure(app =>
                {
                    app.UseRouting();
                    app.UseAuthentication();
                    app.UseAuthorization();
                    app.UseEndpoints(e => e.MapControllers());
                });
            })
            .Start();

    private static async Task<bool> ReadSummaryFlagAsync(HttpClient client)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, "/api/ContentCrawl/articles/summary");
        request.Headers.Authorization = new AuthenticationHeaderValue(ToggleTestAuth.Scheme, "Viewer:viewer");
        using var response = await client.SendAsync(request);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return doc.RootElement.GetProperty("data").GetProperty("websitePublishEnabled").GetBoolean();
    }

    private static HttpRequestMessage Post(string url, string role, object? body = null)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, url)
        {
            Content = body is null ? null : JsonContent.Create(body),
        };
        request.Headers.Authorization = new AuthenticationHeaderValue(ToggleTestAuth.Scheme, $"{role}:{role}-user");
        return request;
    }

    private static T Uninitialized<T>() where T : class
    {
        var type = typeof(T);
        if (type.IsInterface)
            return (T)RuntimeHelpers.GetUninitializedObject(typeof(DisabledBrowser));
        return (T)RuntimeHelpers.GetUninitializedObject(type);
    }

    private static string FindRepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "backend", "appsettings.json")))
            dir = dir.Parent;
        return dir?.FullName ?? throw new InvalidOperationException("Không tìm thấy thư mục gốc repo");
    }

    /// <summary>AI giả: chấm điểm cố định, đếm số lượt gọi (chấm điểm, chống trùng, viết bài).</summary>
    private sealed class FakeAi(int score) : IAiJudgeService
    {
        public int Calls { get; private set; }
        public bool IsAvailable(string? provider = null) => true;

        public Task<string?> AskAsync(string systemPrompt, string userPrompt, int maxTokens = 200,
            double temperature = 0, TimeSpan? timeout = null, CancellationToken ct = default)
        {
            Calls++;
            return Task.FromResult<string?>(
                $$"""{"score":{{score}},"category":"tuyen-sinh","topic":"Tuyển sinh","reason":"r","summary":"s","eventKey":""}""");
        }
    }

    private sealed class SpyWorker(IServiceScopeFactory scopes)
        : ContentCrawlWorker(scopes, Options.Create(new ContentCrawlOptions()), NullLogger<ContentCrawlWorker>.Instance)
    {
        public int Fetch { get; private set; }
        public int Process { get; private set; }
        public int Compose { get; private set; }

        public Task TickAsync(ContentCrawlOptions settings) => RunTickAsync(settings, CancellationToken.None);

        protected override Task FetchDueSourcesAsync(ContentCrawlOptions settings, CancellationToken ct)
        { Fetch++; return Task.CompletedTask; }

        protected override Task ProcessArticlesAsync(CancellationToken ct)
        { Process++; return Task.CompletedTask; }

        protected override Task ComposeQueuedArticlesAsync(CancellationToken ct)
        { Compose++; return Task.CompletedTask; }
    }

    private sealed class StubUser : IUserContext
    {
        public Guid? GetCurrentUserId() => null;
        public string? GetCurrentUserName() => "tester";
        public IReadOnlyList<string> GetCurrentUserRoles() => ["Admin"];
    }

    private sealed class DisabledBrowser : Backend.Shared.OpenClaw.IOpenClawBrowserClient
    {
        public bool IsConfigured => false;
        public Task<bool> PingAsync(CancellationToken ct = default) => Task.FromResult(false);
        public Task<string> NavigateAsync(string url, CancellationToken ct = default) => Task.FromResult(url);
        public Task<Backend.Shared.OpenClaw.BrowserEvalResult> EvaluateAsync(string fn, CancellationToken ct = default) =>
            throw new NotSupportedException();
    }
}

file sealed class ToggleTestAuth(
    IOptionsMonitor<AuthenticationSchemeOptions> options, ILoggerFactory logger, UrlEncoder encoder)
    : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
{
    public const string Scheme = "ToggleTestAuth";

    protected override Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        var header = Request.Headers.Authorization.ToString();
        var value = header.StartsWith(Scheme + " ", StringComparison.Ordinal) ? header[(Scheme.Length + 1)..] : "";
        var parts = value.Split(':', 2);
        if (parts.Length != 2) return Task.FromResult(AuthenticateResult.Fail("no auth"));

        var identity = new ClaimsIdentity(
            [new Claim(ClaimTypes.Name, parts[1]), new Claim(ClaimTypes.Role, parts[0])], Scheme);
        return Task.FromResult(AuthenticateResult.Success(
            new AuthenticationTicket(new ClaimsPrincipal(identity), Scheme)));
    }
}
