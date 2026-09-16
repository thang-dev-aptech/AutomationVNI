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

/// <summary>MEDIA-06: Authorize pipeline thật cho POST create-across-pages.</summary>
public class MediaFolderCreateAcrossPagesAuthPipelineTests : IAsyncLifetime
{
    private readonly SqliteConnection _connection;
    private readonly IHost _host;
    private readonly HttpClient _client;
    private readonly Guid _pageId = Guid.NewGuid();

    public MediaFolderCreateAcrossPagesAuthPipelineTests()
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
                Id = _pageId, Platform = SocialPlatform.Facebook, ChannelType = SocialChannelType.Page,
                PageName = "Auth Page", ExternalPageId = "fb-across-pages", AccessToken = "token",
                IsActive = true, CreatedBy = "admin-user",
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
                    services.AddControllers(o => o.Filters.Add<GlobalExceptionFilter>())
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

    private HttpRequestMessage CreateRequest(string role, string userName)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, "/api/MediaFolder/create-across-pages")
        {
            Content = JsonContent.Create(new CreateMediaFolderAcrossPagesRequest
            {
                Name = "Campaign X",
                SocialChannelIds = [_pageId],
            }),
        };
        request.Headers.Authorization = new AuthenticationHeaderValue(TestAuthHandler.SchemeName, $"{role}:{userName}");
        return request;
    }

    [Theory]
    [InlineData("Admin")]
    [InlineData("ContentManager")]
    public async Task AllowedRoles_ReturnOkAndCreate(string role)
    {
        using var request = CreateRequest(role, "admin-user");
        var response = await _client.SendAsync(request);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Theory]
    [InlineData("Viewer")]
    [InlineData("Reviewer")]
    public async Task ForbiddenRoles_Return403(string role)
    {
        using var request = CreateRequest(role, "admin-user");
        var response = await _client.SendAsync(request);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Unauthenticated_Returns401()
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/MediaFolder/create-across-pages")
        {
            Content = JsonContent.Create(new CreateMediaFolderAcrossPagesRequest { Name = "X", SocialChannelIds = [_pageId] }),
        };
        var response = await _client.SendAsync(request);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }
}

/// <summary>Scheme test: Authorization header value = "{role}:{userName}".</summary>
file sealed class TestAuthHandler : AuthenticationHandler<AuthenticationSchemeOptions>
{
    public const string SchemeName = "TestAcrossPagesAuth";

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
            new(ClaimTypes.Role, parts[0]),
        };
        var identity = new ClaimsIdentity(claims, SchemeName);
        var ticket = new AuthenticationTicket(new ClaimsPrincipal(identity), SchemeName);
        return Task.FromResult(AuthenticateResult.Success(ticket));
    }
}
