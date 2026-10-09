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
/// AC 2e8f4088 (a)-(d): registry nguồn, filter source, GET sources.
/// Revert-to-prove: map messenger sang cả bình luận → (a) đỏ; bỏ lọc kênh hoạt động → (c) đỏ.
/// </summary>
public sealed class CrmInboxSourceTests : IAsyncLifetime
{
    private readonly SqliteConnection _connection;
    private readonly DbContextOptions<AppDbContext> _options;
    private readonly GraphHttpHandler _httpHandler = new();
    private readonly IHost _host;
    private readonly HttpClient _client;
    private readonly TestRegistry _registry = new();
    private readonly Guid _actorUserId = Guid.Parse("aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee");

    public CrmInboxSourceTests()
    {
        _connection = new SqliteConnection("DataSource=:memory:");
        _connection.Open();
        _options = new DbContextOptionsBuilder<AppDbContext>().UseSqlite(_connection).Options;
        using (var seed = new AppDbContext(_options))
            seed.Database.EnsureCreated();

        var options = _options;
        var handler = _httpHandler;
        var registry = _registry;
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
                    services.AddSingleton<IInboxSourceRegistry>(registry);
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

    // (a) filter theo source ----------------------------------------------------
    [Fact]
    public async Task A_FilterBySource_MessengerFacebookInstagram_NoSource_AndUnknown400()
    {
        var (fbMsgs, fbCmt, igMsg, igCmt) = await SeedBaseAsync();

        Assert.Equal(Sorted(fbMsgs), await FilterIdsAsync(new { source = "messenger" }));
        Assert.Equal(Sorted([fbCmt]), await FilterIdsAsync(new { source = "facebook" }));
        Assert.Equal(Sorted([igMsg, igCmt]), await FilterIdsAsync(new { source = "instagram" }));
        Assert.Equal(Sorted([.. fbMsgs, fbCmt, igMsg, igCmt]), await FilterIdsAsync(new { }));

        var bad = await SendAsync(HttpMethod.Post, "/api/CrmInbox/filter", "Admin", new { source = "abc" });
        Assert.Equal(HttpStatusCode.BadRequest, bad.StatusCode);
    }

    // (b) kết hợp với bộ lọc khác ----------------------------------------------
    [Fact]
    public async Task B_SourceCombinedWithChannelStatusUnreadAndKind()
    {
        var (fbMsgs, fbCmt, igMsg, _) = await SeedBaseAsync();
        var fbChannel2 = await SeedChannelAsync("FB 2", SocialPlatform.Facebook);
        var otherChannelConv = await SeedConversationAsync(fbChannel2, "Khác Page");

        // + socialChannelId
        Assert.Equal([otherChannelConv], await FilterIdsAsync(new { source = "messenger", socialChannelId = fbChannel2 }));
        // + status (đặt 1 hội thoại FB sang InProgress)
        await using (var db = new AppDbContext(_options))
        {
            (await db.PageConversations.SingleAsync(x => x.Id == fbMsgs[0])).InboxStatus = MessageInboxStatus.InProgress;
            await db.SaveChangesAsync();
        }
        Assert.Equal([fbMsgs[0]], await FilterIdsAsync(new { source = "messenger", status = (int)MessageInboxStatus.InProgress }));
        // + unreadOnly: đã đọc fbMsgs[1] thì không còn trong kết quả
        await using (var db = new AppDbContext(_options))
        {
            (await db.PageConversations.SingleAsync(x => x.Id == fbMsgs[1])).LastReadAtUtc = DateTime.UtcNow;
            await db.SaveChangesAsync();
        }
        var unread = await FilterIdsAsync(new { source = "messenger", unreadOnly = true });
        Assert.DoesNotContain(fbMsgs[1], unread);
        Assert.Contains(fbMsgs[0], unread);
        // Kind ∩ source: messenger + kind=comment → rỗng; facebook + kind=comment → bình luận FB
        Assert.Empty(await FilterIdsAsync(new { source = "messenger", kind = (int)CrmInboxItemKind.Comment }));
        Assert.Equal([fbCmt], await FilterIdsAsync(new { source = "facebook", kind = (int)CrmInboxItemKind.Comment }));
        Assert.Equal([igMsg], await FilterIdsAsync(new { source = "instagram", kind = (int)CrmInboxItemKind.Message }));
    }

    // (c) GET sources ------------------------------------------------------------
    [Fact]
    public async Task C_Sources_OnlyActiveChannelPlatforms_UnreadMatches_AndRoles()
    {
        var (fbMsgs, _, igMsg, _) = await SeedBaseAsync();
        await using (var db = new AppDbContext(_options))
        {
            // fbMsgs[1] đã đọc → chỉ fbMsgs[0] còn chưa đọc.
            (await db.PageConversations.SingleAsync(x => x.Id == fbMsgs[1])).LastReadAtUtc = DateTime.UtcNow;
            await db.SaveChangesAsync();
        }

        var res = await SendAsync(HttpMethod.Get, "/api/CrmInbox/sources", "Viewer");
        Assert.Equal(HttpStatusCode.OK, res.StatusCode);
        var sources = await ReadSourcesAsync(res);
        Assert.Equal(["messenger", "facebook", "instagram"], sources.Select(s => s.Key));
        var unread = sources.ToDictionary(s => s.Key, s => s.Unread);
        // Unread khớp bộ lọc unreadOnly của cùng nguồn.
        foreach (var key in unread.Keys)
            Assert.Equal((await FilterIdsAsync(new { source = key, unreadOnly = true })).Count, unread[key]);
        Assert.Equal(1, unread["messenger"]);

        // Tắt kênh IG → không còn instagram.
        await using (var db = new AppDbContext(_options))
        {
            foreach (var c in db.SocialChannels.Where(x => x.Platform == SocialPlatform.Instagram))
                c.IsActive = false;
            await db.SaveChangesAsync();
        }
        var after = await ReadSourcesAsync(await SendAsync(HttpMethod.Get, "/api/CrmInbox/sources", "Reviewer"));
        Assert.DoesNotContain(after, s => s.Key == "instagram");

        Assert.Equal(HttpStatusCode.Unauthorized, (await SendAsync(HttpMethod.Get, "/api/CrmInbox/sources", null)).StatusCode);
        _ = igMsg;
    }

    // (d) registry: thêm nguồn giả không cần sửa CrmInboxService --------------------
    [Fact]
    public async Task D_RegistryEntries_HaveValidPlatform_AndFakeSourceWorksWithoutServiceChanges()
    {
        foreach (var s in new DefaultInboxSourceRegistry().All)
        {
            Assert.True(Enum.IsDefined(s.Platform), s.Key);
            Assert.False(string.IsNullOrWhiteSpace(s.Label));
            Assert.True(s.IncludesMessages || s.IncludesComments, s.Key);
        }

        const SocialPlatform fake = (SocialPlatform)99;
        _registry.Extra = new InboxSource("zalo-test", "Zalo test", fake, true, false, 9);
        var channel = await SeedChannelAsync("Zalo OA", fake);
        var conv = await SeedConversationAsync(channel, "Khách Zalo");

        Assert.Equal([conv], await FilterIdsAsync(new { source = "zalo-test" }));
        var sources = await ReadSourcesAsync(await SendAsync(HttpMethod.Get, "/api/CrmInbox/sources", "Admin"));
        Assert.Equal(["zalo-test"], sources.Select(s => s.Key));
        Assert.Equal(1, sources.Single().Unread);
    }

    // ── helpers ──────────────────────────────────────────────────────────────
    private static List<Guid> Sorted(IEnumerable<Guid> ids) => ids.OrderBy(x => x).ToList();

    private async Task<List<Guid>> FilterIdsAsync(object body)
    {
        var res = await SendAsync(HttpMethod.Post, "/api/CrmInbox/filter", "Admin",
            new Dictionary<string, object?>(ToDict(body)) { ["index"] = 1, ["size"] = 100 });
        Assert.True(res.StatusCode == HttpStatusCode.OK, await res.Content.ReadAsStringAsync());
        var json = await res.Content.ReadFromJsonAsync<JsonElement>();
        return json.GetProperty("data").GetProperty("items").EnumerateArray()
            .Select(x => x.GetProperty("id").GetGuid()).OrderBy(x => x).ToList();
    }

    private static Dictionary<string, object?> ToDict(object body)
        => body.GetType().GetProperties().ToDictionary(p => p.Name, p => p.GetValue(body));

    private sealed record SourceRow(string Key, int Unread);

    private static async Task<List<SourceRow>> ReadSourcesAsync(HttpResponseMessage res)
    {
        Assert.Equal(HttpStatusCode.OK, res.StatusCode);
        var json = await res.Content.ReadFromJsonAsync<JsonElement>();
        return json.GetProperty("data").EnumerateArray()
            .Select(x => new SourceRow(x.GetProperty("key").GetString()!, x.GetProperty("unread").GetInt32())).ToList();
    }

    /// <summary>FB: 2 tin nhắn + 1 bình luận; IG: 1 tin nhắn + 1 bình luận.</summary>
    private async Task<(List<Guid> FbMsgs, Guid FbCmt, Guid IgMsg, Guid IgCmt)> SeedBaseAsync()
    {
        var fb = await SeedChannelAsync("FB Page", SocialPlatform.Facebook);
        var ig = await SeedChannelAsync("IG Account", SocialPlatform.Instagram);
        var m1 = await SeedConversationAsync(fb, "FB 1");
        var m2 = await SeedConversationAsync(fb, "FB 2");
        var c1 = await SeedRootCommentAsync(fb, "cmt fb", CommentInboxStatus.New, DateTime.UtcNow.AddMinutes(-5), null);
        var im = await SeedConversationAsync(ig, "IG 1");
        var ic = await SeedRootCommentAsync(ig, "cmt ig", CommentInboxStatus.New, DateTime.UtcNow.AddMinutes(-6), null);
        return ([m1, m2], c1, im, ic);
    }

    private async Task<Guid> SeedChannelAsync(string name, SocialPlatform platform)
    {
        await using var db = new AppDbContext(_options);
        var id = Guid.NewGuid();
        db.SocialChannels.Add(new SocialChannelModel
        {
            Id = id,
            PageName = name,
            Platform = platform,
            ChannelType = SocialChannelType.Page,
            ExternalPageId = $"ext-{id:N}",
            AccessToken = "token",
            IsActive = true,
            CreatedAt = DateTime.UtcNow,
            CreatedBy = "seed"
        });
        await db.SaveChangesAsync();
        return id;
    }

    private async Task<Guid> SeedConversationAsync(Guid channelId, string participantName)
    {
        await using var db = new AppDbContext(_options);
        var id = Guid.NewGuid();
        var at = DateTime.UtcNow.AddMinutes(-20);
        db.PageConversations.Add(new PageConversationModel
        {
            Id = id,
            SocialChannelId = channelId,
            ExternalConversationId = $"t_{id:N}",
            ParticipantExternalId = $"psid-{id:N}",
            ParticipantName = participantName,
            Snippet = participantName,
            LastMessageAt = at,
            LastCustomerMessageAt = at,
            InboxStatus = MessageInboxStatus.New,
            CreatedAt = at,
            CreatedBy = "seed"
        });
        db.PageMessages.Add(new PageMessageModel
        {
            Id = Guid.NewGuid(),
            PageConversationId = id,
            SocialChannelId = channelId,
            ExternalMessageId = $"m-{Guid.NewGuid():N}",
            Text = "hi",
            IsFromPage = false,
            SentAt = at,
            CreatedAt = at,
            CreatedBy = "seed"
        });
        await db.SaveChangesAsync();
        return id;
    }

    private sealed class TestRegistry : IInboxSourceRegistry
    {
        private readonly DefaultInboxSourceRegistry _default = new();
        public InboxSource? Extra { get; set; }

        public IReadOnlyList<InboxSource> All
            => Extra is null ? _default.All : [.. _default.All, Extra];

        public InboxSource? Find(string? key)
            => All.FirstOrDefault(x => string.Equals(x.Key, key, StringComparison.OrdinalIgnoreCase));
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
        public const string SchemeName = "TestCrmInboxSource";

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
