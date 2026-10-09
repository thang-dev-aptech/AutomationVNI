using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using Backend.Data;
using Backend.Modules.Crm.Assignment;
using Backend.Modules.Crm.Customers;
using Backend.Modules.Crm.Inbox;
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
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Xunit;

namespace Backend.Tests.Modules.Crm.Inbox;

/// <summary>
/// AC a181718d (a)-(e): LastReadAtUtc + POST /read + unread thật.
/// Revert-to-prove: fake UnreadCount=1 theo status → (b) đỏ; sync ghi đè UnreadCount → (c) đỏ.
/// </summary>
public sealed class CrmInboxReadStateTests : IAsyncLifetime
{
    private readonly SqliteConnection _connection;
    private readonly DbContextOptions<AppDbContext> _options;
    private readonly GraphHttpHandler _httpHandler = new();
    private readonly IHost _host;
    private readonly HttpClient _client;
    private readonly Guid _actorUserId = Guid.Parse("aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee");

    public CrmInboxReadStateTests()
    {
        _connection = new SqliteConnection("DataSource=:memory:");
        _connection.Open();
        _options = new DbContextOptionsBuilder<AppDbContext>().UseSqlite(_connection).Options;
        using (var seed = new AppDbContext(_options))
            seed.Database.EnsureCreated();

        var options = _options;
        var handler = _httpHandler;
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
                        .ConfigurePrimaryHttpMessageHandler(() => handler);
                    services.AddScoped<PageMessageService>();
                    services.AddScoped<SocialCommentService>();
                    services.AddAuthentication(TestAuthHandler.SchemeName)
                        .AddScheme<AuthenticationSchemeOptions, TestAuthHandler>(
                            TestAuthHandler.SchemeName, _ => { });
                    services.AddAuthorization();
                    services.AddControllers(o => o.Filters.Add<GlobalExceptionFilter>())
                        .AddApplicationPart(typeof(CrmInboxController).Assembly);
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
    public async Task A_MessageUnread_MarkRead_ThenNewCustomerMessage()
    {
        var page = await SeedChannelAsync("Msg Page");
        var convId = await SeedConversationAsync(page, "Khách A", lastReadAt: null);
        var t0 = DateTime.UtcNow.AddHours(-3);
        await SeedPageMessageAsync(convId, page, "c1", fromPage: false, sentAt: t0);
        await SeedPageMessageAsync(convId, page, "c2", fromPage: false, sentAt: t0.AddMinutes(1));
        await SeedPageMessageAsync(convId, page, "c3", fromPage: false, sentAt: t0.AddMinutes(2));
        await SeedPageMessageAsync(convId, page, "p1", fromPage: true, sentAt: t0.AddMinutes(3));

        var before = await FindItemAsync(convId);
        Assert.Equal(3, before.GetProperty("unreadCount").GetInt32());

        var readRes = await SendAsync(HttpMethod.Post, $"/api/CrmInbox/message/{convId}/read", "Reviewer");
        Assert.Equal(HttpStatusCode.OK, readRes.StatusCode);

        var afterRead = await FindItemAsync(convId);
        Assert.Equal(0, afterRead.GetProperty("unreadCount").GetInt32());

        await SeedPageMessageAsync(convId, page, "c4", fromPage: false, sentAt: DateTime.UtcNow.AddMinutes(1));
        var afterNew = await FindItemAsync(convId);
        Assert.Equal(1, afterNew.GetProperty("unreadCount").GetInt32());
    }

    [Fact]
    public async Task B_CommentInProgress_NoFakeUnread_CustomerRepliesCount()
    {
        var page = await SeedChannelAsync("Cmt Page");
        var readAt = DateTime.UtcNow.AddHours(-2);
        var rootId = await SeedRootCommentAsync(
            page,
            "Root in progress",
            CommentInboxStatus.InProgress,
            commentedAt: DateTime.UtcNow.AddHours(-5),
            lastReadAt: readAt);

        var zero = await FindItemAsync(rootId);
        Assert.Equal(0, zero.GetProperty("unreadCount").GetInt32());

        await SeedReplyCommentAsync(rootId, page, "r1", fromPage: false, at: readAt.AddMinutes(10));
        await SeedReplyCommentAsync(rootId, page, "r2", fromPage: false, at: readAt.AddMinutes(20));
        await SeedReplyCommentAsync(rootId, page, "deleted", fromPage: false, at: readAt.AddMinutes(30), deleted: true);
        await SeedReplyCommentAsync(rootId, page, "platform-del", fromPage: false, at: readAt.AddMinutes(40), deletedOnPlatform: true);
        await SeedReplyCommentAsync(rootId, page, "page-reply", fromPage: true, at: readAt.AddMinutes(50));

        var two = await FindItemAsync(rootId);
        Assert.Equal(2, two.GetProperty("unreadCount").GetInt32());
    }

    [Fact]
    public async Task C_SyncGraphUnreadCount_DoesNotOverrideReadState()
    {
        var pageId = "page_ext_sync";
        var page = await SeedChannelAsync("Sync Page", externalPageId: pageId, accessToken: "tok");
        var psid = "psid-sync-1";
        var extConv = "t_sync_1";
        var convId = await SeedConversationAsync(
            page,
            "Sync user",
            participantExternalId: psid,
            externalConversationId: extConv,
            lastReadAt: DateTime.UtcNow);
        await SeedPageMessageAsync(
            convId, page, "old", fromPage: false, sentAt: DateTime.UtcNow.AddHours(-5));

        var updated = DateTime.UtcNow.ToString("O");
        _httpHandler.ConversationsJson =
            "{\"data\":[{\"id\":\"" + extConv + "\",\"unread_count\":5,\"message_count\":1,\"snippet\":\"hi\","
            + "\"updated_time\":\"" + updated + "\","
            + "\"participants\":{\"data\":[{\"id\":\"" + psid + "\",\"name\":\"Sync user\"}]},"
            + "\"messages\":{\"data\":[]}}]}";

        await using (var scope = _host.Services.CreateAsyncScope())
        {
            var svc = scope.ServiceProvider.GetRequiredService<PageMessageService>();
            await using var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var channel = await db.SocialChannels.FirstAsync(x => x.Id == page);
            await svc.SyncChannelAsync(channel, 10, CancellationToken.None);
        }

        await using (var db = new AppDbContext(_options))
        {
            var conv = await db.PageConversations.FirstAsync(x => x.Id == convId);
            Assert.NotNull(conv.LastReadAtUtc);
            // Graph unread_count=5 không được ghi đè cache unread khi đã đọc.
            Assert.NotEqual(5, conv.UnreadCount);
        }

        var item = await FindItemAsync(convId);
        Assert.Equal(0, item.GetProperty("unreadCount").GetInt32());
    }

    [Fact]
    public async Task D_ViewerForbidden_AnonUnauthorized_ReadPreservesStatusAssign_PageReplyClears()
    {
        var page = await SeedChannelAsync("Auth Page");
        var assignee = Guid.Parse("11111111-2222-3333-4444-555555555555");
        var convId = await SeedConversationAsync(
            page,
            "Auth user",
            status: MessageInboxStatus.InProgress,
            assignedUserId: assignee,
            lastReadAt: null);
        await SeedPageMessageAsync(convId, page, "c", fromPage: false, sentAt: DateTime.UtcNow.AddMinutes(-30));

        var viewer = await SendAsync(HttpMethod.Post, $"/api/CrmInbox/message/{convId}/read", "Viewer");
        Assert.Equal(HttpStatusCode.Forbidden, viewer.StatusCode);

        var anon = await SendAsync(HttpMethod.Post, $"/api/CrmInbox/message/{convId}/read", role: null);
        Assert.Equal(HttpStatusCode.Unauthorized, anon.StatusCode);

        var ok = await SendAsync(HttpMethod.Post, $"/api/CrmInbox/message/{convId}/read", "Admin");
        Assert.Equal(HttpStatusCode.OK, ok.StatusCode);

        await using (var db = new AppDbContext(_options))
        {
            var conv = await db.PageConversations.FirstAsync(x => x.Id == convId);
            Assert.Equal(MessageInboxStatus.InProgress, conv.InboxStatus);
            Assert.Equal(assignee, conv.AssignedUserId);
            Assert.NotNull(conv.LastReadAtUtc);
            Assert.True(await db.MessageActionLogs.AnyAsync(x =>
                x.PageConversationId == convId && x.ActionType == MessageActionType.MarkRead));
        }

        // Page gửi trả lời → unread=0
        await SeedPageMessageAsync(convId, page, "new-c", fromPage: false, sentAt: DateTime.UtcNow.AddMinutes(-5));
        _httpHandler.NextSendOk = true;
        var send = await SendAsync(
            HttpMethod.Post,
            $"/api/PageMessage/{convId}/send",
            "Admin",
            new { text = "Page reply" });
        Assert.Equal(HttpStatusCode.OK, send.StatusCode);
        var afterReply = await FindItemAsync(convId);
        Assert.Equal(0, afterReply.GetProperty("unreadCount").GetInt32());
    }

    [Fact]
    public async Task E_UnreadOnly_Filter_UsesRealUnreadDefinition()
    {
        var page = await SeedChannelAsync("Filter Page");
        var unreadMsg = await SeedConversationAsync(page, "Unread msg", lastReadAt: null);
        await SeedPageMessageAsync(unreadMsg, page, "u", fromPage: false, sentAt: DateTime.UtcNow.AddHours(-1));

        var readMsg = await SeedConversationAsync(page, "Read msg", lastReadAt: DateTime.UtcNow);
        await SeedPageMessageAsync(readMsg, page, "r", fromPage: false, sentAt: DateTime.UtcNow.AddHours(-2));

        var unreadCmt = await SeedRootCommentAsync(
            page, "Unread cmt", CommentInboxStatus.InProgress,
            commentedAt: DateTime.UtcNow.AddHours(-1), lastReadAt: null);

        var fakeStatusCmt = await SeedRootCommentAsync(
            page, "InProgress but read", CommentInboxStatus.InProgress,
            commentedAt: DateTime.UtcNow.AddHours(-3),
            lastReadAt: DateTime.UtcNow.AddHours(-1));

        var items = await FilterAsync(new { unreadOnly = true });
        var ids = items.Select(x => x.GetProperty("id").GetGuid()).ToHashSet();
        Assert.Contains(unreadMsg, ids);
        Assert.Contains(unreadCmt, ids);
        Assert.DoesNotContain(readMsg, ids);
        Assert.DoesNotContain(fakeStatusCmt, ids);
    }

    private async Task<JsonElement> FindItemAsync(Guid id)
    {
        var items = await FilterAsync(new { });
        var match = items.FirstOrDefault(x => x.GetProperty("id").GetGuid() == id);
        Assert.True(match.ValueKind != JsonValueKind.Undefined, $"Item {id} missing from filter");
        return match;
    }

    private async Task<List<JsonElement>> FilterAsync(object body)
    {
        var res = await SendAsync(HttpMethod.Post, "/api/CrmInbox/filter", "Admin", body);
        Assert.Equal(HttpStatusCode.OK, res.StatusCode);
        using var doc = JsonDocument.Parse(await res.Content.ReadAsStringAsync());
        var data = doc.RootElement.GetProperty("data");
        var items = data.TryGetProperty("items", out var arr) ? arr : data.GetProperty("Items");
        return items.EnumerateArray().Select(x => x.Clone()).ToList();
    }

    private async Task<HttpResponseMessage> SendAsync(
        HttpMethod method, string url, string? role, object? body = null)
    {
        using var req = new HttpRequestMessage(method, url);
        if (role is not null)
        {
            req.Headers.Authorization = new AuthenticationHeaderValue(
                TestAuthHandler.SchemeName,
                $"{role}:{_actorUserId:N}");
        }

        if (body is not null)
            req.Content = JsonContent.Create(body);
        return await _client.SendAsync(req);
    }

    private async Task<Guid> SeedChannelAsync(
        string name,
        string? externalPageId = null,
        string accessToken = "token")
    {
        await using var db = new AppDbContext(_options);
        var id = Guid.NewGuid();
        db.SocialChannels.Add(new SocialChannelModel
        {
            Id = id,
            PageName = name,
            Platform = SocialPlatform.Facebook,
            ChannelType = SocialChannelType.Page,
            ExternalPageId = externalPageId ?? $"ext-{id:N}",
            AccessToken = accessToken,
            IsActive = true,
            CreatedAt = DateTime.UtcNow,
            CreatedBy = "seed"
        });
        await db.SaveChangesAsync();
        return id;
    }

    private async Task<Guid> SeedConversationAsync(
        Guid channelId,
        string participantName,
        DateTime? lastReadAt = null,
        MessageInboxStatus status = MessageInboxStatus.New,
        Guid? assignedUserId = null,
        string? participantExternalId = null,
        string? externalConversationId = null)
    {
        await using var db = new AppDbContext(_options);
        var id = Guid.NewGuid();
        var at = DateTime.UtcNow.AddHours(-4);
        db.PageConversations.Add(new PageConversationModel
        {
            Id = id,
            SocialChannelId = channelId,
            ExternalConversationId = externalConversationId ?? $"t_{id:N}",
            ParticipantExternalId = participantExternalId ?? $"psid-{id:N}",
            ParticipantName = participantName,
            Snippet = participantName,
            LastMessageAt = at,
            LastCustomerMessageAt = DateTime.UtcNow.AddMinutes(-10),
            LastReadAtUtc = lastReadAt,
            InboxStatus = status,
            AssignedUserId = assignedUserId,
            AssignedTo = assignedUserId?.ToString("N"),
            UnreadCount = 0,
            CreatedAt = at,
            CreatedBy = "seed"
        });
        await db.SaveChangesAsync();
        return id;
    }

    private async Task SeedPageMessageAsync(
        Guid conversationId,
        Guid channelId,
        string text,
        bool fromPage,
        DateTime sentAt)
    {
        await using var db = new AppDbContext(_options);
        db.PageMessages.Add(new PageMessageModel
        {
            Id = Guid.NewGuid(),
            PageConversationId = conversationId,
            SocialChannelId = channelId,
            ExternalMessageId = $"m-{Guid.NewGuid():N}",
            Text = text,
            IsFromPage = fromPage,
            SentAt = sentAt,
            CreatedAt = sentAt,
            CreatedBy = "seed"
        });
        await db.SaveChangesAsync();
    }

    private async Task<Guid> SeedRootCommentAsync(
        Guid channelId,
        string message,
        CommentInboxStatus status,
        DateTime commentedAt,
        DateTime? lastReadAt)
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
        db.SocialComments.Add(new SocialCommentModel
        {
            Id = id,
            SocialChannelId = channelId,
            SocialPostId = postId,
            Platform = SocialPlatform.Facebook,
            ExternalCommentId = $"cmt-{id:N}",
            AuthorExternalId = "author",
            AuthorName = "Commenter",
            Message = message,
            CommentedAt = commentedAt,
            InboxStatus = status,
            LastReadAtUtc = lastReadAt,
            CreatedAt = commentedAt,
            CreatedBy = "seed"
        });
        await db.SaveChangesAsync();
        return id;
    }

    private async Task SeedReplyCommentAsync(
        Guid rootId,
        Guid channelId,
        string message,
        bool fromPage,
        DateTime at,
        bool deleted = false,
        bool deletedOnPlatform = false)
    {
        await using var db = new AppDbContext(_options);
        var root = await db.SocialComments.FirstAsync(x => x.Id == rootId);
        db.SocialComments.Add(new SocialCommentModel
        {
            Id = Guid.NewGuid(),
            SocialChannelId = channelId,
            SocialPostId = root.SocialPostId,
            Platform = SocialPlatform.Facebook,
            ExternalCommentId = $"reply-{Guid.NewGuid():N}",
            ParentCommentId = rootId,
            ParentExternalCommentId = root.ExternalCommentId,
            AuthorExternalId = fromPage ? "page" : "author",
            AuthorName = fromPage ? "Page" : "Commenter",
            Message = message,
            CommentedAt = at,
            IsFromPage = fromPage,
            IsDeleted = deleted,
            IsDeletedOnPlatform = deletedOnPlatform,
            InboxStatus = CommentInboxStatus.New,
            CreatedAt = at,
            CreatedBy = "seed"
        });
        await db.SaveChangesAsync();
    }

    private sealed class GraphHttpHandler : HttpMessageHandler
    {
        public string? ConversationsJson { get; set; }
        public bool NextSendOk { get; set; } = true;

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var path = request.RequestUri?.AbsolutePath ?? "";
            if (path.Contains("/conversations", StringComparison.Ordinal)
                && request.Method == HttpMethod.Get
                && ConversationsJson is not null)
            {
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(ConversationsJson, Encoding.UTF8, "application/json")
                });
            }

            if (path.Contains("/messages", StringComparison.Ordinal)
                && request.Method == HttpMethod.Post
                && NextSendOk)
            {
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent("""{"message_id":"m_reply","recipient_id":"1"}""")
                });
            }

            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("""{"data":[]}""")
            });
        }
    }

    private sealed class TestAuthHandler : AuthenticationHandler<AuthenticationSchemeOptions>
    {
        public const string SchemeName = "TestCrmInboxRead";

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
            return Task.FromResult(
                AuthenticateResult.Success(new AuthenticationTicket(
                    new ClaimsPrincipal(new ClaimsIdentity(claims, SchemeName)), SchemeName)));
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
