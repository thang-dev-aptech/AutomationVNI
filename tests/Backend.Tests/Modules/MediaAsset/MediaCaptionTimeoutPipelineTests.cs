using System.Net;
using System.Net.Http.Headers;
using System.Security.Claims;
using System.Text.Encodings.Web;
using Backend.Data;
using Backend.Modules.MediaAsset;
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
/// MEDIA-CAPTION-02 N4: kiểm chứng mapping timeout/cancellation ở mức controller pipeline thật.
/// </summary>
public sealed class MediaCaptionTimeoutPipelineTests : IAsyncLifetime
{
    private const string ExistingCaption = "cũ";
    private static readonly TimeSpan ShortTimeout = TimeSpan.FromMilliseconds(100);
    private static readonly MediaAiTimeouts ShortTimeouts = MediaAiTimeouts.Default with
    {
        CaptionRequest = ShortTimeout,
        CaptionPreparationAllowance = TimeSpan.FromMilliseconds(100)
    };

    private readonly SqliteConnection _connection = new("DataSource=:memory:");
    private readonly DbContextOptions<AppDbContext> _options;

    public MediaCaptionTimeoutPipelineTests()
    {
        _connection.Open();
        _options = new DbContextOptionsBuilder<AppDbContext>().UseSqlite(_connection).Options;
        using var db = new AppDbContext(_options);
        db.Database.EnsureCreated();
    }

    public Task InitializeAsync() => Task.CompletedTask;

    public async Task DisposeAsync() => await _connection.DisposeAsync();

    [Fact]
    public async Task StorageDeadline_Returns400MediaCaptionFailed_AndKeepsCaption()
    {
        var storage = new HangingStorage();
        await using var pipeline = CreatePipeline(new ScriptedChatHandler(CaptionAi.Json("Bài thử")), storage, ShortTimeouts);
        var id = await SeedAssetAsync();

        var requestTask = SendAsync(pipeline.Client, id);
        await storage.Arrived.Task.WaitAsync(TimeSpan.FromSeconds(5));
        var response = await requestTask;
        var body = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains("MEDIA_CAPTION_FAILED", body);
        Assert.DoesNotContain("500", body);
        Assert.Equal(ExistingCaption, await GetCaptionAsync(id));
    }

    [Fact]
    public async Task AiPerCallTimeout_Returns400MediaCaptionFailed_AndKeepsCaption()
    {
        await using var pipeline = CreatePipeline(
            new DelayedChatHandler(TimeSpan.FromSeconds(5), CaptionAi.Json("Bài thử")),
            new InMemoryImageStorage(),
            MediaAiTimeouts.Default with { CaptionRequest = ShortTimeout });
        var id = await SeedAssetAsync();

        var response = await SendAsync(pipeline.Client, id);
        var body = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains("MEDIA_CAPTION_FAILED", body);
        Assert.Equal(ExistingCaption, await GetCaptionAsync(id));
    }

    [Fact]
    public async Task ClientCancellation_IsNotMappedToTimeout400_AndKeepsCaption()
    {
        var ai = new CancellationAwareChatHandler();
        await using var pipeline = CreatePipeline(ai, new InMemoryImageStorage(), MediaAiTimeouts.Default);
        var id = await SeedAssetAsync();
        using var cancellation = new CancellationTokenSource();

        var requestTask = SendAsync(pipeline.Client, id, cancellation.Token);
        await ai.Arrived.Task.WaitAsync(TimeSpan.FromSeconds(5));
        await cancellation.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => requestTask);
        Assert.Equal(ExistingCaption, await GetCaptionAsync(id));
    }

    private PipelineHost CreatePipeline(HttpMessageHandler ai, IFileStorageService storage, MediaAiTimeouts timeouts)
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
                    services.AddSingleton(storage);
                    services.AddSingleton(CaptionAiOptions.Create());
                    services.AddSingleton(timeouts);
                    services.AddSingleton(_ => new HttpClient(ai));
                    services.AddScoped<MediaIntelligenceService>();
                    services.AddAuthentication(TimeoutPipelineAuthHandler.SchemeName)
                        .AddScheme<AuthenticationSchemeOptions, TimeoutPipelineAuthHandler>(
                            TimeoutPipelineAuthHandler.SchemeName, _ => { });
                    services.AddAuthorization();
                    services.AddControllers().AddApplicationPart(typeof(MediaAssetController).Assembly);
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

    private static Task<HttpResponseMessage> SendAsync(HttpClient client, Guid id, CancellationToken ct = default)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, $"/api/MediaAsset/{id}/generate-caption");
        request.Headers.Authorization = new AuthenticationHeaderValue(TimeoutPipelineAuthHandler.SchemeName, "Admin:admin-user");
        return client.SendAsync(request, ct);
    }

    private async Task<Guid> SeedAssetAsync()
    {
        await using var db = new AppDbContext(_options);
        var asset = new MediaAssetModel
        {
            Id = Guid.NewGuid(),
            FileName = "a.png",
            StoragePath = "media/a.png",
            MimeType = "image/png",
            Caption = ExistingCaption
        };
        db.MediaAssets.Add(asset);
        await db.SaveChangesAsync();
        return asset.Id;
    }

    private async Task<string?> GetCaptionAsync(Guid id)
    {
        await using var db = new AppDbContext(_options);
        return await db.MediaAssets.AsNoTracking().Where(x => x.Id == id).Select(x => x.Caption).SingleAsync();
    }

    private sealed class PipelineHost : IAsyncDisposable
    {
        private readonly IHost _host;
        public HttpClient Client { get; }

        public PipelineHost(IHost host, HttpClient client)
        {
            _host = host;
            Client = client;
        }

        public async ValueTask DisposeAsync()
        {
            Client.Dispose();
            await _host.StopAsync();
            _host.Dispose();
        }
    }
}

file sealed class HangingStorage : IFileStorageService
{
    private readonly TaskCompletionSource<bool> _never = new(TaskCreationOptions.RunContinuationsAsynchronously);
    public TaskCompletionSource Arrived { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

    public Task<bool> ExistsAsync(string storageKey, CancellationToken ct = default)
    {
        Arrived.TrySetResult();
        return _never.Task;
    }

    public Task<Stream> OpenReadAsync(string storageKey, CancellationToken ct = default)
        => Task.FromResult<Stream>(new MemoryStream(InMemoryImageStorage.ImageBytes));

    public Task<FileSaveResult> SaveAsync(Microsoft.AspNetCore.Http.IFormFile file, string folder, CancellationToken ct = default)
        => throw new NotImplementedException();

    public Task<FileSaveResult> SaveBytesAsync(byte[] data, string folder, string extension, string contentType, CancellationToken ct = default)
        => throw new NotImplementedException();

    public Task DeleteAsync(string storageKey, CancellationToken ct = default) => Task.CompletedTask;
}

file sealed class CancellationAwareChatHandler : HttpMessageHandler
{
    public TaskCompletionSource Arrived { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        Arrived.TrySetResult();
        await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
        throw new InvalidOperationException("unreachable");
    }
}

file sealed class TimeoutPipelineAuthHandler : AuthenticationHandler<AuthenticationSchemeOptions>
{
    public const string SchemeName = "MediaCaptionTimeoutPipelineAuth";

    public TimeoutPipelineAuthHandler(
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

        var claims = new[]
        {
            new Claim(ClaimTypes.Name, parts[1]),
            new Claim(ClaimTypes.NameIdentifier, Guid.NewGuid().ToString()),
            new Claim(ClaimTypes.Role, parts[0])
        };
        var identity = new ClaimsIdentity(claims, SchemeName);
        return Task.FromResult(AuthenticateResult.Success(
            new AuthenticationTicket(new ClaimsPrincipal(identity), SchemeName)));
    }
}
