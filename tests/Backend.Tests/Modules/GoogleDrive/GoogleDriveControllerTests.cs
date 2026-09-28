using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text.Encodings.Web;
using Backend.Data;
using Backend.Modules.GoogleDrive;
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

namespace Backend.Tests.Modules.GoogleDrive;

/// <summary>
/// GDRIVE-01 (t4): AC gdrive-authz-test — POST pipeline-state chỉ Admin/ContentManager, vai trò
/// khác bị 403 và không đổi trạng thái DB; GET không đòi role, chỉ cần đăng nhập.
/// </summary>
public class GoogleDriveControllerTests
{
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
