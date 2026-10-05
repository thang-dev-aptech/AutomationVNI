using System.Net;
using System.Net.Http.Headers;
using System.Security.Claims;
using System.Text;
using System.Text.Encodings.Web;
using Backend.Data;
using Backend.Modules.MediaAsset;
using Backend.Modules.MediaFolder;
using Backend.Shared.Ai;
using Backend.Shared.Repositories;
using Backend.Shared.Storage;
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

namespace Backend.Tests.Modules.MediaAsset;

/// <summary>
/// MEDIA-CAPTION-01 AC 9653a663: Authorize pipeline thật cho POST api/MediaAsset/{id}/generate-caption —
/// Viewer/Reviewer 403 + Caption không đổi, Admin/ContentManager 200, chưa đăng nhập 401.
/// </summary>
public class MediaCaptionAuthPipelineTests : IAsyncLifetime
{

    private readonly SqliteConnection _connection;
    private readonly DbContextOptions<AppDbContext> _options;
    private readonly ScriptedChatHandler _ai;
    private readonly IHost _host;
    private readonly HttpClient _client;

    public MediaCaptionAuthPipelineTests()
    {
        _connection = new SqliteConnection("DataSource=:memory:");
        _connection.Open();
        _options = new DbContextOptionsBuilder<AppDbContext>().UseSqlite(_connection).Options;
        using (var seedDb = new AppDbContext(_options))
            seedDb.Database.EnsureCreated();

        _ai = new ScriptedChatHandler(Enumerable.Repeat(CaptionAi.Json("Bài thử"), 10).ToArray());
        var options = _options;
        var ai = _ai;
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
                    services.AddSingleton<IFileStorageService, InMemoryImageStorage>();
                    services.AddSingleton(CaptionAiOptions.Create());
                    services.AddSingleton(_ => new HttpClient(ai));
                    services.AddScoped<MediaIntelligenceService>();
                    services.AddAuthentication(TestAuthHandler.SchemeName)
                        .AddScheme<AuthenticationSchemeOptions, TestAuthHandler>(
                            TestAuthHandler.SchemeName, _ => { });
                    services.AddAuthorization();
                    services.AddControllers()
                        .AddApplicationPart(typeof(MediaAssetController).Assembly);
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

    [Theory]
    [InlineData("Admin")]
    [InlineData("ContentManager")]
    public async Task AllowedRoles_Return200AndSaveCaption(string role)
    {
        var id = await SeedAssetAsync("image/png");
        var response = await SendAsync(id, role);
        var body = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("\"caption\"", body);
        Assert.Equal(CaptionAi.Expected("Bài thử"), await GetCaptionAsync(id));
    }

    [Theory]
    [InlineData("Viewer")]
    [InlineData("Reviewer")]
    public async Task ForbiddenRoles_Return403AndKeepCaption(string role)
    {
        var id = await SeedAssetAsync("image/png");
        var response = await SendAsync(id, role);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Equal("cũ", await GetCaptionAsync(id));
        Assert.Empty(_ai.RequestBodies);
    }

    [Fact]
    public async Task Unauthenticated_Returns401AndKeepsCaption()
    {
        var id = await SeedAssetAsync("image/png");
        var response = await SendAsync(id, role: null);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Equal("cũ", await GetCaptionAsync(id));
    }

    [Fact]
    public async Task UnknownMedia_Returns404()
    {
        var response = await SendAsync(Guid.NewGuid(), "Admin");
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task NonImage_Returns400()
    {
        var id = await SeedAssetAsync("video/mp4");
        var response = await SendAsync(id, "Admin");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("cũ", await GetCaptionAsync(id));
    }

    [Fact]
    public async Task AiNetworkFailure_Returns400AndKeepsCaption()
    {
        await using var pipeline = CreatePipelineHost(new ThrowingChatHandler(new HttpRequestException("DNS failed")));
        var id = await SeedAssetAsync("image/png");

        var response = await SendAsync(pipeline.Client, id, "Admin");
        var body = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains("MEDIA_CAPTION_FAILED", body);
        Assert.Contains("Không kết nối được AI, thử lại sau", body);
        Assert.Equal("cũ", await GetCaptionAsync(id));
    }

    [Fact]
    public async Task AiTimeout_Returns400AndKeepsCaption()
    {
        await using var pipeline = CreatePipelineHost(new ThrowingChatHandler(new TaskCanceledException("HttpClient timeout")));
        var id = await SeedAssetAsync("image/png");

        var response = await SendAsync(pipeline.Client, id, "Admin");
        var body = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains("MEDIA_CAPTION_FAILED", body);
        Assert.Contains("Không kết nối được AI, thử lại sau", body);
        Assert.Equal("cũ", await GetCaptionAsync(id));
    }

    [Theory]
    [InlineData("{\"foo\":1}")]
    [InlineData("<html>gateway</html>")]
    [InlineData("{\"choices\":[]}")]
    public async Task AiMalformedResponse_Returns400AndKeepsCaption(string rawBody)
    {
        await using var pipeline = CreatePipelineHost(new RawBodyChatHandler(rawBody));
        var id = await SeedAssetAsync("image/png");

        var response = await SendAsync(pipeline.Client, id, "Admin");
        var body = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains("MEDIA_CAPTION_FAILED", body);
        Assert.Contains("AI trả phản hồi không hợp lệ", body);
        Assert.DoesNotContain("NOT_FOUND", body);
        Assert.Equal("cũ", await GetCaptionAsync(id));
    }

    private async Task<HttpResponseMessage> SendAsync(Guid id, string? role)
        => await SendAsync(_client, id, role);

    private static async Task<HttpResponseMessage> SendAsync(HttpClient client, Guid id, string? role)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, $"/api/MediaAsset/{id}/generate-caption");
        if (role is not null)
            request.Headers.Authorization = new AuthenticationHeaderValue(TestAuthHandler.SchemeName, $"{role}:admin-user");
        return await client.SendAsync(request);
    }

    private PipelineHost CreatePipelineHost(HttpMessageHandler aiHandler)
    {
        var options = _options;
        var host = new HostBuilder()
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
                    services.AddSingleton<IFileStorageService, InMemoryImageStorage>();
                    services.AddSingleton(CaptionAiOptions.Create());
                    services.AddSingleton(_ => new HttpClient(aiHandler));
                    services.AddScoped<MediaIntelligenceService>();
                    services.AddAuthentication(TestAuthHandler.SchemeName)
                        .AddScheme<AuthenticationSchemeOptions, TestAuthHandler>(
                            TestAuthHandler.SchemeName, _ => { });
                    services.AddAuthorization();
                    services.AddControllers()
                        .AddApplicationPart(typeof(MediaAssetController).Assembly);
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
        return new PipelineHost(host, host.GetTestClient());
    }

    private async Task<Guid> SeedAssetAsync(string mimeType)
    {
        await using var db = new AppDbContext(_options);
        var asset = new MediaAssetModel
        {
            Id = Guid.NewGuid(),
            FileName = "a.png",
            StoragePath = "media/a.png",
            MimeType = mimeType,
            Caption = "cũ"
        };
        db.MediaAssets.Add(asset);
        await db.SaveChangesAsync();
        return asset.Id;
    }

    private async Task<string?> GetCaptionAsync(Guid id)
    {
        await using var db = new AppDbContext(_options);
        return (await db.MediaAssets.AsNoTracking().SingleAsync(x => x.Id == id)).Caption;
    }

    private sealed class PipelineHost(IHost host, HttpClient client) : IAsyncDisposable
    {
        public HttpClient Client { get; } = client;

        public async ValueTask DisposeAsync()
        {
            Client.Dispose();
            await host.StopAsync();
            host.Dispose();
        }
    }
}

file sealed class ThrowingChatHandler(Exception exception) : HttpMessageHandler
{
    protected override Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request, CancellationToken cancellationToken)
        => Task.FromException<HttpResponseMessage>(exception);
}

/// <summary>Trả nguyên body HTTP 200 (không bọc choices) — mô phỏng gateway/AI sai định dạng (F3/F4).</summary>
file sealed class RawBodyChatHandler(string body) : HttpMessageHandler
{
    protected override Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request, CancellationToken cancellationToken)
        => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(body, Encoding.UTF8, "application/json")
        });
}

file sealed class TestAuthHandler : AuthenticationHandler<AuthenticationSchemeOptions>
{
    public const string SchemeName = "TestMediaCaptionAuth";

    public TestAuthHandler(
        IOptionsMonitor<AuthenticationSchemeOptions> options,
        ILoggerFactory logger,
        UrlEncoder encoder)
        : base(options, logger, encoder)
    {
    }

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

        var role = parts[0];
        var userName = parts[1];
        var claims = new List<Claim>
        {
            new(ClaimTypes.Name, userName),
            new(ClaimTypes.NameIdentifier, Guid.NewGuid().ToString()),
            new(ClaimTypes.Role, role)
        };
        var identity = new ClaimsIdentity(claims, SchemeName);
        var principal = new ClaimsPrincipal(identity);
        var ticket = new AuthenticationTicket(principal, SchemeName);
        return Task.FromResult(AuthenticateResult.Success(ticket));
    }
}

