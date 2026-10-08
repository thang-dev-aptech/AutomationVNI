using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text.Encodings.Web;
using System.Text.Json;
using Backend.Data;
using Backend.Modules.ChannelGroup;
using Backend.Modules.Crm.Customers;
using Backend.Modules.Inbox;
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
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Xunit;

namespace Backend.Tests.Modules.Inbox;

/// <summary>
/// AC inbox-list-filter-test (412a72b7) — phạm vi khớp PageMessage/SocialComment filter,
/// bộ lọc OR/AND, phân trang gộp, xoá mềm, 401, revert-to-prove (a).
/// Scope rules (từ code hiện có):
/// - Message: !PageConversation.IsDeleted
/// - Comment: !IsDeleted &amp;&amp; !IsDeletedOnPlatform &amp;&amp; !IsFromPage &amp;&amp; ParentCommentId == null
/// </summary>
public sealed class InboxListFilterPipelineTests : IAsyncLifetime
{
    private readonly SqliteConnection _connection;
    private readonly DbContextOptions<AppDbContext> _options;
    private readonly IHost _host;
    private readonly HttpClient _client;
    private readonly Guid _actorUserId = Guid.Parse("aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee");
    private readonly Guid _otherUserId = Guid.Parse("11111111-2222-3333-4444-555555555555");

    public InboxListFilterPipelineTests()
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
                    services.AddScoped<CrmCustomerService>();
                    services.AddScoped<ChannelGroupRepository>();
                    services.AddScoped<InboxQueryService>();
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
                        .ConfigurePrimaryHttpMessageHandler(() => new StubHttpHandler());
                    services.AddScoped<PageMessageService>();
                    services.AddScoped<SocialCommentService>();
                    services.AddScoped<Backend.Modules.Crm.Assignment.CrmAutoAssignService>();
                    services.AddAuthentication(TestAuthHandler.SchemeName)
                        .AddScheme<AuthenticationSchemeOptions, TestAuthHandler>(
                            TestAuthHandler.SchemeName, _ => { });
                    services.AddAuthorization();
                    services.AddControllers(o => o.Filters.Add<GlobalExceptionFilter>())
                        .AddApplicationPart(typeof(InboxController).Assembly);
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
    public async Task Filter_NoFilter_EqualsUnionOfPageMessageAndSocialCommentScope()
    {
        var pageA = await SeedChannelAsync("Page A");
        var pageB = await SeedChannelAsync("Page B");
        var msgA = await SeedConversationAsync(pageA, "MsgA", lastAt: DateTime.UtcNow.AddHours(-3));
        var msgB = await SeedConversationAsync(pageB, "MsgB", lastAt: DateTime.UtcNow.AddHours(-2));
        var cmtRoot = await SeedCommentAsync(pageA, "Root", DateTime.UtcNow.AddHours(-1), authorId: "a1");
        var replyId = await SeedReplyAsync(cmtRoot, pageA, "reply", DateTime.UtcNow, isFromPage: false);
        var fromPage = await SeedCommentAsync(pageA, "FromPage", DateTime.UtcNow, isFromPage: true);
        var softMsg = await SeedConversationAsync(pageA, "Soft", lastAt: DateTime.UtcNow, softDeleted: true);
        var softCmt = await SeedCommentAsync(pageB, "SoftCmt", DateTime.UtcNow, softDeleted: true);

        var inbox = await FilterAsync(new { });
        var inboxIds = inbox.Select(x => x.GetProperty("id").GetGuid()).ToHashSet();

        Assert.Contains(msgA, inboxIds);
        Assert.Contains(msgB, inboxIds);
        Assert.Contains(cmtRoot, inboxIds);
        Assert.DoesNotContain(replyId, inboxIds);
        Assert.DoesNotContain(fromPage, inboxIds);
        Assert.DoesNotContain(softMsg, inboxIds);
        Assert.DoesNotContain(softCmt, inboxIds);

        // Khớp tập PageMessage/filter ∪ SocialComment/filter (top-level).
        var pm = await PageMessageFilterAsync(new { index = 1, size = 100 });
        var sc = await SocialCommentFilterAsync(new { index = 1, size = 100 });
        var expected = pm.Concat(sc).Select(x => x.GetProperty("id").GetGuid()).ToHashSet();
        Assert.Equal(expected, inboxIds);
    }

    /// <summary>
    /// Revert-to-prove (a): bỏ ràng buộc !IsDeleted trên query thô → soft-deleted lộ ra;
    /// API Inbox vẫn loại chúng.
    /// </summary>
    [Fact]
    public async Task Filter_RevertToProve_A_WithoutSoftDeleteScope_WouldLeakDeleted()
    {
        var page = await SeedChannelAsync("Scope");
        var alive = await SeedConversationAsync(page, "Alive", lastAt: DateTime.UtcNow.AddHours(-1));
        var dead = await SeedConversationAsync(page, "Dead", lastAt: DateTime.UtcNow, softDeleted: true);

        await using var db = new AppDbContext(_options);
        var leaked = await db.PageConversations.AsNoTracking()
            .Select(x => x.Id)
            .ToListAsync();
        Assert.Contains(dead, leaked);
        Assert.Contains(alive, leaked);

        var inbox = await FilterAsync(new { socialChannelIds = new[] { page } });
        var ids = inbox.Select(x => x.GetProperty("id").GetGuid()).ToHashSet();
        Assert.Contains(alive, ids);
        Assert.DoesNotContain(dead, ids);
        Assert.Equal(InboxQueryService.ScopeRuleMessage, "!PageConversation.IsDeleted");
    }

    [Fact]
    public async Task Filter_EachFilterGroup_AndCombinations()
    {
        var pageA = await SeedChannelAsync("A");
        var pageB = await SeedChannelAsync("B");
        var groupId = await SeedChannelGroupAsync("G", [pageA]);

        var openMsg = await SeedConversationAsync(
            pageA, "Open", lastAt: DateTime.UtcNow.AddHours(-2),
            lastCustomer: DateTime.UtcNow.AddHours(-2), unread: 2,
            status: MessageInboxStatus.New, assignedUserId: _actorUserId);
        var closedMsg = await SeedConversationAsync(
            pageA, "Closed", lastAt: DateTime.UtcNow.AddHours(-30),
            lastCustomer: DateTime.UtcNow.AddHours(-30), unread: 0,
            status: MessageInboxStatus.InProgress, assignedUserId: _otherUserId,
            lastPage: DateTime.UtcNow.AddHours(-29));
        var waitingCustomer = await SeedConversationAsync(
            pageB, "WaitCust", lastAt: DateTime.UtcNow.AddHours(-1),
            lastCustomer: DateTime.UtcNow.AddHours(-3),
            lastPage: DateTime.UtcNow.AddHours(-1),
            status: MessageInboxStatus.Replied, unread: 0);
        var cmtNew = await SeedCommentAsync(
            pageA, "Bình luận mới", DateTime.UtcNow.AddMinutes(-30),
            status: CommentInboxStatus.New, authorName: "Nguyễn Văn A");
        var cmtOld = await SeedCommentAsync(
            pageB, "old", DateTime.UtcNow.AddDays(-2),
            status: CommentInboxStatus.Replied);

        // Loại
        var onlyMsg = await FilterAsync(new { kinds = new[] { "message" } });
        Assert.All(onlyMsg, x => Assert.Equal("message", x.GetProperty("kind").GetString()));
        Assert.Contains(onlyMsg, x => x.GetProperty("id").GetGuid() == openMsg);
        Assert.DoesNotContain(onlyMsg, x => x.GetProperty("id").GetGuid() == cmtNew);

        // Trạng thái OR
        var byStatus = await FilterAsync(new { statuses = new[] { 1, 2 } }); // New + InProgress
        var statusIds = byStatus.Select(x => x.GetProperty("id").GetGuid()).ToHashSet();
        Assert.Contains(openMsg, statusIds);
        Assert.Contains(closedMsg, statusIds);
        Assert.Contains(cmtNew, statusIds);
        Assert.DoesNotContain(waitingCustomer, statusIds);

        // Nguồn kênh
        var byChannel = await FilterAsync(new { socialChannelIds = new[] { pageB } });
        var chIds = byChannel.Select(x => x.GetProperty("id").GetGuid()).ToHashSet();
        Assert.Contains(waitingCustomer, chIds);
        Assert.Contains(cmtOld, chIds);
        Assert.DoesNotContain(openMsg, chIds);

        // Nguồn nhóm kênh
        var byGroup = await FilterAsync(new { channelGroupIds = new[] { groupId } });
        var gIds = byGroup.Select(x => x.GetProperty("id").GetGuid()).ToHashSet();
        Assert.Contains(openMsg, gIds);
        Assert.Contains(cmtNew, gIds);
        Assert.DoesNotContain(waitingCustomer, gIds);

        // Chưa đọc
        var unread = await FilterAsync(new { unreadOnly = true });
        var uIds = unread.Select(x => x.GetProperty("id").GetGuid()).ToHashSet();
        Assert.Contains(openMsg, uIds);
        Assert.Contains(cmtNew, uIds);
        Assert.DoesNotContain(closedMsg, uIds);

        // Khoảng thời gian nửa mở [from, to)
        var from = DateTime.UtcNow.AddHours(-4);
        var to = DateTime.UtcNow.AddHours(-1);
        var ranged = await FilterAsync(new { fromUtc = from, toUtc = to });
        var rIds = ranged.Select(x => x.GetProperty("id").GetGuid()).ToHashSet();
        Assert.Contains(openMsg, rIds);
        Assert.DoesNotContain(cmtNew, rIds); // newer than to
        Assert.DoesNotContain(cmtOld, rIds);

        // Nhân viên OR
        var byAssignee = await FilterAsync(new { assignedUserIds = new[] { _actorUserId, _otherUserId } });
        var aIds = byAssignee.Select(x => x.GetProperty("id").GetGuid()).ToHashSet();
        Assert.Contains(openMsg, aIds);
        Assert.Contains(closedMsg, aIds);

        // Khách chưa trả lời (tin cuối page)
        var unanswered = await FilterAsync(new { customerUnansweredOnly = true, kinds = new[] { "message" } });
        Assert.Contains(unanswered, x => x.GetProperty("id").GetGuid() == waitingCustomer);
        Assert.Contains(unanswered, x => x.GetProperty("id").GetGuid() == closedMsg);
        Assert.DoesNotContain(unanswered, x => x.GetProperty("id").GetGuid() == openMsg);

        // Cửa sổ 24h — comment bị loại
        var openWin = await FilterAsync(new { openWindowOnly = true });
        Assert.All(openWin, x => Assert.Equal("message", x.GetProperty("kind").GetString()));
        Assert.Contains(openWin, x => x.GetProperty("id").GetGuid() == openMsg);
        Assert.DoesNotContain(openWin, x => x.GetProperty("id").GetGuid() == closedMsg);
        Assert.DoesNotContain(openWin, x => x.GetProperty("id").GetGuid() == cmtNew);

        // Keyword tiếng Việt có dấu viết hoa
        var kw = await FilterAsync(new { keyword = "NGUYỄN VĂN A" });
        Assert.Contains(kw, x => x.GetProperty("id").GetGuid() == cmtNew);

        // AND giữa nhóm: kênh A + New + unread
        var combo = await FilterAsync(new
        {
            socialChannelIds = new[] { pageA },
            statuses = new[] { 1 },
            unreadOnly = true
        });
        var comboIds = combo.Select(x => x.GetProperty("id").GetGuid()).ToHashSet();
        Assert.Contains(openMsg, comboIds);
        Assert.Contains(cmtNew, comboIds);
        Assert.DoesNotContain(closedMsg, comboIds);
    }

    [Fact]
    public async Task Filter_Pagination_MergedNoDup_SortedByLastActivity()
    {
        var page = await SeedChannelAsync("Page");
        var ids = new List<(Guid Id, DateTime At, string Kind)>();
        for (var i = 0; i < 4; i++)
        {
            var at = DateTime.UtcNow.AddMinutes(-i * 2);
            var id = await SeedConversationAsync(page, $"M{i}", lastAt: at);
            ids.Add((id, at, "message"));
        }

        for (var i = 0; i < 3; i++)
        {
            var at = DateTime.UtcNow.AddMinutes(-(i * 2 + 1));
            var id = await SeedCommentAsync(page, $"C{i}", at);
            ids.Add((id, at, "comment"));
        }

        var expectedOrder = ids.OrderByDescending(x => x.At).ThenByDescending(x => x.Id).ToList();

        var page1 = await FilterPagedAsync(new { socialChannelIds = new[] { page }, index = 1, size = 3 });
        var page2 = await FilterPagedAsync(new { socialChannelIds = new[] { page }, index = 2, size = 3 });
        var page3 = await FilterPagedAsync(new { socialChannelIds = new[] { page }, index = 3, size = 3 });

        Assert.Equal(7, page1.Total);
        var all = page1.Items.Concat(page2.Items).Concat(page3.Items).ToList();
        Assert.Equal(7, all.Count);
        Assert.Equal(7, all.Select(x => x.GetProperty("id").GetGuid()).Distinct().Count());

        for (var i = 0; i < expectedOrder.Count; i++)
        {
            Assert.Equal(expectedOrder[i].Id, all[i].GetProperty("id").GetGuid());
            Assert.Equal(expectedOrder[i].Kind, all[i].GetProperty("kind").GetString());
        }
    }

    [Fact]
    public async Task Filter_CommentLastActivity_UsesLatestReply()
    {
        var page = await SeedChannelAsync("ReplyAct");
        var root = await SeedCommentAsync(page, "root", DateTime.UtcNow.AddHours(-5));
        await SeedReplyAsync(root, page, "late reply", DateTime.UtcNow.AddMinutes(-10), isFromPage: true);
        var newerMsg = await SeedConversationAsync(page, "msg", lastAt: DateTime.UtcNow.AddMinutes(-30));

        var items = await FilterAsync(new { socialChannelIds = new[] { page } });
        Assert.Equal(2, items.Count);
        Assert.Equal(root, items[0].GetProperty("id").GetGuid());
        Assert.Equal(newerMsg, items[1].GetProperty("id").GetGuid());
    }

    [Fact]
    public async Task Filter_SoftDeleted_Hidden()
    {
        var page = await SeedChannelAsync("Del");
        var alive = await SeedConversationAsync(page, "A", lastAt: DateTime.UtcNow);
        await SeedConversationAsync(page, "D", lastAt: DateTime.UtcNow, softDeleted: true);
        await SeedCommentAsync(page, "CD", DateTime.UtcNow, softDeleted: true);
        await SeedCommentAsync(page, "plat", DateTime.UtcNow, deletedOnPlatform: true);

        var items = await FilterAsync(new { socialChannelIds = new[] { page } });
        Assert.Single(items);
        Assert.Equal(alive, items[0].GetProperty("id").GetGuid());
    }

    [Fact]
    public async Task Filter_Unauthenticated_401()
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/Inbox/filter")
        {
            Content = JsonContent.Create(new { })
        };
        using var response = await _client.SendAsync(request);
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Summary_AggregatesUnreadNewInProgress()
    {
        var page = await SeedChannelAsync("Sum");
        await SeedConversationAsync(page, "n", status: MessageInboxStatus.New, unread: 3, lastAt: DateTime.UtcNow);
        await SeedConversationAsync(page, "p", status: MessageInboxStatus.InProgress, unread: 0, lastAt: DateTime.UtcNow);
        await SeedCommentAsync(page, "cn", DateTime.UtcNow, status: CommentInboxStatus.New);
        await SeedCommentAsync(page, "cp", DateTime.UtcNow.AddMinutes(-1), status: CommentInboxStatus.InProgress);

        using var request = new HttpRequestMessage(HttpMethod.Get, "/api/Inbox/summary");
        Authorize(request, "Viewer");
        using var response = await _client.SendAsync(request);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var data = (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("data");
        Assert.True(data.GetProperty("unread").GetInt32() >= 2);
        Assert.True(data.GetProperty("newCount").GetInt32() >= 2);
        Assert.True(data.GetProperty("inProgress").GetInt32() >= 2);
    }

    // --- helpers ---

    private async Task<List<JsonElement>> FilterAsync(object body)
    {
        var page = await FilterPagedAsync(body);
        return page.Items;
    }

    private async Task<(List<JsonElement> Items, int Total)> FilterPagedAsync(object body)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/Inbox/filter")
        {
            Content = JsonContent.Create(body)
        };
        Authorize(request, "Reviewer");
        using var response = await _client.SendAsync(request);
        var raw = await response.Content.ReadAsStringAsync();
        Assert.True(response.StatusCode == HttpStatusCode.OK, $"Inbox filter {(int)response.StatusCode}: {raw}");
        var data = JsonDocument.Parse(raw).RootElement.GetProperty("data");
        return (data.GetProperty("items").EnumerateArray().ToList(), data.GetProperty("total").GetInt32());
    }

    private async Task<List<JsonElement>> PageMessageFilterAsync(object body)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/PageMessage/filter")
        {
            Content = JsonContent.Create(body)
        };
        Authorize(request, "Reviewer");
        using var response = await _client.SendAsync(request);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<JsonElement>())
            .GetProperty("data").GetProperty("items").EnumerateArray().ToList();
    }

    private async Task<List<JsonElement>> SocialCommentFilterAsync(object body)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/SocialComment/filter")
        {
            Content = JsonContent.Create(body)
        };
        Authorize(request, "Reviewer");
        using var response = await _client.SendAsync(request);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<JsonElement>())
            .GetProperty("data").GetProperty("items").EnumerateArray().ToList();
    }

    private void Authorize(HttpRequestMessage request, string role)
        => request.Headers.Authorization = new AuthenticationHeaderValue(
            TestAuthHandler.SchemeName, $"{role}:{_actorUserId:N}");

    private async Task<Guid> SeedChannelAsync(string name)
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
            AccessToken = "token",
            IsActive = true,
            CreatedAt = DateTime.UtcNow,
            CreatedBy = "seed"
        });
        await db.SaveChangesAsync();
        return id;
    }

    private async Task<Guid> SeedChannelGroupAsync(string name, IEnumerable<Guid> channelIds)
    {
        await using var db = new AppDbContext(_options);
        var id = Guid.NewGuid();
        db.ChannelGroups.Add(new ChannelGroupModel
        {
            Id = id,
            Name = name,
            CreatedAt = DateTime.UtcNow,
            CreatedBy = "seed"
        });
        foreach (var ch in channelIds)
        {
            db.ChannelGroupMembers.Add(new ChannelGroupMemberModel
            {
                Id = Guid.NewGuid(),
                ChannelGroupId = id,
                SocialChannelId = ch,
                CreatedAt = DateTime.UtcNow,
                CreatedBy = "seed"
            });
        }

        await db.SaveChangesAsync();
        return id;
    }

    private async Task<Guid> SeedConversationAsync(
        Guid channelId,
        string participantName,
        DateTime? lastAt = null,
        DateTime? lastCustomer = null,
        DateTime? lastPage = null,
        Guid? assignedUserId = null,
        MessageInboxStatus status = MessageInboxStatus.New,
        int unread = 0,
        bool softDeleted = false)
    {
        await using var db = new AppDbContext(_options);
        var id = Guid.NewGuid();
        var at = lastAt ?? DateTime.UtcNow.AddHours(-3);
        db.PageConversations.Add(new PageConversationModel
        {
            Id = id,
            SocialChannelId = channelId,
            ExternalConversationId = $"t_{id:N}",
            ParticipantExternalId = $"psid-{id:N}",
            ParticipantName = participantName,
            Snippet = participantName,
            LastMessageAt = at,
            LastCustomerMessageAt = lastCustomer ?? at,
            LastPageMessageAt = lastPage,
            InboxStatus = status,
            AssignedUserId = assignedUserId,
            AssignedTo = assignedUserId?.ToString("N"),
            UnreadCount = unread,
            CreatedAt = at,
            CreatedBy = "seed",
            IsDeleted = softDeleted,
            DeletedAt = softDeleted ? DateTime.UtcNow : null
        });
        await db.SaveChangesAsync();
        return id;
    }

    private async Task<Guid> SeedCommentAsync(
        Guid channelId,
        string message,
        DateTime commentedAt,
        CommentInboxStatus status = CommentInboxStatus.New,
        string? authorId = null,
        string? authorName = null,
        bool isFromPage = false,
        bool softDeleted = false,
        bool deletedOnPlatform = false,
        Guid? parentId = null)
    {
        await using var db = new AppDbContext(_options);
        var postId = Guid.NewGuid();
        db.SocialPosts.Add(new SocialPostModel
        {
            Id = postId,
            SocialChannelId = channelId,
            Platform = SocialPlatform.Facebook,
            ExternalPostId = $"post-{postId:N}",
            PermalinkUrl = $"https://facebook.com/{postId:N}",
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
            ParentCommentId = parentId,
            AuthorExternalId = authorId ?? $"author-{id:N}",
            AuthorName = authorName ?? "Commenter",
            Message = message,
            CommentedAt = commentedAt,
            InboxStatus = status,
            IsFromPage = isFromPage,
            IsDeletedOnPlatform = deletedOnPlatform,
            PermalinkUrl = $"https://facebook.com/c/{id:N}",
            CreatedAt = commentedAt,
            CreatedBy = "seed",
            IsDeleted = softDeleted,
            DeletedAt = softDeleted ? DateTime.UtcNow : null
        });
        await db.SaveChangesAsync();
        return id;
    }

    private async Task<Guid> SeedReplyAsync(
        Guid parentId, Guid channelId, string message, DateTime at, bool isFromPage)
    {
        await using var db = new AppDbContext(_options);
        var parent = await db.SocialComments.AsNoTracking().FirstAsync(x => x.Id == parentId);
        var id = Guid.NewGuid();
        db.SocialComments.Add(new SocialCommentModel
        {
            Id = id,
            SocialChannelId = channelId,
            SocialPostId = parent.SocialPostId,
            Platform = SocialPlatform.Facebook,
            ExternalCommentId = $"cmt-{id:N}",
            ParentCommentId = parentId,
            AuthorExternalId = isFromPage ? "page" : "author-reply",
            AuthorName = isFromPage ? "Page" : "ReplyUser",
            Message = message,
            CommentedAt = at,
            IsFromPage = isFromPage,
            InboxStatus = CommentInboxStatus.New,
            CreatedAt = at,
            CreatedBy = "seed"
        });
        await db.SaveChangesAsync();
        return id;
    }

    private sealed class StubHttpHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
            => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("{}")
            });
    }

    private sealed class TestAuthHandler : AuthenticationHandler<AuthenticationSchemeOptions>
    {
        public const string SchemeName = "TestInboxList";

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

            var userId = Guid.TryParseExact(parts[1], "N", out var parsed) ? parsed : Guid.NewGuid();
            var claims = new[]
            {
                new Claim(ClaimTypes.Name, "actor"),
                new Claim(ClaimTypes.NameIdentifier, userId.ToString()),
                new Claim(ClaimTypes.Role, parts[0]),
            };
            return Task.FromResult(AuthenticateResult.Success(
                new AuthenticationTicket(new ClaimsPrincipal(new ClaimsIdentity(claims, SchemeName)), SchemeName)));
        }
    }

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
}
