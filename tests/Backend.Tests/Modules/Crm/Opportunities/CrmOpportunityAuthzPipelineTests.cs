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
using Backend.Modules.SocialComment.Enums;
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
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Xunit;

namespace Backend.Tests.Modules.Crm.Opportunities;

/// <summary>
/// AC 78da3dbb (a)-(f): CRM Opportunity data boundary + authz (HTTP TestServer).
/// </summary>
public sealed class CrmOpportunityAuthzPipelineTests : IAsyncLifetime
{
    private readonly SqliteConnection _connection;
    private readonly DbContextOptions<AppDbContext> _options;
    private readonly IHost _host;
    private readonly HttpClient _client;
    private readonly Guid _actorUserId = Guid.Parse("aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee");
    private readonly Guid _watcherUserId = Guid.Parse("bbbbbbbb-cccc-dddd-eeee-ffffffffffff");

    public CrmOpportunityAuthzPipelineTests()
    {
        _connection = new SqliteConnection("DataSource=:memory:");
        _connection.Open();
        _options = new DbContextOptionsBuilder<AppDbContext>().UseSqlite(_connection)
            .AddInterceptors(new RaceHookInterceptor(this)).Options;
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

    [Theory]
    [InlineData("Admin")]
    [InlineData("ContentManager")]
    [InlineData("Reviewer")]
    public async Task A_WriteRoles_CanCreateUpdateMoveAssignWatchArchiveDelete(string role)
    {
        var customerId = await SeedCustomerAsync("Write User", "+84901110001");
        var createRes = await SendAsync(HttpMethod.Post, "/api/CrmOpportunity", role, new
        {
            crmCustomerId = customerId,
            title = $"Opp-{role}",
            expectedValue = 1000
        });
        Assert.Equal(HttpStatusCode.OK, createRes.StatusCode);
        var created = await ReadDataAsync(createRes);
        var oppId = created.GetProperty("id").GetGuid();

        Assert.Equal(HttpStatusCode.OK, (await SendAsync(HttpMethod.Put, $"/api/CrmOpportunity/{oppId}", role, new
        {
            title = $"Opp-{role}-upd",
            expectedValue = 2000
        })).StatusCode);

        Assert.Equal(HttpStatusCode.OK, (await SendAsync(HttpMethod.Post, $"/api/CrmOpportunity/{oppId}/move-stage", role, new
        {
            stageId = CrmOpportunityStageIds.DuDieuKien
        })).StatusCode);

        Assert.Equal(HttpStatusCode.OK, (await SendAsync(HttpMethod.Post, $"/api/CrmOpportunity/{oppId}/assign", role, new
        {
            assigneeUserId = _actorUserId,
            assignedTo = "actor"
        })).StatusCode);

        Assert.Equal(HttpStatusCode.OK, (await SendAsync(
            HttpMethod.Post, $"/api/CrmOpportunity/{oppId}/watchers/{_watcherUserId}", role)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await SendAsync(
            HttpMethod.Delete, $"/api/CrmOpportunity/{oppId}/watchers/{_watcherUserId}", role)).StatusCode);

        Assert.Equal(HttpStatusCode.OK, (await SendAsync(
            HttpMethod.Post, $"/api/CrmOpportunity/{oppId}/archive", role)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await SendAsync(
            HttpMethod.Post, $"/api/CrmOpportunity/{oppId}/unarchive", role)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await SendAsync(
            HttpMethod.Delete, $"/api/CrmOpportunity/{oppId}", role)).StatusCode);
    }

    [Fact]
    public async Task A_ViewerWrite_Forbidden_AnonymousUnauthorized_ViewerReadOk_StageWriteForbidden()
    {
        var customerId = await SeedCustomerAsync("Viewer Gate", "+84901110002");
        var createOk = await SendAsync(HttpMethod.Post, "/api/CrmOpportunity", "Admin", new
        {
            crmCustomerId = customerId,
            title = "Gate Opp",
            expectedValue = 0
        });
        Assert.Equal(HttpStatusCode.OK, createOk.StatusCode);
        var oppId = (await ReadDataAsync(createOk)).GetProperty("id").GetGuid();

        var writeBodies = new (HttpMethod Method, string Url, object? Body)[]
        {
            (HttpMethod.Post, "/api/CrmOpportunity", new { crmCustomerId = customerId, title = "V", expectedValue = 0 }),
            (HttpMethod.Put, $"/api/CrmOpportunity/{oppId}", new { title = "V2", expectedValue = 1 }),
            (HttpMethod.Post, $"/api/CrmOpportunity/{oppId}/move-stage", new { stageId = CrmOpportunityStageIds.BamDuoi }),
            (HttpMethod.Post, $"/api/CrmOpportunity/{oppId}/assign", new { assigneeUserId = _actorUserId }),
            (HttpMethod.Post, $"/api/CrmOpportunity/{oppId}/watchers/{_watcherUserId}", null),
            (HttpMethod.Delete, $"/api/CrmOpportunity/{oppId}/watchers/{_watcherUserId}", null),
            (HttpMethod.Post, $"/api/CrmOpportunity/{oppId}/archive", null),
            (HttpMethod.Post, $"/api/CrmOpportunity/{oppId}/unarchive", null),
            (HttpMethod.Delete, $"/api/CrmOpportunity/{oppId}", null),
            (HttpMethod.Post, "/api/CrmOpportunity/from-conversation", new { kind = "message", id = Guid.NewGuid() }),
        };

        foreach (var (method, url, body) in writeBodies)
        {
            Assert.Equal(HttpStatusCode.Forbidden, (await SendAsync(method, url, "Viewer", body)).StatusCode);

            using var anon = new HttpRequestMessage(method, url);
            if (body is not null)
                anon.Content = JsonContent.Create(body);
            Assert.Equal(HttpStatusCode.Unauthorized, (await _client.SendAsync(anon)).StatusCode);
        }

        Assert.Equal(HttpStatusCode.OK, (await SendAsync(HttpMethod.Post, "/api/CrmOpportunity/filter", "Viewer",
            new { index = 1, size = 20 })).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await SendAsync(HttpMethod.Post, "/api/CrmOpportunity/pipeline", "Viewer",
            new { perStage = 5 })).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await SendAsync(HttpMethod.Get, $"/api/CrmOpportunity/{oppId}", "Viewer")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await SendAsync(HttpMethod.Get, "/api/CrmOpportunityStage", "Viewer")).StatusCode);

        var stageBody = new { name = "Extra", color = "#111111", kind = (int)CrmOpportunityStageKind.Open };
        Assert.Equal(HttpStatusCode.Forbidden,
            (await SendAsync(HttpMethod.Post, "/api/CrmOpportunityStage", "Viewer", stageBody)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden,
            (await SendAsync(HttpMethod.Post, "/api/CrmOpportunityStage", "Reviewer", stageBody)).StatusCode);
    }

    [Fact]
    public async Task B_Create_MissingOrDeletedCustomer_Returns404_NoRow()
    {
        var before = await CountOppsAsync();

        var missing = await SendAsync(HttpMethod.Post, "/api/CrmOpportunity", "Admin", new
        {
            crmCustomerId = Guid.NewGuid(),
            title = "Missing",
            expectedValue = 0
        });
        Assert.Equal(HttpStatusCode.NotFound, missing.StatusCode);
        Assert.Equal(before, await CountOppsAsync());

        var deletedId = await SeedCustomerAsync("Deleted Cust", null);
        await using (var db = new AppDbContext(_options))
        {
            var row = await db.CrmCustomers.FirstAsync(x => x.Id == deletedId);
            row.IsDeleted = true;
            row.DeletedAt = DateTime.UtcNow;
            await db.SaveChangesAsync();
        }

        var deleted = await SendAsync(HttpMethod.Post, "/api/CrmOpportunity", "Admin", new
        {
            crmCustomerId = deletedId,
            title = "Deleted",
            expectedValue = 0
        });
        Assert.Equal(HttpStatusCode.NotFound, deleted.StatusCode);
        Assert.Equal(before, await CountOppsAsync());
    }

    [Fact]
    public async Task C_FromConversation_MessageAndComment_IdentityExact_NoWrongLink_EnsureLinked_Idempotent()
    {
        var channelId = await SeedChannelAsync("Page Opp", accessToken: "SECRET_TOKEN");
        const string name = "Trùng Tên Opp";

        // (1) Exact identity match for message
        var matchedCustomerId = await SeedCustomerWithIdentityAsync(
            name, "+84902220001", channelId, "psid-exact-msg", CrmIdentitySource.Message);
        var matchedConv = await SeedConversationAsync(channelId, "psid-exact-msg", name);
        var matchRes = await SendAsync(HttpMethod.Post, "/api/CrmOpportunity/from-conversation", "Admin", new
        {
            kind = "message",
            id = matchedConv,
            title = "From matched message"
        });
        Assert.Equal(HttpStatusCode.OK, matchRes.StatusCode);
        var matchData = await ReadDataAsync(matchRes);
        Assert.Equal(matchedCustomerId, matchData.GetProperty("crmCustomerId").GetGuid());
        var matchedOppId = matchData.GetProperty("id").GetGuid();

        // Same name / phone, different ExternalId → must NOT attach to matchedCustomerId
        await SeedCustomerWithIdentityAsync(
            name, "+84902220001", channelId, "psid-other-name", CrmIdentitySource.Message);
        var wrongConv = await SeedConversationAsync(channelId, "psid-unlinked-name", name);
        var beforeCustomers = await CountCustomersAsync();
        var beforeIdentities = await CountIdentitiesAsync();
        var wrongRes = await SendAsync(HttpMethod.Post, "/api/CrmOpportunity/from-conversation", "Admin", new
        {
            kind = "message",
            id = wrongConv
        });
        Assert.Equal(HttpStatusCode.OK, wrongRes.StatusCode);
        var wrongData = await ReadDataAsync(wrongRes);
        Assert.NotEqual(matchedCustomerId, wrongData.GetProperty("crmCustomerId").GetGuid());
        Assert.Equal(beforeCustomers + 1, await CountCustomersAsync());
        Assert.Equal(beforeIdentities + 1, await CountIdentitiesAsync());

        // No customer yet → EnsureLinked creates exactly 1 customer + 1 identity
        var newConv = await SeedConversationAsync(channelId, "psid-brand-new", "Người mới");
        beforeCustomers = await CountCustomersAsync();
        beforeIdentities = await CountIdentitiesAsync();
        var ensureRes = await SendAsync(HttpMethod.Post, "/api/CrmOpportunity/from-conversation", "Admin", new
        {
            kind = "message",
            id = newConv
        });
        Assert.Equal(HttpStatusCode.OK, ensureRes.StatusCode);
        var ensureData = await ReadDataAsync(ensureRes);
        Assert.Equal(beforeCustomers + 1, await CountCustomersAsync());
        Assert.Equal(beforeIdentities + 1, await CountIdentitiesAsync());
        var ensureOppId = ensureData.GetProperty("id").GetGuid();

        // Second call returns same Open opportunity
        var again = await SendAsync(HttpMethod.Post, "/api/CrmOpportunity/from-conversation", "Admin", new
        {
            kind = "message",
            id = newConv
        });
        Assert.Equal(HttpStatusCode.OK, again.StatusCode);
        Assert.Equal(ensureOppId, (await ReadDataAsync(again)).GetProperty("id").GetGuid());
        Assert.Equal(1, await CountOppsForConversationAsync(newConv));

        // Comment path: exact identity + idempotent
        const string author = "author-exact-cmt";
        var commentCustomer = await SeedCustomerWithIdentityAsync(
            "Commenter", null, channelId, author, CrmIdentitySource.Comment);
        var commentId = await SeedTopLevelCommentAsync(channelId, author, "Hỏi học phí");
        var cmtRes = await SendAsync(HttpMethod.Post, "/api/CrmOpportunity/from-conversation", "Admin", new
        {
            kind = "comment",
            id = commentId
        });
        Assert.Equal(HttpStatusCode.OK, cmtRes.StatusCode);
        var cmtData = await ReadDataAsync(cmtRes);
        Assert.Equal(commentCustomer, cmtData.GetProperty("crmCustomerId").GetGuid());
        var cmtOppId = cmtData.GetProperty("id").GetGuid();

        var cmtAgain = await SendAsync(HttpMethod.Post, "/api/CrmOpportunity/from-conversation", "Admin", new
        {
            kind = "comment",
            id = commentId
        });
        Assert.Equal(HttpStatusCode.OK, cmtAgain.StatusCode);
        Assert.Equal(cmtOppId, (await ReadDataAsync(cmtAgain)).GetProperty("id").GetGuid());

        // by-conversation returns the open opp
        var byConv = await SendAsync(HttpMethod.Get,
            $"/api/CrmOpportunity/by-conversation/message/{matchedConv}", "Viewer");
        Assert.Equal(HttpStatusCode.OK, byConv.StatusCode);
        Assert.Equal(matchedOppId, (await ReadDataAsync(byConv)).GetProperty("id").GetGuid());
    }

    [Fact]
    public async Task C2_SameExternalIdOnOtherPage_CreatesNewCustomer_NotLinkedToOtherPageCustomer()
    {
        var pageA = await SeedChannelAsync("Page A");
        var pageB = await SeedChannelAsync("Page B");
        var customerOnA = await SeedCustomerWithIdentityAsync(
            "Khách trên A", null, pageA, "psid-shared", CrmIdentitySource.Message);
        var convOnB = await SeedConversationAsync(pageB, "psid-shared", "Khách trên B");
        var beforeCustomers = await CountCustomersAsync();
        var beforeIdentities = await CountIdentitiesAsync();

        var res = await SendAsync(HttpMethod.Post, "/api/CrmOpportunity/from-conversation", "Admin", new
        {
            kind = "message",
            id = convOnB
        });

        Assert.Equal(HttpStatusCode.OK, res.StatusCode);
        Assert.NotEqual(customerOnA, (await ReadDataAsync(res)).GetProperty("crmCustomerId").GetGuid());
        Assert.Equal(beforeCustomers + 1, await CountCustomersAsync());
        Assert.Equal(beforeIdentities + 1, await CountIdentitiesAsync());
    }

    [Fact]
    public async Task N1_MoveStageBackToOpen_WhenAnotherOpenExistsForConversation_Is400()
    {
        var (conv, first) = await CreateFromConversationAsync("psid-n1-move");
        Assert.Equal(HttpStatusCode.OK, (await SendAsync(HttpMethod.Post, $"/api/CrmOpportunity/{first}/move-stage", "Admin",
            new { stageId = CrmOpportunityStageIds.DaMua })).StatusCode);
        var second = await FromConversationIdAsync(conv); // first đã Won → tạo cơ hội Open mới
        Assert.NotEqual(first, second);

        var back = await SendAsync(HttpMethod.Post, $"/api/CrmOpportunity/{first}/move-stage", "Admin",
            new { stageId = CrmOpportunityStageIds.Moi });

        Assert.Equal(HttpStatusCode.BadRequest, back.StatusCode);
        await using var db = new AppDbContext(_options);
        Assert.Equal(CrmOpportunityStatus.Won, (await db.CrmOpportunities.SingleAsync(x => x.Id == first)).Status);
    }

    [Fact]
    public async Task N1_Unarchive_WhenAnotherOpenExistsForConversation_Is400()
    {
        var (conv, first) = await CreateFromConversationAsync("psid-n1-unarchive");
        Assert.Equal(HttpStatusCode.OK, (await SendAsync(HttpMethod.Post, $"/api/CrmOpportunity/{first}/archive", "Admin")).StatusCode);
        var second = await FromConversationIdAsync(conv);
        Assert.NotEqual(first, second);

        var unarchive = await SendAsync(HttpMethod.Post, $"/api/CrmOpportunity/{first}/unarchive", "Admin");

        Assert.Equal(HttpStatusCode.BadRequest, unarchive.StatusCode);
        await using var db = new AppDbContext(_options);
        Assert.True((await db.CrmOpportunities.SingleAsync(x => x.Id == first)).IsArchived);
    }

    [Fact]
    public async Task N1_ConcurrentFromConversation_UniqueIndexLoser_ReturnsWinnersOpportunity_NotServerError()
    {
        var channelId = await SeedChannelAsync("Page Race");
        var conv = await SeedConversationAsync(channelId, "psid-race", "Race");
        var customerId = await SeedCustomerWithIdentityAsync("Race", null, channelId, "psid-race", CrmIdentitySource.Message);
        var winner = Guid.NewGuid();
        // Mô phỏng request song song thắng cuộc: dòng Open của cùng hội thoại xuất hiện ngay trước SaveChanges của request này.
        _beforeOpportunitySave = () =>
        {
            _beforeOpportunitySave = null;
            using var cmd = _connection.CreateCommand();
            cmd.CommandText = "INSERT INTO CrmOpportunities (Id, CrmCustomerId, Title, StageId, Status, IsArchived, Source, PageConversationId, ExpectedValue, IsDeleted, CreatedAt) " +
                              $"VALUES ('{winner.ToString().ToUpperInvariant()}', '{customerId.ToString().ToUpperInvariant()}', 'Winner', '{CrmOpportunityStageIds.Moi.ToString().ToUpperInvariant()}', 1, 0, 1, '{conv.ToString().ToUpperInvariant()}', 0, 0, '2026-10-09 00:00:00')";
            cmd.ExecuteNonQuery();
        };

        var res = await SendAsync(HttpMethod.Post, "/api/CrmOpportunity/from-conversation", "Admin", new { kind = "message", id = conv });

        Assert.True(res.StatusCode == HttpStatusCode.OK, await res.Content.ReadAsStringAsync());
        Assert.Equal(winner, (await ReadDataAsync(res)).GetProperty("id").GetGuid());
        Assert.Equal(1, await CountOppsForConversationAsync(conv));
    }

    private Action? _beforeOpportunitySave;

    private sealed class RaceHookInterceptor(CrmOpportunityAuthzPipelineTests owner) : SaveChangesInterceptor
    {
        public override InterceptionResult<int> SavingChanges(DbContextEventData eventData, InterceptionResult<int> result)
        {
            if (eventData.Context!.ChangeTracker.Entries<CrmOpportunityModel>().Any(e => e.State == EntityState.Added))
                owner._beforeOpportunitySave?.Invoke();
            return result;
        }

        public override ValueTask<InterceptionResult<int>> SavingChangesAsync(
            DbContextEventData eventData, InterceptionResult<int> result, CancellationToken ct = default)
            => ValueTask.FromResult(SavingChanges(eventData, result));
    }

    private async Task<(Guid Conversation, Guid Opportunity)> CreateFromConversationAsync(string externalId)
    {
        var channelId = await SeedChannelAsync("Page " + externalId);
        var conv = await SeedConversationAsync(channelId, externalId, externalId);
        return (conv, await FromConversationIdAsync(conv));
    }

    private async Task<Guid> FromConversationIdAsync(Guid conversationId)
    {
        var res = await SendAsync(HttpMethod.Post, "/api/CrmOpportunity/from-conversation", "Admin",
            new { kind = "message", id = conversationId });
        Assert.Equal(HttpStatusCode.OK, res.StatusCode);
        return (await ReadDataAsync(res)).GetProperty("id").GetGuid();
    }

    [Fact]
    public async Task N1_FromConversation_AssigneeIsConversationOwner_ElseCreator()
    {
        var channelId = await SeedChannelAsync("Page Assignee");
        var owner = Guid.Parse("dddddddd-eeee-ffff-0000-111111111111");

        var assignedConv = await SeedConversationAsync(channelId, "psid-assigned", "Có phụ trách");
        await using (var db = new AppDbContext(_options))
        {
            (await db.PageConversations.SingleAsync(x => x.Id == assignedConv)).AssignedUserId = owner;
            await db.SaveChangesAsync();
        }
        var unassignedConv = await SeedConversationAsync(channelId, "psid-unassigned", "Chưa phụ trách");

        var withOwner = await SendAsync(HttpMethod.Post, "/api/CrmOpportunity/from-conversation", "Admin",
            new { kind = "message", id = assignedConv });
        var withoutOwner = await SendAsync(HttpMethod.Post, "/api/CrmOpportunity/from-conversation", "Admin",
            new { kind = "message", id = unassignedConv });

        Assert.Equal(owner, (await ReadDataAsync(withOwner)).GetProperty("assigneeUserId").GetGuid());
        Assert.Equal(_actorUserId, (await ReadDataAsync(withoutOwner)).GetProperty("assigneeUserId").GetGuid());

        // Hiện ở chế độ mặc định "của tôi" của người tạo (chỉ cơ hội không có phụ trách → gán cho người tạo).
        var mine = await SendAsync(HttpMethod.Post, "/api/CrmOpportunity/filter", "Admin",
            new { assigneeFilter = "mine", index = 1, size = 20 });
        var mineIds = (await ReadDataAsync(mine)).GetProperty("items").EnumerateArray()
            .Select(x => x.GetProperty("pageConversationId").GetGuid()).ToList();
        Assert.Contains(unassignedConv, mineIds);
        Assert.DoesNotContain(assignedConv, mineIds);
    }

    [Fact]
    public async Task N1_FromConversation_Comment_AssigneeIsCommentOwner_ElseCreator()
    {
        var channelId = await SeedChannelAsync("Page Cmt Assignee");
        var owner = Guid.Parse("dddddddd-eeee-ffff-0000-222222222222");
        var assigned = await SeedTopLevelCommentAsync(channelId, "author-assigned", "x");
        var plain = await SeedTopLevelCommentAsync(channelId, "author-plain", "y");
        await using (var db = new AppDbContext(_options))
        {
            (await db.SocialComments.SingleAsync(c => c.Id == assigned)).AssignedUserId = owner;
            await db.SaveChangesAsync();
        }

        var r1 = await SendAsync(HttpMethod.Post, "/api/CrmOpportunity/from-conversation", "Admin", new { kind = "comment", id = assigned });
        var r2 = await SendAsync(HttpMethod.Post, "/api/CrmOpportunity/from-conversation", "Admin", new { kind = "comment", id = plain });

        Assert.Equal(owner, (await ReadDataAsync(r1)).GetProperty("assigneeUserId").GetGuid());
        Assert.Equal(_actorUserId, (await ReadDataAsync(r2)).GetProperty("assigneeUserId").GetGuid());
    }

    /// <summary>
    /// "Phụ trách" hiện tên người dùng (như inbox), không phải id dạng hex — kể cả bản ghi cũ
    /// từng lưu hex trong AssignedTo.
    /// </summary>
    [Fact]
    public async Task AssignedTo_IsUserDisplayName_NotHexId_IncludingLegacyRows()
    {
        await using (var db = new AppDbContext(_options))
        {
            db.Users.Add(new ApplicationUser
            {
                Id = _actorUserId,
                UserName = "reviewer@vni.local",
                NormalizedUserName = "REVIEWER@VNI.LOCAL",
                Email = "reviewer@vni.local",
                SecurityStamp = Guid.NewGuid().ToString()
            });
            await db.SaveChangesAsync();
        }

        var channelId = await SeedChannelAsync("Page Display Name");
        var convId = await SeedConversationAsync(channelId, "psid-display-name", "Khách tên");
        var created = await ReadDataAsync(await SendAsync(
            HttpMethod.Post, "/api/CrmOpportunity/from-conversation", "Admin",
            new { kind = "message", id = convId }));
        var oppId = created.GetProperty("id").GetGuid();
        Assert.Equal("reviewer@vni.local", created.GetProperty("assignedTo").GetString());

        // Bản ghi cũ: AssignedTo lưu id hex → đọc ra vẫn là tên.
        await using (var db = new AppDbContext(_options))
        {
            (await db.CrmOpportunities.SingleAsync(x => x.Id == oppId)).AssignedTo = _actorUserId.ToString("N");
            await db.SaveChangesAsync();
        }

        var detail = await ReadDataAsync(await SendAsync(HttpMethod.Get, $"/api/CrmOpportunity/{oppId}", "Viewer"));
        Assert.Equal("reviewer@vni.local", detail.GetProperty("assignedTo").GetString());

        var list = await ReadDataAsync(await SendAsync(HttpMethod.Post, "/api/CrmOpportunity/filter", "Admin",
            new { assigneeFilter = "mine", index = 1, size = 20 }));
        var row = list.GetProperty("items").EnumerateArray().Single(x => x.GetProperty("id").GetGuid() == oppId);
        Assert.Equal("reviewer@vni.local", row.GetProperty("assignedTo").GetString());

        var assigned = await ReadDataAsync(await SendAsync(
            HttpMethod.Post, $"/api/CrmOpportunity/{oppId}/assign", "Admin",
            new { assigneeUserId = _actorUserId }));
        Assert.Equal("reviewer@vni.local", assigned.GetProperty("assignedTo").GetString());
        await using (var db = new AppDbContext(_options))
            Assert.Equal("reviewer@vni.local",
                (await db.CrmOpportunities.AsNoTracking().SingleAsync(x => x.Id == oppId)).AssignedTo);
    }

    [Fact]
    public async Task D_SeedingConversationAlone_DoesNotCreateOpportunity()
    {
        var before = await CountOppsAsync();
        var channelId = await SeedChannelAsync("Page No Auto Opp");
        await SeedConversationAsync(channelId, "psid-no-auto", "No Auto");
        await SeedTopLevelCommentAsync(channelId, "author-no-auto", "comment only");
        Assert.Equal(before, await CountOppsAsync());
    }

    [Fact]
    public async Task E_EachWrite_CreatesCustomerActionLog()
    {
        var customerId = await SeedCustomerAsync("Log Cust", "+84903330001");

        async Task AssertLogAsync(string actionType, Func<Task> act)
        {
            var before = await CountLogsAsync(customerId, actionType);
            await act();
            Assert.Equal(before + 1, await CountLogsAsync(customerId, actionType));
        }

        Guid oppId = Guid.Empty;
        await AssertLogAsync("OppCreate", async () =>
        {
            var res = await SendAsync(HttpMethod.Post, "/api/CrmOpportunity", "Admin", new
            {
                crmCustomerId = customerId,
                title = "Log Opp",
                expectedValue = 10
            });
            Assert.Equal(HttpStatusCode.OK, res.StatusCode);
            oppId = (await ReadDataAsync(res)).GetProperty("id").GetGuid();
        });

        await AssertLogAsync("OppUpdate", async () =>
        {
            Assert.Equal(HttpStatusCode.OK, (await SendAsync(HttpMethod.Put, $"/api/CrmOpportunity/{oppId}", "Admin", new
            {
                title = "Log Opp 2",
                expectedValue = 20
            })).StatusCode);
        });

        await AssertLogAsync("OppMoveStage", async () =>
        {
            Assert.Equal(HttpStatusCode.OK, (await SendAsync(HttpMethod.Post, $"/api/CrmOpportunity/{oppId}/move-stage", "Admin", new
            {
                stageId = CrmOpportunityStageIds.BamDuoi
            })).StatusCode);
        });

        await AssertLogAsync("OppAssign", async () =>
        {
            Assert.Equal(HttpStatusCode.OK, (await SendAsync(HttpMethod.Post, $"/api/CrmOpportunity/{oppId}/assign", "Admin", new
            {
                assigneeUserId = _actorUserId
            })).StatusCode);
        });

        await AssertLogAsync("OppAddWatcher", async () =>
        {
            Assert.Equal(HttpStatusCode.OK, (await SendAsync(
                HttpMethod.Post, $"/api/CrmOpportunity/{oppId}/watchers/{_watcherUserId}", "Admin")).StatusCode);
        });

        await AssertLogAsync("OppRemoveWatcher", async () =>
        {
            Assert.Equal(HttpStatusCode.OK, (await SendAsync(
                HttpMethod.Delete, $"/api/CrmOpportunity/{oppId}/watchers/{_watcherUserId}", "Admin")).StatusCode);
        });

        await AssertLogAsync("OppArchive", async () =>
        {
            Assert.Equal(HttpStatusCode.OK, (await SendAsync(
                HttpMethod.Post, $"/api/CrmOpportunity/{oppId}/archive", "Admin")).StatusCode);
        });

        await AssertLogAsync("OppUnarchive", async () =>
        {
            Assert.Equal(HttpStatusCode.OK, (await SendAsync(
                HttpMethod.Post, $"/api/CrmOpportunity/{oppId}/unarchive", "Admin")).StatusCode);
        });

        await AssertLogAsync("OppSoftDelete", async () =>
        {
            Assert.Equal(HttpStatusCode.OK, (await SendAsync(
                HttpMethod.Delete, $"/api/CrmOpportunity/{oppId}", "Admin")).StatusCode);
        });
    }

    [Fact]
    public async Task F_Response_NoAccessToken_SoftDelete_GoneFromFilter()
    {
        var channelId = await SeedChannelAsync("Page Leak", accessToken: "LEAK_ME_TOKEN");
        var customerId = await SeedCustomerWithIdentityAsync(
            "Leak Cust", "+84904440001", channelId, "psid-leak", CrmIdentitySource.Message);
        var convId = await SeedConversationAsync(channelId, "psid-leak", "Leak Cust");

        var create = await SendAsync(HttpMethod.Post, "/api/CrmOpportunity", "Admin", new
        {
            crmCustomerId = customerId,
            title = "Leak Opp",
            expectedValue = 0,
            socialChannelId = channelId,
            pageConversationId = convId,
            source = (int)CrmOpportunitySource.Message
        });
        Assert.Equal(HttpStatusCode.OK, create.StatusCode);
        var raw = await create.Content.ReadAsStringAsync();
        Assert.DoesNotContain("LEAK_ME_TOKEN", raw, StringComparison.Ordinal);
        Assert.DoesNotContain("accessToken", raw, StringComparison.OrdinalIgnoreCase);

        var data = await ReadDataAsync(create);
        var oppId = data.GetProperty("id").GetGuid();

        var del = await SendAsync(HttpMethod.Delete, $"/api/CrmOpportunity/{oppId}", "Admin");
        Assert.Equal(HttpStatusCode.OK, del.StatusCode);

        await using (var db = new AppDbContext(_options))
        {
            var row = await db.CrmOpportunities.IgnoreQueryFilters()
                .FirstAsync(x => x.Id == oppId);
            Assert.True(row.IsDeleted);
        }

        var filter = await SendAsync(HttpMethod.Post, "/api/CrmOpportunity/filter", "Admin", new
        {
            index = 1,
            size = 100,
            isArchived = false
        });
        Assert.Equal(HttpStatusCode.OK, filter.StatusCode);
        var items = (await ReadDataAsync(filter)).GetProperty("items").EnumerateArray().ToList();
        Assert.DoesNotContain(items, x => x.GetProperty("id").GetGuid() == oppId);

        var get = await SendAsync(HttpMethod.Get, $"/api/CrmOpportunity/{oppId}", "Admin");
        Assert.Equal(HttpStatusCode.NotFound, get.StatusCode);
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

    private async Task<int> CountOppsAsync()
    {
        await using var db = new AppDbContext(_options);
        return await db.CrmOpportunities.CountAsync(x => !x.IsDeleted);
    }

    private async Task<int> CountOppsForConversationAsync(Guid conversationId)
    {
        await using var db = new AppDbContext(_options);
        return await db.CrmOpportunities.CountAsync(x =>
            !x.IsDeleted && x.PageConversationId == conversationId);
    }

    private async Task<int> CountCustomersAsync()
    {
        await using var db = new AppDbContext(_options);
        return await db.CrmCustomers.CountAsync(x => !x.IsDeleted);
    }

    private async Task<int> CountIdentitiesAsync()
    {
        await using var db = new AppDbContext(_options);
        return await db.CrmCustomerIdentities.CountAsync(x => !x.IsDeleted);
    }

    private async Task<int> CountLogsAsync(Guid customerId, string actionType)
    {
        await using var db = new AppDbContext(_options);
        return await db.CrmCustomerActionLogs.CountAsync(x =>
            x.CrmCustomerId == customerId && x.ActionType == actionType);
    }

    private async Task<Guid> SeedChannelAsync(string name, string? accessToken = "token")
    {
        await using var db = new AppDbContext(_options);
        var id = Guid.NewGuid();
        db.SocialChannels.Add(new SocialChannelModel
        {
            Id = id,
            Platform = SocialPlatform.Facebook,
            ChannelType = SocialChannelType.Page,
            PageName = name,
            ExternalPageId = $"ext-{id:N}",
            AccessToken = accessToken,
            IsActive = true,
            CreatedAt = DateTime.UtcNow,
            CreatedBy = "seed"
        });
        await db.SaveChangesAsync();
        return id;
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

    private async Task<Guid> SeedCustomerWithIdentityAsync(
        string displayName,
        string? phoneE164,
        Guid channelId,
        string externalId,
        CrmIdentitySource source)
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
        db.CrmCustomerIdentities.Add(new CrmCustomerIdentityModel
        {
            Id = Guid.NewGuid(),
            CrmCustomerId = id,
            Platform = SocialPlatform.Facebook,
            SocialChannelId = channelId,
            ExternalId = externalId,
            Source = source,
            DisplayName = displayName,
            CreatedAt = DateTime.UtcNow,
            CreatedBy = "seed"
        });
        await db.SaveChangesAsync();
        return id;
    }

    private async Task<Guid> SeedConversationAsync(
        Guid channelId, string participantExternalId, string participantName)
    {
        await using var db = new AppDbContext(_options);
        var id = Guid.NewGuid();
        var at = DateTime.UtcNow.AddHours(-2);
        db.PageConversations.Add(new PageConversationModel
        {
            Id = id,
            SocialChannelId = channelId,
            ExternalConversationId = $"t_{id:N}",
            ParticipantExternalId = participantExternalId,
            ParticipantName = participantName,
            Snippet = participantName,
            LastMessageAt = at,
            LastCustomerMessageAt = at,
            InboxStatus = MessageInboxStatus.New,
            CreatedAt = at,
            CreatedBy = "seed"
        });
        await db.SaveChangesAsync();
        return id;
    }

    private async Task<Guid> SeedTopLevelCommentAsync(Guid channelId, string authorExternalId, string message)
    {
        await using var db = new AppDbContext(_options);
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
        var at = DateTime.UtcNow.AddHours(-1);
        db.SocialComments.Add(new SocialCommentModel
        {
            Id = id,
            SocialChannelId = channelId,
            SocialPostId = postId,
            Platform = SocialPlatform.Facebook,
            ExternalCommentId = $"cmt-{id:N}",
            AuthorExternalId = authorExternalId,
            AuthorName = "Commenter",
            Message = message,
            CommentedAt = at,
            ParentCommentId = null,
            InboxStatus = CommentInboxStatus.New,
            CreatedAt = at,
            CreatedBy = "seed"
        });
        await db.SaveChangesAsync();
        return id;
    }

    private sealed class TestAuthHandler : AuthenticationHandler<AuthenticationSchemeOptions>
    {
        public const string SchemeName = "TestCrmOpportunityAuthz";

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
