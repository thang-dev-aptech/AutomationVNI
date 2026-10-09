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
using Backend.Modules.Crm.ScheduledMessages;
using Microsoft.Extensions.Logging.Abstractions;
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

namespace Backend.Tests.Modules.Crm.ScheduledMessages;

/// <summary>
/// AC 19bf8617 (a)-(f): tin hẹn giờ Messenger. DB file tạm (mỗi DbContext một connection) để claim song song là thật.
/// Revert-to-prove: bỏ claim có điều kiện → (e) đỏ; bỏ kiểm tra cửa sổ khi gửi → (d) đỏ.
/// </summary>
public sealed class CrmScheduledMessageTests : IAsyncLifetime
{
    private readonly string _dbPath = Path.Combine(Path.GetTempPath(), $"sched-{Guid.NewGuid():N}.db");
    private readonly DbContextOptions<AppDbContext> _options;
    private readonly FakeGraph _graph = new();
    private readonly TestClock _clock = new();
    private readonly IHost _host;
    private readonly HttpClient _client;
    private readonly Guid _actorUserId = Guid.Parse("aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee");

    public CrmScheduledMessageTests()
    {
        _options = new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlite($"Data Source={_dbPath};Default Timeout=30").Options;
        using (var seed = new AppDbContext(_options))
        {
            seed.Database.EnsureCreated();
            seed.Database.ExecuteSqlRaw("PRAGMA journal_mode=WAL;");
        }

        var options = _options;
        var graph = _graph;
        var clock = _clock;
        _host = new HostBuilder()
            .ConfigureWebHost(web =>
            {
                web.UseTestServer();
                web.ConfigureServices(services =>
                {
                    services.AddSingleton(options);
                    services.AddScoped(_ => new AppDbContext(options));
                    services.AddSingleton<TimeProvider>(clock);
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
                    services.AddSingleton<ISocialCommentProvider, StubFacebookCommentProvider>();
                    services.AddScoped<SocialCommentService>();
                    services.AddScoped<CrmScheduledMessageService>();
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
                    services.AddHttpClient<FacebookPageMessagingProvider>()
                        .ConfigurePrimaryHttpMessageHandler(() => graph);
                    services.AddScoped<PageMessageService>();
                    services.AddAuthentication(TestAuthHandler.SchemeName)
                        .AddScheme<AuthenticationSchemeOptions, TestAuthHandler>(
                            TestAuthHandler.SchemeName, _ => { });
                    services.AddAuthorization();
                    services.AddControllers(o => o.Filters.Add<GlobalExceptionFilter>())
                        .AddApplicationPart(typeof(CrmScheduledMessageController).Assembly);
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
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        foreach (var f in new[] { _dbPath, _dbPath + "-wal", _dbPath + "-shm" })
        {
            try { File.Delete(f); } catch { /* best effort */ }
        }
    }

    private CrmScheduledMessageWorker NewWorker()
        => new(_host.Services.GetRequiredService<IServiceScopeFactory>(),
            Options.Create(new CrmScheduledMessageWorkerOptions { Enabled = true, IntervalSeconds = 1 }),
            NullLogger<CrmScheduledMessageWorker>.Instance);

    // ── (a) tạo/validate/quyền ──────────────────────────────────────────────
    [Fact]
    public async Task A_Create_Validation_AndRoles()
    {
        var conv = await SeedConversationAsync(lastCustomer: DateTime.UtcNow.AddHours(-1)); // đóng lúc ~now+23h
        var okAt = _clock.UtcNow.AddMinutes(10);

        var tooSoon = await PostAsync("/api/CrmScheduledMessage", "Reviewer",
            new { pageConversationId = conv, text = "hi", scheduledAtUtc = _clock.UtcNow.AddSeconds(30) });
        Assert.Equal(HttpStatusCode.BadRequest, tooSoon.StatusCode);

        var closesAt = DateTime.UtcNow.AddHours(-1).AddHours(24);
        var tooLate = await PostAsync("/api/CrmScheduledMessage", "Reviewer",
            new { pageConversationId = conv, text = "hi", scheduledAtUtc = closesAt.AddMinutes(30) });
        Assert.Equal(HttpStatusCode.BadRequest, tooLate.StatusCode);
        Assert.Contains("Cửa sổ đóng lúc", await tooLate.Content.ReadAsStringAsync());

        foreach (var text in new[] { "", "   ", new string('x', CrmScheduledMessageService.MaxTextLength + 1) })
        {
            var bad = await PostAsync("/api/CrmScheduledMessage", "Reviewer",
                new { pageConversationId = conv, text, scheduledAtUtc = okAt });
            Assert.Equal(HttpStatusCode.BadRequest, bad.StatusCode);
        }

        var viewer = await PostAsync("/api/CrmScheduledMessage", "Viewer",
            new { pageConversationId = conv, text = "hi", scheduledAtUtc = okAt });
        Assert.Equal(HttpStatusCode.Forbidden, viewer.StatusCode);
        var anon = await PostAsync("/api/CrmScheduledMessage", null,
            new { pageConversationId = conv, text = "hi", scheduledAtUtc = okAt });
        Assert.Equal(HttpStatusCode.Unauthorized, anon.StatusCode);

        var ok = await PostAsync("/api/CrmScheduledMessage", "Reviewer",
            new { pageConversationId = conv, text = "  Xin chào  ", scheduledAtUtc = okAt });
        Assert.Equal(HttpStatusCode.OK, ok.StatusCode);
        Assert.Equal(0, await CountAsync(CrmScheduledMessageStatus.Sent));
        Assert.Equal(1, await CountAsync(CrmScheduledMessageStatus.Pending));
        await using var db = new AppDbContext(_options);
        Assert.Equal("Xin chào", (await db.CrmScheduledMessages.SingleAsync()).Text);
    }

    // ── (b) sửa/huỷ chỉ Pending ──────────────────────────────────────────────
    [Fact]
    public async Task B_UpdateAndCancel_OnlyPending()
    {
        var conv = await SeedConversationAsync();
        var pending = await SeedScheduledAsync(conv, CrmScheduledMessageStatus.Pending);
        var newAt = _clock.UtcNow.AddMinutes(30);

        var upd = await SendAsync(HttpMethod.Put, $"/api/CrmScheduledMessage/{pending}", "Admin",
            new { text = "Sửa", scheduledAtUtc = newAt });
        Assert.Equal(HttpStatusCode.OK, upd.StatusCode);
        var cancel = await SendAsync(HttpMethod.Post, $"/api/CrmScheduledMessage/{pending}/cancel", "Admin");
        Assert.Equal(HttpStatusCode.OK, cancel.StatusCode);

        foreach (var st in new[] { CrmScheduledMessageStatus.Sent, CrmScheduledMessageStatus.Failed, CrmScheduledMessageStatus.Cancelled })
        {
            var id = await SeedScheduledAsync(conv, st);
            Assert.Equal(HttpStatusCode.BadRequest, (await SendAsync(HttpMethod.Put, $"/api/CrmScheduledMessage/{id}", "Admin",
                new { text = "x", scheduledAtUtc = newAt })).StatusCode);
            Assert.Equal(HttpStatusCode.BadRequest, (await SendAsync(HttpMethod.Post, $"/api/CrmScheduledMessage/{id}/cancel", "Admin")).StatusCode);
        }
    }

    // ── (c) chưa tới giờ không gửi; tới giờ gửi đúng 1 lần ───────────────────
    [Fact]
    public async Task C_NotDue_NoSend_Due_SendsOnce_WithEchoAndReplied()
    {
        var conv = await SeedConversationAsync();
        var id = await SeedScheduledAsync(conv, CrmScheduledMessageStatus.Pending, _clock.UtcNow.AddMinutes(10));
        var worker = NewWorker();

        Assert.Equal(0, await worker.RunOnceAsync());
        Assert.Equal(0, _graph.SendCount);

        _clock.Advance(TimeSpan.FromMinutes(11));
        Assert.Equal(1, await worker.RunOnceAsync());
        Assert.Equal(0, await worker.RunOnceAsync());

        Assert.Equal(1, _graph.SendCount);
        await using var db = new AppDbContext(_options);
        var row = await db.CrmScheduledMessages.SingleAsync(x => x.Id == id);
        Assert.Equal(CrmScheduledMessageStatus.Sent, row.Status);
        Assert.Equal("m_scheduled_1", row.SentMessageId);
        Assert.True(await db.PageMessages.AnyAsync(x => x.PageConversationId == conv && x.IsEcho && x.ExternalMessageId == "m_scheduled_1"));
        Assert.Equal(MessageInboxStatus.Replied, (await db.PageConversations.SingleAsync(x => x.Id == conv)).InboxStatus);
    }

    // ── (d) cửa sổ đóng → Failed, không gọi Graph; provider lỗi → Failed, tin kế vẫn xử lý ──
    [Fact]
    public async Task D_WindowClosedAtSendTime_Failed_NoGraphCall()
    {
        // Cửa sổ còn mở theo đồng hồ thật (SendAsync dùng DateTime.UtcNow) nhưng đã đóng theo đồng hồ worker.
        var conv = await SeedConversationAsync(lastCustomer: DateTime.UtcNow.AddHours(-23));
        var id = await SeedScheduledAsync(conv, CrmScheduledMessageStatus.Pending, _clock.UtcNow.AddMinutes(10));
        _clock.Advance(TimeSpan.FromHours(2));

        await NewWorker().RunOnceAsync();

        Assert.Equal(0, _graph.SendCount);
        await using var db = new AppDbContext(_options);
        var row = await db.CrmScheduledMessages.SingleAsync(x => x.Id == id);
        Assert.Equal(CrmScheduledMessageStatus.Failed, row.Status);
        Assert.Contains("Cửa sổ 24h đã đóng", row.Error);
    }

    [Fact]
    public async Task D_ProviderThrows_Failed_AndWorkerContinuesWithNextMessage()
    {
        var conv1 = await SeedConversationAsync();
        var conv2 = await SeedConversationAsync();
        var first = await SeedScheduledAsync(conv1, CrmScheduledMessageStatus.Pending, _clock.UtcNow.AddMinutes(5));
        var second = await SeedScheduledAsync(conv2, CrmScheduledMessageStatus.Pending, _clock.UtcNow.AddMinutes(6));
        _graph.FailNext = true;
        _clock.Advance(TimeSpan.FromMinutes(10));

        var processed = await NewWorker().RunOnceAsync();

        Assert.Equal(2, processed);
        await using var db = new AppDbContext(_options);
        var r1 = await db.CrmScheduledMessages.SingleAsync(x => x.Id == first);
        Assert.Equal(CrmScheduledMessageStatus.Failed, r1.Status);
        Assert.False(string.IsNullOrWhiteSpace(r1.Error));
        Assert.Equal(CrmScheduledMessageStatus.Sent, (await db.CrmScheduledMessages.SingleAsync(x => x.Id == second)).Status);
    }

    [Fact]
    public async Task D_ChannelWithoutToken_Failed_NoGraphCall()
    {
        var conv = await SeedConversationAsync(accessToken: "");
        var id = await SeedScheduledAsync(conv, CrmScheduledMessageStatus.Pending, _clock.UtcNow.AddMinutes(5));
        _clock.Advance(TimeSpan.FromMinutes(10));

        await NewWorker().RunOnceAsync();

        Assert.Equal(0, _graph.SendCount);
        await using var db = new AppDbContext(_options);
        var row = await db.CrmScheduledMessages.SingleAsync(x => x.Id == id);
        Assert.Equal(CrmScheduledMessageStatus.Failed, row.Status);
        Assert.Contains("token", row.Error);
    }

    // ── (e) hai worker song song → gửi đúng 1 lần ───────────────────────────
    [Fact]
    public async Task E_TwoWorkersInParallel_SendExactlyOnce()
    {
        var conv = await SeedConversationAsync();
        var id = await SeedScheduledAsync(conv, CrmScheduledMessageStatus.Pending, _clock.UtcNow.AddMinutes(5));
        _clock.Advance(TimeSpan.FromMinutes(10));
        _graph.SendDelay = TimeSpan.FromMilliseconds(300);

        // Hai worker cùng thấy tin tới hạn (bỏ qua bước liệt kê để ép đua) rồi cùng xử lý đúng tin đó.
        async Task<bool> ProcessInOwnScopeAsync()
        {
            await using var scope = _host.Services.CreateAsyncScope();
            return await scope.ServiceProvider.GetRequiredService<CrmScheduledMessageService>().ProcessAsync(id);
        }

        var results = await Task.WhenAll(ProcessInOwnScopeAsync(), ProcessInOwnScopeAsync());
        await Task.WhenAll(NewWorker().RunOnceAsync(), NewWorker().RunOnceAsync());

        Assert.Single(results, r => r);
        Assert.Equal(1, _graph.SendCount);
        await using var db = new AppDbContext(_options);
        Assert.Equal(CrmScheduledMessageStatus.Sent, (await db.CrmScheduledMessages.SingleAsync(x => x.Id == id)).Status);
    }

    // ── (f) kẹt Sending trước restart → Failed, không gửi lại ───────────────
    [Fact]
    public async Task F_StuckSendingAtStartup_BecomesFailed_NotResent()
    {
        var conv = await SeedConversationAsync();
        var stuck = await SeedScheduledAsync(conv, CrmScheduledMessageStatus.Sending, _clock.UtcNow.AddMinutes(-30),
            claimedAt: _clock.UtcNow.AddMinutes(-20));
        var fresh = await SeedScheduledAsync(conv, CrmScheduledMessageStatus.Sending, _clock.UtcNow.AddMinutes(-1),
            claimedAt: _clock.UtcNow.AddMinutes(-1));

        var worker = NewWorker();
        await worker.StartAsync(CancellationToken.None);
        for (var i = 0; i < 50 && await CountAsync(CrmScheduledMessageStatus.Failed) == 0; i++)
            await Task.Delay(100);
        await worker.StopAsync(CancellationToken.None);

        Assert.Equal(0, _graph.SendCount);
        await using var db = new AppDbContext(_options);
        var s = await db.CrmScheduledMessages.SingleAsync(x => x.Id == stuck);
        Assert.Equal(CrmScheduledMessageStatus.Failed, s.Status);
        Assert.Equal(CrmScheduledMessageService.UnknownOutcomeError, s.Error);
        // Mới claim (dưới ngưỡng 5 phút) thì giữ nguyên.
        Assert.Equal(CrmScheduledMessageStatus.Sending, (await db.CrmScheduledMessages.SingleAsync(x => x.Id == fresh)).Status);
    }

    // ── Inbox list: HasPendingScheduled ─────────────────────────────────────
    [Fact]
    public async Task InboxList_HasPendingScheduled_OnlyForConversationsWithPending()
    {
        var withPending = await SeedConversationAsync();
        var without = await SeedConversationAsync();
        await SeedScheduledAsync(withPending, CrmScheduledMessageStatus.Pending);
        await SeedScheduledAsync(without, CrmScheduledMessageStatus.Cancelled);

        var res = await PostAsync("/api/CrmInbox/filter", "Admin", new { index = 1, size = 20 });
        Assert.True(res.StatusCode == HttpStatusCode.OK, await res.Content.ReadAsStringAsync());
        var json = await res.Content.ReadFromJsonAsync<JsonElement>();
        var items = json.GetProperty("data").GetProperty("items").EnumerateArray()
            .ToDictionary(x => x.GetProperty("id").GetGuid(), x => x.GetProperty("hasPendingScheduled").GetBoolean());

        Assert.True(items[withPending]);
        Assert.False(items[without]);
    }

    // ── helpers ──────────────────────────────────────────────────────────────
    private Task<HttpResponseMessage> PostAsync(string url, string? role, object body)
        => SendAsync(HttpMethod.Post, url, role, body);

    private async Task<HttpResponseMessage> SendAsync(HttpMethod method, string url, string? role, object? body = null)
    {
        using var req = new HttpRequestMessage(method, url);
        if (role is not null)
            req.Headers.Authorization = new AuthenticationHeaderValue(TestAuthHandler.SchemeName, $"{role}:{_actorUserId:N}");
        if (body is not null)
            req.Content = JsonContent.Create(body);
        return await _client.SendAsync(req);
    }

    private async Task<int> CountAsync(CrmScheduledMessageStatus status)
    {
        await using var db = new AppDbContext(_options);
        return await db.CrmScheduledMessages.CountAsync(x => x.Status == status);
    }

    private async Task<Guid> SeedConversationAsync(DateTime? lastCustomer = null, string accessToken = "token")
    {
        await using var db = new AppDbContext(_options);
        var channelId = Guid.NewGuid();
        db.SocialChannels.Add(new SocialChannelModel
        {
            Id = channelId,
            PageName = "Page",
            Platform = SocialPlatform.Facebook,
            ChannelType = SocialChannelType.Page,
            ExternalPageId = $"ext-{channelId:N}",
            AccessToken = accessToken,
            IsActive = true,
            CreatedAt = DateTime.UtcNow,
            CreatedBy = "seed"
        });
        var id = Guid.NewGuid();
        db.PageConversations.Add(new PageConversationModel
        {
            Id = id,
            SocialChannelId = channelId,
            ExternalConversationId = $"t_{id:N}",
            ParticipantExternalId = $"psid-{id:N}",
            ParticipantName = "Khách",
            Snippet = "hi",
            LastMessageAt = DateTime.UtcNow.AddMinutes(-10),
            LastCustomerMessageAt = lastCustomer ?? DateTime.UtcNow.AddMinutes(-10),
            InboxStatus = MessageInboxStatus.New,
            CreatedAt = DateTime.UtcNow.AddMinutes(-10),
            CreatedBy = "seed"
        });
        await db.SaveChangesAsync();
        return id;
    }

    private async Task<Guid> SeedScheduledAsync(
        Guid conversationId, CrmScheduledMessageStatus status, DateTime? scheduledAt = null, DateTime? claimedAt = null)
    {
        await using var db = new AppDbContext(_options);
        var id = Guid.NewGuid();
        db.CrmScheduledMessages.Add(new CrmScheduledMessageModel
        {
            Id = id,
            PageConversationId = conversationId,
            Text = "Tin hẹn giờ",
            ScheduledAtUtc = scheduledAt ?? _clock.UtcNow.AddMinutes(30),
            Status = status,
            ClaimedAtUtc = claimedAt,
            ClaimToken = claimedAt is null ? null : Guid.NewGuid(),
            CreatedAt = DateTime.UtcNow,
            CreatedBy = "seed"
        });
        await db.SaveChangesAsync();
        return id;
    }

    private sealed class TestClock : TimeProvider
    {
        private TimeSpan _offset;
        public DateTime UtcNow => GetUtcNow().UtcDateTime;
        public void Advance(TimeSpan by) => _offset += by;
        public override DateTimeOffset GetUtcNow() => DateTimeOffset.UtcNow + _offset;
    }

    private sealed class FakeGraph : HttpMessageHandler
    {
        private int _sendCount;
        public int SendCount => Volatile.Read(ref _sendCount);
        public bool FailNext { get; set; }
        public TimeSpan SendDelay { get; set; }

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var path = request.RequestUri?.AbsolutePath ?? "";
            if (path.Contains("/messages", StringComparison.Ordinal) && request.Method == HttpMethod.Post)
            {
                var n = Interlocked.Increment(ref _sendCount);
                if (SendDelay > TimeSpan.Zero) await Task.Delay(SendDelay, cancellationToken);
                if (FailNext)
                {
                    FailNext = false;
                    return new HttpResponseMessage(HttpStatusCode.BadRequest)
                    {
                        Content = new StringContent("""{"error":{"message":"Graph từ chối"}}""", Encoding.UTF8, "application/json")
                    };
                }

                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent($$"""{"message_id":"m_scheduled_{{n}}","recipient_id":"1"}""")
                };
            }

            return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("""{"data":[]}""") };
        }
    }

    private sealed class TestAuthHandler : AuthenticationHandler<AuthenticationSchemeOptions>
    {
        public const string SchemeName = "TestCrmScheduled";

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
