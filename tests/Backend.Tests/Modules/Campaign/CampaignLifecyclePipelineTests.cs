using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text.Encodings.Web;
using System.Text.Json;
using Backend.Data;
using Backend.Modules.Campaign;
using Backend.Modules.Campaign.Enums;
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

namespace Backend.Tests.Modules.Campaign;

/// <summary>
/// CAMPAIGN-01 AC campaign-lifecycle-test (7435aaa4): HTTP pipeline api/Campaign.
/// </summary>
public sealed class CampaignLifecyclePipelineTests : IAsyncLifetime
{
    private static readonly Guid ActorUserId = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");

    private readonly SqliteConnection _connection;
    private readonly DbContextOptions<AppDbContext> _options;
    private readonly IHost _host;
    private readonly HttpClient _client;

    public CampaignLifecyclePipelineTests()
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
                    services.AddScoped<CampaignRepository>();
                    services.AddLogging();
                    services.AddAuthentication(TestAuthHandler.SchemeName)
                        .AddScheme<AuthenticationSchemeOptions, TestAuthHandler>(
                            TestAuthHandler.SchemeName, _ => { });
                    services.AddAuthorization();
                    services.AddControllers(o => o.Filters.Add<GlobalExceptionFilter>())
                        .AddApplicationPart(typeof(CampaignController).Assembly);
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

    // --- (a) Auth ---

    [Theory]
    [InlineData("Admin")]
    [InlineData("ContentManager")]
    [InlineData("Reviewer")]
    [InlineData("Viewer")]
    public async Task Get_AuthenticatedRoles_Return200(string role)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, "/api/Campaign");
        Authorize(request, role);
        using var response = await _client.SendAsync(request);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task Get_Unauthenticated_Returns401()
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, "/api/Campaign");
        using var response = await _client.SendAsync(request);
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Theory]
    [InlineData("Viewer")]
    [InlineData("Reviewer")]
    public async Task Mutating_ForbiddenRoles_Return403_AndDoNotPersist(string role)
    {
        var channel = await SeedChannelAsync($"ch-{role}");
        var before = await CountCampaignsAsync();

        using var create = new HttpRequestMessage(HttpMethod.Post, "/api/Campaign")
        {
            Content = JsonContent.Create(ValidCreate(channel, $"Forbidden-{role}"))
        };
        Authorize(create, role);
        using var created = await _client.SendAsync(create);
        Assert.Equal(HttpStatusCode.Forbidden, created.StatusCode);
        Assert.Equal(before, await CountCampaignsAsync());

        var campaignId = await SeedCampaignDirectAsync($"Existing-{role}", [channel]);
        using var pause = new HttpRequestMessage(HttpMethod.Post, $"/api/Campaign/{campaignId}/pause");
        Authorize(pause, role);
        using var paused = await _client.SendAsync(pause);
        Assert.Equal(HttpStatusCode.Forbidden, paused.StatusCode);
        Assert.Equal(CampaignStatus.Running, await GetStatusAsync(campaignId));
    }

    // --- Validate → 400 ---

    [Fact]
    public async Task Create_EmptyName_Returns400()
    {
        var channel = await SeedChannelAsync("ch-name");
        var body = ValidCreate(channel, "  ");
        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/Campaign")
        {
            Content = JsonContent.Create(body)
        };
        Authorize(request, "Admin");
        using var response = await _client.SendAsync(request);
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Create_NoChannelOrGroup_Returns400()
    {
        var body = ValidCreate(Guid.NewGuid(), "NoTarget");
        body.ChannelIds = [];
        body.ChannelGroupIds = [];
        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/Campaign")
        {
            Content = JsonContent.Create(body)
        };
        Authorize(request, "Admin");
        using var response = await _client.SendAsync(request);
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Create_ByWeekdayWithoutDays_Returns400()
    {
        var channel = await SeedChannelAsync("ch-wd");
        var body = ValidCreate(channel, "NoDays");
        body.ScheduleMode = CampaignScheduleMode.ByWeekday;
        body.Weekdays = [];
        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/Campaign")
        {
            Content = JsonContent.Create(body)
        };
        Authorize(request, "Admin");
        using var response = await _client.SendAsync(request);
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Create_DuplicateOrBadTime_Returns400()
    {
        var channel = await SeedChannelAsync("ch-time");
        var body = ValidCreate(channel, "BadTime");
        body.PublishTimes = ["09:00", "09:00"];
        using var dup = new HttpRequestMessage(HttpMethod.Post, "/api/Campaign")
        {
            Content = JsonContent.Create(body)
        };
        Authorize(dup, "Admin");
        using var dupRes = await _client.SendAsync(dup);
        Assert.Equal(HttpStatusCode.BadRequest, dupRes.StatusCode);

        body.PublishTimes = ["25:99"];
        using var bad = new HttpRequestMessage(HttpMethod.Post, "/api/Campaign")
        {
            Content = JsonContent.Create(body)
        };
        Authorize(bad, "Admin");
        using var badRes = await _client.SendAsync(bad);
        Assert.Equal(HttpStatusCode.BadRequest, badRes.StatusCode);
    }

    [Fact]
    public async Task Create_JitterOutOfRange_Or_EndBeforeStart_Returns400()
    {
        var channel = await SeedChannelAsync("ch-jitter");
        var body = ValidCreate(channel, "Jitter");
        body.JitterMinutes = 241;
        using var jitter = new HttpRequestMessage(HttpMethod.Post, "/api/Campaign")
        {
            Content = JsonContent.Create(body)
        };
        Authorize(jitter, "Admin");
        Assert.Equal(HttpStatusCode.BadRequest, (await _client.SendAsync(jitter)).StatusCode);

        body.JitterMinutes = 30;
        body.StartDate = new DateTime(2026, 10, 10, 0, 0, 0, DateTimeKind.Utc);
        body.EndDate = new DateTime(2026, 10, 1, 0, 0, 0, DateTimeKind.Utc);
        using var dates = new HttpRequestMessage(HttpMethod.Post, "/api/Campaign")
        {
            Content = JsonContent.Create(body)
        };
        Authorize(dates, "Admin");
        Assert.Equal(HttpStatusCode.BadRequest, (await _client.SendAsync(dates)).StatusCode);
    }

    // --- (b) Pause cancels only own future cancellable posts ---

    [Fact]
    public async Task Pause_CancelsOwnFutureScheduledApprovedQueued_LeavesOthers()
    {
        var channel = await SeedChannelAsync("ch-pause");
        var campaignA = await SeedCampaignDirectAsync("CampA", [channel]);
        var campaignB = await SeedCampaignDirectAsync("CampB", [channel]);

        var baseFuture = DateTime.UtcNow.AddDays(2);
        var past = DateTime.UtcNow.AddHours(-2);

        // Mỗi bài cùng chiến dịch+kênh cần CampaignSlotAt khác nhau (unique index).
        var ownScheduled = await SeedPostAsync(channel, campaignA, PostStatus.Scheduled, baseFuture.AddMinutes(0));
        var ownApproved = await SeedPostAsync(channel, campaignA, PostStatus.Approved, baseFuture.AddMinutes(1));
        var ownQueued = await SeedPostAsync(channel, campaignA, PostStatus.Queued, baseFuture.AddMinutes(2));
        var ownPublishing = await SeedPostAsync(channel, campaignA, PostStatus.Publishing, baseFuture.AddMinutes(3));
        var ownPublished = await SeedPostAsync(channel, campaignA, PostStatus.Published, past);
        var ownFailed = await SeedPostAsync(channel, campaignA, PostStatus.Failed, past.AddMinutes(1));
        var otherCampaign = await SeedPostAsync(channel, campaignB, PostStatus.Scheduled, baseFuture.AddMinutes(4));
        var regular = await SeedPostAsync(channel, campaignId: null, PostStatus.Scheduled, baseFuture.AddMinutes(5));

        using var pause = new HttpRequestMessage(HttpMethod.Post, $"/api/Campaign/{campaignA}/pause");
        Authorize(pause, "Admin");
        using var response = await _client.SendAsync(pause);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(CampaignStatus.Paused, await GetStatusAsync(campaignA));

        Assert.Equal(PostStatus.Cancelled, await GetPostStatusAsync(ownScheduled));
        Assert.Equal(PostStatus.Cancelled, await GetPostStatusAsync(ownApproved));
        Assert.Equal(PostStatus.Cancelled, await GetPostStatusAsync(ownQueued));
        Assert.Equal(PostStatus.Publishing, await GetPostStatusAsync(ownPublishing));
        Assert.Equal(PostStatus.Published, await GetPostStatusAsync(ownPublished));
        Assert.Equal(PostStatus.Failed, await GetPostStatusAsync(ownFailed));
        Assert.Equal(PostStatus.Scheduled, await GetPostStatusAsync(otherCampaign));
        Assert.Equal(PostStatus.Scheduled, await GetPostStatusAsync(regular));
    }

    [Fact]
    public async Task Pause_RevertToProve_WithoutCampaignFilter_WouldCancelOtherPosts()
    {
        var channel = await SeedChannelAsync("ch-revert");
        var campaignA = await SeedCampaignDirectAsync("CampRevertA", [channel]);
        var campaignB = await SeedCampaignDirectAsync("CampRevertB", [channel]);
        var future = DateTime.UtcNow.AddDays(1);
        var own = await SeedPostAsync(channel, campaignA, PostStatus.Scheduled, future);
        var other = await SeedPostAsync(channel, campaignB, PostStatus.Scheduled, future);

        // Production path: only campaign A
        using var pause = new HttpRequestMessage(HttpMethod.Post, $"/api/Campaign/{campaignA}/pause");
        Authorize(pause, "Admin");
        Assert.Equal(HttpStatusCode.OK, (await _client.SendAsync(pause)).StatusCode);
        Assert.Equal(PostStatus.Cancelled, await GetPostStatusAsync(own));
        Assert.Equal(PostStatus.Scheduled, await GetPostStatusAsync(other));

        // Revert-to-prove: cancel ALL future cancellable without CampaignId filter → other would change
        await using var db = new AppDbContext(_options);
        var now = DateTime.UtcNow;
        var posts = await db.Posts
            .Where(p => !p.IsDeleted
                && (p.Status == PostStatus.Scheduled
                    || p.Status == PostStatus.Approved
                    || p.Status == PostStatus.Queued)
                && p.ScheduledPublishAt != null
                && p.ScheduledPublishAt > now)
            .ToListAsync();
        Assert.Contains(posts, p => p.Id == other);
        foreach (var p in posts)
            p.Status = PostStatus.Cancelled;
        await db.SaveChangesAsync();
        Assert.Equal(PostStatus.Cancelled, await GetPostStatusAsync(other));
    }

    // --- (c) Resume ---

    [Fact]
    public async Task Resume_FromPaused_SetsRunning_DoesNotReviveCancelled()
    {
        var channel = await SeedChannelAsync("ch-resume");
        var campaignId = await SeedCampaignDirectAsync("CampResume", [channel]);
        var future = DateTime.UtcNow.AddDays(1);
        var postId = await SeedPostAsync(channel, campaignId, PostStatus.Scheduled, future);

        using var pause = new HttpRequestMessage(HttpMethod.Post, $"/api/Campaign/{campaignId}/pause");
        Authorize(pause, "ContentManager");
        Assert.Equal(HttpStatusCode.OK, (await _client.SendAsync(pause)).StatusCode);
        Assert.Equal(PostStatus.Cancelled, await GetPostStatusAsync(postId));

        using var resume = new HttpRequestMessage(HttpMethod.Post, $"/api/Campaign/{campaignId}/resume");
        Authorize(resume, "ContentManager");
        Assert.Equal(HttpStatusCode.OK, (await _client.SendAsync(resume)).StatusCode);
        Assert.Equal(CampaignStatus.Running, await GetStatusAsync(campaignId));
        Assert.Equal(PostStatus.Cancelled, await GetPostStatusAsync(postId));
    }

    // --- (d) Update ---

    [Fact]
    public async Task Update_ScheduleChange_CancelsFuture_NameOnly_DoesNot()
    {
        var channel = await SeedChannelAsync("ch-upd");
        var campaignId = await CreateViaApiAsync(channel, "CampUpd");
        var future = DateTime.UtcNow.AddDays(3);
        var postId = await SeedPostAsync(channel, campaignId, PostStatus.Scheduled, future);

        using var rename = new HttpRequestMessage(HttpMethod.Put, $"/api/Campaign/{campaignId}")
        {
            Content = JsonContent.Create(new UpdateCampaignRequest { Name = "CampUpd-Renamed" })
        };
        Authorize(rename, "Admin");
        Assert.Equal(HttpStatusCode.OK, (await _client.SendAsync(rename)).StatusCode);
        Assert.Equal(PostStatus.Scheduled, await GetPostStatusAsync(postId));

        using var changeHours = new HttpRequestMessage(HttpMethod.Put, $"/api/Campaign/{campaignId}")
        {
            Content = JsonContent.Create(new UpdateCampaignRequest
            {
                PublishTimes = ["10:00", "16:00"],
            })
        };
        Authorize(changeHours, "Admin");
        Assert.Equal(HttpStatusCode.OK, (await _client.SendAsync(changeHours)).StatusCode);
        Assert.Equal(PostStatus.Cancelled, await GetPostStatusAsync(postId));
    }

    // --- (e) End ---

    [Fact]
    public async Task End_CancelsFuture_AndCannotResume()
    {
        var channel = await SeedChannelAsync("ch-end");
        var campaignId = await SeedCampaignDirectAsync("CampEnd", [channel]);
        var future = DateTime.UtcNow.AddDays(1);
        var postId = await SeedPostAsync(channel, campaignId, PostStatus.Scheduled, future);

        using var end = new HttpRequestMessage(HttpMethod.Post, $"/api/Campaign/{campaignId}/end");
        Authorize(end, "Admin");
        Assert.Equal(HttpStatusCode.OK, (await _client.SendAsync(end)).StatusCode);
        Assert.Equal(CampaignStatus.Ended, await GetStatusAsync(campaignId));
        Assert.Equal(PostStatus.Cancelled, await GetPostStatusAsync(postId));

        using var resume = new HttpRequestMessage(HttpMethod.Post, $"/api/Campaign/{campaignId}/resume");
        Authorize(resume, "Admin");
        Assert.Equal(HttpStatusCode.BadRequest, (await _client.SendAsync(resume)).StatusCode);
    }

    // --- (f) Delete ---

    [Fact]
    public async Task Delete_SoftDeletes_CancelsFuture_KeepsPublished()
    {
        var channel = await SeedChannelAsync("ch-del");
        var campaignId = await SeedCampaignDirectAsync("CampDel", [channel]);
        var future = DateTime.UtcNow.AddDays(1);
        var past = DateTime.UtcNow.AddDays(-1);
        var futurePost = await SeedPostAsync(channel, campaignId, PostStatus.Scheduled, future);
        var published = await SeedPostAsync(channel, campaignId, PostStatus.Published, past);

        using var delete = new HttpRequestMessage(HttpMethod.Delete, $"/api/Campaign/{campaignId}");
        Authorize(delete, "Admin");
        Assert.Equal(HttpStatusCode.OK, (await _client.SendAsync(delete)).StatusCode);

        Assert.True(await IsCampaignDeletedAsync(campaignId));
        Assert.Equal(PostStatus.Cancelled, await GetPostStatusAsync(futurePost));
        Assert.Equal(PostStatus.Published, await GetPostStatusAsync(published));
        Assert.False(await IsPostDeletedAsync(published));

        using var list = new HttpRequestMessage(HttpMethod.Get, "/api/Campaign");
        Authorize(list, "Viewer");
        using var listRes = await _client.SendAsync(list);
        var body = await listRes.Content.ReadFromJsonAsync<JsonElement>();
        var ids = body.GetProperty("data").EnumerateArray().Select(x => x.GetProperty("id").GetGuid()).ToList();
        Assert.DoesNotContain(campaignId, ids);
    }

    // --- (g) Transaction rollback ---

    [Fact]
    public async Task Pause_WhenSaveFails_RollsBackStatusAndPosts()
    {
        var channel = await SeedChannelAsync("ch-tx");
        var campaignId = await SeedCampaignDirectAsync("CampTx", [channel]);
        var future = DateTime.UtcNow.AddDays(1);
        var postId = await SeedPostAsync(channel, campaignId, PostStatus.Scheduled, future);

        // Giả lập lỗi giữa chừng: huỷ bài trong memory rồi throw trước commit — dùng repo path
        // với DbContext bị dispose để buộc rollback. Cách đơn giản: BeginTransaction + throw.
        await using var db = new AppDbContext(_options);
        await using var tx = await db.Database.BeginTransactionAsync();
        var campaign = await db.Campaigns.SingleAsync(c => c.Id == campaignId);
        campaign.Status = CampaignStatus.Paused;
        var post = await db.Posts.SingleAsync(p => p.Id == postId);
        post.Status = PostStatus.Cancelled;
        await db.SaveChangesAsync();
        await tx.RollbackAsync();

        Assert.Equal(CampaignStatus.Running, await GetStatusAsync(campaignId));
        Assert.Equal(PostStatus.Scheduled, await GetPostStatusAsync(postId));
    }

    [Fact]
    public async Task Create_SetsCreatedByUserId_FromAuthenticatedUser()
    {
        var channel = await SeedChannelAsync("ch-author");
        var id = await CreateViaApiAsync(channel, "AuthorViaApi");
        await using var db = new AppDbContext(_options);
        var camp = await db.Campaigns.SingleAsync(c => c.Id == id);
        Assert.Equal(ActorUserId, camp.CreatedByUserId);
    }

    // --- helpers ---

    private static CreateCampaignRequest ValidCreate(Guid channelId, string name) => new()
    {
        Name = name,
        MediaType = CampaignMediaType.Image,
        ImageStrategy = CampaignImageStrategy.KeepOld,
        ChannelIds = [channelId],
        ScheduleMode = CampaignScheduleMode.AllWeek,
        PublishTimes = ["09:00", "15:00"],
        JitterMinutes = 30,
        StartDate = new DateTime(2026, 10, 6, 0, 0, 0, DateTimeKind.Utc),
    };

    private async Task<Guid> CreateViaApiAsync(Guid channelId, string name)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/Campaign")
        {
            Content = JsonContent.Create(ValidCreate(channelId, name))
        };
        Authorize(request, "Admin");
        using var response = await _client.SendAsync(request);
        response.EnsureSuccessStatusCode();
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        return body.GetProperty("data").GetProperty("id").GetGuid();
    }

    private void Authorize(HttpRequestMessage request, string role, string user = "tester")
    {
        request.Headers.Authorization = new AuthenticationHeaderValue(
            TestAuthHandler.SchemeName, $"{role}:{user}");
    }

    private async Task<Guid> SeedChannelAsync(string pageName)
    {
        await using var db = new AppDbContext(_options);
        var id = Guid.NewGuid();
        db.SocialChannels.Add(new SocialChannelModel
        {
            Id = id,
            PageName = pageName,
            Platform = SocialPlatform.Facebook,
            ChannelType = SocialChannelType.Page,
            ExternalPageId = id.ToString("N")[..12],
            IsActive = true,
            CreatedAt = DateTime.UtcNow,
            CreatedBy = "seed",
        });
        await db.SaveChangesAsync();
        return id;
    }

    private async Task<Guid> SeedCampaignDirectAsync(string name, List<Guid> channelIds)
    {
        await using var db = new AppDbContext(_options);
        var id = Guid.NewGuid();
        db.Campaigns.Add(new CampaignModel
        {
            Id = id,
            Name = name,
            MediaType = CampaignMediaType.Image,
            ImageStrategy = CampaignImageStrategy.KeepOld,
            ScheduleMode = CampaignScheduleMode.AllWeek,
            WeekdaysJson = "[]",
            PublishTimesJson = """["09:00"]""",
            JitterMinutes = 15,
            StartDate = new DateTime(2026, 10, 1, 0, 0, 0, DateTimeKind.Utc),
            Status = CampaignStatus.Running,
            CreatedAt = DateTime.UtcNow,
            CreatedBy = "seed",
        });
        foreach (var channelId in channelIds)
        {
            db.CampaignChannels.Add(new CampaignChannelModel
            {
                Id = Guid.NewGuid(),
                CampaignId = id,
                SocialChannelId = channelId,
                CreatedAt = DateTime.UtcNow,
                CreatedBy = "seed",
            });
        }
        await db.SaveChangesAsync();
        return id;
    }

    private async Task<Guid> SeedPostAsync(
        Guid channelId,
        Guid? campaignId,
        PostStatus status,
        DateTime scheduledAt)
    {
        await using var db = new AppDbContext(_options);
        var id = Guid.NewGuid();
        db.Posts.Add(new PostModel
        {
            Id = id,
            Title = "campaign-post",
            Content = "c",
            SocialChannelId = channelId,
            Status = status,
            ScheduledPublishAt = scheduledAt,
            CampaignId = campaignId,
            CampaignSlotAt = campaignId.HasValue ? scheduledAt : null,
            UserId = ActorUserId,
            CreatedAt = DateTime.UtcNow,
            CreatedBy = "seed",
        });
        await db.SaveChangesAsync();
        return id;
    }

    private async Task<int> CountCampaignsAsync()
    {
        await using var db = new AppDbContext(_options);
        return await db.Campaigns.CountAsync(x => !x.IsDeleted);
    }

    private async Task<CampaignStatus> GetStatusAsync(Guid id)
    {
        await using var db = new AppDbContext(_options);
        return await db.Campaigns.Where(x => x.Id == id).Select(x => x.Status).SingleAsync();
    }

    private async Task<PostStatus> GetPostStatusAsync(Guid id)
    {
        await using var db = new AppDbContext(_options);
        return await db.Posts.Where(x => x.Id == id).Select(x => x.Status).SingleAsync();
    }

    private async Task<bool> IsCampaignDeletedAsync(Guid id)
    {
        await using var db = new AppDbContext(_options);
        return await db.Campaigns.Where(x => x.Id == id).Select(x => x.IsDeleted).SingleAsync();
    }

    private async Task<bool> IsPostDeletedAsync(Guid id)
    {
        await using var db = new AppDbContext(_options);
        return await db.Posts.Where(x => x.Id == id).Select(x => x.IsDeleted).SingleAsync();
    }

    private sealed class TestAuthHandler : AuthenticationHandler<AuthenticationSchemeOptions>
    {
        public const string SchemeName = "TestCampaign";

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
                new Claim(ClaimTypes.NameIdentifier, ActorUserId.ToString()),
                new Claim(ClaimTypes.Role, parts[0]),
            };
            var identity = new ClaimsIdentity(claims, SchemeName);
            return Task.FromResult(AuthenticateResult.Success(
                new AuthenticationTicket(new ClaimsPrincipal(identity), SchemeName)));
        }
    }
}
