using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text.Encodings.Web;
using Backend.Data;
using Backend.Modules.GoogleDrive;
using Backend.Modules.MediaAsset;
using Backend.Modules.MediaCaption;
using Backend.Modules.MediaFolder;
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

namespace Backend.Tests.Modules.MediaCaption;

/// <summary>
/// MEDIA-CAPTION-02 AC f51fc165: TestServer authorization pipeline for every caption-job endpoint.
/// </summary>
public sealed class MediaCaptionJobAuthPipelineTests : IAsyncLifetime
{
    private readonly SqliteConnection _connection;
    private readonly DbContextOptions<AppDbContext> _options;
    private readonly IHost _host;
    private readonly HttpClient _client;
    private readonly Guid _rootId = Guid.NewGuid();
    private readonly Guid _failedJobId = Guid.NewGuid();
    private readonly Guid _failedItemId = Guid.NewGuid();

    public MediaCaptionJobAuthPipelineTests()
    {
        _connection = new SqliteConnection("DataSource=:memory:");
        _connection.Open();
        _options = new DbContextOptionsBuilder<AppDbContext>().UseSqlite(_connection).Options;

        using (var db = new AppDbContext(_options))
        {
            db.Database.EnsureCreated();
            Seed(db);
            db.SaveChanges();
        }

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
                    services.AddScoped<MediaCaptionJobService>();
                    services.AddAuthentication(TestAuthHandler.SchemeName)
                        .AddScheme<AuthenticationSchemeOptions, TestAuthHandler>(
                            TestAuthHandler.SchemeName, _ => { });
                    services.AddAuthorization();
                    services.AddControllers()
                        .AddApplicationPart(typeof(MediaCaptionJobController).Assembly);
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
    public async Task AllowedRoles_AllEndpointsReturn2xx(string role)
    {
        foreach (var request in CreateEndpointRequests())
        {
            request.Headers.Authorization = new AuthenticationHeaderValue(
                TestAuthHandler.SchemeName, $"{role}:admin-user");
            using (request)
            using (var response = await _client.SendAsync(request))
                Assert.InRange((int)response.StatusCode, 200, 299);
        }
    }

    [Theory]
    [InlineData("Viewer")]
    [InlineData("Reviewer")]
    public async Task ForbiddenRoles_AllEndpointsReturn403AndDoNotMutate(string role)
    {
        var jobsBefore = await CountJobsAsync();
        var failedBefore = await GetItemStatusAsync();

        foreach (var request in CreateEndpointRequests())
        {
            request.Headers.Authorization = new AuthenticationHeaderValue(
                TestAuthHandler.SchemeName, $"{role}:denied-user");
            using (request)
            using (var response = await _client.SendAsync(request))
                Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        }

        Assert.Equal(jobsBefore, await CountJobsAsync());
        Assert.Equal(failedBefore, await GetItemStatusAsync());
    }

    [Fact]
    public async Task Unauthenticated_AllEndpointsReturn401AndDoNotMutate()
    {
        var jobsBefore = await CountJobsAsync();
        var failedBefore = await GetItemStatusAsync();

        foreach (var request in CreateEndpointRequests())
        {
            using (request)
            using (var response = await _client.SendAsync(request))
                Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        }

        Assert.Equal(jobsBefore, await CountJobsAsync());
        Assert.Equal(failedBefore, await GetItemStatusAsync());
    }

    private IEnumerable<HttpRequestMessage> CreateEndpointRequests()
    {
        yield return new HttpRequestMessage(HttpMethod.Post, "/api/MediaCaptionJob")
        {
            Content = JsonContent.Create(new CreateMediaCaptionJobRequest { FolderId = _rootId })
        };
        yield return new HttpRequestMessage(HttpMethod.Get, "/api/MediaCaptionJob");
        yield return new HttpRequestMessage(HttpMethod.Get, $"/api/MediaCaptionJob/{_failedJobId}");
        yield return new HttpRequestMessage(HttpMethod.Post, $"/api/MediaCaptionJob/items/{_failedItemId}/retry");
        yield return new HttpRequestMessage(HttpMethod.Post, $"/api/MediaCaptionJob/{_failedJobId}/retry-failed");
    }

    private void Seed(AppDbContext db)
    {
        var now = DateTime.UtcNow;
        db.MediaFolders.Add(new MediaFolderModel
        {
            Id = _rootId, Name = "Google Drive", CreatedAt = now
        });
        db.MediaAssets.Add(new MediaAssetModel
        {
            Id = Guid.NewGuid(), FolderId = _rootId, FileName = "create.png",
            StoragePath = "media/create.png", MimeType = "image/png", CreatedAt = now
        });
        db.MediaCaptionJobs.Add(new MediaCaptionJobModel
        {
            Id = _failedJobId, FolderId = Guid.NewGuid(), FolderName = "Completed job",
            Status = MediaCaptionJobStatus.Completed, Total = 1, Failed = 1,
            CreatedAt = now, FinishedAt = now
        });
        db.MediaCaptionJobItems.Add(new MediaCaptionJobItemModel
        {
            Id = _failedItemId, JobId = _failedJobId, MediaAssetId = Guid.NewGuid(),
            FileName = "failed.png", Status = MediaCaptionJobItemStatus.Failed,
            Error = "AI failed", CreatedAt = now
        });
        db.GoogleDriveSyncStates.Single(x => x.Id == GoogleDriveSyncStateModel.SingletonId)
            .DedicatedFolderId = _rootId;
    }

    private async Task<int> CountJobsAsync()
    {
        await using var db = new AppDbContext(_options);
        return await db.MediaCaptionJobs.CountAsync();
    }

    private async Task<MediaCaptionJobItemStatus> GetItemStatusAsync()
    {
        await using var db = new AppDbContext(_options);
        return await db.MediaCaptionJobItems
            .Where(x => x.Id == _failedItemId)
            .Select(x => x.Status)
            .SingleAsync();
    }
}

file sealed class TestAuthHandler : AuthenticationHandler<AuthenticationSchemeOptions>
{
    public const string SchemeName = "TestMediaCaptionJobAuth";

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
        return Task.FromResult(AuthenticateResult.Success(
            new AuthenticationTicket(new ClaimsPrincipal(identity), SchemeName)));
    }
}
