using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text.Encodings.Web;
using System.Text.Json;
using Backend.Data;
using Backend.Modules.PageMessage;
using Backend.Modules.SocialChannel;
using Backend.Modules.SocialChannel.Enums;
using Backend.Modules.SocialComment;
using Backend.Modules.SocialComment.Enums;
using Backend.Modules.Crm.Assignment;
using Backend.Modules.Users;
using Backend.Shared;
using Backend.Shared.PageMessage;
using Backend.Shared.Repositories;
using Backend.Shared.SocialPublish;
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

namespace Backend.Tests.Modules.Users;

/// <summary>
/// CRM AC crm-assignment-test (91c199c8) phần (a) danh sách users + (c) gán tay id+tên;
/// revert-to-prove (a).
/// </summary>
public sealed class CrmAssignmentUsersPipelineTests : IAsyncLifetime
{
    private static readonly HashSet<string> AllowedUserJsonKeys =
        new(StringComparer.OrdinalIgnoreCase) { "id", "displayName", "roles", "isActive" };

    private static readonly string[] ForbiddenUserJsonKeys =
    [
        "email", "phone", "phoneNumber", "passwordHash", "securityStamp",
        "concurrencyStamp", "token", "accessToken", "refreshToken", "normalizedEmail"
    ];

    private readonly SqliteConnection _connection;
    private readonly DbContextOptions<AppDbContext> _options;
    private readonly IHost _host;
    private readonly HttpClient _client;

    public CrmAssignmentUsersPipelineTests()
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
                    services.AddScoped<Backend.Modules.Crm.Customers.CrmCustomerService>();
                    services.AddSingleton(Options.Create(new SocialPublishOptions()));
                    services.AddHttpClient<FacebookPageMessagingProvider>();
                    services.AddScoped<PageMessageService>();
                    services.AddScoped<SocialCommentService>();
                    services.AddSingleton<Backend.Shared.SocialComment.ISocialCommentProvider, StubFacebookCommentProvider>();
                    services.AddSingleton(Options.Create(new Backend.Shared.Meta.MetaOAuthOptions()));
                    services.AddSingleton(Options.Create(new Backend.Shared.Threads.ThreadsOAuthOptions()));
                    services.AddAuthentication(TestAuthHandler.SchemeName)
                        .AddScheme<AuthenticationSchemeOptions, TestAuthHandler>(
                            TestAuthHandler.SchemeName, _ => { });
                    services.AddAuthorization();
                    services.AddControllers(o => o.Filters.Add<GlobalExceptionFilter>())
                        .AddApplicationPart(typeof(UsersController).Assembly);
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

    public async Task InitializeAsync()
    {
        foreach (var role in new[] { "Admin", "ContentManager", "Reviewer", "Viewer" })
            await EnsureRoleAsync(role);
    }

    public async Task DisposeAsync()
    {
        _client.Dispose();
        await _host.StopAsync();
        _host.Dispose();
        await _connection.DisposeAsync();
    }

    // --- (a) list users ---

    [Theory]
    [InlineData("Admin")]
    [InlineData("ContentManager")]
    [InlineData("Reviewer")]
    public async Task ListUsers_AllowedRoles_ReturnOnlySafeFields(string role)
    {
        var userId = await SeedUserAsync("sale@vni.test", "Reviewer");

        using var request = new HttpRequestMessage(HttpMethod.Get, "/api/Users");
        Authorize(request, role);
        using var response = await _client.SendAsync(request);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.True(body.GetProperty("success").GetBoolean());
        var data = body.GetProperty("data");
        Assert.Equal(JsonValueKind.Array, data.ValueKind);

        var item = data.EnumerateArray().Single(x => x.GetProperty("id").GetGuid() == userId);
        foreach (var prop in item.EnumerateObject())
            Assert.Contains(prop.Name, AllowedUserJsonKeys);

        foreach (var forbidden in ForbiddenUserJsonKeys)
            Assert.False(item.TryGetProperty(forbidden, out _), $"Không được lộ {forbidden}");

        // DisplayName = UserName; seed Identity dùng email làm UserName (giống IdentitySeeder).
        Assert.Equal("sale@vni.test", item.GetProperty("displayName").GetString());
        Assert.True(item.GetProperty("isActive").GetBoolean());
        var roles = item.GetProperty("roles").EnumerateArray().Select(x => x.GetString()).ToList();
        Assert.Contains("Reviewer", roles);
    }

    [Fact]
    public async Task ListUsers_Viewer_Returns403()
    {
        await SeedUserAsync("a@vni.test", "Admin");

        using var request = new HttpRequestMessage(HttpMethod.Get, "/api/Users");
        Authorize(request, "Viewer");
        using var response = await _client.SendAsync(request);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task ListUsers_Unauthenticated_Returns401()
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, "/api/Users");
        using var response = await _client.SendAsync(request);
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task ListUsers_ExcludesLockedOutUsers()
    {
        var activeId = await SeedUserAsync("active@vni.test", "Reviewer");
        var lockedId = await SeedUserAsync("locked@vni.test", "Reviewer", lockout: true);

        using var request = new HttpRequestMessage(HttpMethod.Get, "/api/Users");
        Authorize(request, "Admin");
        using var response = await _client.SendAsync(request);
        var data = (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("data");
        var ids = data.EnumerateArray().Select(x => x.GetProperty("id").GetGuid()).ToHashSet();

        Assert.Contains(activeId, ids);
        Assert.DoesNotContain(lockedId, ids);
    }

    /// <summary>
    /// Revert-to-prove (a): serialize ApplicationUser thô sẽ lộ email/passwordHash/securityStamp;
    /// endpoint /api/Users thì không.
    /// </summary>
    [Fact]
    public async Task ListUsers_RevertToProve_RawUserEntityWouldExposeSecrets()
    {
        var userId = await SeedUserAsync("secret@vni.test", "Admin");
        await using var db = new AppDbContext(_options);
        var raw = await db.Users.AsNoTracking().SingleAsync(u => u.Id == userId);
        var unsafeJson = JsonSerializer.Serialize(new
        {
            raw.Id,
            raw.Email,
            raw.PhoneNumber,
            raw.PasswordHash,
            raw.SecurityStamp,
            token = "would-leak"
        });
        using var unsafeDoc = JsonDocument.Parse(unsafeJson);
        var unsafeRoot = unsafeDoc.RootElement;
        Assert.True(unsafeRoot.TryGetProperty("Email", out _) || unsafeRoot.TryGetProperty("email", out _));
        Assert.True(
            unsafeRoot.TryGetProperty("PasswordHash", out _) ||
            unsafeRoot.TryGetProperty("passwordHash", out _));
        Assert.True(
            unsafeRoot.TryGetProperty("SecurityStamp", out _) ||
            unsafeRoot.TryGetProperty("securityStamp", out _));

        using var request = new HttpRequestMessage(HttpMethod.Get, "/api/Users");
        Authorize(request, "Admin");
        using var response = await _client.SendAsync(request);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        var item = body.GetProperty("data").EnumerateArray()
            .Single(x => x.GetProperty("id").GetGuid() == userId);
        foreach (var forbidden in ForbiddenUserJsonKeys)
            Assert.False(item.TryGetProperty(forbidden, out _), $"Endpoint an toàn không được có {forbidden}");
        foreach (var prop in item.EnumerateObject())
            Assert.Contains(prop.Name, AllowedUserJsonKeys);
    }

    // --- (c) assign by id stores id + display name ---

    [Fact]
    public async Task AssignMessage_ByUserId_StoresIdAndDisplayName()
    {
        var assigneeId = await SeedUserAsync("assignee@vni.test", "Reviewer");
        var conversationId = await SeedConversationAsync();

        using var request = new HttpRequestMessage(
            HttpMethod.Post, $"/api/PageMessage/{conversationId}/assign")
        {
            Content = JsonContent.Create(new { assignedUserId = assigneeId })
        };
        Authorize(request, "Admin");
        using var response = await _client.SendAsync(request);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var data = (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("data");
        Assert.Equal(assigneeId, data.GetProperty("assignedUserId").GetGuid());
        Assert.Equal("assignee@vni.test", data.GetProperty("assignedTo").GetString());

        await using var db = new AppDbContext(_options);
        var row = await db.PageConversations.SingleAsync(x => x.Id == conversationId);
        Assert.Equal(assigneeId, row.AssignedUserId);
        Assert.Equal("assignee@vni.test", row.AssignedTo);
    }

    [Fact]
    public async Task AssignComment_ByUserId_StoresIdAndDisplayName()
    {
        var assigneeId = await SeedUserAsync("cmt-assignee@vni.test", "ContentManager");
        var commentId = await SeedCommentAsync();

        using var request = new HttpRequestMessage(
            HttpMethod.Post, $"/api/SocialComment/{commentId}/assign")
        {
            Content = JsonContent.Create(new { assignedUserId = assigneeId })
        };
        Authorize(request, "Reviewer");
        using var response = await _client.SendAsync(request);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var data = (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("data");
        Assert.Equal(assigneeId, data.GetProperty("assignedUserId").GetGuid());
        Assert.Equal("cmt-assignee@vni.test", data.GetProperty("assignedTo").GetString());

        await using var db = new AppDbContext(_options);
        var row = await db.SocialComments.SingleAsync(x => x.Id == commentId);
        Assert.Equal(assigneeId, row.AssignedUserId);
        Assert.Equal("cmt-assignee@vni.test", row.AssignedTo);
    }

    [Fact]
    public async Task AssignMessage_LegacyAssignedToOnly_KeepsNameWithoutUserId()
    {
        var conversationId = await SeedConversationAsync();

        using var request = new HttpRequestMessage(
            HttpMethod.Post, $"/api/PageMessage/{conversationId}/assign")
        {
            Content = JsonContent.Create(new { assignedTo = "Legacy Name" })
        };
        Authorize(request, "Admin");
        using var response = await _client.SendAsync(request);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        await using var db = new AppDbContext(_options);
        var row = await db.PageConversations.SingleAsync(x => x.Id == conversationId);
        Assert.Null(row.AssignedUserId);
        Assert.Equal("Legacy Name", row.AssignedTo);
    }

    // --- helpers ---

    private static void Authorize(HttpRequestMessage request, string role)
        => request.Headers.Authorization = new AuthenticationHeaderValue(
            TestAuthHandler.SchemeName, $"{role}:actor");

    private async Task EnsureRoleAsync(string role)
    {
        await using var scope = _host.Services.CreateAsyncScope();
        var roles = scope.ServiceProvider.GetRequiredService<RoleManager<ApplicationRole>>();
        if (!await roles.RoleExistsAsync(role))
            await roles.CreateAsync(new ApplicationRole { Id = Guid.NewGuid(), Name = role });
    }

    private async Task<Guid> SeedUserAsync(string email, string role, bool lockout = false)
    {
        await using var scope = _host.Services.CreateAsyncScope();
        var users = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        var existing = await users.FindByEmailAsync(email);
        if (existing is not null)
            return existing.Id;

        var user = new ApplicationUser
        {
            Id = Guid.NewGuid(),
            Email = email,
            UserName = email,
            EmailConfirmed = true,
            LockoutEnabled = lockout,
            LockoutEnd = lockout ? DateTimeOffset.UtcNow.AddDays(7) : null
        };
        var created = await users.CreateAsync(user, "Pass1234!");
        Assert.True(created.Succeeded, string.Join("; ", created.Errors.Select(e => e.Description)));
        await users.AddToRoleAsync(user, role);
        return user.Id;
    }

    private async Task<Guid> SeedConversationAsync()
    {
        await using var db = new AppDbContext(_options);
        var channelId = Guid.NewGuid();
        db.SocialChannels.Add(new SocialChannelModel
        {
            Id = channelId,
            Platform = SocialPlatform.Facebook,
            ChannelType = SocialChannelType.Page,
            PageName = "Page Test",
            ExternalPageId = $"ext-{channelId:N}",
            AccessToken = "token",
            IsActive = true,
            CreatedAt = DateTime.UtcNow,
            CreatedBy = "seed"
        });
        var id = Guid.NewGuid();
        db.PageConversations.Add(new PageConversationModel
        {
            Id = id,
            SocialChannelId = channelId,
            ExternalConversationId = $"t_{id:N}",
            ParticipantExternalId = "psid-1",
            ParticipantName = "Customer",
            InboxStatus = MessageInboxStatus.New,
            CreatedAt = DateTime.UtcNow,
            CreatedBy = "seed"
        });
        await db.SaveChangesAsync();
        return id;
    }

    private async Task<Guid> SeedCommentAsync()
    {
        await using var db = new AppDbContext(_options);
        var channelId = Guid.NewGuid();
        db.SocialChannels.Add(new SocialChannelModel
        {
            Id = channelId,
            Platform = SocialPlatform.Facebook,
            ChannelType = SocialChannelType.Page,
            PageName = "Page Cmt",
            ExternalPageId = $"ext-{channelId:N}",
            AccessToken = "token",
            IsActive = true,
            CreatedAt = DateTime.UtcNow,
            CreatedBy = "seed"
        });
        var postId = Guid.NewGuid();
        db.SocialPosts.Add(new SocialPostModel
        {
            Id = postId,
            SocialChannelId = channelId,
            Platform = SocialPlatform.Facebook,
            ExternalPostId = $"post-{postId:N}",
            CreatedAt = DateTime.UtcNow,
            CreatedBy = "seed"
        });
        var id = Guid.NewGuid();
        db.SocialComments.Add(new SocialCommentModel
        {
            Id = id,
            SocialChannelId = channelId,
            SocialPostId = postId,
            Platform = SocialPlatform.Facebook,
            ExternalCommentId = $"cmt-{id:N}",
            AuthorExternalId = "author-1",
            AuthorName = "Commenter",
            Message = "hello",
            InboxStatus = CommentInboxStatus.New,
            CreatedAt = DateTime.UtcNow,
            CreatedBy = "seed"
        });
        await db.SaveChangesAsync();
        return id;
    }

    private sealed class TestAuthHandler : AuthenticationHandler<AuthenticationSchemeOptions>
    {
        public const string SchemeName = "TestCrmUsers";

        public TestAuthHandler(
            IOptionsMonitor<AuthenticationSchemeOptions> options,
            ILoggerFactory logger,
            UrlEncoder encoder) : base(options, logger, encoder) { }

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
                new Claim(ClaimTypes.Role, parts[0]),
            };
            var identity = new ClaimsIdentity(claims, SchemeName);
            return Task.FromResult(
                AuthenticateResult.Success(new AuthenticationTicket(new ClaimsPrincipal(identity), SchemeName)));
        }
    }

    /// <summary>Stub đủ để SocialCommentService.ResolveProvider(Facebook) không ném khi gán.</summary>
    private sealed class StubFacebookCommentProvider : Backend.Shared.SocialComment.ISocialCommentProvider
    {
        public SocialPlatform Platform => SocialPlatform.Facebook;
        public SocialCommentCapabilities Capabilities { get; } = new() { CanReply = true };

        public Task<(List<Backend.Shared.SocialComment.ProviderPostDto> Items, string? NextCursor)> ListPostsAsync(
            string externalPageId, string accessToken, string? cursor, int limit, CancellationToken ct = default)
            => Task.FromResult((new List<Backend.Shared.SocialComment.ProviderPostDto>(), (string?)null));

        public Task<Backend.Shared.SocialComment.ProviderPageProfileDto?> GetPageProfileAsync(
            string externalPageId, string accessToken, CancellationToken ct = default)
            => Task.FromResult<Backend.Shared.SocialComment.ProviderPageProfileDto?>(null);

        public Task<(List<Backend.Shared.SocialComment.ProviderCommentDto> Items, string? NextCursor)> ListCommentsAsync(
            string externalPostId, string accessToken, string? cursor, int limit, CancellationToken ct = default)
            => Task.FromResult((new List<Backend.Shared.SocialComment.ProviderCommentDto>(), (string?)null));

        public Task<Backend.Shared.SocialComment.ProviderCommentDto?> GetCommentAsync(
            string externalCommentId, string accessToken, CancellationToken ct = default)
            => Task.FromResult<Backend.Shared.SocialComment.ProviderCommentDto?>(null);

        public Task<Backend.Shared.SocialComment.ProviderActionResult> ReplyAsync(
            string externalCommentId, string accessToken, string message, string? pageExternalId = null,
            CancellationToken ct = default)
            => Task.FromResult(Backend.Shared.SocialComment.ProviderActionResult.Ok());

        public Task<Backend.Shared.SocialComment.ProviderActionResult> HideAsync(
            string externalCommentId, string accessToken, bool hide, CancellationToken ct = default)
            => Task.FromResult(Backend.Shared.SocialComment.ProviderActionResult.Ok());

        public Task<Backend.Shared.SocialComment.ProviderActionResult> DeleteAsync(
            string externalCommentId, string accessToken, CancellationToken ct = default)
            => Task.FromResult(Backend.Shared.SocialComment.ProviderActionResult.Ok());

        public Task<Backend.Shared.SocialComment.ProviderActionResult> ManagePendingAsync(
            string externalCommentId, string accessToken, bool approve, CancellationToken ct = default)
            => Task.FromResult(Backend.Shared.SocialComment.ProviderActionResult.Ok());
    }
}
