using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text.Encodings.Web;
using Backend.Data;
using Backend.Modules.MediaAsset;
using Backend.Modules.MediaFolder;
using Backend.Modules.SocialChannel;
using Backend.Modules.SocialChannel.Enums;
using Backend.Shared.Ai;
using Backend.Shared.Repositories;
using Backend.Shared.Storage;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
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
/// Kiểm tra Authorize pipeline thật (401/403/200/404) cho Move/Delete media asset,
/// không chỉ reflection attribute. Gương MediaFolderLifecycleAuthPipelineTests.
/// </summary>
public class MediaAssetLifecycleAuthPipelineTests : IAsyncLifetime
{
    private readonly SqliteConnection _connection;
    private readonly IHost _host;
    private readonly HttpClient _client;
    private readonly Guid _ownedPageId = Guid.NewGuid();
    private readonly Guid _otherPageId = Guid.NewGuid();
    private const string SecretFolderName = "SecretTargetFolder-DoNotLeak";

    public MediaAssetLifecycleAuthPipelineTests()
    {
        _connection = new SqliteConnection("DataSource=:memory:");
        _connection.Open();

        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlite(_connection)
            .Options;

        using (var seedDb = new AppDbContext(options))
        {
            seedDb.Database.EnsureCreated();
            seedDb.SocialChannels.AddRange(
                new SocialChannelModel
                {
                    Id = _ownedPageId,
                    Platform = SocialPlatform.Facebook,
                    ChannelType = SocialChannelType.Page,
                    PageName = "Owned Auth Page",
                    ExternalPageId = "fb-mediaasset-auth-owned",
                    AccessToken = "token",
                    IsActive = true,
                    CreatedBy = "admin-user"
                },
                new SocialChannelModel
                {
                    Id = _otherPageId,
                    Platform = SocialPlatform.Facebook,
                    ChannelType = SocialChannelType.Page,
                    PageName = "Other Auth Page",
                    ExternalPageId = "fb-mediaasset-auth-other",
                    AccessToken = "token",
                    IsActive = true,
                    CreatedBy = "other-owner"
                });
            seedDb.SaveChanges();
        }

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
                    services.AddSingleton<IFileStorageService, NoopFileStorageService>();
                    services.AddSingleton(Options.Create(new AiProvidersOptions()));
                    services.AddSingleton(_ => new HttpClient());
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
    public async Task Delete_AllowedRoles_ReturnOkAndDelete(string role)
    {
        var assetId = await CreateAssetDirectAsync($"ToDelete-{role}.jpg");
        using var request = new HttpRequestMessage(HttpMethod.Delete, $"/api/MediaAsset/{assetId}");
        Authorize(request, role);

        var response = await _client.SendAsync(request);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.True(await AssetIsDeletedAsync(assetId));
    }

    [Theory]
    [InlineData("Viewer")]
    [InlineData("Reviewer")]
    public async Task Delete_ForbiddenRoles_Return403AndDoNotDelete(string role)
    {
        var assetId = await CreateAssetDirectAsync($"NoDelete-{role}.jpg");
        using var request = new HttpRequestMessage(HttpMethod.Delete, $"/api/MediaAsset/{assetId}");
        Authorize(request, role);

        var response = await _client.SendAsync(request);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.False(await AssetIsDeletedAsync(assetId));
    }

    [Fact]
    public async Task Delete_Unauthenticated_Return401AndDoesNotDelete()
    {
        var assetId = await CreateAssetDirectAsync("Denied-Anon-Delete.jpg");
        using var request = new HttpRequestMessage(HttpMethod.Delete, $"/api/MediaAsset/{assetId}");

        var response = await _client.SendAsync(request);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.False(await AssetIsDeletedAsync(assetId));
    }

    [Theory]
    [InlineData("Admin")]
    [InlineData("ContentManager")]
    public async Task Move_AllowedRoles_ReturnOkAndPersist(string role)
    {
        var assetId = await CreateAssetDirectAsync($"ToMove-{role}.jpg");
        var folderId = await CreateFolderDirectAsync($"Dest-{role}", _ownedPageId);
        using var request = MoveRequest(assetId, folderId);
        Authorize(request, role);

        var response = await _client.SendAsync(request);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(folderId, await GetAssetFolderIdAsync(assetId));
    }

    [Theory]
    [InlineData("Viewer")]
    [InlineData("Reviewer")]
    public async Task Move_ForbiddenRoles_Return403AndDoNotPersist(string role)
    {
        var assetId = await CreateAssetDirectAsync($"NoMove-{role}.jpg");
        var folderId = await CreateFolderDirectAsync($"DeniedDest-{role}", _ownedPageId);
        using var request = MoveRequest(assetId, folderId);
        Authorize(request, role);

        var response = await _client.SendAsync(request);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Null(await GetAssetFolderIdAsync(assetId));
    }

    [Fact]
    public async Task Move_Unauthenticated_Return401AndDoNotPersist()
    {
        var assetId = await CreateAssetDirectAsync("Denied-Anon-Move.jpg");
        var folderId = await CreateFolderDirectAsync("Denied-Anon-Dest", _ownedPageId);
        using var request = MoveRequest(assetId, folderId);

        var response = await _client.SendAsync(request);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Null(await GetAssetFolderIdAsync(assetId));
    }

    [Fact]
    public async Task Move_ContentManager_UnwritablePage_Returns404WithoutLeakingFolderMetadata()
    {
        var assetId = await CreateAssetDirectAsync("CrossPage.jpg");
        var folderId = await CreateFolderDirectAsync(SecretFolderName, _otherPageId);
        using var request = MoveRequest(assetId, folderId);
        Authorize(request, "ContentManager", "admin-user");

        var response = await _client.SendAsync(request);
        var body = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Contains("Page/Kênh không tồn tại.", body);
        Assert.DoesNotContain(SecretFolderName, body);
        Assert.DoesNotContain(folderId.ToString(), body);
        Assert.DoesNotContain(_otherPageId.ToString(), body);
        Assert.Null(await GetAssetFolderIdAsync(assetId));
    }

    [Fact]
    public async Task Move_Admin_OtherUsersPage_Succeeds()
    {
        var assetId = await CreateAssetDirectAsync("AdminCrossPage.jpg");
        var folderId = await CreateFolderDirectAsync("AdminDest", _otherPageId);
        using var request = MoveRequest(assetId, folderId);
        Authorize(request, "Admin");

        var response = await _client.SendAsync(request);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(folderId, await GetAssetFolderIdAsync(assetId));
    }

    private static void Authorize(HttpRequestMessage request, string role, string userName = "admin-user") =>
        request.Headers.Authorization = new AuthenticationHeaderValue(
            TestAuthHandler.SchemeName, $"{role}:{userName}");

    private static HttpRequestMessage MoveRequest(Guid assetId, Guid folderId) =>
        new(HttpMethod.Post, "/api/MediaAsset/move")
        {
            Content = JsonContent.Create(new MoveMediaAssetsRequest
            {
                Ids = [assetId],
                FolderId = folderId
            })
        };

    private async Task<Guid> CreateAssetDirectAsync(string fileName)
    {
        await using var db = new AppDbContext(
            new DbContextOptionsBuilder<AppDbContext>().UseSqlite(_connection).Options);
        var asset = new MediaAssetModel
        {
            Id = Guid.NewGuid(),
            FileName = fileName,
            StoragePath = "p",
            PublicUrl = "u"
        };
        db.MediaAssets.Add(asset);
        await db.SaveChangesAsync();
        return asset.Id;
    }

    private async Task<Guid> CreateFolderDirectAsync(string name, Guid pageId)
    {
        await using var db = new AppDbContext(
            new DbContextOptionsBuilder<AppDbContext>().UseSqlite(_connection).Options);
        var folder = new MediaFolderModel
        {
            Id = Guid.NewGuid(),
            Name = name,
            SocialChannelId = pageId
        };
        db.MediaFolders.Add(folder);
        await db.SaveChangesAsync();
        return folder.Id;
    }

    private async Task<bool> AssetIsDeletedAsync(Guid id)
    {
        await using var db = new AppDbContext(
            new DbContextOptionsBuilder<AppDbContext>().UseSqlite(_connection).Options);
        var asset = await db.MediaAssets.FindAsync(id);
        return asset is { IsDeleted: true };
    }

    private async Task<Guid?> GetAssetFolderIdAsync(Guid id)
    {
        await using var db = new AppDbContext(
            new DbContextOptionsBuilder<AppDbContext>().UseSqlite(_connection).Options);
        var asset = await db.MediaAssets.FindAsync(id);
        return asset?.FolderId;
    }
}

file sealed class TestAuthHandler : AuthenticationHandler<AuthenticationSchemeOptions>
{
    public const string SchemeName = "TestMediaAssetAuth";

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

file sealed class NoopFileStorageService : IFileStorageService
{
    public Task<FileSaveResult> SaveAsync(IFormFile file, string folder, CancellationToken ct = default)
        => throw new NotImplementedException();

    public Task<FileSaveResult> SaveBytesAsync(
        byte[] data, string folder, string extension, string contentType, CancellationToken ct = default)
        => throw new NotImplementedException();

    public Task<Stream> OpenReadAsync(string storageKey, CancellationToken ct = default)
        => throw new NotImplementedException();

    public Task<bool> ExistsAsync(string storageKey, CancellationToken ct = default)
        => Task.FromResult(false);

    public Task DeleteAsync(string storageKey, CancellationToken ct = default)
        => Task.CompletedTask;
}
