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
/// AC inbox-customer-panel-test (541307bf) — profile stats, media, activities,
/// other conversations same-page only, 404 ngoài phạm vi, revert-to-prove (d).
/// </summary>
public sealed class InboxCustomerPanelPipelineTests : IAsyncLifetime
{
    private readonly SqliteConnection _connection;
    private readonly DbContextOptions<AppDbContext> _options;
    private readonly IHost _host;
    private readonly HttpClient _client;
    private readonly Guid _actorUserId = Guid.Parse("aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee");

    public InboxCustomerPanelPipelineTests()
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
    public async Task Profile_Message_StatsMediaActivitiesAndWindow()
    {
        var page = await SeedChannelAsync("Page Stats");
        var t0 = DateTime.UtcNow.AddHours(-5);
        var t1 = DateTime.UtcNow.AddHours(-2);
        var t2 = DateTime.UtcNow.AddHours(-1);
        var convId = await SeedConversationAsync(page, "Cust", "psid-same", t2, note: "ghi chú nội bộ");

        await SeedPageMessageAsync(convId, page, "hello", isFromPage: false, sentAt: t0,
            attachmentsJson: """[{"type":"image","url":"https://cdn.example/a.jpg"}]""");
        await SeedPageMessageAsync(convId, page, "hi back", isFromPage: true, sentAt: t1);
        await SeedPageMessageAsync(convId, page, "more", isFromPage: false, sentAt: t2,
            attachmentsJson: """[{"type":"video","payload":{"url":"https://cdn.example/v.mp4"}}]""");

        await using (var db = new AppDbContext(_options))
        {
            db.MessageActionLogs.Add(new MessageActionLogModel
            {
                Id = Guid.NewGuid(),
                PageConversationId = convId,
                ActionType = MessageActionType.Assign,
                ActorUserName = "admin",
                PayloadJson = "staff",
                Success = true,
                CreatedAt = t1,
                CreatedBy = "seed"
            });
            db.MessageActionLogs.Add(new MessageActionLogModel
            {
                Id = Guid.NewGuid(),
                PageConversationId = convId,
                ActionType = MessageActionType.SetStatus,
                ActorUserName = "admin",
                PayloadJson = "InProgress",
                Success = true,
                CreatedAt = t2,
                CreatedBy = "seed"
            });
            await db.SaveChangesAsync();
        }

        var data = await GetProfileAsync("message", convId);
        Assert.Equal("message", data.GetProperty("kind").GetString());
        Assert.Equal("Cust", data.GetProperty("participantName").GetString());
        Assert.Equal("ghi chú nội bộ", data.GetProperty("internalNote").GetString());
        Assert.Equal("Page Stats", data.GetProperty("channelName").GetString());

        var stats = data.GetProperty("stats");
        Assert.Equal(2, stats.GetProperty("customerCount").GetInt32());
        Assert.Equal(1, stats.GetProperty("pageCount").GetInt32());
        Assert.True(data.GetProperty("isReplyWindowOpen").GetBoolean());

        var media = data.GetProperty("media").EnumerateArray().ToList();
        Assert.Equal(2, media.Count);
        Assert.Contains(media, m => m.GetProperty("url").GetString() == "https://cdn.example/a.jpg");
        Assert.Contains(media, m => m.GetProperty("url").GetString() == "https://cdn.example/v.mp4");

        var activities = data.GetProperty("activities").EnumerateArray().ToList();
        Assert.Equal(2, activities.Count);
        Assert.True(activities[0].GetProperty("createdAt").GetDateTime()
            >= activities[1].GetProperty("createdAt").GetDateTime());
    }

    [Fact]
    public async Task Profile_OtherConversations_SamePageOnly_NoCrossKind()
    {
        var pageA = await SeedChannelAsync("Page A");
        var pageB = await SeedChannelAsync("Page B");
        const string psid = "psid-shared";

        var main = await SeedConversationAsync(pageA, "Main", psid, DateTime.UtcNow.AddHours(-1));
        var samePage = await SeedConversationAsync(pageA, "Other same", psid, DateTime.UtcNow.AddHours(-2));
        var otherPage = await SeedConversationAsync(pageB, "Other page", psid, DateTime.UtcNow.AddHours(-3));
        var otherPerson = await SeedConversationAsync(pageA, "Someone", "psid-other", DateTime.UtcNow);

        // Comment cùng author trên cùng page — không được trộn vào otherConversations của message.
        await SeedCommentAsync(pageA, "cmt", DateTime.UtcNow, authorId: psid);

        var data = await GetProfileAsync("message", main);
        var others = data.GetProperty("otherConversations").EnumerateArray().ToList();
        var otherIds = others.Select(x => x.GetProperty("id").GetGuid()).ToHashSet();

        Assert.Contains(samePage, otherIds);
        Assert.DoesNotContain(otherPage, otherIds);
        Assert.DoesNotContain(otherPerson, otherIds);
        Assert.DoesNotContain(main, otherIds);
        Assert.All(others, x => Assert.Equal("message", x.GetProperty("kind").GetString()));
    }

    /// <summary>
    /// Revert-to-prove (d): query thô theo participant không lọc SocialChannelId
    /// sẽ kéo hội thoại page khác; API profile thì không.
    /// </summary>
    [Fact]
    public async Task Profile_RevertToProve_D_WithoutSamePageFilter_WouldLeakOtherPage()
    {
        var pageA = await SeedChannelAsync("A");
        var pageB = await SeedChannelAsync("B");
        const string psid = "psid-x";
        var main = await SeedConversationAsync(pageA, "Main", psid, DateTime.UtcNow);
        var otherPage = await SeedConversationAsync(pageB, "Leak", psid, DateTime.UtcNow.AddHours(-1));

        await using var db = new AppDbContext(_options);
        var leaked = await db.PageConversations.AsNoTracking()
            .Where(x => !x.IsDeleted && x.ParticipantExternalId == psid && x.Id != main)
            .Select(x => x.Id)
            .ToListAsync();
        Assert.Contains(otherPage, leaked);

        var data = await GetProfileAsync("message", main);
        var others = data.GetProperty("otherConversations").EnumerateArray()
            .Select(x => x.GetProperty("id").GetGuid()).ToHashSet();
        Assert.DoesNotContain(otherPage, others);
    }

    [Fact]
    public async Task Profile_Comment_CountsAndSameAuthorSamePage()
    {
        var pageA = await SeedChannelAsync("CA");
        var pageB = await SeedChannelAsync("CB");
        const string author = "author-1";
        var root = await SeedCommentAsync(pageA, "root", DateTime.UtcNow.AddHours(-2), authorId: author);
        await SeedReplyAsync(root, pageA, "page reply", DateTime.UtcNow.AddHours(-1), isFromPage: true);
        await SeedReplyAsync(root, pageA, "cust again", DateTime.UtcNow.AddMinutes(-30), isFromPage: false);
        var samePage = await SeedCommentAsync(pageA, "other thread", DateTime.UtcNow.AddHours(-3), authorId: author);
        var otherPage = await SeedCommentAsync(pageB, "cross", DateTime.UtcNow, authorId: author);

        await using (var db = new AppDbContext(_options))
        {
            db.CommentActionLogs.Add(new CommentActionLogModel
            {
                Id = Guid.NewGuid(),
                SocialCommentId = root,
                ActionType = CommentActionType.Reply,
                ActorUserName = "staff",
                Success = true,
                CreatedAt = DateTime.UtcNow.AddMinutes(-20),
                CreatedBy = "seed"
            });
            await db.SaveChangesAsync();
        }

        var data = await GetProfileAsync("comment", root);
        var stats = data.GetProperty("stats");
        Assert.Equal(2, stats.GetProperty("customerCount").GetInt32()); // root + cust again
        Assert.Equal(1, stats.GetProperty("pageCount").GetInt32());

        var others = data.GetProperty("otherConversations").EnumerateArray()
            .Select(x => x.GetProperty("id").GetGuid()).ToHashSet();
        Assert.Contains(samePage, others);
        Assert.DoesNotContain(otherPage, others);
        Assert.All(
            data.GetProperty("otherConversations").EnumerateArray(),
            x => Assert.Equal("comment", x.GetProperty("kind").GetString()));

        var activities = data.GetProperty("activities").EnumerateArray().ToList();
        Assert.NotEmpty(activities);
        Assert.Equal("comment", activities[0].GetProperty("source").GetString());
    }

    [Fact]
    public async Task Profile_OutOfScope_Returns404()
    {
        var page = await SeedChannelAsync("404");
        var soft = await SeedConversationAsync(page, "Soft", "p", DateTime.UtcNow, softDeleted: true);
        var replyParent = await SeedCommentAsync(page, "root", DateTime.UtcNow);
        var reply = await SeedReplyAsync(replyParent, page, "r", DateTime.UtcNow, isFromPage: false);

        Assert.Equal(HttpStatusCode.NotFound, await ProfileStatusAsync("message", soft));
        Assert.Equal(HttpStatusCode.NotFound, await ProfileStatusAsync("message", Guid.NewGuid()));
        Assert.Equal(HttpStatusCode.NotFound, await ProfileStatusAsync("comment", reply));
        Assert.Equal(HttpStatusCode.NotFound, await ProfileStatusAsync("unknown", replyParent));
    }

    // --- helpers ---

    private async Task<JsonElement> GetProfileAsync(string kind, Guid id)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, $"/api/Inbox/{kind}/{id}/profile");
        Authorize(request, "Reviewer");
        using var response = await _client.SendAsync(request);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("data");
    }

    private async Task<HttpStatusCode> ProfileStatusAsync(string kind, Guid id)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, $"/api/Inbox/{kind}/{id}/profile");
        Authorize(request, "Reviewer");
        using var response = await _client.SendAsync(request);
        return response.StatusCode;
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

    private async Task<Guid> SeedConversationAsync(
        Guid channelId,
        string name,
        string participantExternalId,
        DateTime lastAt,
        string? note = null,
        bool softDeleted = false)
    {
        await using var db = new AppDbContext(_options);
        var id = Guid.NewGuid();
        db.PageConversations.Add(new PageConversationModel
        {
            Id = id,
            SocialChannelId = channelId,
            ExternalConversationId = $"t_{id:N}",
            ParticipantExternalId = participantExternalId,
            ParticipantName = name,
            Snippet = name,
            LastMessageAt = lastAt,
            LastCustomerMessageAt = lastAt,
            InternalNote = note,
            InboxStatus = MessageInboxStatus.New,
            CreatedAt = lastAt,
            CreatedBy = "seed",
            IsDeleted = softDeleted,
            DeletedAt = softDeleted ? DateTime.UtcNow : null
        });
        await db.SaveChangesAsync();
        return id;
    }

    private async Task SeedPageMessageAsync(
        Guid conversationId,
        Guid channelId,
        string text,
        bool isFromPage,
        DateTime sentAt,
        string? attachmentsJson = null)
    {
        await using var db = new AppDbContext(_options);
        db.PageMessages.Add(new PageMessageModel
        {
            Id = Guid.NewGuid(),
            PageConversationId = conversationId,
            SocialChannelId = channelId,
            ExternalMessageId = $"m_{Guid.NewGuid():N}",
            Text = text,
            IsFromPage = isFromPage,
            AttachmentsJson = attachmentsJson,
            SentAt = sentAt,
            CreatedAt = sentAt,
            CreatedBy = "seed"
        });
        await db.SaveChangesAsync();
    }

    private async Task<Guid> SeedCommentAsync(
        Guid channelId,
        string message,
        DateTime commentedAt,
        string? authorId = null)
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
            AuthorExternalId = authorId ?? $"author-{id:N}",
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
        public const string SchemeName = "TestInboxProfile";

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
