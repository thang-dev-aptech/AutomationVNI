using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text.Encodings.Web;
using Backend.Data;
using Backend.Modules.GoogleDrive;
using Backend.Modules.MediaAsset;
using Backend.Modules.MediaFolder;
using Backend.Shared;
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

namespace Backend.Tests.Modules.GoogleDrive;

/// <summary>
/// GDRIVE-01 (t4): AC gdrive-authz-test — POST pipeline-state chỉ Admin/ContentManager, vai trò
/// khác bị 403 và không đổi trạng thái DB; GET không đòi role, chỉ cần đăng nhập.
/// GDRIVE-03 (t1): AC gdrive03-scan-now-authz-test — cùng khuôn phân quyền cho POST scan-now.
/// </summary>
public class GoogleDriveControllerTests
{
    [Fact]
    public async Task ScanNowEndpoint_EnforcesRoles()
    {
        var connection = new SqliteConnection("DataSource=:memory:");
        await connection.OpenAsync();
        var dbOptions = new DbContextOptionsBuilder<AppDbContext>().UseSqlite(connection).Options;
        await using (var db = new AppDbContext(dbOptions))
            await db.Database.EnsureCreatedAsync();

        using var host = CreateApiHost(dbOptions);
        using var client = host.GetTestClient();

        using (var anonymous = await client.PostAsync("/api/GoogleDrive/scan-now", null))
            Assert.Equal(HttpStatusCode.Unauthorized, anonymous.StatusCode);

        using (var forbidden = ScanNowRequest("Viewer", "viewer-user"))
        using (var response = await client.SendAsync(forbidden))
            Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);

        foreach (var role in new[] { "Admin", "ContentManager" })
        {
            using var allowed = ScanNowRequest(role, $"{role}-user");
            using var response = await client.SendAsync(allowed);
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        }

        await connection.DisposeAsync();
    }

    [Fact]
    public async Task ScanNowEndpoint_ReturnsDisabledMessage_WhenStateDisabled()
    {
        // AC gdrive03-scan-now-disabled-test (84a92bd6) — phần controller.
        var connection = new SqliteConnection("DataSource=:memory:");
        await connection.OpenAsync();
        var dbOptions = new DbContextOptionsBuilder<AppDbContext>().UseSqlite(connection).Options;
        await using (var db = new AppDbContext(dbOptions))
            await db.Database.EnsureCreatedAsync();
        await SetEnabledAsync(dbOptions, false, "test-setup");

        using var host = CreateApiHost(dbOptions);
        using var client = host.GetTestClient();

        using var request = ScanNowRequest("Admin", "admin-user");
        using var response = await client.SendAsync(request);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var body = await response.Content.ReadFromJsonAsync<System.Text.Json.JsonElement>();
        var message = body.GetProperty("message").GetString();
        Assert.Contains("dừng", message, StringComparison.OrdinalIgnoreCase);
        var data = body.GetProperty("data");
        Assert.False(data.GetProperty("enabled").GetBoolean());
        Assert.Equal(0, data.GetProperty("importedCount").GetInt32());

        await connection.DisposeAsync();
    }

    [Fact]
    public async Task PipelineState_ExhaustedCountIsUncapped_AndPostReturnsSameCount()
    {
        var connection = new SqliteConnection("DataSource=:memory:");
        await connection.OpenAsync();
        var dbOptions = new DbContextOptionsBuilder<AppDbContext>().UseSqlite(connection).Options;
        await using (var db = new AppDbContext(dbOptions))
        {
            await db.Database.EnsureCreatedAsync();
            for (var i = 0; i < 21; i++)
            {
                db.Add(new GoogleDriveImportFailureModel
                {
                    GoogleDriveFileId = $"exhausted-{i}",
                    FileName = $"exhausted-{i}.jpg",
                    MimeType = "image/jpeg",
                    SizeBytes = 10,
                    AttemptCount = 5,
                    LastAttemptAt = DateTime.UtcNow,
                    LastError = "hết lượt",
                });
            }

            await db.SaveChangesAsync();
        }

        using var host = CreateApiHost(dbOptions);
        using var client = host.GetTestClient();

        using (var get = new HttpRequestMessage(HttpMethod.Get, "/api/GoogleDrive/pipeline-state"))
        {
            Authorize(get, "Viewer", "viewer-user");
            using var response = await client.SendAsync(get);
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            var body = await response.Content.ReadFromJsonAsync<System.Text.Json.JsonElement>();
            var data = body.GetProperty("data");
            Assert.Equal(21, data.GetProperty("exhaustedFailureCount").GetInt32());
            Assert.Equal(20, data.GetProperty("exhaustedFailures").GetArrayLength());
        }

        using var post = PipelineStateRequest(true, "Admin", "admin-user");
        using var postResponse = await client.SendAsync(post);
        Assert.Equal(HttpStatusCode.OK, postResponse.StatusCode);
        var postBody = await postResponse.Content.ReadFromJsonAsync<System.Text.Json.JsonElement>();
        Assert.Equal(21, postBody.GetProperty("data").GetProperty("exhaustedFailureCount").GetInt32());

        await connection.DisposeAsync();
    }

    private static HttpRequestMessage ScanNowRequest(string role, string userName)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, "/api/GoogleDrive/scan-now");
        Authorize(request, role, userName);
        return request;
    }

    [Fact]
    public async Task PipelineStateEndpoints_EnforceAuthenticationAndRolesWithoutForbiddenWrites()
    {
        var connection = new SqliteConnection("DataSource=:memory:");
        await connection.OpenAsync();
        var dbOptions = new DbContextOptionsBuilder<AppDbContext>().UseSqlite(connection).Options;
        await using (var db = new AppDbContext(dbOptions))
            await db.Database.EnsureCreatedAsync();

        using var host = CreateApiHost(dbOptions);
        using var client = host.GetTestClient();

        using (var anonymousGet = await client.GetAsync("/api/GoogleDrive/pipeline-state"))
            Assert.Equal(HttpStatusCode.Unauthorized, anonymousGet.StatusCode);

        using (var viewerGet = new HttpRequestMessage(HttpMethod.Get, "/api/GoogleDrive/pipeline-state"))
        {
            Authorize(viewerGet, "Viewer", "viewer-user");
            using var response = await client.SendAsync(viewerGet);
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        }

        using (var forbidden = PipelineStateRequest(false, "Viewer", "viewer-user"))
        using (var response = await client.SendAsync(forbidden))
            Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.True(await GetEnabledAsync(dbOptions));

        foreach (var role in new[] { "Admin", "ContentManager" })
        {
            using var allowed = PipelineStateRequest(false, role, $"{role}-user");
            using var response = await client.SendAsync(allowed);
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            Assert.False(await GetEnabledAsync(dbOptions));

            await SetEnabledAsync(dbOptions, true, "test-reset");
        }

        await connection.DisposeAsync();
    }

    private static async Task<bool> GetEnabledAsync(DbContextOptions<AppDbContext> dbOptions)
    {
        await using var db = new AppDbContext(dbOptions);
        var state = await CreateRepository(db).GetSyncStateAsync();
        return state.IsEnabled;
    }

    private static async Task SetEnabledAsync(DbContextOptions<AppDbContext> dbOptions, bool enabled, string userName)
    {
        await using var db = new AppDbContext(dbOptions);
        await CreateRepository(db).SetEnabledAsync(enabled, userName);
    }

    private static GoogleDriveRepository CreateRepository(AppDbContext db) => new(db, new StubUserContext());

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
                    services.AddScoped<GoogleDriveRepository>();
                    services.AddScoped<MediaFolderRepository>();
                    services.AddSingleton<IFileStorageService>(new NoopFileStorageService());
                    services.AddScoped<MediaAssetRepository>();
                    services.AddSingleton<IGoogleDriveClient>(new NotConfiguredGoogleDriveClient());
                    services.AddSingleton<IOptions<GoogleDriveOptions>>(Options.Create(new GoogleDriveOptions()));
                    services.AddSingleton<IOptions<FileStorageOptions>>(Options.Create(new FileStorageOptions()));
                    services.AddScoped<GoogleDriveSyncService>();
                    services.AddLogging();
                    services.AddAuthentication(GoogleDriveTestAuthHandler.SchemeName)
                        .AddScheme<AuthenticationSchemeOptions, GoogleDriveTestAuthHandler>(
                            GoogleDriveTestAuthHandler.SchemeName, _ => { });
                    services.AddAuthorization();
                    services.AddControllers()
                        .AddApplicationPart(typeof(GoogleDriveController).Assembly);
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

    private static HttpRequestMessage PipelineStateRequest(bool enabled, string role, string userName)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, "/api/GoogleDrive/pipeline-state")
        {
            Content = JsonContent.Create(new SetGoogleDrivePipelineStateRequest { Enabled = enabled })
        };
        Authorize(request, role, userName);
        return request;
    }

    private static void Authorize(HttpRequestMessage request, string role, string userName) =>
        request.Headers.Authorization = new AuthenticationHeaderValue(
            GoogleDriveTestAuthHandler.SchemeName, $"{role}:{userName}");

    private sealed class StubUserContext : IUserContext
    {
        public Guid? GetCurrentUserId() => null;
        public string? GetCurrentUserName() => "test";
        public IReadOnlyList<string> GetCurrentUserRoles() => [];
    }

    /// <summary>
    /// ScanNowEndpoint_EnforcesRoles chỉ cần kiểm tra 401/403/200 — client không cấu hình khiến
    /// GoogleDriveSyncService dừng sớm sau pha retry (rỗng), không cần mô phỏng Drive API thật.
    /// </summary>
    private sealed class NotConfiguredGoogleDriveClient : IGoogleDriveClient
    {
        public bool IsConfigured() => false;
        public string? DescribeConfigIssue() => "Chưa cấu hình (test).";
        public Task<string> GetStartPageTokenAsync(CancellationToken ct = default) => Task.FromResult("token");
        public Task<GoogleDriveChangesPage> ListChangesAsync(string? pageToken, int maxResults, CancellationToken ct = default)
            => Task.FromResult(new GoogleDriveChangesPage());
        public Task<byte[]> DownloadFileAsync(string fileId, CancellationToken ct = default)
            => throw new InvalidOperationException("Không cấu hình.");
        public Task<GoogleDriveFolderTree> ListFolderTreeAsync(string rootFolderId, CancellationToken ct = default)
            => Task.FromResult(new GoogleDriveFolderTree());
    }

    private sealed class NoopFileStorageService : IFileStorageService
    {
        public Task<FileSaveResult> SaveAsync(IFormFile file, string folder, CancellationToken ct = default)
            => throw new NotImplementedException();
        public Task<FileSaveResult> SaveBytesAsync(
            byte[] data, string folder, string extension, string contentType, CancellationToken ct = default)
            => throw new NotImplementedException();
        public Task<Stream> OpenReadAsync(string storageKey, CancellationToken ct = default)
            => throw new NotImplementedException();
        public Task<bool> ExistsAsync(string storageKey, CancellationToken ct = default) => Task.FromResult(false);
        public Task DeleteAsync(string storageKey, CancellationToken ct = default) => Task.CompletedTask;
    }
}

file sealed class GoogleDriveTestAuthHandler : AuthenticationHandler<AuthenticationSchemeOptions>
{
    public const string SchemeName = "GoogleDriveTestAuth";

    public GoogleDriveTestAuthHandler(
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
