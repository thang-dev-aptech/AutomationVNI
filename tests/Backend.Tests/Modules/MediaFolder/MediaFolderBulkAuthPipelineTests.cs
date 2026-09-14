using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text.Encodings.Web;
using Backend.Data;
using Backend.Modules.MediaFolder;
using Backend.Modules.SocialChannel;
using Backend.Modules.SocialChannel.Enums;
using Backend.Shared;
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

namespace Backend.Tests.Modules.MediaFolder;

/// <summary>
/// MEDIA-03-AC2: kiểm tra Authorize pipeline thật (401/403/200), không chỉ reflection attribute.
/// </summary>
public class MediaFolderBulkAuthPipelineTests : IAsyncLifetime
{
    private readonly SqliteConnection _connection;
    private readonly IHost _host;
    private readonly HttpClient _client;
    private readonly Guid _pageId = Guid.NewGuid();

    public MediaFolderBulkAuthPipelineTests()
    {
        _connection = new SqliteConnection("DataSource=:memory:");
        _connection.Open();

        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlite(_connection)
            .Options;

        using (var seedDb = new AppDbContext(options))
        {
            seedDb.Database.EnsureCreated();
            seedDb.SocialChannels.Add(new SocialChannelModel
            {
                Id = _pageId,
                Platform = SocialPlatform.Facebook,
                ChannelType = SocialChannelType.Page,
                PageName = "Auth Page",
                ExternalPageId = "fb-auth-page",
                AccessToken = "token",
                IsActive = true,
                CreatedBy = "admin-user"
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
                    services.AddAuthentication(TestAuthHandler.SchemeName)
                        .AddScheme<AuthenticationSchemeOptions, TestAuthHandler>(
                            TestAuthHandler.SchemeName, _ => { });
                    services.AddAuthorization();
                    services.AddControllers()
                        .AddApplicationPart(typeof(MediaFolderController).Assembly);
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
    public async Task BulkCreate_AllowedRoles_ReturnOkAndPersist(string role)
    {
        var countBefore = await CountFoldersAsync();
        using var request = CreateBulkRequest($"Allowed-{role}");
        request.Headers.Authorization = new AuthenticationHeaderValue(
            TestAuthHandler.SchemeName, $"{role}:admin-user");

        var response = await _client.SendAsync(request);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(countBefore + 1, await CountFoldersAsync());
        Assert.True(await FolderExistsAsync($"Allowed-{role}"));
    }

    [Theory]
    [InlineData("Viewer")]
    [InlineData("Reviewer")]
    public async Task BulkCreate_ForbiddenRoles_Return403AndDoNotPersist(string role)
    {
        var countBefore = await CountFoldersAsync();
        using var request = CreateBulkRequest($"Denied-{role}");
        request.Headers.Authorization = new AuthenticationHeaderValue(
            TestAuthHandler.SchemeName, $"{role}:admin-user");

        var response = await _client.SendAsync(request);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Equal(countBefore, await CountFoldersAsync());
        Assert.False(await FolderExistsAsync($"Denied-{role}"));
    }

    [Fact]
    public async Task BulkCreate_Unauthenticated_Return401AndDoNotPersist()
    {
        var countBefore = await CountFoldersAsync();
        using var request = CreateBulkRequest("Denied-Anon");

        var response = await _client.SendAsync(request);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Equal(countBefore, await CountFoldersAsync());
        Assert.False(await FolderExistsAsync("Denied-Anon"));
    }

    private HttpRequestMessage CreateBulkRequest(string folderName)
    {
        var body = new BulkCreateMediaFolderRequest
        {
            SocialChannelId = _pageId,
            Folders =
            [
                new BulkCreateMediaFolderItem { ClientRef = "n1", Name = folderName }
            ]
        };
        return new HttpRequestMessage(HttpMethod.Post, "/api/MediaFolder/bulk")
        {
            Content = JsonContent.Create(body)
        };
    }

    private async Task<int> CountFoldersAsync()
    {
        await using var db = new AppDbContext(
            new DbContextOptionsBuilder<AppDbContext>().UseSqlite(_connection).Options);
        return await db.MediaFolders.CountAsync();
    }

    private async Task<bool> FolderExistsAsync(string name)
    {
        await using var db = new AppDbContext(
            new DbContextOptionsBuilder<AppDbContext>().UseSqlite(_connection).Options);
        return await db.MediaFolders.AnyAsync(x => x.Name == name);
    }
}

/// <summary>
/// Scheme test: Authorization header value = "{role}:{userName}".
/// </summary>
file sealed class TestAuthHandler : AuthenticationHandler<AuthenticationSchemeOptions>
{
    public const string SchemeName = "TestBulkAuth";

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
