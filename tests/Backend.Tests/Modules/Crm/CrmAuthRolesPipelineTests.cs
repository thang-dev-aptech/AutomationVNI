using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text;
using System.Text.Encodings.Web;
using Backend.Data;
using Backend.Modules.Crm.Assignment;
using Backend.Modules.Crm.Audit;
using Backend.Modules.Crm.Customers;
using Backend.Modules.Crm.Inbox;
using Backend.Modules.Crm.Reminders;
using Backend.Modules.Crm.Tags;
using Backend.Modules.Notification;
using Backend.Modules.PageMessage;
using Backend.Modules.SocialChannel;
using Backend.Modules.SocialChannel.Enums;
using Backend.Modules.SocialComment;
using Backend.Modules.Users;
using Backend.Shared;
using Backend.Shared.Meta;
using Backend.Shared.PageMessage;
using Backend.Shared.Repositories;
using Backend.Shared.SocialComment;
using Backend.Shared.SocialPublish;
using Backend.Shared.Threads;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Xunit;

namespace Backend.Tests.Modules.Crm;

/// <summary>CRM AC crm-auth-roles-test (42a67d24) phần backend — endpoint CRM t2–t6.</summary>
public sealed class CrmAuthRolesPipelineTests : IAsyncLifetime
{
    private readonly SqliteConnection _connection;
    private readonly DbContextOptions<AppDbContext> _options;
    private readonly IHost _host;
    private readonly HttpClient _client;

    public CrmAuthRolesPipelineTests()
    {
        _connection = new SqliteConnection("DataSource=:memory:");
        _connection.Open();
        _options = new DbContextOptionsBuilder<AppDbContext>().UseSqlite(_connection).Options;
        using (var seed = new AppDbContext(_options))
            seed.Database.EnsureCreated();

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
                    services.AddLogging();
                    services.AddIdentityCore<ApplicationUser>(o =>
                        {
                            o.Password.RequireDigit = false;
                            o.Password.RequiredLength = 4;
                            o.Password.RequireNonAlphanumeric = false;
                            o.Password.RequireUppercase = false;
                            o.Password.RequireLowercase = false;
                            o.User.RequireUniqueEmail = true;
                        })
                        .AddRoles<ApplicationRole>()
                        .AddEntityFrameworkStores<AppDbContext>()
                        .AddDefaultTokenProviders();
                    services.AddScoped<UsersService>();
                    services.AddScoped<CrmAutoAssignService>();
                    services.AddScoped<CrmTagService>();
                    services.AddScoped<CrmInboxService>();
                    services.AddScoped<CrmCustomerService>();
                    services.AddScoped<CrmCustomerCareService>();
                    services.AddScoped<CrmReminderService>();
                    services.AddScoped<CrmAuditService>();
                    services.AddScoped<NotificationService>();
                    services.AddSingleton(Options.Create(new SocialPublishOptions()));
                    services.AddSingleton(Options.Create(new MetaOAuthOptions()));
                    services.AddSingleton(Options.Create(new ThreadsOAuthOptions()));
                    services.AddSingleton<ISocialCommentProvider, StubFacebookCommentProvider>();
                    services.AddHttpClient<FacebookPageMessagingProvider>();
                    services.AddScoped<PageMessageService>();
                    services.AddScoped<SocialCommentService>();
                    services.AddAuthentication(TestAuthHandler.SchemeName)
                        .AddScheme<AuthenticationSchemeOptions, TestAuthHandler>(
                            TestAuthHandler.SchemeName, _ => { });
                    services.AddAuthorization();
                    services.AddControllers()
                        .AddApplicationPart(typeof(CrmCustomerController).Assembly);
                });
                web.Configure(app =>
                {
                    app.UseRouting();
                    app.UseAuthentication();
                    app.UseAuthorization();
                    app.UseEndpoints(e => e.MapControllers());
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
    [InlineData("GET", "/api/Users")]
    [InlineData("GET", "/api/CrmTag")]
    [InlineData("GET", "/api/CrmAutoAssign")]
    [InlineData("POST", "/api/CrmInbox/filter")]
    [InlineData("POST", "/api/CrmCustomer/filter")]
    [InlineData("GET", "/api/CrmReminder/buckets")]
    [InlineData("GET", "/api/CrmAudit")]
    [InlineData("GET", "/api/CrmCustomer/export")]
    public async Task Unauthenticated_Returns401(string method, string url)
    {
        using var req = new HttpRequestMessage(new HttpMethod(method), url);
        if (method == "POST")
            req.Content = JsonContent.Create(new { index = 1, size = 10 });
        using var res = await _client.SendAsync(req);
        Assert.Equal(HttpStatusCode.Unauthorized, res.StatusCode);
    }

    [Fact]
    public async Task Viewer_CanRead_CannotWriteOrExport()
    {
        var customerId = await SeedCustomerAsync();
        var beforeCount = await CountCustomersAsync();

        Assert.Equal(HttpStatusCode.OK, await StatusAsync(HttpMethod.Post, "/api/CrmCustomer/filter",
            "Viewer", new { index = 1, size = 10 }));
        Assert.Equal(HttpStatusCode.OK, await StatusAsync(HttpMethod.Get, $"/api/CrmCustomer/{customerId}", "Viewer"));
        Assert.Equal(HttpStatusCode.OK, await StatusAsync(HttpMethod.Post, "/api/CrmInbox/filter",
            "Viewer", new { index = 1, size = 10 }));
        Assert.Equal(HttpStatusCode.OK, await StatusAsync(HttpMethod.Get, "/api/CrmReminder/buckets", "Viewer"));

        Assert.Equal(HttpStatusCode.Forbidden, await StatusAsync(HttpMethod.Post, "/api/CrmCustomer",
            "Viewer", new { displayName = "X" }));
        Assert.Equal(HttpStatusCode.Forbidden, await StatusAsync(HttpMethod.Get, "/api/CrmCustomer/export", "Viewer"));
        Assert.Equal(HttpStatusCode.Forbidden, await StatusAsync(HttpMethod.Post, "/api/CrmTag",
            "Viewer", new { name = "T", color = "#111111" }));
        Assert.Equal(HttpStatusCode.Forbidden, await StatusAsync(HttpMethod.Put, "/api/CrmAutoAssign",
            "Viewer", new { isEnabled = true, assigneeUserIds = Array.Empty<Guid>() }));
        Assert.Equal(HttpStatusCode.Forbidden, await StatusAsync(HttpMethod.Post, "/api/CrmReminder",
            "Viewer", new { crmCustomerId = customerId, title = "r", dueAtUtc = DateTime.UtcNow }));

        Assert.Equal(beforeCount, await CountCustomersAsync());
    }

    [Fact]
    public async Task Reviewer_CanCare_CannotManageCsvTagConfigHardDelete()
    {
        var customerId = await SeedCustomerAsync();

        Assert.Equal(HttpStatusCode.OK, await StatusAsync(HttpMethod.Post, "/api/CrmCustomer",
            "Reviewer", new { displayName = "Care Create" }));
        Assert.Equal(HttpStatusCode.OK, await StatusAsync(HttpMethod.Post, $"/api/CrmCustomer/{customerId}/notes",
            "Reviewer", new { body = "note" }));
        Assert.Equal(HttpStatusCode.OK, await StatusAsync(HttpMethod.Post, "/api/CrmReminder",
            "Reviewer", new { crmCustomerId = customerId, title = "call", dueAtUtc = DateTime.UtcNow.AddDays(1) }));
        Assert.Equal(HttpStatusCode.OK, await StatusAsync(HttpMethod.Get, "/api/CrmCustomer/export", "Reviewer"));
        Assert.Equal(HttpStatusCode.OK, await StatusAsync(HttpMethod.Get, "/api/Users", "Reviewer"));

        Assert.Equal(HttpStatusCode.Forbidden, await StatusAsync(HttpMethod.Post, "/api/CrmTag",
            "Reviewer", new { name = "No", color = "#222222" }));
        Assert.Equal(HttpStatusCode.Forbidden, await StatusAsync(HttpMethod.Put, "/api/CrmAutoAssign",
            "Reviewer", new { isEnabled = false, assigneeUserIds = Array.Empty<Guid>() }));
        Assert.Equal(HttpStatusCode.Forbidden, await StatusAsync(HttpMethod.Get, "/api/CrmCustomer/import/template", "Reviewer"));
        Assert.Equal(HttpStatusCode.Forbidden, await StatusAsync(HttpMethod.Delete, $"/api/CrmCustomer/{customerId}/hard", "Reviewer"));
        Assert.Equal(HttpStatusCode.Forbidden, await StatusAsync(HttpMethod.Post, "/api/CrmCustomer/backfill", "Reviewer"));
    }

    [Fact]
    public async Task ContentManager_CanManage_ButNotHardDelete_AdminCanHardDelete()
    {
        var customerId = await SeedCustomerAsync();

        Assert.Equal(HttpStatusCode.OK, await StatusAsync(HttpMethod.Post, "/api/CrmTag",
            "ContentManager", new { name = $"Tag{Guid.NewGuid():N}"[..12], color = "#333333" }));
        Assert.Equal(HttpStatusCode.OK, await StatusAsync(HttpMethod.Put, "/api/CrmAutoAssign",
            "ContentManager", new { isEnabled = false, assigneeUserIds = Array.Empty<Guid>() }));
        Assert.Equal(HttpStatusCode.OK, await StatusAsync(HttpMethod.Get, "/api/CrmCustomer/import/template", "ContentManager"));
        Assert.Equal(HttpStatusCode.OK, await StatusAsync(HttpMethod.Delete, $"/api/CrmCustomer/{customerId}", "ContentManager"));

        // Soft-deleted — recreate for hard delete checks
        var hardId = await SeedCustomerAsync();
        Assert.Equal(HttpStatusCode.Forbidden,
            await StatusAsync(HttpMethod.Delete, $"/api/CrmCustomer/{hardId}/hard", "ContentManager"));
        Assert.Equal(HttpStatusCode.OK,
            await StatusAsync(HttpMethod.Delete, $"/api/CrmCustomer/{hardId}/hard", "Admin"));

        await using var db = new AppDbContext(_options);
        Assert.False(await db.CrmCustomers.AnyAsync(x => x.Id == hardId));
    }

    [Fact]
    public async Task CsvImport_ReviewerForbidden_AdminOk()
    {
        var csv = "Tên,Số điện thoại\nAuth CSV,0912333444\n";
        using var content = new MultipartFormDataContent();
        content.Add(new ByteArrayContent(Encoding.UTF8.GetBytes(csv)), "file", "a.csv");
        using var req = new HttpRequestMessage(HttpMethod.Post, "/api/CrmCustomer/import/preview") { Content = content };
        Authorize(req, "Reviewer");
        using var res = await _client.SendAsync(req);
        Assert.Equal(HttpStatusCode.Forbidden, res.StatusCode);

        using var content2 = new MultipartFormDataContent();
        content2.Add(new ByteArrayContent(Encoding.UTF8.GetBytes(csv)), "file", "a.csv");
        using var req2 = new HttpRequestMessage(HttpMethod.Post, "/api/CrmCustomer/import/preview") { Content = content2 };
        Authorize(req2, "Admin");
        using var res2 = await _client.SendAsync(req2);
        Assert.Equal(HttpStatusCode.OK, res2.StatusCode);
    }

    private async Task<Guid> SeedCustomerAsync()
    {
        await using var scope = _host.Services.CreateAsyncScope();
        var care = scope.ServiceProvider.GetRequiredService<CrmCustomerCareService>();
        var created = await care.CreateManualAsync(new CreateCrmCustomerRequest
        {
            DisplayName = $"C-{Guid.NewGuid():N}"[..10]
        });
        return created.Id;
    }

    private async Task<int> CountCustomersAsync()
    {
        await using var db = new AppDbContext(_options);
        return await db.CrmCustomers.CountAsync(x => !x.IsDeleted);
    }

    private async Task<HttpStatusCode> StatusAsync(
        HttpMethod method, string url, string role, object? body = null)
    {
        using var req = new HttpRequestMessage(method, url);
        if (body is not null)
            req.Content = JsonContent.Create(body);
        Authorize(req, role);
        using var res = await _client.SendAsync(req);
        return res.StatusCode;
    }

    private static void Authorize(HttpRequestMessage request, string role)
        => request.Headers.Authorization = new AuthenticationHeaderValue(
            TestAuthHandler.SchemeName, $"{role}:actor");

    private sealed class StubFacebookCommentProvider : ISocialCommentProvider
    {
        public SocialPlatform Platform => SocialPlatform.Facebook;
        public SocialCommentCapabilities Capabilities { get; } = new()
        {
            CanReply = true, CanHide = true, CanUnhide = true, CanDelete = true
        };

        public Task<(List<ProviderPostDto> Items, string? NextCursor)> ListPostsAsync(
            string externalPageId, string accessToken, string? cursor, int limit, CancellationToken ct = default)
            => Task.FromResult((new List<ProviderPostDto>(), (string?)null));

        public Task<ProviderPageProfileDto?> GetPageProfileAsync(
            string externalPageId, string accessToken, CancellationToken ct = default)
            => Task.FromResult<ProviderPageProfileDto?>(null);

        public Task<(List<ProviderCommentDto> Items, string? NextCursor)> ListCommentsAsync(
            string externalPostId, string accessToken, string? cursor, int limit, CancellationToken ct = default)
            => Task.FromResult((new List<ProviderCommentDto>(), (string?)null));

        public Task<ProviderCommentDto?> GetCommentAsync(
            string externalCommentId, string accessToken, CancellationToken ct = default)
            => Task.FromResult<ProviderCommentDto?>(null);

        public Task<ProviderActionResult> ReplyAsync(
            string externalCommentId, string accessToken, string message, string? pageExternalId = null,
            CancellationToken ct = default)
            => Task.FromResult(ProviderActionResult.Ok("reply_ext"));

        public Task<ProviderActionResult> HideAsync(
            string externalCommentId, string accessToken, bool hide, CancellationToken ct = default)
            => Task.FromResult(ProviderActionResult.Ok());

        public Task<ProviderActionResult> DeleteAsync(
            string externalCommentId, string accessToken, CancellationToken ct = default)
            => Task.FromResult(ProviderActionResult.Ok());

        public Task<ProviderActionResult> ManagePendingAsync(
            string externalCommentId, string accessToken, bool approve, CancellationToken ct = default)
            => Task.FromResult(ProviderActionResult.Ok());
    }

    private sealed class TestAuthHandler(
        IOptionsMonitor<AuthenticationSchemeOptions> options,
        ILoggerFactory logger,
        UrlEncoder encoder) : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
    {
        public const string SchemeName = "Test";

        protected override Task<AuthenticateResult> HandleAuthenticateAsync()
        {
            var header = Request.Headers.Authorization.ToString();
            if (string.IsNullOrWhiteSpace(header) || !header.StartsWith(SchemeName, StringComparison.Ordinal))
                return Task.FromResult(AuthenticateResult.NoResult());
            var payload = header[(SchemeName.Length + 1)..];
            var parts = payload.Split(':', 2);
            var identity = new ClaimsIdentity(new[]
            {
                new Claim(ClaimTypes.Name, parts.Length > 1 ? parts[1] : "actor"),
                new Claim(ClaimTypes.NameIdentifier, Guid.NewGuid().ToString()),
                new Claim(ClaimTypes.Role, parts[0])
            }, SchemeName);
            return Task.FromResult(AuthenticateResult.Success(
                new AuthenticationTicket(new ClaimsPrincipal(identity), SchemeName)));
        }
    }
}
