using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text.Encodings.Web;
using Backend.Data;
using Backend.Modules.Inbox;
using Backend.Shared;
using Backend.Shared.Ai;
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

namespace Backend.Tests.Modules.Inbox;

/// <summary>
/// AC inbox-single-source-test (85e37852) (a): filter / summary / profile đã gỡ → 404.
/// </summary>
public sealed class InboxRemovedRoutesTests : IAsyncLifetime
{
    private readonly SqliteConnection _connection;
    private readonly IHost _host;
    private readonly HttpClient _client;
    private readonly Guid _actorUserId = Guid.Parse("aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee");

    public InboxRemovedRoutesTests()
    {
        _connection = new SqliteConnection("DataSource=:memory:");
        _connection.Open();
        var options = new DbContextOptionsBuilder<AppDbContext>().UseSqlite(_connection).Options;
        using (var seed = new AppDbContext(options))
            seed.Database.EnsureCreated();

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
                    services.AddLogging();
                    services.AddSingleton<IAiTextGenerationService, StubAi>();
                    services.AddScoped<InboxSuggestService>();
                    services.AddAuthentication(TestAuthHandler.SchemeName)
                        .AddScheme<AuthenticationSchemeOptions, TestAuthHandler>(
                            TestAuthHandler.SchemeName, _ => { });
                    services.AddAuthorization();
                    services.AddControllers(o => o.Filters.Add<GlobalExceptionFilter>())
                        .AddApplicationPart(typeof(InboxSuggestController).Assembly);
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

    public Task InitializeAsync() => Task.CompletedTask;

    public async Task DisposeAsync()
    {
        _client.Dispose();
        await _host.StopAsync();
        _host.Dispose();
        await _connection.DisposeAsync();
    }

    [Fact]
    public async Task RemovedInboxRoutes_Return404()
    {
        Assert.Equal(HttpStatusCode.NotFound, await SendAsync(HttpMethod.Post, "/api/Inbox/filter", new { }));
        Assert.Equal(HttpStatusCode.NotFound, await SendAsync(HttpMethod.Get, "/api/Inbox/summary"));
        Assert.Equal(
            HttpStatusCode.NotFound,
            await SendAsync(HttpMethod.Get, $"/api/Inbox/message/{Guid.NewGuid()}/profile"));
    }

    [Fact]
    public async Task SuggestReply_StillMapped()
    {
        // Route còn tồn tại (có thể 404 business vì id không có) — không phải "no route" cho filter.
        using var request = new HttpRequestMessage(
            HttpMethod.Post, $"/api/Inbox/message/{Guid.NewGuid()}/suggest-reply");
        Authorize(request);
        using var response = await _client.SendAsync(request);
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        // Suggest controller trả ApiResponse, không phải empty framework 404 HTML-only.
        var body = await response.Content.ReadAsStringAsync();
        Assert.Contains("NOT_FOUND", body, StringComparison.Ordinal);
    }

    private async Task<HttpStatusCode> SendAsync(HttpMethod method, string url, object? body = null)
    {
        using var request = new HttpRequestMessage(method, url);
        if (body is not null)
            request.Content = JsonContent.Create(body);
        Authorize(request);
        using var response = await _client.SendAsync(request);
        return response.StatusCode;
    }

    private void Authorize(HttpRequestMessage request)
        => request.Headers.Authorization = new AuthenticationHeaderValue(
            TestAuthHandler.SchemeName, $"Admin:{_actorUserId:N}");

    private sealed class StubAi : IAiTextGenerationService
    {
        public bool IsAvailable(string? provider = null) => true;

        public Task<AiTextGenerationResult> GenerateAsync(
            AiTextGenerationRequest request, CancellationToken ct = default)
            => Task.FromResult(new AiTextGenerationResult { Caption = "x" });

        public Task<List<string>> SuggestIdeasAsync(
            string topic, int count, string? category, CancellationToken ct = default)
            => Task.FromResult(new List<string>());

        public Task<string> ComposeImagePromptAsync(
            AiImagePromptRequest request, CancellationToken ct = default)
            => Task.FromResult(string.Empty);
    }

    private sealed class TestAuthHandler : AuthenticationHandler<AuthenticationSchemeOptions>
    {
        public const string SchemeName = "TestInboxRemoved";

        public TestAuthHandler(
            IOptionsMonitor<AuthenticationSchemeOptions> options,
            ILoggerFactory logger,
            UrlEncoder encoder) : base(options, logger, encoder) { }

        protected override Task<AuthenticateResult> HandleAuthenticateAsync()
        {
            var header = Request.Headers.Authorization.ToString();
            if (string.IsNullOrWhiteSpace(header))
                return Task.FromResult(AuthenticateResult.Fail("missing"));

            var value = header.StartsWith(SchemeName + " ", StringComparison.OrdinalIgnoreCase)
                ? header[(SchemeName.Length + 1)..].Trim()
                : header.Trim();
            var parts = value.Split(':', 2);
            if (parts.Length != 2)
                return Task.FromResult(AuthenticateResult.Fail("bad"));

            var userId = Guid.TryParseExact(parts[1], "N", out var parsed) ? parsed : Guid.NewGuid();
            var claims = new[]
            {
                new Claim(ClaimTypes.Name, "actor"),
                new Claim(ClaimTypes.NameIdentifier, userId.ToString()),
                new Claim(ClaimTypes.Role, parts[0]),
            };
            return Task.FromResult(AuthenticateResult.Success(
                new AuthenticationTicket(
                    new ClaimsPrincipal(new ClaimsIdentity(claims, SchemeName)), SchemeName)));
        }
    }
}
