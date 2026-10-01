using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text.Encodings.Web;
using System.Text.Json;
using Backend.Data;
using Backend.Modules.MediaAsset;
using Backend.Modules.MediaCaption;
using Backend.Modules.MediaFolder;
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
/// MEDIA-CAPTION-02 AC 895c5442 (invariant caption-locked-while-queued): pipeline thật, Admin.
/// </summary>
public class MediaCaptionLockPipelineTests : IAsyncLifetime
{
    private static readonly string[] FiveLines = ["Một", "Hai", "Ba", "Bốn", "Năm"];

    private readonly SqliteConnection _connection;
    private readonly DbContextOptions<AppDbContext> _options;
    private readonly ScriptedChatHandler _ai;
    private readonly IHost _host;
    private readonly HttpClient _client;

    public MediaCaptionLockPipelineTests()
    {
        _connection = new SqliteConnection("DataSource=:memory:");
        _connection.Open();
        _options = new DbContextOptionsBuilder<AppDbContext>().UseSqlite(_connection).Options;
        using (var seedDb = new AppDbContext(_options))
            seedDb.Database.EnsureCreated();

        _ai = new ScriptedChatHandler(Enumerable.Repeat(ScriptedChatHandler.Lines(FiveLines), 10).ToArray());
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
    [InlineData(MediaCaptionJobItemStatus.Pending)]
    [InlineData(MediaCaptionJobItemStatus.Running)]
    public async Task Put_CaptionWhileQueued_Returns409_AndNothingInRequestIsApplied(MediaCaptionJobItemStatus status)
    {
        var id = await SeedAssetAsync(status);

        var response = await PutAsync(id, new { caption = "tay", altText = "mới" });
        var body = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Contains("MEDIA_CAPTION_QUEUED", body);
        Assert.Contains("Ảnh đang trong hàng chờ sinh caption", body);
        var (caption, altText) = await GetFieldsAsync(id);
        Assert.Equal("cũ", caption);
        Assert.Equal("alt cũ", altText);
    }

    [Theory]
    [InlineData(MediaCaptionJobItemStatus.Succeeded)]
    [InlineData(MediaCaptionJobItemStatus.Failed)]
    [InlineData(MediaCaptionJobItemStatus.Skipped)]
    public async Task Put_CaptionWhenItemFinished_Succeeds(MediaCaptionJobItemStatus status)
    {
        var id = await SeedAssetAsync(status);

        var response = await PutAsync(id, new { caption = "tay" });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("tay", (await GetFieldsAsync(id)).Caption);
    }

    [Fact]
    public async Task Put_CaptionWhenJobDeleted_Succeeds()
    {
        var id = await SeedAssetAsync(MediaCaptionJobItemStatus.Pending, jobDeleted: true);

        var response = await PutAsync(id, new { caption = "tay" });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("tay", (await GetFieldsAsync(id)).Caption);
    }

    [Fact]
    public async Task Put_CaptionWhenItemDeleted_Succeeds()
    {
        var id = await SeedAssetAsync(MediaCaptionJobItemStatus.Pending, itemDeleted: true);

        var response = await PutAsync(id, new { caption = "tay" });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task Put_WithoutCaptionWhileQueued_Succeeds_AndResponseKeepsCaptionQueued()
    {
        var id = await SeedAssetAsync(MediaCaptionJobItemStatus.Pending);

        var response = await PutAsync(id, new { altText = "mới" });
        var data = ReadData(await response.Content.ReadAsStringAsync());

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var (caption, altText) = await GetFieldsAsync(id);
        Assert.Equal("cũ", caption);
        Assert.Equal("mới", altText);
        Assert.True(data.GetProperty("captionQueued").GetBoolean());
    }

    [Fact]
    public async Task GenerateCaption_WhileQueued_Returns409_AndDoesNotCallAi()
    {
        var id = await SeedAssetAsync(MediaCaptionJobItemStatus.Pending);

        var response = await PostAsync($"/api/MediaAsset/{id}/generate-caption");
        var body = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Contains("MEDIA_CAPTION_QUEUED", body);
        Assert.Empty(_ai.RequestBodies);
        Assert.Equal("cũ", (await GetFieldsAsync(id)).Caption);
    }

    [Fact]
    public async Task GenerateCaption_WhenNotQueued_StillWorks()
    {
        var id = await SeedAssetAsync(MediaCaptionJobItemStatus.Succeeded);

        var response = await PostAsync($"/api/MediaAsset/{id}/generate-caption");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("Một\nHai\nBa\nBốn\nNăm", (await GetFieldsAsync(id)).Caption);
    }

    [Fact]
    public async Task GetById_And_Filter_ReturnCaptionQueuedFlag()
    {
        var queued = await SeedAssetAsync(MediaCaptionJobItemStatus.Running);
        var running = await SeedAssetAsync(MediaCaptionJobItemStatus.Pending);
        var done = await SeedAssetAsync(MediaCaptionJobItemStatus.Succeeded);
        var plain = await SeedAssetAsync(status: null);

        Assert.True(await GetByIdFlagAsync(queued));
        Assert.True(await GetByIdFlagAsync(running));
        Assert.False(await GetByIdFlagAsync(done));
        Assert.False(await GetByIdFlagAsync(plain));

        var response = await PostAsync("/api/MediaAsset/filter", JsonContent.Create(new { index = 1, size = 20 }));
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var items = ReadData(await response.Content.ReadAsStringAsync()).GetProperty("items");
        var flags = items.EnumerateArray().ToDictionary(
            x => x.GetProperty("id").GetGuid(),
            x => x.GetProperty("captionQueued").GetBoolean());
        Assert.True(flags[queued]);
        Assert.True(flags[running]);
        Assert.False(flags[done]);
        Assert.False(flags[plain]);
    }

    [Fact]
    public async Task WorkerPath_GenerateCaptionAsync_IsNotBlockedByLock()
    {
        var id = await SeedAssetAsync(MediaCaptionJobItemStatus.Running);

        using var scope = _host.Services.CreateScope();
        var intelligence = scope.ServiceProvider.GetRequiredService<MediaIntelligenceService>();
        await intelligence.GenerateCaptionAsync(id);

        Assert.Equal("Một\nHai\nBa\nBốn\nNăm", (await GetFieldsAsync(id)).Caption);
    }

    private async Task<bool> GetByIdFlagAsync(Guid id)
    {
        var response = await _client.SendAsync(WithAdmin(new HttpRequestMessage(HttpMethod.Get, $"/api/MediaAsset/{id}")));
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return ReadData(await response.Content.ReadAsStringAsync()).GetProperty("captionQueued").GetBoolean();
    }

    private static JsonElement ReadData(string json)
        => JsonDocument.Parse(json).RootElement.GetProperty("data").Clone();

    private Task<HttpResponseMessage> PutAsync(Guid id, object payload)
        => _client.SendAsync(WithAdmin(new HttpRequestMessage(HttpMethod.Put, $"/api/MediaAsset/{id}")
        {
            Content = JsonContent.Create(payload)
        }));

    private Task<HttpResponseMessage> PostAsync(string url, HttpContent? content = null)
        => _client.SendAsync(WithAdmin(new HttpRequestMessage(HttpMethod.Post, url) { Content = content }));

    private static HttpRequestMessage WithAdmin(HttpRequestMessage request)
    {
        request.Headers.Authorization = new AuthenticationHeaderValue(TestAuthHandler.SchemeName, "Admin:admin-user");
        return request;
    }

    private async Task<Guid> SeedAssetAsync(
        MediaCaptionJobItemStatus? status, bool jobDeleted = false, bool itemDeleted = false)
    {
        await using var db = new AppDbContext(_options);
        var asset = new MediaAssetModel
        {
            Id = Guid.NewGuid(), FileName = "a.png", StoragePath = "media/a.png", MimeType = "image/png",
            Caption = "cũ", AltText = "alt cũ"
        };
        db.MediaAssets.Add(asset);
        if (status.HasValue)
        {
            var job = new MediaCaptionJobModel
            {
                Id = Guid.NewGuid(), FolderId = Guid.NewGuid(), FolderName = "folder",
                Status = MediaCaptionJobStatus.Running, Total = 1, CreatedAt = DateTime.UtcNow, IsDeleted = jobDeleted
            };
            db.MediaCaptionJobs.Add(job);
            db.MediaCaptionJobItems.Add(new MediaCaptionJobItemModel
            {
                Id = Guid.NewGuid(), JobId = job.Id, MediaAssetId = asset.Id, FileName = asset.FileName,
                Status = status.Value, CreatedAt = DateTime.UtcNow, IsDeleted = itemDeleted
            });
        }
        await db.SaveChangesAsync();
        return asset.Id;
    }

    private async Task<(string? Caption, string? AltText)> GetFieldsAsync(Guid id)
    {
        await using var db = new AppDbContext(_options);
        var asset = await db.MediaAssets.AsNoTracking().SingleAsync(x => x.Id == id);
        return (asset.Caption, asset.AltText);
    }
}

file sealed class TestAuthHandler : AuthenticationHandler<AuthenticationSchemeOptions>
{
    public const string SchemeName = "TestMediaCaptionLockAuth";

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

        var claims = new List<Claim>
        {
            new(ClaimTypes.Name, parts[1]),
            new(ClaimTypes.NameIdentifier, Guid.NewGuid().ToString()),
            new(ClaimTypes.Role, parts[0])
        };
        var identity = new ClaimsIdentity(claims, SchemeName);
        var ticket = new AuthenticationTicket(new ClaimsPrincipal(identity), SchemeName);
        return Task.FromResult(AuthenticateResult.Success(ticket));
    }
}
