using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Runtime.CompilerServices;
using System.Security.Claims;
using System.Text.Encodings.Web;
using Backend.Data;
using Backend.Modules.ContentCrawl;
using Backend.Shared.OpenClaw;
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
using Microsoft.Extensions.Options;
using Xunit;

namespace Backend.Tests.Modules.ContentCrawl;

public class ContentCrawlPipelineStateTests
{
    [Fact]
    public async Task Worker_DisabledSkipsAllSteps_ThenEnabledRunsAllStepsWithoutRestart()
    {
        await using var fixture = await PipelineFixture.CreateAsync();
        var logger = new RecordingLogger<ContentCrawlWorker>();
        var worker = new SpyContentCrawlWorker(
            fixture.Services.GetRequiredService<IServiceScopeFactory>(),
            Options.Create(new ContentCrawlOptions()),
            logger);

        await fixture.SetEnabledAsync(false, "admin");
        await worker.RunSingleTickAsync();
        await worker.RunSingleTickAsync();

        Assert.Equal(0, worker.FetchCalls);
        Assert.Equal(0, worker.ProcessCalls);
        Assert.Equal(0, worker.ComposeCalls);
        Assert.Single(logger.Messages, x => x.Contains("đã dừng", StringComparison.Ordinal));

        await fixture.SetEnabledAsync(true, "admin");
        await worker.RunSingleTickAsync();
        await worker.RunSingleTickAsync();

        Assert.Equal(2, worker.FetchCalls);
        Assert.Equal(2, worker.ProcessCalls);
        Assert.Equal(2, worker.ComposeCalls);
        Assert.Single(logger.Messages, x => x.Contains("đã bật", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Repository_StateSurvivesFreshDbContext()
    {
        await using var fixture = await PipelineFixture.CreateAsync();

        await fixture.SetEnabledAsync(false, "admin-user");

        await using var freshContext = new AppDbContext(fixture.DbOptions);
        var repository = CreateRepository(freshContext);
        var state = await repository.GetPipelineStateAsync();

        Assert.False(state.IsEnabled);
        Assert.Equal("admin-user", state.UpdatedByUserName);
        Assert.NotNull(state.UpdatedAt);
    }

    [Fact]
    public async Task PipelineStateEndpoints_EnforceAuthenticationAndRolesWithoutForbiddenWrites()
    {
        await using var fixture = await PipelineFixture.CreateAsync();
        using var host = CreateApiHost(fixture.DbOptions);
        using var client = host.GetTestClient();

        using (var anonymousGet = await client.GetAsync("/api/ContentCrawl/pipeline-state"))
            Assert.Equal(HttpStatusCode.Unauthorized, anonymousGet.StatusCode);

        using (var viewerGet = new HttpRequestMessage(HttpMethod.Get, "/api/ContentCrawl/pipeline-state"))
        {
            Authorize(viewerGet, "Viewer", "viewer-user");
            using var response = await client.SendAsync(viewerGet);
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        }

        using (var forbidden = PipelineStateRequest(false, "Viewer", "viewer-user"))
        using (var response = await client.SendAsync(forbidden))
            Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.True(await fixture.GetEnabledAsync());

        foreach (var role in new[] { "Admin", "ContentManager" })
        {
            using var allowed = PipelineStateRequest(false, role, $"{role}-user");
            using var response = await client.SendAsync(allowed);
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            Assert.False(await fixture.GetEnabledAsync());

            await fixture.SetEnabledAsync(true, "test-reset");
        }
    }

    private static IHost CreateApiHost(DbContextOptions<AppDbContext> dbOptions) =>
        new HostBuilder()
            .ConfigureWebHost(web =>
            {
                web.UseTestServer();
                web.ConfigureServices(services =>
                {
                    services.AddSingleton(dbOptions);
                    services.AddScoped(_ => new AppDbContext(dbOptions));
                    services.AddHttpContextAccessor();
                    services.AddScoped<IUserContext, HttpUserContext>();
                    services.AddSingleton<IOptions<ContentCrawlOptions>>(
                        Options.Create(new ContentCrawlOptions()));
                    services.AddScoped<ContentCrawlRepository>();
                    services.AddSingleton(Uninitialized<ContentCrawlPipelineService>());
                    services.AddSingleton(Uninitialized<HttpArticleFetcher>());
                    services.AddSingleton(Uninitialized<FeedSourceFetcher>());
                    services.AddSingleton(Uninitialized<FeedDiscoveryService>());
                    services.AddSingleton(Uninitialized<CrawlSourcePortability>());
                    services.AddSingleton<IOpenClawBrowserClient, DisabledBrowserClient>();
                    services.AddLogging();
                    services.AddAuthentication(PipelineTestAuthHandler.SchemeName)
                        .AddScheme<AuthenticationSchemeOptions, PipelineTestAuthHandler>(
                            PipelineTestAuthHandler.SchemeName, _ => { });
                    services.AddAuthorization();
                    services.AddControllers()
                        .AddApplicationPart(typeof(ContentCrawlController).Assembly);
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

    private static T Uninitialized<T>() where T : class
        => (T)RuntimeHelpers.GetUninitializedObject(typeof(T));

    private static HttpRequestMessage PipelineStateRequest(bool enabled, string role, string userName)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, "/api/ContentCrawl/pipeline-state")
        {
            Content = JsonContent.Create(new SetContentCrawlPipelineStateRequest { Enabled = enabled })
        };
        Authorize(request, role, userName);
        return request;
    }

    private static void Authorize(HttpRequestMessage request, string role, string userName) =>
        request.Headers.Authorization = new AuthenticationHeaderValue(
            PipelineTestAuthHandler.SchemeName, $"{role}:{userName}");

    private static ContentCrawlRepository CreateRepository(AppDbContext db) => new(
        db,
        new StubUserContext(),
        Options.Create(new ContentCrawlOptions()));

    private sealed class PipelineFixture : IAsyncDisposable
    {
        private readonly SqliteConnection _connection;
        public ServiceProvider Services { get; }
        public DbContextOptions<AppDbContext> DbOptions { get; }

        private PipelineFixture(
            SqliteConnection connection,
            DbContextOptions<AppDbContext> dbOptions,
            ServiceProvider services)
        {
            _connection = connection;
            DbOptions = dbOptions;
            Services = services;
        }

        public static async Task<PipelineFixture> CreateAsync()
        {
            var connection = new SqliteConnection("DataSource=:memory:");
            await connection.OpenAsync();
            var dbOptions = new DbContextOptionsBuilder<AppDbContext>()
                .UseSqlite(connection)
                .Options;

            await using (var db = new AppDbContext(dbOptions))
                await db.Database.EnsureCreatedAsync();

            var services = new ServiceCollection()
                .AddSingleton(dbOptions)
                .AddScoped(_ => new AppDbContext(dbOptions))
                .AddScoped<IUserContext, StubUserContext>()
                .AddSingleton<IOptions<ContentCrawlOptions>>(Options.Create(new ContentCrawlOptions()))
                .AddScoped<ContentCrawlRepository>()
                .BuildServiceProvider();

            return new PipelineFixture(connection, dbOptions, services);
        }

        public async Task SetEnabledAsync(bool enabled, string userName)
        {
            await using var db = new AppDbContext(DbOptions);
            await CreateRepository(db).SetPipelineEnabledAsync(enabled, userName);
        }

        public async Task<bool> GetEnabledAsync()
        {
            await using var db = new AppDbContext(DbOptions);
            return await CreateRepository(db).GetPipelineEnabledAsync();
        }

        public async ValueTask DisposeAsync()
        {
            await Services.DisposeAsync();
            await _connection.DisposeAsync();
        }
    }

    private sealed class SpyContentCrawlWorker(
        IServiceScopeFactory scopeFactory,
        IOptions<ContentCrawlOptions> options,
        ILogger<ContentCrawlWorker> logger)
        : ContentCrawlWorker(scopeFactory, options, logger)
    {
        public int FetchCalls { get; private set; }
        public int ProcessCalls { get; private set; }
        public int ComposeCalls { get; private set; }

        public Task RunSingleTickAsync() =>
            RunTickAsync(new ContentCrawlOptions(), CancellationToken.None);

        protected override Task FetchDueSourcesAsync(ContentCrawlOptions settings, CancellationToken ct)
        {
            FetchCalls++;
            return Task.CompletedTask;
        }

        protected override Task ProcessArticlesAsync(CancellationToken ct)
        {
            ProcessCalls++;
            return Task.CompletedTask;
        }

        protected override Task ComposeQueuedArticlesAsync(CancellationToken ct)
        {
            ComposeCalls++;
            return Task.CompletedTask;
        }
    }

    private sealed class StubUserContext : IUserContext
    {
        public Guid? GetCurrentUserId() => null;
        public string? GetCurrentUserName() => "test";
        public IReadOnlyList<string> GetCurrentUserRoles() => [];
    }

    private sealed class DisabledBrowserClient : IOpenClawBrowserClient
    {
        public bool IsConfigured => false;
        public Task<bool> PingAsync(CancellationToken ct = default) => Task.FromResult(false);
        public Task<string> NavigateAsync(string url, CancellationToken ct = default) => Task.FromResult(url);
        public Task<BrowserEvalResult> EvaluateAsync(string fn, CancellationToken ct = default) =>
            throw new NotSupportedException();
    }

    private sealed class RecordingLogger<T> : ILogger<T>
    {
        public List<string> Messages { get; } = [];
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel logLevel) => true;
        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state,
            Exception? exception, Func<TState, Exception?, string> formatter) =>
            Messages.Add(formatter(state, exception));
    }
}

file sealed class PipelineTestAuthHandler : AuthenticationHandler<AuthenticationSchemeOptions>
{
    public const string SchemeName = "PipelineTestAuth";

    public PipelineTestAuthHandler(
        IOptionsMonitor<AuthenticationSchemeOptions> options,
        ILoggerFactory logger,
        UrlEncoder encoder)
        : base(options, logger, encoder) { }

    protected override Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        var header = Request.Headers.Authorization.ToString();
        if (string.IsNullOrWhiteSpace(header))
            return Task.FromResult(AuthenticateResult.Fail("Missing Authorization header."));

        var value = header.StartsWith(SchemeName + " ", StringComparison.OrdinalIgnoreCase)
            ? header[(SchemeName.Length + 1)..].Trim()
            : header.Trim();
        var parts = value.Split(':', 2);
        if (parts.Length != 2)
            return Task.FromResult(AuthenticateResult.Fail("Invalid test auth payload."));

        var claims = new[]
        {
            new Claim(ClaimTypes.Name, parts[1]),
            new Claim(ClaimTypes.NameIdentifier, Guid.NewGuid().ToString()),
            new Claim(ClaimTypes.Role, parts[0])
        };
        var identity = new ClaimsIdentity(claims, SchemeName);
        var ticket = new AuthenticationTicket(new ClaimsPrincipal(identity), SchemeName);
        return Task.FromResult(AuthenticateResult.Success(ticket));
    }
}
