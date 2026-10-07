using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text.Encodings.Web;
using System.Text.Json;
using Backend.Data;
using Backend.Modules.Crm.Assignment;
using Backend.Modules.Crm.Tags;
using Backend.Modules.PageMessage;
using Backend.Modules.SocialChannel;
using Backend.Modules.SocialChannel.Enums;
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

namespace Backend.Tests.Modules.Crm;

/// <summary>
/// CRM AC crm-assignment-test (91c199c8) phần (b) tự chia + (d) tag;
/// revert-to-prove lượt song song (b).
/// </summary>
public sealed class CrmAssignmentTagsAutoAssignTests : IAsyncLifetime
{
    private readonly SqliteConnection _connection;
    private readonly DbContextOptions<AppDbContext> _options;
    private readonly IHost _host;
    private readonly HttpClient _client;

    public CrmAssignmentTagsAutoAssignTests()
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
                    services.AddScoped<Backend.Modules.Crm.Customers.CrmCustomerService>();
                    services.AddScoped<Backend.Modules.Crm.Customers.CrmCustomerCareService>();
                    services.AddSingleton(Options.Create(new SocialPublishOptions()));
                    services.AddHttpClient<FacebookPageMessagingProvider>();
                    services.AddScoped<PageMessageService>();
                    services.AddAuthentication(TestAuthHandler.SchemeName)
                        .AddScheme<AuthenticationSchemeOptions, TestAuthHandler>(
                            TestAuthHandler.SchemeName, _ => { });
                    services.AddAuthorization();
                    services.AddControllers(o => o.Filters.Add<GlobalExceptionFilter>())
                        .AddApplicationPart(typeof(CrmTagController).Assembly);
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

    // --- (d) tags ---

    [Fact]
    public async Task Tag_ReviewerCreate_Returns403()
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/CrmTag")
        {
            Content = JsonContent.Create(new { name = "VIP", color = "#FF0000" })
        };
        Authorize(request, "Reviewer");
        using var response = await _client.SendAsync(request);
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Tag_ReviewerAttach_Returns200()
    {
        var tagId = await CreateTagAsAdminAsync("Hot", "#00FF00");
        var conversationId = await SeedConversationAsync(assignedUserId: null);

        using var request = new HttpRequestMessage(HttpMethod.Post, $"/api/CrmTag/{tagId}/attach")
        {
            Content = JsonContent.Create(new
            {
                targetType = CrmTagTargetType.PageConversation,
                targetId = conversationId
            })
        };
        Authorize(request, "Reviewer");
        using var response = await _client.SendAsync(request);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        await using var db = new AppDbContext(_options);
        Assert.True(await db.CrmTagLinks.AnyAsync(x =>
            !x.IsDeleted
            && x.CrmTagId == tagId
            && x.TargetType == CrmTagTargetType.PageConversation
            && x.TargetId == conversationId));
    }

    [Theory]
    [InlineData("Admin")]
    [InlineData("ContentManager")]
    public async Task Tag_ManageRoles_CanCreate(string role)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/CrmTag")
        {
            Content = JsonContent.Create(new { name = $"Tag-{role}", color = "#123456" })
        };
        Authorize(request, role);
        using var response = await _client.SendAsync(request);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    // --- (b) auto-assign ---

    [Fact]
    public async Task AutoAssign_Disabled_DoesNotAssign()
    {
        var a = await SeedUserAsync("a@vni.test", "Reviewer");
        await PutAutoAssignAsync(enabled: false, [a]);
        var conversationId = await SeedConversationAsync(assignedUserId: null);

        await using var scope = _host.Services.CreateAsyncScope();
        var svc = scope.ServiceProvider.GetRequiredService<CrmAutoAssignService>();
        var assigned = await svc.TryAssignNewConversationAsync(conversationId);
        Assert.False(assigned);

        await using var db = new AppDbContext(_options);
        var row = await db.PageConversations.SingleAsync(x => x.Id == conversationId);
        Assert.Null(row.AssignedUserId);
    }

    [Fact]
    public async Task AutoAssign_Enabled_RoundRobin_ThreeUsers_NineConversations()
    {
        var u1 = await SeedUserAsync("u1@vni.test", "Reviewer");
        var u2 = await SeedUserAsync("u2@vni.test", "Reviewer");
        var u3 = await SeedUserAsync("u3@vni.test", "Reviewer");
        await PutAutoAssignAsync(enabled: true, [u1, u2, u3]);

        await using var scope = _host.Services.CreateAsyncScope();
        var svc = scope.ServiceProvider.GetRequiredService<CrmAutoAssignService>();

        var ids = new List<Guid>();
        for (var i = 0; i < 9; i++)
        {
            var id = await SeedConversationAsync(assignedUserId: null);
            ids.Add(id);
            Assert.True(await svc.TryAssignNewConversationAsync(id));
        }

        await using var db = new AppDbContext(_options);
        var counts = await db.PageConversations
            .Where(x => ids.Contains(x.Id))
            .GroupBy(x => x.AssignedUserId!.Value)
            .Select(g => new { UserId = g.Key, Count = g.Count() })
            .ToListAsync();

        Assert.Equal(3, counts.Count);
        Assert.All(counts, c => Assert.Equal(3, c.Count));
        Assert.Equal(9, await db.MessageActionLogs.CountAsync(x =>
            ids.Contains(x.PageConversationId)
            && x.ActionType == MessageActionType.Assign
            && x.ActorUserName == "system:auto-assign"));
    }

    [Fact]
    public async Task AutoAssign_SkipsAlreadyAssigned()
    {
        var u1 = await SeedUserAsync("keep@vni.test", "Reviewer");
        var u2 = await SeedUserAsync("other@vni.test", "Reviewer");
        await PutAutoAssignAsync(enabled: true, [u2]);
        var conversationId = await SeedConversationAsync(assignedUserId: u1, assignedTo: "keep@vni.test");

        await using var scope = _host.Services.CreateAsyncScope();
        var svc = scope.ServiceProvider.GetRequiredService<CrmAutoAssignService>();
        Assert.False(await svc.TryAssignNewConversationAsync(conversationId));

        await using var db = new AppDbContext(_options);
        var row = await db.PageConversations.SingleAsync(x => x.Id == conversationId);
        Assert.Equal(u1, row.AssignedUserId);
    }

    [Fact]
    public async Task AutoAssign_SkipsLockedOutAssignees()
    {
        var locked = await SeedUserAsync("locked@vni.test", "Reviewer", lockout: true);
        var active = await SeedUserAsync("active2@vni.test", "Reviewer");
        await PutAutoAssignAsync(enabled: true, [locked, active]);
        var conversationId = await SeedConversationAsync(assignedUserId: null);

        await using var scope = _host.Services.CreateAsyncScope();
        var svc = scope.ServiceProvider.GetRequiredService<CrmAutoAssignService>();
        Assert.True(await svc.TryAssignNewConversationAsync(conversationId));

        await using var db = new AppDbContext(_options);
        var row = await db.PageConversations.SingleAsync(x => x.Id == conversationId);
        Assert.Equal(active, row.AssignedUserId);
    }

    [Fact]
    public async Task AutoAssign_Parallel_Cas_OnlyOneWins()
    {
        var u1 = await SeedUserAsync("p1@vni.test", "Reviewer");
        var u2 = await SeedUserAsync("p2@vni.test", "Reviewer");
        await PutAutoAssignAsync(enabled: true, [u1, u2]);
        var conversationId = await SeedConversationAsync(assignedUserId: null);

        await using var scope1 = _host.Services.CreateAsyncScope();
        await using var scope2 = _host.Services.CreateAsyncScope();
        var svc1 = scope1.ServiceProvider.GetRequiredService<CrmAutoAssignService>();
        var svc2 = scope2.ServiceProvider.GetRequiredService<CrmAutoAssignService>();

        var results = await Task.WhenAll(
            svc1.TryAssignNewConversationAsync(conversationId),
            svc2.TryAssignNewConversationAsync(conversationId));

        Assert.Equal(1, results.Count(x => x));
        Assert.Equal(1, results.Count(x => !x));

        await using var db = new AppDbContext(_options);
        var row = await db.PageConversations.SingleAsync(x => x.Id == conversationId);
        Assert.NotNull(row.AssignedUserId);
        Assert.Equal(1, await db.MessageActionLogs.CountAsync(x =>
            x.PageConversationId == conversationId
            && x.ActionType == MessageActionType.Assign
            && x.ActorUserName == "system:auto-assign"));
    }

    /// <summary>
    /// Revert-to-prove (b): bỏ điều kiện AssignedUserId == null thì hai lượt song song
    /// đều ghi đè được; CAS thật chỉ cho một lần thắng.
    /// </summary>
    [Fact]
    public async Task AutoAssign_Parallel_RevertToProve_WithoutCasBothOverwrite()
    {
        var first = await SeedUserAsync("first@vni.test", "Reviewer");
        var second = await SeedUserAsync("second@vni.test", "Reviewer");
        var conversationId = await SeedConversationAsync(assignedUserId: null);

        // Unsafe: UPDATE không có WHERE AssignedUserId IS NULL
        async Task UnsafeAssignAsync(Guid userId, string name)
        {
            await using var db = new AppDbContext(_options);
            var now = DateTime.UtcNow;
            await db.PageConversations
                .Where(x => x.Id == conversationId && !x.IsDeleted)
                .ExecuteUpdateAsync(s => s
                    .SetProperty(x => x.AssignedUserId, userId)
                    .SetProperty(x => x.AssignedTo, name)
                    .SetProperty(x => x.UpdatedAt, now));
        }

        await Task.WhenAll(
            UnsafeAssignAsync(first, "first@vni.test"),
            UnsafeAssignAsync(second, "second@vni.test"));

        await using var afterUnsafe = new AppDbContext(_options);
        var overwritten = await afterUnsafe.PageConversations.SingleAsync(x => x.Id == conversationId);
        Assert.NotNull(overwritten.AssignedUserId);
        // Cả hai "thành công" ghi — kết quả cuối là một trong hai; chứng minh không có CAS.
        Assert.Contains(overwritten.AssignedUserId!.Value, new[] { first, second });

        // Reset rồi chạy CAS thật: chỉ một lần thắng.
        overwritten.AssignedUserId = null;
        overwritten.AssignedTo = null;
        await afterUnsafe.SaveChangesAsync();

        await PutAutoAssignAsync(enabled: true, [first, second]);
        await using var scope1 = _host.Services.CreateAsyncScope();
        await using var scope2 = _host.Services.CreateAsyncScope();
        var results = await Task.WhenAll(
            scope1.ServiceProvider.GetRequiredService<CrmAutoAssignService>()
                .TryAssignNewConversationAsync(conversationId),
            scope2.ServiceProvider.GetRequiredService<CrmAutoAssignService>()
                .TryAssignNewConversationAsync(conversationId));
        Assert.Equal(1, results.Count(x => x));
    }

    [Fact]
    public async Task AutoAssign_Config_Reviewer_Returns403()
    {
        using var request = new HttpRequestMessage(HttpMethod.Put, "/api/CrmAutoAssign")
        {
            Content = JsonContent.Create(new { isEnabled = true, assigneeUserIds = Array.Empty<Guid>() })
        };
        Authorize(request, "Reviewer");
        using var response = await _client.SendAsync(request);
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
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
        if (existing is not null) return existing.Id;

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

    private async Task PutAutoAssignAsync(bool enabled, IReadOnlyList<Guid> userIds)
    {
        using var request = new HttpRequestMessage(HttpMethod.Put, "/api/CrmAutoAssign")
        {
            Content = JsonContent.Create(new { isEnabled = enabled, assigneeUserIds = userIds })
        };
        Authorize(request, "Admin");
        using var response = await _client.SendAsync(request);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    private async Task<Guid> CreateTagAsAdminAsync(string name, string color)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/CrmTag")
        {
            Content = JsonContent.Create(new { name, color })
        };
        Authorize(request, "Admin");
        using var response = await _client.SendAsync(request);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        return body.GetProperty("data").GetProperty("id").GetGuid();
    }

    private async Task<Guid> SeedConversationAsync(Guid? assignedUserId, string? assignedTo = null)
    {
        await using var db = new AppDbContext(_options);
        var channelId = Guid.NewGuid();
        db.SocialChannels.Add(new SocialChannelModel
        {
            Id = channelId,
            Platform = SocialPlatform.Facebook,
            ChannelType = SocialChannelType.Page,
            PageName = "Page Auto",
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
            ParticipantExternalId = $"psid-{id:N}",
            ParticipantName = "Customer",
            InboxStatus = MessageInboxStatus.New,
            AssignedUserId = assignedUserId,
            AssignedTo = assignedTo,
            CreatedAt = DateTime.UtcNow,
            CreatedBy = "seed"
        });
        await db.SaveChangesAsync();
        return id;
    }

    private sealed class TestAuthHandler : AuthenticationHandler<AuthenticationSchemeOptions>
    {
        public const string SchemeName = "TestCrmAssignTags";

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
}
