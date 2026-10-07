using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text.Encodings.Web;
using System.Text.Json;
using Backend.Data;
using Backend.Modules.ChannelGroup;
using Backend.Modules.Post;
using Backend.Modules.Post.Enums;
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

namespace Backend.Tests.Modules.ChannelGroup;

/// <summary>
/// CHANNEL-GROUP-01 AC channel-group-backend-test (430728ee): HTTP pipeline api/ChannelGroup.
/// </summary>
public sealed class ChannelGroupAuthPipelineTests : IAsyncLifetime
{
    private readonly SqliteConnection _connection;
    private readonly DbContextOptions<AppDbContext> _options;
    private readonly IHost _host;
    private readonly HttpClient _client;

    public ChannelGroupAuthPipelineTests()
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
                    services.AddScoped<ChannelGroupRepository>();
                    services.AddLogging();
                    services.AddAuthentication(TestAuthHandler.SchemeName)
                        .AddScheme<AuthenticationSchemeOptions, TestAuthHandler>(
                            TestAuthHandler.SchemeName, _ => { });
                    services.AddAuthorization();
                    services.AddControllers(o => o.Filters.Add<GlobalExceptionFilter>())
                        .AddApplicationPart(typeof(ChannelGroupController).Assembly);
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

    // --- (a) GET ---

    [Theory]
    [InlineData("Admin")]
    [InlineData("ContentManager")]
    [InlineData("Reviewer")]
    [InlineData("Viewer")]
    public async Task Get_AuthenticatedRoles_Return200(string role)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, "/api/ChannelGroup");
        Authorize(request, role);

        using var response = await _client.SendAsync(request);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task Get_Unauthenticated_Returns401()
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, "/api/ChannelGroup");

        using var response = await _client.SendAsync(request);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    // --- (b) POST/PUT/DELETE roles ---

    [Theory]
    [InlineData("Admin")]
    [InlineData("ContentManager")]
    public async Task Mutating_AllowedRoles_Succeed(string role)
    {
        var channel = await SeedChannelAsync("Page A");
        using var create = new HttpRequestMessage(HttpMethod.Post, "/api/ChannelGroup")
        {
            Content = JsonContent.Create(new CreateChannelGroupRequest
            {
                Name = $"Nhóm-{role}",
                ChannelIds = [channel]
            })
        };
        Authorize(create, role);

        using var created = await _client.SendAsync(create);
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        var body = await created.Content.ReadFromJsonAsync<JsonElement>();
        var id = body.GetProperty("data").GetProperty("id").GetGuid();

        using var update = new HttpRequestMessage(HttpMethod.Put, $"/api/ChannelGroup/{id}")
        {
            Content = JsonContent.Create(new UpdateChannelGroupRequest { Name = $"Nhóm-{role}-2" })
        };
        Authorize(update, role);
        using var updated = await _client.SendAsync(update);
        Assert.Equal(HttpStatusCode.OK, updated.StatusCode);

        using var delete = new HttpRequestMessage(HttpMethod.Delete, $"/api/ChannelGroup/{id}");
        Authorize(delete, role);
        using var deleted = await _client.SendAsync(delete);
        Assert.Equal(HttpStatusCode.OK, deleted.StatusCode);
        Assert.True(await IsGroupDeletedAsync(id));
    }

    [Theory]
    [InlineData("Viewer")]
    [InlineData("Reviewer")]
    public async Task Mutating_ForbiddenRoles_Return403_AndDoNotPersist(string role)
    {
        var channel = await SeedChannelAsync($"Page-{role}");
        var before = await CountGroupsAsync();

        using var create = new HttpRequestMessage(HttpMethod.Post, "/api/ChannelGroup")
        {
            Content = JsonContent.Create(new CreateChannelGroupRequest
            {
                Name = $"Forbidden-{role}",
                ChannelIds = [channel]
            })
        };
        Authorize(create, role);
        using var created = await _client.SendAsync(create);
        Assert.Equal(HttpStatusCode.Forbidden, created.StatusCode);
        Assert.Equal(before, await CountGroupsAsync());

        var groupId = await SeedGroupDirectAsync("Existing", [channel]);
        using var update = new HttpRequestMessage(HttpMethod.Put, $"/api/ChannelGroup/{groupId}")
        {
            Content = JsonContent.Create(new UpdateChannelGroupRequest { Name = "ShouldNotApply" })
        };
        Authorize(update, role);
        using var updated = await _client.SendAsync(update);
        Assert.Equal(HttpStatusCode.Forbidden, updated.StatusCode);
        Assert.Equal("Existing", await GetGroupNameAsync(groupId));

        using var delete = new HttpRequestMessage(HttpMethod.Delete, $"/api/ChannelGroup/{groupId}");
        Authorize(delete, role);
        using var deleted = await _client.SendAsync(delete);
        Assert.Equal(HttpStatusCode.Forbidden, deleted.StatusCode);
        Assert.False(await IsGroupDeletedAsync(groupId));
    }

    // --- (c) name validation ---

    [Fact]
    public async Task Create_EmptyName_Returns400()
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/ChannelGroup")
        {
            Content = JsonContent.Create(new CreateChannelGroupRequest { Name = "   " })
        };
        Authorize(request, "Admin");

        using var response = await _client.SendAsync(request);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Create_DuplicateName_CaseInsensitive_Returns400()
    {
        var channel = await SeedChannelAsync("Dup page");
        await SeedGroupDirectAsync("Nhóm Alpha", [channel]);

        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/ChannelGroup")
        {
            Content = JsonContent.Create(new CreateChannelGroupRequest
            {
                Name = "nhóm alpha",
                ChannelIds = [channel]
            })
        };
        Authorize(request, "Admin");

        using var response = await _client.SendAsync(request);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(1, await CountGroupsAsync());
    }

    // --- (d) channel ids ---

    [Fact]
    public async Task Create_DuplicateChannelIdsInRequest_AreDeduped()
    {
        var channel = await SeedChannelAsync("Dedup");
        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/ChannelGroup")
        {
            Content = JsonContent.Create(new CreateChannelGroupRequest
            {
                Name = "Dedup group",
                ChannelIds = [channel, channel, channel]
            })
        };
        Authorize(request, "Admin");

        using var response = await _client.SendAsync(request);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        Assert.Equal(1, body.GetProperty("data").GetProperty("channelCount").GetInt32());
        Assert.Equal(1, await CountMembersAsync(body.GetProperty("data").GetProperty("id").GetGuid()));
    }

    [Fact]
    public async Task Create_MissingOrDeletedChannel_Returns400()
    {
        var missing = Guid.NewGuid();
        var deleted = await SeedChannelAsync("Gone", isDeleted: true);

        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/ChannelGroup")
        {
            Content = JsonContent.Create(new CreateChannelGroupRequest
            {
                Name = "Bad channels",
                ChannelIds = [missing, deleted]
            })
        };
        Authorize(request, "Admin");

        using var response = await _client.SendAsync(request);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(0, await CountGroupsAsync());
    }

    // --- (e) soft-deleted channel disappears from GET ---

    [Fact]
    public async Task Get_OmitsSoftDeletedChannels()
    {
        var keep = await SeedChannelAsync("Keep");
        var drop = await SeedChannelAsync("Drop later");
        var groupId = await SeedGroupDirectAsync("Live group", [keep, drop]);

        await SoftDeleteChannelAsync(drop);

        using var request = new HttpRequestMessage(HttpMethod.Get, $"/api/ChannelGroup/{groupId}");
        Authorize(request, "Viewer");
        using var response = await _client.SendAsync(request);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        var channels = body.GetProperty("data").GetProperty("channels");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(1, channels.GetArrayLength());
        Assert.Equal(keep, channels[0].GetProperty("id").GetGuid());
    }

    // --- (f) soft-delete group does not touch channels/posts ---

    [Fact]
    public async Task Delete_SoftDeletesGroup_LeavesChannelsAndPosts()
    {
        var channel = await SeedChannelAsync("Stay");
        var postId = await SeedPostAsync(channel);
        var groupId = await SeedGroupDirectAsync("To delete", [channel]);

        using var request = new HttpRequestMessage(HttpMethod.Delete, $"/api/ChannelGroup/{groupId}");
        Authorize(request, "Admin");
        using var response = await _client.SendAsync(request);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.True(await IsGroupDeletedAsync(groupId));
        Assert.False(await IsChannelDeletedAsync(channel));
        Assert.False(await IsPostDeletedAsync(postId));
    }

    // --- (g) VNi-first channel order ---

    [Fact]
    public async Task Get_ChannelsOrderedVniFirst()
    {
        var zeta = await SeedChannelAsync("Zeta");
        var vni = await SeedChannelAsync("VNi Hà Nội");
        var alpha = await SeedChannelAsync("Alpha");
        var groupId = await SeedGroupDirectAsync("Order", [zeta, alpha, vni]);

        using var request = new HttpRequestMessage(HttpMethod.Get, $"/api/ChannelGroup/{groupId}");
        Authorize(request, "Admin");
        using var response = await _client.SendAsync(request);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        var names = body.GetProperty("data").GetProperty("channels")
            .EnumerateArray()
            .Select(x => x.GetProperty("pageName").GetString())
            .ToList();

        Assert.Equal(["VNi Hà Nội", "Alpha", "Zeta"], names);
    }

    [Fact]
    public async Task ResolveChannelIdsAsync_SkipsDeletedGroupAndChannel()
    {
        var a = await SeedChannelAsync("A");
        var b = await SeedChannelAsync("B");
        var live = await SeedGroupDirectAsync("Live", [a, b]);
        var gone = await SeedGroupDirectAsync("Gone", [a]);
        await SoftDeleteGroupDirectAsync(gone);
        await SoftDeleteChannelAsync(b);

        await using var scope = _host.Services.CreateAsyncScope();
        var repo = scope.ServiceProvider.GetRequiredService<ChannelGroupRepository>();
        var resolved = await repo.ResolveChannelIdsAsync([live, gone, Guid.NewGuid()]);

        Assert.Equal([a], resolved);
    }

    // --- helpers ---

    private static void Authorize(HttpRequestMessage request, string role)
        => request.Headers.Authorization = new AuthenticationHeaderValue(
            TestAuthHandler.SchemeName, $"{role}:actor");

    private async Task<Guid> SeedChannelAsync(string pageName, bool isDeleted = false)
    {
        await using var db = new AppDbContext(_options);
        var id = Guid.NewGuid();
        db.SocialChannels.Add(new SocialChannelModel
        {
            Id = id,
            Platform = SocialPlatform.Facebook,
            ChannelType = SocialChannelType.Page,
            PageName = pageName,
            ExternalPageId = $"ext-{id:N}",
            AccessToken = "token",
            IsActive = true,
            IsDeleted = isDeleted,
            CreatedAt = DateTime.UtcNow,
            CreatedBy = "seed"
        });
        await db.SaveChangesAsync();
        return id;
    }

    private async Task<Guid> SeedGroupDirectAsync(string name, IReadOnlyList<Guid> channelIds)
    {
        await using var db = new AppDbContext(_options);
        var groupId = Guid.NewGuid();
        db.ChannelGroups.Add(new ChannelGroupModel
        {
            Id = groupId,
            Name = name,
            CreatedAt = DateTime.UtcNow,
            CreatedBy = "seed"
        });
        foreach (var channelId in channelIds)
        {
            db.ChannelGroupMembers.Add(new ChannelGroupMemberModel
            {
                Id = Guid.NewGuid(),
                ChannelGroupId = groupId,
                SocialChannelId = channelId,
                CreatedAt = DateTime.UtcNow,
                CreatedBy = "seed"
            });
        }
        await db.SaveChangesAsync();
        return groupId;
    }

    private async Task SoftDeleteGroupDirectAsync(Guid groupId)
    {
        await using var db = new AppDbContext(_options);
        var group = await db.ChannelGroups.SingleAsync(x => x.Id == groupId);
        group.IsDeleted = true;
        group.DeletedAt = DateTime.UtcNow;
        await db.SaveChangesAsync();
    }

    private async Task SoftDeleteChannelAsync(Guid channelId)
    {
        await using var db = new AppDbContext(_options);
        var channel = await db.SocialChannels.SingleAsync(x => x.Id == channelId);
        channel.IsDeleted = true;
        channel.DeletedAt = DateTime.UtcNow;
        await db.SaveChangesAsync();
    }

    private async Task<Guid> SeedPostAsync(Guid channelId)
    {
        await using var db = new AppDbContext(_options);
        var id = Guid.NewGuid();
        db.Posts.Add(new PostModel
        {
            Id = id,
            Title = "post",
            Content = "c",
            SocialChannelId = channelId,
            Status = PostStatus.Draft,
            UserId = Guid.NewGuid(),
            CreatedAt = DateTime.UtcNow,
            CreatedBy = "seed"
        });
        await db.SaveChangesAsync();
        return id;
    }

    private async Task<int> CountGroupsAsync()
    {
        await using var db = new AppDbContext(_options);
        return await db.ChannelGroups.CountAsync(x => !x.IsDeleted);
    }

    private async Task<int> CountMembersAsync(Guid groupId)
    {
        await using var db = new AppDbContext(_options);
        return await db.ChannelGroupMembers.CountAsync(x => !x.IsDeleted && x.ChannelGroupId == groupId);
    }

    private async Task<bool> IsGroupDeletedAsync(Guid id)
    {
        await using var db = new AppDbContext(_options);
        return await db.ChannelGroups.Where(x => x.Id == id).Select(x => x.IsDeleted).SingleAsync();
    }

    private async Task<bool> IsChannelDeletedAsync(Guid id)
    {
        await using var db = new AppDbContext(_options);
        return await db.SocialChannels.Where(x => x.Id == id).Select(x => x.IsDeleted).SingleAsync();
    }

    private async Task<bool> IsPostDeletedAsync(Guid id)
    {
        await using var db = new AppDbContext(_options);
        return await db.Posts.Where(x => x.Id == id).Select(x => x.IsDeleted).SingleAsync();
    }

    private async Task<string> GetGroupNameAsync(Guid id)
    {
        await using var db = new AppDbContext(_options);
        return await db.ChannelGroups.Where(x => x.Id == id).Select(x => x.Name).SingleAsync();
    }

    private sealed class TestAuthHandler : AuthenticationHandler<AuthenticationSchemeOptions>
    {
        public const string SchemeName = "TestChannelGroup";

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
            return Task.FromResult(AuthenticateResult.Success(
                new AuthenticationTicket(new ClaimsPrincipal(identity), SchemeName)));
        }
    }
}
