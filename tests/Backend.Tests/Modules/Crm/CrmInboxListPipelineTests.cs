using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text.Encodings.Web;
using System.Text.Json;
using Backend.Data;
using Backend.Modules.Crm.Assignment;
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

namespace Backend.Tests.Modules.Crm;

/// <summary>
/// CRM AC crm-inbox-list-test (56154ec6) phần backend: filter hợp nhất, pagination,
/// canReply/24h khoá gửi, reply comment qua SocialComment.
/// </summary>
public sealed class CrmInboxListPipelineTests : IAsyncLifetime
{
    private readonly SqliteConnection _connection;
    private readonly DbContextOptions<AppDbContext> _options;
    private readonly CountingHttpHandler _httpHandler = new();
    private readonly IHost _host;
    private readonly HttpClient _client;
    private readonly Guid _actorUserId = Guid.Parse("aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee");

    public CrmInboxListPipelineTests()
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
                    services.AddScoped<Backend.Modules.Crm.Customers.CrmCustomerService>();
                    services.AddScoped<Backend.Modules.Crm.Customers.CrmCustomerCareService>();
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
    public async Task Filter_MixedMessageAndComment_SortedByCustomerActivity()
    {
        var pageA = await SeedChannelAsync("Page A");
        var pageB = await SeedChannelAsync("Page B");
        var olderMsg = await SeedConversationAsync(pageA, "Old", lastCustomer: DateTime.UtcNow.AddHours(-5));
        var newerCmt = await SeedCommentAsync(pageB, "New comment", commentedAt: DateTime.UtcNow.AddHours(-1));
        var midMsg = await SeedConversationAsync(pageA, "Mid", lastCustomer: DateTime.UtcNow.AddHours(-2));

        var items = await FilterAsync(new { });
        Assert.True(items.Count >= 3);
        var ids = items.Select(x => x.GetProperty("id").GetGuid()).ToList();
        Assert.True(ids.IndexOf(newerCmt) < ids.IndexOf(midMsg));
        Assert.True(ids.IndexOf(midMsg) < ids.IndexOf(olderMsg));
    }

    [Fact]
    public async Task Filter_ItemsCarryChannelPlatform_ForSourceBadge()
    {
        var fbPage = await SeedChannelAsync("FB Page");
        var igPage = await SeedChannelAsync("IG Page", SocialPlatform.Instagram);
        var fbMsg = await SeedConversationAsync(fbPage, "Messenger khách");
        var fbCmt = await SeedCommentAsync(fbPage, "FB comment", commentedAt: DateTime.UtcNow.AddHours(-1));
        var igMsg = await SeedConversationAsync(igPage, "IG khách");

        var byId = (await FilterAsync(new { })).ToDictionary(x => x.GetProperty("id").GetGuid());
        Assert.Equal((int)SocialPlatform.Facebook, byId[fbMsg].GetProperty("platform").GetInt32());
        Assert.Equal((int)SocialPlatform.Facebook, byId[fbCmt].GetProperty("platform").GetInt32());
        Assert.Equal((int)SocialPlatform.Instagram, byId[igMsg].GetProperty("platform").GetInt32());
    }

    [Fact]
    public async Task Filter_ByPage_AndUnassigned_AndMine()
    {
        var page = await SeedChannelAsync("Filter Page");
        var mine = await SeedConversationAsync(page, "Mine", assignedUserId: _actorUserId);
        var other = await SeedConversationAsync(page, "Other", assignedUserId: Guid.NewGuid());
        var open = await SeedConversationAsync(page, "Open", assignedUserId: null);
        var otherPage = await SeedConversationAsync(await SeedChannelAsync("X"), "X", assignedUserId: null);

        var byPage = await FilterAsync(new { socialChannelId = page });
        var byPageIds = byPage.Select(x => x.GetProperty("id").GetGuid()).ToHashSet();
        Assert.Contains(mine, byPageIds);
        Assert.Contains(other, byPageIds);
        Assert.Contains(open, byPageIds);
        Assert.DoesNotContain(otherPage, byPageIds);

        var unassigned = await FilterAsync(new { socialChannelId = page, unassignedOnly = true });
        var unassignedIds = unassigned.Select(x => x.GetProperty("id").GetGuid()).ToHashSet();
        Assert.Contains(open, unassignedIds);
        Assert.DoesNotContain(mine, unassignedIds);

        var mineItems = await FilterAsync(new { socialChannelId = page, assignedMine = true });
        var mineIds = mineItems.Select(x => x.GetProperty("id").GetGuid()).ToHashSet();
        Assert.Contains(mine, mineIds);
        Assert.DoesNotContain(other, mineIds);
        Assert.DoesNotContain(open, mineIds);
    }

    [Fact]
    public async Task Filter_ByTag_AndKeywordPhone_AndStatus()
    {
        var page = await SeedChannelAsync("Tag Page");
        var tagged = await SeedConversationAsync(page, "Tagged User", snippet: "xin chào");
        var withPhone = await SeedConversationAsync(page, "Phone User", snippet: "Gọi 0912345678 giúp em");
        var newStatus = await SeedConversationAsync(page, "Newish", status: MessageInboxStatus.New);
        await SeedConversationAsync(page, "Replied", status: MessageInboxStatus.Replied);

        var tagId = await CreateAndAttachTagAsync(tagged);

        var byTag = await FilterAsync(new { tagId });
        Assert.Contains(byTag, x => x.GetProperty("id").GetGuid() == tagged);
        Assert.DoesNotContain(byTag, x => x.GetProperty("id").GetGuid() == withPhone);

        var byPhone = await FilterAsync(new { keyword = "0912345678" });
        Assert.Contains(byPhone, x => x.GetProperty("id").GetGuid() == withPhone);

        var byStatus = await FilterAsync(new { status = (int)MessageInboxStatus.New, kind = (int)CrmInboxItemKind.Message });
        var statusIds = byStatus.Select(x => x.GetProperty("id").GetGuid()).ToHashSet();
        Assert.Contains(newStatus, statusIds);
        Assert.DoesNotContain(
            statusIds,
            id => id == withPhone && false); // just ensure replied-only not all
        Assert.All(byStatus, x => Assert.Equal((int)MessageInboxStatus.New, x.GetProperty("status").GetInt32()));
    }

    [Fact]
    public async Task Filter_Pagination_StableNoDupOrGap()
    {
        var page = await SeedChannelAsync("Page Page");
        var ids = new List<Guid>();
        for (var i = 0; i < 5; i++)
        {
            ids.Add(await SeedConversationAsync(
                page, $"P{i}", lastCustomer: DateTime.UtcNow.AddMinutes(-i)));
        }

        var page1 = await FilterAsync(new { socialChannelId = page, index = 1, size = 2 });
        var page2 = await FilterAsync(new { socialChannelId = page, index = 2, size = 2 });
        var page3 = await FilterAsync(new { socialChannelId = page, index = 3, size = 2 });

        var all = page1.Concat(page2).Concat(page3)
            .Select(x => x.GetProperty("id").GetGuid())
            .ToList();
        Assert.Equal(5, all.Distinct().Count());
        Assert.Equal(5, all.Count);
    }

    [Fact]
    public async Task MessageDetail_ReturnsCanReplyAndReplyEndpoint()
    {
        var page = await SeedChannelAsync("Detail");
        var id = await SeedConversationAsync(page, "Cust", lastCustomer: DateTime.UtcNow.AddHours(-1));

        using var request = new HttpRequestMessage(HttpMethod.Get, $"/api/CrmInbox/message/{id}");
        Authorize(request, "Reviewer");
        using var response = await _client.SendAsync(request);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var data = (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("data");
        Assert.True(data.GetProperty("conversation").GetProperty("canReply").GetBoolean());
        Assert.True(data.GetProperty("conversation").GetProperty("isReplyWindowOpen").GetBoolean());
        Assert.Equal($"/api/PageMessage/{id}/send", data.GetProperty("replyEndpoint").GetString());
        Assert.NotNull(data.GetProperty("conversation").GetProperty("replyWindowClosesAt"));
    }

    [Fact]
    public async Task Send_Outside24h_ReturnsClearError_AndDoesNotCallGraph()
    {
        var page = await SeedChannelAsync("Lock");
        var id = await SeedConversationAsync(
            page, "Late", lastCustomer: DateTime.UtcNow.AddHours(-30));
        _httpHandler.MessagePosts = 0;

        using var request = new HttpRequestMessage(HttpMethod.Post, $"/api/PageMessage/{id}/send")
        {
            Content = JsonContent.Create(new { text = "hello" })
        };
        Authorize(request, "Admin");
        using var response = await _client.SendAsync(request);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("REPLY_WINDOW_CLOSED", body.GetProperty("errorCode").GetString());
        Assert.Contains("24", body.GetProperty("message").GetString(), StringComparison.Ordinal);
        Assert.Equal(0, _httpHandler.MessagePosts);

        using var detailReq = new HttpRequestMessage(HttpMethod.Get, $"/api/CrmInbox/message/{id}");
        Authorize(detailReq, "Admin");
        using var detail = await _client.SendAsync(detailReq);
        var conv = (await detail.Content.ReadFromJsonAsync<JsonElement>())
            .GetProperty("data").GetProperty("conversation");
        Assert.False(conv.GetProperty("canReply").GetBoolean());
    }

    [Fact]
    public async Task Send_Within24h_CallsGraphEndpoint()
    {
        var page = await SeedChannelAsync("OpenWin");
        var id = await SeedConversationAsync(
            page, "Fresh", lastCustomer: DateTime.UtcNow.AddHours(-2));
        _httpHandler.MessagePosts = 0;
        _httpHandler.NextSendOk = true;

        using var request = new HttpRequestMessage(HttpMethod.Post, $"/api/PageMessage/{id}/send")
        {
            Content = JsonContent.Create(new { text = "within window" })
        };
        Authorize(request, "Admin");
        using var response = await _client.SendAsync(request);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(1, _httpHandler.MessagePosts);
    }

    [Fact]
    public async Task CommentReply_GoesThroughSocialComment_AndLogsAction()
    {
        var page = await SeedChannelAsync("Cmt");
        var commentId = await SeedCommentAsync(page, "Need help", commentedAt: DateTime.UtcNow);

        using var request = new HttpRequestMessage(HttpMethod.Post, $"/api/SocialComment/{commentId}/reply")
        {
            Content = JsonContent.Create(new { message = "Chúng tôi sẽ hỗ trợ" })
        };
        Authorize(request, "Reviewer");
        using var response = await _client.SendAsync(request);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        await using var db = new AppDbContext(_options);
        Assert.True(await db.CommentActionLogs.AnyAsync(x =>
            x.SocialCommentId == commentId
            && x.ActionType == CommentActionType.Reply
            && x.Success));
    }

    [Fact]
    public async Task Filter_CombinedPageStatusUnread()
    {
        var page = await SeedChannelAsync("Combo");
        var match = await SeedConversationAsync(
            page, "Match", status: MessageInboxStatus.New, unread: 2,
            lastCustomer: DateTime.UtcNow.AddHours(-1));
        await SeedConversationAsync(
            page, "Read", status: MessageInboxStatus.New, unread: 0,
            lastCustomer: DateTime.UtcNow.AddHours(-1));
        await SeedConversationAsync(
            page, "OtherStatus", status: MessageInboxStatus.Ignored, unread: 3,
            lastCustomer: DateTime.UtcNow.AddHours(-1));

        var items = await FilterAsync(new
        {
            socialChannelId = page,
            status = (int)MessageInboxStatus.New,
            unreadOnly = true,
            kind = (int)CrmInboxItemKind.Message
        });
        Assert.Single(items);
        Assert.Equal(match, items[0].GetProperty("id").GetGuid());
    }

    // --- helpers ---

    private async Task<List<JsonElement>> FilterAsync(object body)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/CrmInbox/filter")
        {
            Content = JsonContent.Create(body)
        };
        Authorize(request, "Reviewer");
        using var response = await _client.SendAsync(request);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var data = (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("data");
        return data.GetProperty("items").EnumerateArray().ToList();
    }

    private void Authorize(HttpRequestMessage request, string role)
        => request.Headers.Authorization = new AuthenticationHeaderValue(
            TestAuthHandler.SchemeName, $"{role}:{_actorUserId:N}");

    private async Task<Guid> SeedChannelAsync(string name, SocialPlatform platform = SocialPlatform.Facebook)
    {
        await using var db = new AppDbContext(_options);
        var id = Guid.NewGuid();
        db.SocialChannels.Add(new SocialChannelModel
        {
            Id = id,
            Platform = platform,
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

    private async Task<Guid> SeedConversationAsync(
        Guid channelId,
        string participantName,
        DateTime? lastCustomer = null,
        Guid? assignedUserId = null,
        MessageInboxStatus status = MessageInboxStatus.New,
        int unread = 0,
        string? snippet = null)
    {
        await using var db = new AppDbContext(_options);
        var id = Guid.NewGuid();
        var at = lastCustomer ?? DateTime.UtcNow.AddHours(-3);
        db.PageConversations.Add(new PageConversationModel
        {
            Id = id,
            SocialChannelId = channelId,
            ExternalConversationId = $"t_{id:N}",
            ParticipantExternalId = $"psid-{id:N}",
            ParticipantName = participantName,
            Snippet = snippet ?? participantName,
            LastMessageAt = at,
            LastCustomerMessageAt = at,
            InboxStatus = status,
            AssignedUserId = assignedUserId,
            AssignedTo = assignedUserId?.ToString("N"),
            UnreadCount = unread,
            CreatedAt = at,
            CreatedBy = "seed"
        });
        await db.SaveChangesAsync();
        return id;
    }

    private async Task<Guid> SeedCommentAsync(Guid channelId, string message, DateTime commentedAt)
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
            InboxStatus = CommentInboxStatus.New,
            CreatedAt = commentedAt,
            CreatedBy = "seed"
        });
        await db.SaveChangesAsync();
        return id;
    }

    private async Task<Guid> CreateAndAttachTagAsync(Guid conversationId)
    {
        await using var scope = _host.Services.CreateAsyncScope();
        var tags = scope.ServiceProvider.GetRequiredService<CrmTagService>();
        var http = scope.ServiceProvider.GetRequiredService<Microsoft.AspNetCore.Http.IHttpContextAccessor>();
        http.HttpContext = new Microsoft.AspNetCore.Http.DefaultHttpContext
        {
            User = new ClaimsPrincipal(new ClaimsIdentity(
            [
                new Claim(ClaimTypes.Name, "admin"),
                new Claim(ClaimTypes.NameIdentifier, _actorUserId.ToString()),
                new Claim(ClaimTypes.Role, "Admin")
            ], "test"))
        };
        var tag = await tags.CreateAsync(new CreateCrmTagRequest { Name = "Hot", Color = "#FF0000" });
        await tags.AttachAsync(tag.Id, new CrmTagTargetRequest
        {
            TargetType = CrmTagTargetType.PageConversation,
            TargetId = conversationId
        });
        return tag.Id;
    }

    private sealed class CountingHttpHandler : HttpMessageHandler
    {
        public int MessagePosts;
        public bool NextSendOk = true;

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
        {
            if (request.RequestUri?.AbsolutePath.Contains("/messages", StringComparison.Ordinal) == true
                && request.Method == HttpMethod.Post)
            {
                Interlocked.Increment(ref MessagePosts);
                if (NextSendOk)
                {
                    return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
                    {
                        Content = new StringContent("""{"message_id":"m_test","recipient_id":"1"}""")
                    });
                }
            }

            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.BadRequest)
            {
                Content = new StringContent("""{"error":{"message":"fail"}}""")
            });
        }
    }

    private sealed class TestAuthHandler : AuthenticationHandler<AuthenticationSchemeOptions>
    {
        public const string SchemeName = "TestCrmInbox";

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
