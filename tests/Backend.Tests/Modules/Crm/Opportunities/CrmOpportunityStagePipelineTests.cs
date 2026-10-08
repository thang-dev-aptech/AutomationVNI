using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text.Encodings.Web;
using System.Text.Json;
using Backend.Data;
using Backend.Modules.Crm.Assignment;
using Backend.Modules.Crm.Customers;
using Backend.Modules.Crm.Inbox;
using Backend.Modules.Crm.Opportunities;
using Backend.Modules.Crm.Tags;
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

namespace Backend.Tests.Modules.Crm.Opportunities;

/// <summary>
/// AC 894baa0e (a)-(f): stages, move-stage, stats, filter, pipeline.
/// Activity count may be 0 until t2 (reminders/notes linked to opportunities).
/// </summary>
public sealed class CrmOpportunityStagePipelineTests : IAsyncLifetime
{
    private readonly SqliteConnection _connection;
    private readonly DbContextOptions<AppDbContext> _options;
    private readonly IHost _host;
    private readonly HttpClient _client;
    private readonly Guid _actorUserId = Guid.Parse("aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee");
    private readonly Guid _otherUserId = Guid.Parse("cccccccc-dddd-eeee-ffff-000000000001");

    public CrmOpportunityStagePipelineTests()
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
                        })
                        .AddRoles<ApplicationRole>()
                        .AddEntityFrameworkStores<AppDbContext>();
                    services.AddScoped<UsersService>();
                    services.AddScoped<CrmAutoAssignService>();
                    services.AddScoped<CrmTagService>();
                    services.AddScoped<CrmCustomerService>();
                    services.AddScoped<CrmCustomerCareService>();
                    services.AddScoped<CrmInboxService>();
                    services.AddScoped<CrmOpportunityService>();
                    services.AddScoped<CrmOpportunityStageService>();
                    services.AddSingleton(Options.Create(new SocialPublishOptions
                    {
                        Facebook = new FacebookPublishOptions
                        {
                            GraphBaseUrl = "https://graph.facebook.com",
                            GraphVersion = "v21.0"
                        }
                    }));
                    services.AddSingleton(Options.Create(new MetaOAuthOptions()));
                    services.AddSingleton(Options.Create(new ThreadsOAuthOptions()));
                    services.AddSingleton<ISocialCommentProvider, StubFacebookCommentProvider>();
                    services.AddHttpClient<FacebookPageMessagingProvider>()
                        .ConfigurePrimaryHttpMessageHandler(() => new HttpClientHandler());
                    services.AddScoped<PageMessageService>();
                    services.AddScoped<SocialCommentService>();
                    services.AddAuthentication(TestAuthHandler.SchemeName)
                        .AddScheme<AuthenticationSchemeOptions, TestAuthHandler>(
                            TestAuthHandler.SchemeName, _ => { });
                    services.AddAuthorization();
                    services.AddControllers(o => o.Filters.Add<GlobalExceptionFilter>())
                        .AddApplicationPart(typeof(CrmOpportunityController).Assembly);
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

    [Fact]
    public async Task A_EnsureCreated_SeedsExactlySixStagesWithCorrectKinds()
    {
        var res = await SendAsync(HttpMethod.Get, "/api/CrmOpportunityStage", "Admin");
        Assert.Equal(HttpStatusCode.OK, res.StatusCode);
        var stages = (await ReadDataAsync(res)).EnumerateArray().ToList();
        Assert.Equal(6, stages.Count);

        Assert.Contains(stages, s =>
            s.GetProperty("id").GetGuid() == CrmOpportunityStageIds.Moi
            && s.GetProperty("kind").GetInt32() == (int)CrmOpportunityStageKind.Open
            && s.GetProperty("name").GetString() == "Mới");
        Assert.Contains(stages, s =>
            s.GetProperty("id").GetGuid() == CrmOpportunityStageIds.DuDieuKien
            && s.GetProperty("kind").GetInt32() == (int)CrmOpportunityStageKind.Open);
        Assert.Contains(stages, s =>
            s.GetProperty("id").GetGuid() == CrmOpportunityStageIds.BamDuoi
            && s.GetProperty("kind").GetInt32() == (int)CrmOpportunityStageKind.Open);
        Assert.Contains(stages, s =>
            s.GetProperty("id").GetGuid() == CrmOpportunityStageIds.DamPhanChot
            && s.GetProperty("kind").GetInt32() == (int)CrmOpportunityStageKind.Open);
        Assert.Contains(stages, s =>
            s.GetProperty("id").GetGuid() == CrmOpportunityStageIds.DaMua
            && s.GetProperty("kind").GetInt32() == (int)CrmOpportunityStageKind.Won
            && s.GetProperty("name").GetString() == "Đã mua");
        Assert.Contains(stages, s =>
            s.GetProperty("id").GetGuid() == CrmOpportunityStageIds.ThatBai
            && s.GetProperty("kind").GetInt32() == (int)CrmOpportunityStageKind.Lost
            && s.GetProperty("name").GetString() == "Thất bại");

        await using var db = new AppDbContext(_options);
        Assert.False(db.Database.HasPendingModelChanges());
        Assert.Equal(6, await db.CrmOpportunityStages.CountAsync(x => !x.IsDeleted));
        Assert.Equal(4, await db.CrmOpportunityStages.CountAsync(x =>
            !x.IsDeleted && x.Kind == CrmOpportunityStageKind.Open));
        Assert.Equal(1, await db.CrmOpportunityStages.CountAsync(x =>
            !x.IsDeleted && x.Kind == CrmOpportunityStageKind.Won));
        Assert.Equal(1, await db.CrmOpportunityStages.CountAsync(x =>
            !x.IsDeleted && x.Kind == CrmOpportunityStageKind.Lost));
    }

    [Fact]
    public async Task B_MoveStage_WonLostReason_AndBackToOpen()
    {
        var customerId = await SeedCustomerAsync("Move Cust", null);
        var create = await SendAsync(HttpMethod.Post, "/api/CrmOpportunity", "Admin", new
        {
            crmCustomerId = customerId,
            title = "Move Opp",
            expectedValue = 100
        });
        Assert.Equal(HttpStatusCode.OK, create.StatusCode);
        var oppId = (await ReadDataAsync(create)).GetProperty("id").GetGuid();

        var toWon = await SendAsync(HttpMethod.Post, $"/api/CrmOpportunity/{oppId}/move-stage", "Admin", new
        {
            stageId = CrmOpportunityStageIds.DaMua
        });
        Assert.Equal(HttpStatusCode.OK, toWon.StatusCode);
        var won = await ReadDataAsync(toWon);
        Assert.Equal((int)CrmOpportunityStatus.Won, won.GetProperty("status").GetInt32());
        Assert.NotEqual(JsonValueKind.Null, won.GetProperty("closedAtUtc").ValueKind);

        var lostNoReason = await SendAsync(HttpMethod.Post, $"/api/CrmOpportunity/{oppId}/move-stage", "Admin", new
        {
            stageId = CrmOpportunityStageIds.ThatBai
        });
        Assert.Equal(HttpStatusCode.BadRequest, lostNoReason.StatusCode);

        var toLost = await SendAsync(HttpMethod.Post, $"/api/CrmOpportunity/{oppId}/move-stage", "Admin", new
        {
            stageId = CrmOpportunityStageIds.ThatBai,
            lostReason = "Giá cao"
        });
        Assert.Equal(HttpStatusCode.OK, toLost.StatusCode);
        var lost = await ReadDataAsync(toLost);
        Assert.Equal((int)CrmOpportunityStatus.Lost, lost.GetProperty("status").GetInt32());
        Assert.Equal("Giá cao", lost.GetProperty("lostReason").GetString());
        Assert.NotEqual(JsonValueKind.Null, lost.GetProperty("closedAtUtc").ValueKind);

        var toOpen = await SendAsync(HttpMethod.Post, $"/api/CrmOpportunity/{oppId}/move-stage", "Admin", new
        {
            stageId = CrmOpportunityStageIds.Moi
        });
        Assert.Equal(HttpStatusCode.OK, toOpen.StatusCode);
        var open = await ReadDataAsync(toOpen);
        Assert.Equal((int)CrmOpportunityStatus.Open, open.GetProperty("status").GetInt32());
        Assert.Equal(JsonValueKind.Null, open.GetProperty("closedAtUtc").ValueKind);
        Assert.Equal(JsonValueKind.Null, open.GetProperty("lostReason").ValueKind);
    }

    [Fact]
    public async Task C_DeleteStage_WithOppOrLastWonLost_Returns400()
    {
        var customerId = await SeedCustomerAsync("Stage Del", null);
        var create = await SendAsync(HttpMethod.Post, "/api/CrmOpportunity", "Admin", new
        {
            crmCustomerId = customerId,
            title = "In Moi",
            stageId = CrmOpportunityStageIds.Moi,
            expectedValue = 0
        });
        Assert.Equal(HttpStatusCode.OK, create.StatusCode);

        var delWithOpp = await SendAsync(HttpMethod.Delete,
            $"/api/CrmOpportunityStage/{CrmOpportunityStageIds.Moi}", "Admin");
        Assert.Equal(HttpStatusCode.BadRequest, delWithOpp.StatusCode);

        var delLastWon = await SendAsync(HttpMethod.Delete,
            $"/api/CrmOpportunityStage/{CrmOpportunityStageIds.DaMua}", "Admin");
        Assert.Equal(HttpStatusCode.BadRequest, delLastWon.StatusCode);

        var delLastLost = await SendAsync(HttpMethod.Delete,
            $"/api/CrmOpportunityStage/{CrmOpportunityStageIds.ThatBai}", "Admin");
        Assert.Equal(HttpStatusCode.BadRequest, delLastLost.StatusCode);
    }

    [Fact]
    public async Task D_Stats_OpenWonLostRev_AndAssigneeMineFilter()
    {
        // 3 Open (mine), 2 Won (1e6 + 2.5e6), 1 Lost, 1 archived Open → Total=7
        // Activity may be 0 until t2.
        var c1 = await SeedCustomerAsync("S1", null);
        var c2 = await SeedCustomerAsync("S2", null);
        var c3 = await SeedCustomerAsync("S3", null);
        var c4 = await SeedCustomerAsync("S4", null);
        var c5 = await SeedCustomerAsync("S5", null);
        var c6 = await SeedCustomerAsync("S6", null);
        var c7 = await SeedCustomerAsync("S7", null);

        await SeedOppDirectAsync(c1, "Open1", CrmOpportunityStageIds.Moi, CrmOpportunityStatus.Open,
            expectedValue: 10, assigneeUserId: _actorUserId, isArchived: false);
        await SeedOppDirectAsync(c2, "Open2", CrmOpportunityStageIds.DuDieuKien, CrmOpportunityStatus.Open,
            expectedValue: 20, assigneeUserId: _actorUserId, isArchived: false);
        await SeedOppDirectAsync(c3, "Open3", CrmOpportunityStageIds.BamDuoi, CrmOpportunityStatus.Open,
            expectedValue: 30, assigneeUserId: null, isArchived: false);

        await SeedOppDirectAsync(c4, "Won1", CrmOpportunityStageIds.DaMua, CrmOpportunityStatus.Won,
            expectedValue: 1_000_000m, assigneeUserId: _actorUserId, isArchived: false,
            closedAtUtc: DateTime.UtcNow);
        await SeedOppDirectAsync(c5, "Won2", CrmOpportunityStageIds.DaMua, CrmOpportunityStatus.Won,
            expectedValue: 2_500_000m, assigneeUserId: _otherUserId, isArchived: false,
            closedAtUtc: DateTime.UtcNow);

        await SeedOppDirectAsync(c6, "Lost1", CrmOpportunityStageIds.ThatBai, CrmOpportunityStatus.Lost,
            expectedValue: 50, assigneeUserId: _otherUserId, isArchived: false,
            closedAtUtc: DateTime.UtcNow, lostReason: "No budget");

        await SeedOppDirectAsync(c7, "ArchOpen", CrmOpportunityStageIds.Moi, CrmOpportunityStatus.Open,
            expectedValue: 99, assigneeUserId: _actorUserId, isArchived: true);

        var statsRes = await SendAsync(HttpMethod.Post, "/api/CrmOpportunity/stats", "Admin", new { });
        Assert.Equal(HttpStatusCode.OK, statsRes.StatusCode);
        var stats = await ReadDataAsync(statsRes);
        Assert.Equal(7, stats.GetProperty("total").GetInt32());
        Assert.Equal(3, stats.GetProperty("open").GetInt32());
        Assert.Equal(2, stats.GetProperty("won").GetInt32());
        Assert.Equal(1, stats.GetProperty("lost").GetInt32());
        Assert.Equal(0, stats.GetProperty("activity").GetInt32());
        Assert.Equal(3_500_000m, stats.GetProperty("rev").GetDecimal());

        var mineRes = await SendAsync(HttpMethod.Post, "/api/CrmOpportunity/stats", "Admin", new
        {
            assigneeFilter = "mine"
        });
        Assert.Equal(HttpStatusCode.OK, mineRes.StatusCode);
        var mine = await ReadDataAsync(mineRes);
        // mine: Open1, Open2, Won1, ArchOpen → Total=4; Open=2; Won=1; Lost=0; Rev=1e6
        Assert.Equal(4, mine.GetProperty("total").GetInt32());
        Assert.Equal(2, mine.GetProperty("open").GetInt32());
        Assert.Equal(1, mine.GetProperty("won").GetInt32());
        Assert.Equal(0, mine.GetProperty("lost").GetInt32());
        Assert.Equal(1_000_000m, mine.GetProperty("rev").GetDecimal());
        Assert.NotEqual(stats.GetProperty("total").GetInt32(), mine.GetProperty("total").GetInt32());
    }

    [Fact]
    public async Task E_Filter_ClampKeywordSortAssignee()
    {
        var phoneCust = await SeedCustomerAsync("Nguyễn Filter", "+84905550001");
        var otherCust = await SeedCustomerAsync("Other Filter", "+84905550002");

        var t1 = DateTime.UtcNow.AddHours(-3);
        var t2 = DateTime.UtcNow.AddHours(-2);
        var t3 = DateTime.UtcNow.AddHours(-1);

        var a = await SeedOppDirectAsync(phoneCust, "Alpha Opp", CrmOpportunityStageIds.Moi,
            CrmOpportunityStatus.Open, 1, _actorUserId, false, lastActivityAtUtc: t1);
        var b = await SeedOppDirectAsync(otherCust, "Beta Opp", CrmOpportunityStageIds.DuDieuKien,
            CrmOpportunityStatus.Open, 2, null, false, lastActivityAtUtc: t3);
        var c = await SeedOppDirectAsync(phoneCust, "Gamma Opp", CrmOpportunityStageIds.BamDuoi,
            CrmOpportunityStatus.Open, 3, _otherUserId, false, lastActivityAtUtc: t2);

        // Size > 100 clamped
        var clamp = await SendAsync(HttpMethod.Post, "/api/CrmOpportunity/filter", "Admin", new
        {
            index = 1,
            size = 500
        });
        Assert.Equal(HttpStatusCode.OK, clamp.StatusCode);
        var clampData = await ReadDataAsync(clamp);
        Assert.Equal(100, clampData.GetProperty("size").GetInt32());
        Assert.Equal(1, clampData.GetProperty("index").GetInt32());

        // Keyword by phone
        var byPhone = await SendAsync(HttpMethod.Post, "/api/CrmOpportunity/filter", "Admin", new
        {
            index = 1,
            size = 50,
            keyword = "84905550001"
        });
        var phoneItems = (await ReadDataAsync(byPhone)).GetProperty("items").EnumerateArray()
            .Select(x => x.GetProperty("id").GetGuid()).ToList();
        Assert.Equal(2, phoneItems.Count);
        Assert.Contains(a, phoneItems);
        Assert.Contains(c, phoneItems);
        Assert.DoesNotContain(b, phoneItems);

        // Keyword by customer name
        var byName = await SendAsync(HttpMethod.Post, "/api/CrmOpportunity/filter", "Admin", new
        {
            index = 1,
            size = 50,
            keyword = "Nguyễn Filter"
        });
        Assert.Equal(2, (await ReadDataAsync(byName)).GetProperty("total").GetInt32());

        // Sort LastActivityAtUtc desc → b (t3), c (t2), a (t1)
        var sorted = await SendAsync(HttpMethod.Post, "/api/CrmOpportunity/filter", "Admin", new
        {
            index = 1,
            size = 50
        });
        var sortedIds = (await ReadDataAsync(sorted)).GetProperty("items").EnumerateArray()
            .Select(x => x.GetProperty("id").GetGuid()).ToList();
        Assert.Equal(new[] { b, c, a }, sortedIds.Take(3).ToArray());

        // assignee mine
        var mine = await SendAsync(HttpMethod.Post, "/api/CrmOpportunity/filter", "Admin", new
        {
            index = 1,
            size = 50,
            assigneeFilter = "mine"
        });
        var mineIds = (await ReadDataAsync(mine)).GetProperty("items").EnumerateArray()
            .Select(x => x.GetProperty("id").GetGuid()).ToList();
        Assert.Single(mineIds);
        Assert.Equal(a, mineIds[0]);

        // assignee unassigned
        var unassigned = await SendAsync(HttpMethod.Post, "/api/CrmOpportunity/filter", "Admin", new
        {
            index = 1,
            size = 50,
            assigneeFilter = "unassigned"
        });
        var unIds = (await ReadDataAsync(unassigned)).GetProperty("items").EnumerateArray()
            .Select(x => x.GetProperty("id").GetGuid()).ToList();
        Assert.Single(unIds);
        Assert.Equal(b, unIds[0]);

        // stage filter
        var stageFilter = await SendAsync(HttpMethod.Post, "/api/CrmOpportunity/filter", "Admin", new
        {
            index = 1,
            size = 50,
            stageId = CrmOpportunityStageIds.DuDieuKien
        });
        var stageIds = (await ReadDataAsync(stageFilter)).GetProperty("items").EnumerateArray()
            .Select(x => x.GetProperty("id").GetGuid()).ToList();
        Assert.Single(stageIds);
        Assert.Equal(b, stageIds[0]);
    }

    [Fact]
    public async Task F_Pipeline_PerStageLimit_AndStagePagination()
    {
        var customerId = await SeedCustomerAsync("Pipe Cust", null);
        const int totalInMoi = 5;
        for (var i = 0; i < totalInMoi; i++)
        {
            await SeedOppDirectAsync(customerId, $"Pipe {i}", CrmOpportunityStageIds.Moi,
                CrmOpportunityStatus.Open, i, null, false,
                lastActivityAtUtc: DateTime.UtcNow.AddMinutes(-i));
        }

        await SeedOppDirectAsync(customerId, "Pipe Won", CrmOpportunityStageIds.DaMua,
            CrmOpportunityStatus.Won, 9, null, false, closedAtUtc: DateTime.UtcNow);

        const int perStage = 2;
        var pipe = await SendAsync(HttpMethod.Post, "/api/CrmOpportunity/pipeline", "Admin", new
        {
            perStage,
            filters = new { }
        });
        Assert.Equal(HttpStatusCode.OK, pipe.StatusCode);
        var columns = (await ReadDataAsync(pipe)).GetProperty("columns").EnumerateArray().ToList();
        Assert.Equal(6, columns.Count);

        var moiCol = columns.First(c => c.GetProperty("stageId").GetGuid() == CrmOpportunityStageIds.Moi);
        Assert.Equal(totalInMoi, moiCol.GetProperty("total").GetInt32());
        Assert.Equal(perStage, moiCol.GetProperty("items").GetArrayLength());

        var wonCol = columns.First(c => c.GetProperty("stageId").GetGuid() == CrmOpportunityStageIds.DaMua);
        Assert.Equal(1, wonCol.GetProperty("total").GetInt32());
        Assert.Equal(1, wonCol.GetProperty("items").GetArrayLength());

        // Pagination for Moi: page 1 size 2, page 2 size 2, page 3 size 1 — no overlap / gap
        var p1 = await SendAsync(HttpMethod.Post,
            $"/api/CrmOpportunity/pipeline/{CrmOpportunityStageIds.Moi}", "Admin", new
            {
                index = 1,
                size = 2,
                filters = new { }
            });
        var p1Data = await ReadDataAsync(p1);
        Assert.Equal(totalInMoi, p1Data.GetProperty("total").GetInt32());
        Assert.Equal(2, p1Data.GetProperty("items").GetArrayLength());
        Assert.Equal(2, p1Data.GetProperty("size").GetInt32());
        var page1Ids = p1Data.GetProperty("items").EnumerateArray()
            .Select(x => x.GetProperty("id").GetGuid()).ToList();

        var p2 = await SendAsync(HttpMethod.Post,
            $"/api/CrmOpportunity/pipeline/{CrmOpportunityStageIds.Moi}", "Admin", new
            {
                index = 2,
                size = 2
            });
        var page2Ids = (await ReadDataAsync(p2)).GetProperty("items").EnumerateArray()
            .Select(x => x.GetProperty("id").GetGuid()).ToList();
        Assert.Equal(2, page2Ids.Count);
        Assert.Empty(page1Ids.Intersect(page2Ids));

        var p3 = await SendAsync(HttpMethod.Post,
            $"/api/CrmOpportunity/pipeline/{CrmOpportunityStageIds.Moi}", "Admin", new
            {
                index = 3,
                size = 2
            });
        var page3Ids = (await ReadDataAsync(p3)).GetProperty("items").EnumerateArray()
            .Select(x => x.GetProperty("id").GetGuid()).ToList();
        Assert.Single(page3Ids);
        Assert.Empty(page1Ids.Intersect(page3Ids));
        Assert.Empty(page2Ids.Intersect(page3Ids));
        Assert.Equal(totalInMoi, page1Ids.Count + page2Ids.Count + page3Ids.Count);
    }

    private async Task<HttpResponseMessage> SendAsync(
        HttpMethod method, string url, string role, object? body = null)
    {
        using var req = new HttpRequestMessage(method, url);
        req.Headers.Authorization = new AuthenticationHeaderValue(
            TestAuthHandler.SchemeName, $"{role}:{_actorUserId:N}");
        if (body is not null)
            req.Content = JsonContent.Create(body);
        return await _client.SendAsync(req);
    }

    private static async Task<JsonElement> ReadDataAsync(HttpResponseMessage res)
    {
        var json = await res.Content.ReadFromJsonAsync<JsonElement>();
        Assert.True(json.GetProperty("success").GetBoolean());
        return json.GetProperty("data");
    }

    private async Task<Guid> SeedCustomerAsync(string displayName, string? phoneE164)
    {
        await using var db = new AppDbContext(_options);
        var id = Guid.NewGuid();
        db.CrmCustomers.Add(new CrmCustomerModel
        {
            Id = id,
            DisplayName = displayName,
            PhoneE164 = phoneE164,
            CreatedAt = DateTime.UtcNow,
            CreatedBy = "seed"
        });
        await db.SaveChangesAsync();
        return id;
    }

    private async Task<Guid> SeedOppDirectAsync(
        Guid customerId,
        string title,
        Guid stageId,
        CrmOpportunityStatus status,
        decimal expectedValue,
        Guid? assigneeUserId,
        bool isArchived,
        DateTime? closedAtUtc = null,
        DateTime? lastActivityAtUtc = null,
        string? lostReason = null)
    {
        await using var db = new AppDbContext(_options);
        var id = Guid.NewGuid();
        var now = DateTime.UtcNow;
        db.CrmOpportunities.Add(new CrmOpportunityModel
        {
            Id = id,
            CrmCustomerId = customerId,
            Title = title,
            StageId = stageId,
            Status = status,
            IsArchived = isArchived,
            AssigneeUserId = assigneeUserId,
            AssignedTo = assigneeUserId?.ToString("N"),
            Source = CrmOpportunitySource.Manual,
            ExpectedValue = expectedValue,
            LostReason = lostReason,
            ClosedAtUtc = closedAtUtc,
            LastActivityAtUtc = lastActivityAtUtc ?? now,
            CreatedAt = now,
            CreatedBy = "seed"
        });
        await db.SaveChangesAsync();
        return id;
    }

    private sealed class TestAuthHandler : AuthenticationHandler<AuthenticationSchemeOptions>
    {
        public const string SchemeName = "TestCrmOpportunityStage";

        public TestAuthHandler(
            IOptionsMonitor<AuthenticationSchemeOptions> options,
            ILoggerFactory logger,
            UrlEncoder encoder) : base(options, logger, encoder) { }

        protected override Task<AuthenticateResult> HandleAuthenticateAsync()
        {
            var header = Request.Headers.Authorization.ToString();
            if (string.IsNullOrWhiteSpace(header))
                return Task.FromResult(AuthenticateResult.Fail("missing"));

            var value = header.StartsWith(SchemeName + " ", StringComparison.OrdinalIgnoreCase)
                ? header[(SchemeName.Length + 1)..].Trim()
                : header.Trim();
            var parts = value.Split(':', 2);
            if (parts.Length != 2)
                return Task.FromResult(AuthenticateResult.Fail("bad"));

            var userId = Guid.TryParseExact(parts[1], "N", out var parsed)
                ? parsed
                : Guid.NewGuid();
            var claims = new[]
            {
                new Claim(ClaimTypes.Name, "actor"),
                new Claim(ClaimTypes.NameIdentifier, userId.ToString()),
                new Claim(ClaimTypes.Role, parts[0]),
            };
            var identity = new ClaimsIdentity(claims, SchemeName);
            return Task.FromResult(
                AuthenticateResult.Success(new AuthenticationTicket(new ClaimsPrincipal(identity), SchemeName)));
        }
    }

    private sealed class StubFacebookCommentProvider : ISocialCommentProvider
    {
        public SocialPlatform Platform => SocialPlatform.Facebook;
        public SocialCommentCapabilities Capabilities { get; } = new()
        {
            CanReply = true,
            CanHide = true,
            CanUnhide = true,
            CanDelete = true
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
}
