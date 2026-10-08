using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text.Encodings.Web;
using System.Text.Json;
using Backend.Data;
using Backend.Modules.Inbox;
using Backend.Modules.PageContext;
using Backend.Modules.PageMessage;
using Backend.Modules.SocialChannel;
using Backend.Modules.SocialChannel.Enums;
using Backend.Modules.SocialComment;
using Backend.Modules.SocialComment.Enums;
using Backend.Shared;
using Backend.Shared.Ai;
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

namespace Backend.Tests.Modules.Inbox;

/// <summary>
/// AC inbox-ai-suggest-test (6e976e31) phần backend: roles, ngữ cảnh AI đúng hội thoại + PageContext,
/// không ghi DB / không gửi, AI lỗi → 400, 404 ngoài phạm vi, revert-to-prove (b).
/// </summary>
public sealed class InboxSuggestReplyPipelineTests : IAsyncLifetime
{
    private readonly SqliteConnection _connection;
    private readonly DbContextOptions<AppDbContext> _options;
    private readonly CapturingAiTextService _ai = new();
    private readonly IHost _host;
    private readonly HttpClient _client;
    private readonly Guid _actorUserId = Guid.Parse("aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee");

    public InboxSuggestReplyPipelineTests()
    {
        _connection = new SqliteConnection("DataSource=:memory:");
        _connection.Open();
        _options = new DbContextOptionsBuilder<AppDbContext>().UseSqlite(_connection).Options;
        using (var seed = new AppDbContext(_options))
            seed.Database.EnsureCreated();

        var options = _options;
        var ai = _ai;
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
                    services.AddSingleton<IAiTextGenerationService>(ai);
                    services.AddScoped<InboxSuggestService>();
                    services.AddAuthentication(TestAuthHandler.SchemeName)
                        .AddScheme<AuthenticationSchemeOptions, TestAuthHandler>(
                            TestAuthHandler.SchemeName, _ => { });
                    services.AddAuthorization();
                    services.AddControllers(o => o.Filters.Add<GlobalExceptionFilter>())
                        .AddApplicationPart(typeof(InboxSuggestController).Assembly);
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
    public async Task Suggest_AllowedRoles_ReturnDraft(string role)
    {
        var page = await SeedChannelAsync("Page A", accessToken: "SECRET_TOKEN_PAGE_A");
        await SeedPageContextAsync(page, "Brand A", "thân thiện");
        var conv = await SeedConversationAsync(page, "Cust", "psid-1");
        await SeedMessageAsync(conv, page, "Xin chào shop", isFromPage: false);

        _ai.NextResult = new AiTextGenerationResult { Caption = "Chào bạn, shop hỗ trợ ngay ạ!" };
        var (status, body) = await SuggestAsync("message", conv, role);
        Assert.Equal(HttpStatusCode.OK, status);
        Assert.Equal("Chào bạn, shop hỗ trợ ngay ạ!", body.GetProperty("data").GetProperty("draft").GetString());
    }

    [Fact]
    public async Task Suggest_Viewer_403_Unauthenticated_401()
    {
        var page = await SeedChannelAsync("P");
        var conv = await SeedConversationAsync(page, "C", "psid");

        var (viewerStatus, _) = await SuggestAsync("message", conv, "Viewer");
        Assert.Equal(HttpStatusCode.Forbidden, viewerStatus);

        using var anon = new HttpRequestMessage(
            HttpMethod.Post, $"/api/Inbox/message/{conv}/suggest-reply");
        using var anonRes = await _client.SendAsync(anon);
        Assert.Equal(HttpStatusCode.Unauthorized, anonRes.StatusCode);
    }

    [Fact]
    public async Task Suggest_AiRequest_OnlyThisConversationAndPageContext_NoToken()
    {
        var pageA = await SeedChannelAsync("Page A", accessToken: "TOKEN_SHOULD_NEVER_APPEAR");
        var pageB = await SeedChannelAsync("Page B", accessToken: "OTHER_TOKEN");
        await SeedPageContextAsync(pageA, "Brand Alpha", "chuyên nghiệp", hotline: "1900-1111");
        await SeedPageContextAsync(pageB, "Brand Beta SECRET", "khác", hotline: "1900-9999");

        var target = await SeedConversationAsync(pageA, "Target Cust", "psid-target");
        await SeedMessageAsync(target, pageA, "TARGET_UNIQUE_HELLO", isFromPage: false);
        await SeedMessageAsync(target, pageA, "TARGET_PAGE_REPLY", isFromPage: true);

        var otherSamePage = await SeedConversationAsync(pageA, "Other", "psid-other");
        await SeedMessageAsync(otherSamePage, pageA, "LEAK_OTHER_CONV_SAME_PAGE", isFromPage: false);

        var otherPage = await SeedConversationAsync(pageB, "Cross", "psid-cross");
        await SeedMessageAsync(otherPage, pageB, "LEAK_OTHER_PAGE_MSG", isFromPage: false);

        _ai.NextResult = new AiTextGenerationResult { Caption = "ok draft" };
        var (status, _) = await SuggestAsync("message", target, "Admin");
        Assert.Equal(HttpStatusCode.OK, status);

        Assert.NotNull(_ai.LastRequest);
        var prompt = _ai.LastRequest!.PromptOverride ?? "";
        var brand = _ai.LastRequest.BrandContext ?? "";
        var blob = prompt + "\n" + brand + "\n" + (_ai.LastRequest.Tone ?? "");

        Assert.Contains("TARGET_UNIQUE_HELLO", blob, StringComparison.Ordinal);
        Assert.Contains("Brand Alpha", blob, StringComparison.Ordinal);
        Assert.Contains("1900-1111", blob, StringComparison.Ordinal);

        Assert.DoesNotContain("LEAK_OTHER_CONV_SAME_PAGE", blob, StringComparison.Ordinal);
        Assert.DoesNotContain("LEAK_OTHER_PAGE_MSG", blob, StringComparison.Ordinal);
        Assert.DoesNotContain("Brand Beta", blob, StringComparison.Ordinal);
        Assert.DoesNotContain("TOKEN_SHOULD_NEVER_APPEAR", blob, StringComparison.Ordinal);
        Assert.DoesNotContain("OTHER_TOKEN", blob, StringComparison.Ordinal);
        Assert.DoesNotContain("SECRET", blob, StringComparison.Ordinal);
    }

    /// <summary>
    /// Revert-to-prove (b): nếu ghép prompt từ mọi tin cùng participant không lọc conversationId
    /// thì nội dung hội thoại khác sẽ lọt — chứng minh service thật đã lọc đúng hội thoại.
    /// </summary>
    [Fact]
    public async Task Suggest_RevertToProve_B_WithoutConversationFilter_WouldLeakOtherThread()
    {
        var page = await SeedChannelAsync("Page");
        await SeedPageContextAsync(page, "Brand", "tone");
        const string psid = "psid-shared";
        var main = await SeedConversationAsync(page, "Main", psid);
        await SeedMessageAsync(main, page, "MAIN_ONLY", isFromPage: false);
        var other = await SeedConversationAsync(page, "Other", psid);
        await SeedMessageAsync(other, page, "SHOULD_LEAK_IF_NO_FILTER", isFromPage: false);

        await using var db = new AppDbContext(_options);
        var unsafeBlob = string.Join('\n', await db.PageMessages.AsNoTracking()
            .Where(m => !m.IsDeleted)
            .Join(db.PageConversations.AsNoTracking().Where(c => !c.IsDeleted && c.ParticipantExternalId == psid),
                m => m.PageConversationId, c => c.Id, (m, _) => m.Text!)
            .ToListAsync());
        Assert.Contains("SHOULD_LEAK_IF_NO_FILTER", unsafeBlob, StringComparison.Ordinal);

        _ai.NextResult = new AiTextGenerationResult { Caption = "draft" };
        await SuggestAsync("message", main, "Admin");
        var prompt = _ai.LastRequest!.PromptOverride ?? "";
        Assert.Contains("MAIN_ONLY", prompt, StringComparison.Ordinal);
        Assert.DoesNotContain("SHOULD_LEAK_IF_NO_FILTER", prompt, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Suggest_DoesNotWriteDb_OrChangeStatusAssign()
    {
        var page = await SeedChannelAsync("P");
        await SeedPageContextAsync(page, "B", "t");
        var conv = await SeedConversationAsync(
            page, "C", "psid", status: MessageInboxStatus.New, assignedTo: "staff-a");
        await SeedMessageAsync(conv, page, "hi", isFromPage: false);

        await using (var db = new AppDbContext(_options))
        {
            Assert.Equal(1, await db.PageMessages.CountAsync(x => !x.IsDeleted));
            Assert.Equal(0, await db.MessageActionLogs.CountAsync());
        }

        _ai.NextResult = new AiTextGenerationResult { Caption = "draft" };
        var (status, _) = await SuggestAsync("message", conv, "Reviewer");
        Assert.Equal(HttpStatusCode.OK, status);

        await using (var db = new AppDbContext(_options))
        {
            Assert.Equal(1, await db.PageMessages.CountAsync(x => !x.IsDeleted));
            Assert.Equal(0, await db.MessageActionLogs.CountAsync());
            var c = await db.PageConversations.AsNoTracking().FirstAsync(x => x.Id == conv);
            Assert.Equal(MessageInboxStatus.New, c.InboxStatus);
            Assert.Equal("staff-a", c.AssignedTo);
        }
    }

    [Fact]
    public async Task Suggest_AiError_Returns400_ClearMessage()
    {
        var page = await SeedChannelAsync("P");
        var conv = await SeedConversationAsync(page, "C", "psid");
        _ai.ThrowOnGenerate = new AiTextGenerationException("provider timeout");

        var (status, body) = await SuggestAsync("message", conv, "Admin");
        Assert.Equal(HttpStatusCode.BadRequest, status);
        Assert.Equal("AI_SUGGEST_FAILED", body.GetProperty("errorCode").GetString());
        Assert.Contains("AI", body.GetProperty("message").GetString(), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Suggest_AiUnavailable_Returns400()
    {
        var page = await SeedChannelAsync("P");
        var conv = await SeedConversationAsync(page, "C", "psid");
        _ai.Available = false;

        var (status, body) = await SuggestAsync("message", conv, "Admin");
        Assert.Equal(HttpStatusCode.BadRequest, status);
        Assert.Equal("AI_SUGGEST_FAILED", body.GetProperty("errorCode").GetString());
    }

    [Fact]
    public async Task Suggest_OutOfScope_404()
    {
        var page = await SeedChannelAsync("P");
        var soft = await SeedConversationAsync(page, "S", "psid", softDeleted: true);
        var root = await SeedCommentAsync(page, "root", authorId: "a1");
        var reply = await SeedReplyAsync(root, page, "r", isFromPage: false);

        Assert.Equal(HttpStatusCode.NotFound, (await SuggestAsync("message", soft, "Admin")).Status);
        Assert.Equal(HttpStatusCode.NotFound, (await SuggestAsync("message", Guid.NewGuid(), "Admin")).Status);
        Assert.Equal(HttpStatusCode.NotFound, (await SuggestAsync("comment", reply, "Admin")).Status);
        Assert.Equal(HttpStatusCode.NotFound, (await SuggestAsync("unknown", root, "Admin")).Status);
    }

    [Fact]
    public async Task Suggest_Comment_UsesThreadOnly_AndPageContext()
    {
        var pageA = await SeedChannelAsync("CA", accessToken: "tokA");
        var pageB = await SeedChannelAsync("CB", accessToken: "tokB");
        await SeedPageContextAsync(pageA, "CtxA", "giọng A");
        await SeedPageContextAsync(pageB, "CtxB", "giọng B");

        var root = await SeedCommentAsync(pageA, "COMMENT_ROOT_UNIQUE", authorId: "auth");
        await SeedReplyAsync(root, pageA, "COMMENT_REPLY_UNIQUE", isFromPage: true);
        var other = await SeedCommentAsync(pageA, "OTHER_COMMENT_LEAK", authorId: "auth");
        await SeedCommentAsync(pageB, "CROSS_PAGE_COMMENT", authorId: "auth");

        _ai.NextResult = new AiTextGenerationResult { Caption = "cmt draft" };
        var (status, _) = await SuggestAsync("comment", root, "ContentManager");
        Assert.Equal(HttpStatusCode.OK, status);

        var prompt = _ai.LastRequest!.PromptOverride ?? "";
        Assert.Contains("COMMENT_ROOT_UNIQUE", prompt, StringComparison.Ordinal);
        Assert.Contains("COMMENT_REPLY_UNIQUE", prompt, StringComparison.Ordinal);
        Assert.Contains("CtxA", prompt + _ai.LastRequest.BrandContext, StringComparison.Ordinal);
        Assert.DoesNotContain("OTHER_COMMENT_LEAK", prompt, StringComparison.Ordinal);
        Assert.DoesNotContain("CROSS_PAGE_COMMENT", prompt, StringComparison.Ordinal);
        Assert.DoesNotContain("tokA", prompt, StringComparison.Ordinal);
        _ = other; // seeded for leak check
    }

    // --- helpers ---

    private async Task<(HttpStatusCode Status, JsonElement Body)> SuggestAsync(
        string kind, Guid id, string role)
    {
        using var request = new HttpRequestMessage(
            HttpMethod.Post, $"/api/Inbox/{kind}/{id}/suggest-reply");
        Authorize(request, role);
        using var response = await _client.SendAsync(request);
        var raw = await response.Content.ReadAsStringAsync();
        var body = string.IsNullOrWhiteSpace(raw)
            ? default
            : JsonDocument.Parse(raw).RootElement.Clone();
        return (response.StatusCode, body);
    }

    private void Authorize(HttpRequestMessage request, string role)
        => request.Headers.Authorization = new AuthenticationHeaderValue(
            TestAuthHandler.SchemeName, $"{role}:{_actorUserId:N}");

    private async Task<Guid> SeedChannelAsync(string name, string accessToken = "token")
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

    private async Task SeedPageContextAsync(
        Guid channelId, string brand, string tone, string? hotline = null)
    {
        await using var db = new AppDbContext(_options);
        db.PageContexts.Add(new PageContextModel
        {
            Id = Guid.NewGuid(),
            SocialChannelId = channelId,
            BrandName = brand,
            ToneOfVoice = tone,
            Hotline = hotline,
            CtaText = "Inbox ngay",
            CreatedAt = DateTime.UtcNow,
            CreatedBy = "seed"
        });
        await db.SaveChangesAsync();
    }

    private async Task<Guid> SeedConversationAsync(
        Guid channelId,
        string name,
        string psid,
        MessageInboxStatus status = MessageInboxStatus.New,
        string? assignedTo = null,
        bool softDeleted = false)
    {
        await using var db = new AppDbContext(_options);
        var id = Guid.NewGuid();
        var at = DateTime.UtcNow;
        db.PageConversations.Add(new PageConversationModel
        {
            Id = id,
            SocialChannelId = channelId,
            ExternalConversationId = $"t_{id:N}",
            ParticipantExternalId = psid,
            ParticipantName = name,
            Snippet = name,
            LastMessageAt = at,
            LastCustomerMessageAt = at,
            InboxStatus = status,
            AssignedTo = assignedTo,
            CreatedAt = at,
            CreatedBy = "seed",
            IsDeleted = softDeleted,
            DeletedAt = softDeleted ? at : null
        });
        await db.SaveChangesAsync();
        return id;
    }

    private async Task SeedMessageAsync(
        Guid conversationId, Guid channelId, string text, bool isFromPage)
    {
        await using var db = new AppDbContext(_options);
        var at = DateTime.UtcNow;
        db.PageMessages.Add(new PageMessageModel
        {
            Id = Guid.NewGuid(),
            PageConversationId = conversationId,
            SocialChannelId = channelId,
            ExternalMessageId = $"m_{Guid.NewGuid():N}",
            Text = text,
            IsFromPage = isFromPage,
            SentAt = at,
            CreatedAt = at,
            CreatedBy = "seed"
        });
        await db.SaveChangesAsync();
    }

    private async Task<Guid> SeedCommentAsync(Guid channelId, string message, string authorId)
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
        var at = DateTime.UtcNow;
        db.SocialComments.Add(new SocialCommentModel
        {
            Id = id,
            SocialChannelId = channelId,
            SocialPostId = postId,
            Platform = SocialPlatform.Facebook,
            ExternalCommentId = $"cmt-{id:N}",
            AuthorExternalId = authorId,
            AuthorName = "Commenter",
            Message = message,
            CommentedAt = at,
            InboxStatus = CommentInboxStatus.New,
            CreatedAt = at,
            CreatedBy = "seed"
        });
        await db.SaveChangesAsync();
        return id;
    }

    private async Task<Guid> SeedReplyAsync(
        Guid parentId, Guid channelId, string message, bool isFromPage)
    {
        await using var db = new AppDbContext(_options);
        var parent = await db.SocialComments.AsNoTracking().FirstAsync(x => x.Id == parentId);
        var id = Guid.NewGuid();
        var at = DateTime.UtcNow;
        db.SocialComments.Add(new SocialCommentModel
        {
            Id = id,
            SocialChannelId = channelId,
            SocialPostId = parent.SocialPostId,
            Platform = SocialPlatform.Facebook,
            ExternalCommentId = $"cmt-{id:N}",
            ParentCommentId = parentId,
            AuthorExternalId = isFromPage ? "page" : "reply-user",
            AuthorName = isFromPage ? "Page" : "Reply",
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

    private sealed class CapturingAiTextService : IAiTextGenerationService
    {
        public bool Available { get; set; } = true;
        public AiTextGenerationRequest? LastRequest { get; private set; }
        public AiTextGenerationResult NextResult { get; set; } = new() { Caption = "draft" };
        public Exception? ThrowOnGenerate { get; set; }

        public bool IsAvailable(string? provider = null) => Available;

        public Task<AiTextGenerationResult> GenerateAsync(
            AiTextGenerationRequest request, CancellationToken ct = default)
        {
            LastRequest = request;
            if (ThrowOnGenerate is not null)
                throw ThrowOnGenerate;
            return Task.FromResult(NextResult);
        }

        public Task<List<string>> SuggestIdeasAsync(
            string topic, int count, string? category, CancellationToken ct = default)
            => Task.FromResult(new List<string>());

        public Task<string> ComposeImagePromptAsync(
            AiImagePromptRequest request, CancellationToken ct = default)
            => Task.FromResult(string.Empty);
    }

    private sealed class TestAuthHandler : AuthenticationHandler<AuthenticationSchemeOptions>
    {
        public const string SchemeName = "TestInboxSuggest";

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
                new AuthenticationTicket(
                    new ClaimsPrincipal(new ClaimsIdentity(claims, SchemeName)), SchemeName)));
        }
    }
}
