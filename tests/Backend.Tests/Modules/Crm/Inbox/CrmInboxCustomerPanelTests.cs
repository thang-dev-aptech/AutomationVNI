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
/// AC e2810e98 (a)-(e): GET api/CrmInbox/{kind}/{id}/customer — hồ sơ cột phải SO9.
/// Revert-to-prove: khớp DisplayName → (b) đỏ; SaveChanges trên GET → (c) đỏ.
/// </summary>
public sealed class CrmInboxCustomerPanelTests : IAsyncLifetime
{
    private readonly SqliteConnection _connection;
    private readonly DbContextOptions<AppDbContext> _options;
    private readonly IHost _host;
    private readonly HttpClient _client;
    private readonly Guid _actorUserId = Guid.Parse("aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee");

    public CrmInboxCustomerPanelTests()
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
    public async Task A_MatchingIdentity_ReturnsLinkedCustomerWithStatsMediaActivities()
    {
        var channelId = await SeedChannelAsync("Page A", accessToken: "SECRET_TOKEN_SHOULD_NOT_LEAK");
        const string psid = "psid-match-001";
        var customerId = await SeedCustomerAsync(
            "Nguyễn Văn A",
            "+84901234567",
            channelId,
            psid,
            platform: SocialPlatform.Facebook);
        var tagId = await SeedCustomerTagAsync(customerId, "VIP");
        await SeedNoteAsync(customerId, "Ghi chú test");
        await SeedReminderAsync(customerId, "Nhắc gọi lại");

        var convId = await SeedConversationAsync(
            channelId,
            psid,
            "Nguyễn Văn A",
            lastCustomer: DateTime.UtcNow.AddHours(-2));
        await SeedMessageAsync(convId, channelId, "Xin chào", attachmentsJson: null, sentAt: DateTime.UtcNow.AddHours(-3));
        await SeedMessageAsync(
            convId,
            channelId,
            "Ảnh",
            attachmentsJson: """{"data":[{"mime_type":"image/jpeg","image_data":{"url":"https://cdn.example/a.jpg"}}]}""",
            sentAt: DateTime.UtcNow.AddHours(-2));
        await SeedMessageAsync(
            convId,
            channelId,
            "Video",
            attachmentsJson: """{"data":[{"type":"video","video_data":{"url":"https://cdn.example/v.mp4"}}]}""",
            sentAt: DateTime.UtcNow.AddHours(-1));

        var body = await GetCustomerAsync("message", convId, "Admin");

        Assert.True(body.GetProperty("linked").GetBoolean());
        Assert.Equal(psid, body.GetProperty("participant").GetProperty("externalId").GetString());
        Assert.Equal("Nguyễn Văn A", body.GetProperty("participant").GetProperty("displayName").GetString());
        Assert.Equal("Page A", body.GetProperty("participant").GetProperty("channelName").GetString());

        var customer = body.GetProperty("customer");
        Assert.Equal(customerId, customer.GetProperty("id").GetGuid());
        Assert.Equal("+84901234567", customer.GetProperty("phoneE164").GetString());
        Assert.Equal(JsonValueKind.Null, customer.GetProperty("email").ValueKind);
        Assert.Contains(tagId, customer.GetProperty("tagIds").EnumerateArray().Select(x => x.GetGuid()));
        Assert.Equal(1, customer.GetProperty("noteCount").GetInt32());
        Assert.Equal(1, customer.GetProperty("reminderCount").GetInt32());
        Assert.NotEmpty(customer.GetProperty("identities").EnumerateArray());

        var stats = body.GetProperty("stats");
        Assert.Equal(3, stats.GetProperty("messageCount").GetInt32());
        Assert.Equal(JsonValueKind.Null, stats.GetProperty("commentCount").ValueKind);
        Assert.True(stats.GetProperty("isReplyWindowOpen").GetBoolean());
        Assert.NotEqual(JsonValueKind.Null, stats.GetProperty("replyWindowClosesAt").ValueKind);

        var media = body.GetProperty("media").EnumerateArray().ToList();
        Assert.Equal(2, media.Count);
        Assert.Contains(media, m => m.GetProperty("type").GetString() == "image");
        Assert.Contains(media, m => m.GetProperty("type").GetString() == "video");

        Assert.NotEmpty(body.GetProperty("activities").EnumerateArray());
        Assert.DoesNotContain("SECRET_TOKEN", body.ToString(), StringComparison.Ordinal);
        Assert.DoesNotContain("accessToken", body.ToString(), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task B_SameNameOrPhone_DifferentExternalIdOrPage_NotLinked()
    {
        var pageA = await SeedChannelAsync("Page A");
        var pageB = await SeedChannelAsync("Page B");
        const string name = "Trùng Tên";
        const string phone = "+84901112233";

        // Khách thật trên Page A / psid-real
        await SeedCustomerAsync(name, phone, pageA, "psid-real");

        // Khách khác cùng tên+SĐT nhưng khác ExternalId trên cùng Page
        await SeedCustomerAsync(name, phone, pageA, "psid-other");

        // Hội thoại psid chưa có identity — cùng tên với khách trên Page B
        await SeedCustomerAsync(name, phone, pageB, "psid-page-b");

        var unlinkedConv = await SeedConversationAsync(pageA, "psid-unknown", name);
        var wrongPageConv = await SeedConversationAsync(pageA, "psid-page-b", name);

        var unlinked = await GetCustomerAsync("message", unlinkedConv, "Admin");
        Assert.False(unlinked.GetProperty("linked").GetBoolean());
        Assert.Equal(JsonValueKind.Null, unlinked.GetProperty("customer").ValueKind);
        Assert.Equal(name, unlinked.GetProperty("participant").GetProperty("displayName").GetString());
        Assert.Equal("psid-unknown", unlinked.GetProperty("participant").GetProperty("externalId").GetString());

        // psid-page-b trên Page A không khớp identity (Page B)
        var crossPage = await GetCustomerAsync("message", wrongPageConv, "Admin");
        Assert.False(crossPage.GetProperty("linked").GetBoolean());
        Assert.Equal(JsonValueKind.Null, crossPage.GetProperty("customer").ValueKind);
    }

    [Fact]
    public async Task C_GetDoesNotCreateOrMerge_RowCountsUnchanged()
    {
        var channelId = await SeedChannelAsync("Page C");
        var convId = await SeedConversationAsync(channelId, "psid-new", "Người mới");

        await using (var db = new AppDbContext(_options))
        {
            Assert.Equal(0, await db.CrmCustomers.CountAsync());
            Assert.Equal(0, await db.CrmCustomerIdentities.CountAsync());
        }

        var beforeCustomers = await CountAsync(db => db.CrmCustomers.CountAsync());
        var beforeIdentities = await CountAsync(db => db.CrmCustomerIdentities.CountAsync());

        var body = await GetCustomerAsync("message", convId, "Admin");
        Assert.False(body.GetProperty("linked").GetBoolean());

        var afterCustomers = await CountAsync(db => db.CrmCustomers.CountAsync());
        var afterIdentities = await CountAsync(db => db.CrmCustomerIdentities.CountAsync());
        Assert.Equal(beforeCustomers, afterCustomers);
        Assert.Equal(beforeIdentities, afterIdentities);
    }

    [Fact]
    public async Task D_InvalidIdOrKind_404_ViewerOk_AnonymousUnauthorized()
    {
        var channelId = await SeedChannelAsync("Page D");
        var convId = await SeedConversationAsync(channelId, "psid-d", "D");

        var badKind = await SendAsync(HttpMethod.Get, $"/api/CrmInbox/weird/{convId}/customer", "Admin");
        Assert.Equal(HttpStatusCode.NotFound, badKind.StatusCode);

        var missing = await SendAsync(HttpMethod.Get, $"/api/CrmInbox/message/{Guid.NewGuid()}/customer", "Admin");
        Assert.Equal(HttpStatusCode.NotFound, missing.StatusCode);

        var viewer = await SendAsync(HttpMethod.Get, $"/api/CrmInbox/message/{convId}/customer", "Viewer");
        Assert.Equal(HttpStatusCode.OK, viewer.StatusCode);

        using var anon = new HttpRequestMessage(HttpMethod.Get, $"/api/CrmInbox/message/{convId}/customer");
        var anonRes = await _client.SendAsync(anon);
        Assert.Equal(HttpStatusCode.Unauthorized, anonRes.StatusCode);
    }

    [Fact]
    public async Task E_StatsAndMedia_OnlyFromThisConversation_NoAccessToken()
    {
        var channelId = await SeedChannelAsync("Page E", accessToken: "LEAK_ME");
        const string psid = "psid-e";
        await SeedCustomerAsync("E", null, channelId, psid);

        var target = await SeedConversationAsync(channelId, psid, "E");
        var other = await SeedConversationAsync(channelId, "psid-other-e", "Other");
        await SeedMessageAsync(
            target,
            channelId,
            "target-img",
            """{"data":[{"mime_type":"image/png","image_data":{"url":"https://cdn.example/target.png"}}]}""",
            DateTime.UtcNow.AddMinutes(-10));
        await SeedMessageAsync(
            other,
            channelId,
            "other-img",
            """{"data":[{"mime_type":"image/png","image_data":{"url":"https://cdn.example/other.png"}}]}""",
            DateTime.UtcNow.AddMinutes(-5));
        await SeedMessageAsync(target, channelId, "text only", null, DateTime.UtcNow.AddMinutes(-1));

        var body = await GetCustomerAsync("message", target, "Reviewer");
        Assert.Equal(2, body.GetProperty("stats").GetProperty("messageCount").GetInt32());
        var urls = body.GetProperty("media").EnumerateArray()
            .Select(x => x.GetProperty("url").GetString())
            .ToList();
        Assert.Single(urls);
        Assert.Equal("https://cdn.example/target.png", urls[0]);
        Assert.DoesNotContain("LEAK_ME", body.ToString(), StringComparison.Ordinal);
        Assert.DoesNotContain("accessToken", body.ToString(), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Comment_MatchingAuthor_ReturnsLinked_AndEmptyMedia()
    {
        var channelId = await SeedChannelAsync("Page Comment");
        const string author = "author-cmt-1";
        var customerId = await SeedCustomerAsync("Commenter", null, channelId, author, source: CrmIdentitySource.Comment);
        var commentId = await SeedTopLevelCommentAsync(channelId, author, "Hỏi học phí");

        var body = await GetCustomerAsync("comment", commentId, "ContentManager");
        Assert.True(body.GetProperty("linked").GetBoolean());
        Assert.Equal(customerId, body.GetProperty("customer").GetProperty("id").GetGuid());
        Assert.Equal(1, body.GetProperty("stats").GetProperty("commentCount").GetInt32());
        Assert.Equal(JsonValueKind.Null, body.GetProperty("stats").GetProperty("messageCount").ValueKind);
        Assert.Empty(body.GetProperty("media").EnumerateArray());
    }

    private async Task<JsonElement> GetCustomerAsync(string kind, Guid id, string role)
    {
        var res = await SendAsync(HttpMethod.Get, $"/api/CrmInbox/{kind}/{id}/customer", role);
        Assert.Equal(HttpStatusCode.OK, res.StatusCode);
        var json = await res.Content.ReadFromJsonAsync<JsonElement>();
        Assert.True(json.GetProperty("success").GetBoolean());
        return json.GetProperty("data");
    }

    private async Task<HttpResponseMessage> SendAsync(HttpMethod method, string url, string role)
    {
        using var req = new HttpRequestMessage(method, url);
        req.Headers.Authorization = new AuthenticationHeaderValue(
            TestAuthHandler.SchemeName, $"{role}:{_actorUserId:N}");
        return await _client.SendAsync(req);
    }

    private async Task<int> CountAsync(Func<AppDbContext, Task<int>> query)
    {
        await using var db = new AppDbContext(_options);
        return await query(db);
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

    private async Task<Guid> SeedCustomerAsync(
        string displayName,
        string? phoneE164,
        Guid channelId,
        string externalId,
        SocialPlatform platform = SocialPlatform.Facebook,
        CrmIdentitySource source = CrmIdentitySource.Message)
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
            Platform = platform,
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

    private async Task<Guid> SeedCustomerTagAsync(Guid customerId, string name)
    {
        await using var db = new AppDbContext(_options);
        var tagId = Guid.NewGuid();
        db.CrmTags.Add(new CrmTagModel
        {
            Id = tagId,
            Name = name,
            Color = "#00AA00",
            CreatedAt = DateTime.UtcNow,
            CreatedBy = "seed"
        });
        db.CrmCustomerTagLinks.Add(new CrmCustomerTagLinkModel
        {
            Id = Guid.NewGuid(),
            CrmCustomerId = customerId,
            CrmTagId = tagId,
            CreatedAt = DateTime.UtcNow,
            CreatedBy = "seed"
        });
        await db.SaveChangesAsync();
        return tagId;
    }

    private async Task SeedNoteAsync(Guid customerId, string body)
    {
        await using var db = new AppDbContext(_options);
        db.CrmCustomerNotes.Add(new CrmCustomerNoteModel
        {
            Id = Guid.NewGuid(),
            CrmCustomerId = customerId,
            Body = body,
            CreatedAt = DateTime.UtcNow,
            CreatedBy = "seed"
        });
        await db.SaveChangesAsync();
    }

    private async Task SeedReminderAsync(Guid customerId, string title)
    {
        await using var db = new AppDbContext(_options);
        db.CrmCustomerReminders.Add(new CrmCustomerReminderModel
        {
            Id = Guid.NewGuid(),
            CrmCustomerId = customerId,
            Title = title,
            DueAtUtc = DateTime.UtcNow.AddDays(1),
            CreatedAt = DateTime.UtcNow,
            CreatedBy = "seed"
        });
        await db.SaveChangesAsync();
    }

    private async Task<Guid> SeedConversationAsync(
        Guid channelId,
        string participantExternalId,
        string participantName,
        DateTime? lastCustomer = null)
    {
        await using var db = new AppDbContext(_options);
        var id = Guid.NewGuid();
        var at = lastCustomer ?? DateTime.UtcNow.AddHours(-3);
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

    private async Task SeedMessageAsync(
        Guid conversationId,
        Guid channelId,
        string text,
        string? attachmentsJson,
        DateTime sentAt)
    {
        await using var db = new AppDbContext(_options);
        db.PageMessages.Add(new PageMessageModel
        {
            Id = Guid.NewGuid(),
            PageConversationId = conversationId,
            SocialChannelId = channelId,
            ExternalMessageId = $"m_{Guid.NewGuid():N}",
            Text = text,
            AttachmentsJson = attachmentsJson,
            IsFromPage = false,
            SentAt = sentAt,
            CreatedAt = sentAt,
            CreatedBy = "seed"
        });
        await db.SaveChangesAsync();
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
        public const string SchemeName = "TestCrmInboxCustomer";

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
