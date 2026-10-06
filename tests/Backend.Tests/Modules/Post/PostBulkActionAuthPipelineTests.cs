using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text.Encodings.Web;
using System.Text.Json;
using Backend.Data;
using Backend.Modules.GenerationJob;
using Backend.Modules.Post;
using Backend.Modules.Post.Enums;
using Backend.Modules.PublishLog;
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

namespace Backend.Tests.Modules.Post;

/// <summary>
/// R-033 AC bulk-action-authz-test (aa4c740a): HTTP pipeline cho POST api/Post/bulk-action
/// — quyền từng bài, lô trộn, giới hạn 100, 401, revert-to-prove (a)/(b).
/// </summary>
public sealed class PostBulkActionAuthPipelineTests : IAsyncLifetime
{
    private static readonly Guid ActorUserId = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid OtherUserId = Guid.Parse("22222222-2222-2222-2222-222222222222");

    private readonly SqliteConnection _connection;
    private readonly DbContextOptions<AppDbContext> _options;
    private readonly IHost _host;
    private readonly HttpClient _client;
    private readonly ControllablePublishPipeline _publish;

    public PostBulkActionAuthPipelineTests()
    {
        _connection = new SqliteConnection("DataSource=:memory:");
        _connection.Open();
        _options = new DbContextOptionsBuilder<AppDbContext>().UseSqlite(_connection).Options;
        using (var seed = new AppDbContext(_options))
            seed.Database.EnsureCreated();

        _publish = new ControllablePublishPipeline(_options);
        var options = _options;
        var publish = _publish;
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
                    services.AddScoped<PostRepository>();
                    services.AddScoped<GenerationJobRepository>();
                    services.AddScoped<PublishLogRepository>();
                    services.AddScoped<PostWorkflowService>();
                    services.AddScoped<PostBulkActionService>();
                    services.AddSingleton<IPublishPipelineService>(publish);
                    services.AddAuthentication(BulkAuthHandler.SchemeName)
                        .AddScheme<AuthenticationSchemeOptions, BulkAuthHandler>(
                            BulkAuthHandler.SchemeName, _ => { });
                    services.AddAuthorization();
                    services.AddControllers()
                        .AddApplicationPart(typeof(PostBulkController).Assembly);
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

    // --- (a) cancelSchedule ---

    [Theory]
    [InlineData("Admin")]
    [InlineData("ContentManager")]
    public async Task CancelSchedule_AdminOrContentManager_CancelsAnyScheduledPost(string role)
    {
        var mine = await SeedPostAsync(ActorUserId, PostStatus.Scheduled);
        var other = await SeedPostAsync(OtherUserId, PostStatus.Scheduled);

        using var response = await SendBulkAsync(
            role, ActorUserId, PostBulkActions.CancelSchedule, [mine, other]);
        var results = await ReadResultsAsync(response);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.All(results, r => Assert.True(r.Success));
        Assert.Equal(PostStatus.Approved, await GetStatusAsync(mine));
        Assert.Equal(PostStatus.Approved, await GetStatusAsync(other));
    }

    [Theory]
    [InlineData("Reviewer")]
    [InlineData("Viewer")]
    public async Task CancelSchedule_NonPrivileged_OwnOk_OtherForbidden_StaysScheduled(string role)
    {
        var mine = await SeedPostAsync(ActorUserId, PostStatus.Scheduled);
        var other = await SeedPostAsync(OtherUserId, PostStatus.Scheduled);

        using var response = await SendBulkAsync(
            role, ActorUserId, PostBulkActions.CancelSchedule, [mine, other]);
        var results = await ReadResultsAsync(response);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.True(results[0].Success);
        Assert.False(results[1].Success);
        Assert.Equal("FORBIDDEN", results[1].ErrorCode);
        Assert.Contains("không có quyền", results[1].Message!, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(PostStatus.Approved, await GetStatusAsync(mine));
        Assert.Equal(PostStatus.Scheduled, await GetStatusAsync(other));
    }

    [Fact]
    public async Task CancelSchedule_WrongStatus_ReturnsStatusError_NoChange()
    {
        var draft = await SeedPostAsync(ActorUserId, PostStatus.Draft);

        using var response = await SendBulkAsync(
            "Admin", ActorUserId, PostBulkActions.CancelSchedule, [draft]);
        var results = await ReadResultsAsync(response);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.False(results[0].Success);
        Assert.Contains("trạng thái", results[0].Message!, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(PostStatus.Draft, await GetStatusAsync(draft));
    }

    /// <summary>
    /// Revert-to-prove (a): tắt tạm CanCancelSchedule → Reviewer huỷ được bài người khác
    /// (test phủ định sẽ fail nếu gắn lại vào suite với bypass=true). Chứng minh check là chỗ chặn.
    /// </summary>
    [Fact]
    public async Task CancelSchedule_RevertToProve_WithoutOwnershipCheck_OtherPostWouldSucceed()
    {
        var other = await SeedPostAsync(OtherUserId, PostStatus.Scheduled);
        await using var scope = _host.Services.CreateAsyncScope();
        var workflow = scope.ServiceProvider.GetRequiredService<PostWorkflowService>();
        var http = scope.ServiceProvider.GetRequiredService<Microsoft.AspNetCore.Http.IHttpContextAccessor>();
        http.HttpContext = BuildHttpContext("Reviewer", ActorUserId);

        var post = await workflow.GetPostAsync(other, CancellationToken.None);
        Assert.NotNull(post);
        // Không gọi CanCancelSchedule — chứng minh nếu bỏ check thì CancelScheduleAsync vẫn chạy.
        await workflow.CancelScheduleAsync(other, CancellationToken.None);
        Assert.Equal(PostStatus.Approved, await GetStatusAsync(other));

        // Restore: seed lại Scheduled và chứng minh bulk path CÓ check → FORBIDDEN.
        await SetStatusAsync(other, PostStatus.Scheduled, scheduledAt: DateTime.UtcNow.AddHours(2));
        using var response = await SendBulkAsync(
            "Reviewer", ActorUserId, PostBulkActions.CancelSchedule, [other]);
        var results = await ReadResultsAsync(response);
        Assert.False(results[0].Success);
        Assert.Equal("FORBIDDEN", results[0].ErrorCode);
        Assert.Equal(PostStatus.Scheduled, await GetStatusAsync(other));
    }

    // --- (b) publishNow ---

    [Fact]
    public async Task PublishNow_Viewer_AllDenied_NoPostBecomesPublishing()
    {
        var a = await SeedPostAsync(ActorUserId, PostStatus.Approved);
        var b = await SeedPostAsync(OtherUserId, PostStatus.Scheduled);

        using var response = await SendBulkAsync(
            "Viewer", ActorUserId, PostBulkActions.PublishNow, [a, b]);
        var results = await ReadResultsAsync(response);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.All(results, r =>
        {
            Assert.False(r.Success);
            Assert.Equal("FORBIDDEN", r.ErrorCode);
            Assert.Contains("không có quyền", r.Message!, StringComparison.OrdinalIgnoreCase);
        });
        Assert.Equal(PostStatus.Approved, await GetStatusAsync(a));
        Assert.Equal(PostStatus.Scheduled, await GetStatusAsync(b));
        Assert.Equal(0, _publish.ProcessCalls);
    }

    /// <summary>
    /// Revert-to-prove (b): bỏ check role → Viewer gọi PublishNowAsync được (status Publishing);
    /// bulk path vẫn chặn Viewer.
    /// </summary>
    [Fact]
    public async Task PublishNow_RevertToProve_WithoutRoleCheck_ViewerCouldPublish()
    {
        var postId = await SeedPostAsync(ActorUserId, PostStatus.Approved);
        await using var scope = _host.Services.CreateAsyncScope();
        var workflow = scope.ServiceProvider.GetRequiredService<PostWorkflowService>();
        var http = scope.ServiceProvider.GetRequiredService<Microsoft.AspNetCore.Http.IHttpContextAccessor>();
        http.HttpContext = BuildHttpContext("Viewer", ActorUserId);

        // Không có role check → PublishNowAsync đổi sang Publishing.
        await workflow.PublishNowAsync(postId, CancellationToken.None);
        Assert.Equal(PostStatus.Publishing, await GetStatusAsync(postId));

        // Restore Approved rồi chứng minh bulk từ chối Viewer.
        await SetStatusAsync(postId, PostStatus.Approved);
        using var response = await SendBulkAsync(
            "Viewer", ActorUserId, PostBulkActions.PublishNow, [postId]);
        var results = await ReadResultsAsync(response);
        Assert.False(results[0].Success);
        Assert.Equal("FORBIDDEN", results[0].ErrorCode);
        Assert.Equal(PostStatus.Approved, await GetStatusAsync(postId));
    }

    [Theory]
    [InlineData("Admin")]
    [InlineData("Reviewer")]
    [InlineData("ContentManager")]
    public async Task PublishNow_AllowedRoles_MovesToPublishing(string role)
    {
        var postId = await SeedPostAsync(ActorUserId, PostStatus.Approved);

        using var response = await SendBulkAsync(
            role, ActorUserId, PostBulkActions.PublishNow, [postId]);
        var results = await ReadResultsAsync(response);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.True(results[0].Success);
        Assert.Equal(PostStatus.Publishing, await GetStatusAsync(postId));
    }

    [Fact]
    public async Task PublishNow_PipelineFailure_RevertsFromPublishing()
    {
        var postId = await SeedPostAsync(ActorUserId, PostStatus.Approved);
        _publish.ThrowOnProcess = true;

        using var response = await SendBulkAsync(
            "Admin", ActorUserId, PostBulkActions.PublishNow, [postId]);
        var results = await ReadResultsAsync(response);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.False(results[0].Success);
        Assert.Equal("PUBLISH_FAILED", results[0].ErrorCode);
        Assert.Equal(1, _publish.RevertCalls);
        Assert.Equal(PostStatus.Approved, await GetStatusAsync(postId));
        _publish.ThrowOnProcess = false;
    }

    // --- (c) delete ---

    [Fact]
    public async Task Delete_SoftDeletes_LikeSingleDelete()
    {
        var postId = await SeedPostAsync(ActorUserId, PostStatus.Approved);

        using var response = await SendBulkAsync(
            "Viewer", ActorUserId, PostBulkActions.Delete, [postId]);
        var results = await ReadResultsAsync(response);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.True(results[0].Success);
        Assert.True(await IsDeletedAsync(postId));
    }

    [Fact]
    public async Task Delete_Scheduled_Blocked()
    {
        var postId = await SeedPostAsync(ActorUserId, PostStatus.Scheduled);

        using var response = await SendBulkAsync(
            "Admin", ActorUserId, PostBulkActions.Delete, [postId]);
        var results = await ReadResultsAsync(response);

        Assert.False(results[0].Success);
        Assert.Equal("POST_SCHEDULED", results[0].ErrorCode);
        Assert.False(await IsDeletedAsync(postId));
    }

    // --- (d) mixed batch ---

    [Fact]
    public async Task MixedBatch_ValidSucceeds_InvalidFails_PerItem()
    {
        var ok = await SeedPostAsync(ActorUserId, PostStatus.Scheduled);
        var denied = await SeedPostAsync(OtherUserId, PostStatus.Scheduled);
        var wrong = await SeedPostAsync(ActorUserId, PostStatus.Draft);

        using var response = await SendBulkAsync(
            "Reviewer", ActorUserId, PostBulkActions.CancelSchedule, [ok, denied, wrong]);
        var results = await ReadResultsAsync(response);

        Assert.True(results[0].Success);
        Assert.False(results[1].Success);
        Assert.Equal("FORBIDDEN", results[1].ErrorCode);
        Assert.False(results[2].Success);
        Assert.Equal(PostStatus.Approved, await GetStatusAsync(ok));
        Assert.Equal(PostStatus.Scheduled, await GetStatusAsync(denied));
        Assert.Equal(PostStatus.Draft, await GetStatusAsync(wrong));
    }

    // --- (e) 101 ids / missing ---

    [Fact]
    public async Task OverLimit_101Ids_Returns400_NoChange()
    {
        var postId = await SeedPostAsync(ActorUserId, PostStatus.Scheduled);
        var ids = Enumerable.Range(0, 101).Select(_ => postId).ToList();

        using var response = await SendBulkAsync(
            "Admin", ActorUserId, PostBulkActions.CancelSchedule, ids);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(PostStatus.Scheduled, await GetStatusAsync(postId));
    }

    [Fact]
    public async Task MissingOrDeleted_ReturnsNotFoundForThatItem()
    {
        var missing = Guid.NewGuid();
        var deleted = await SeedPostAsync(ActorUserId, PostStatus.Approved, isDeleted: true);

        using var response = await SendBulkAsync(
            "Admin", ActorUserId, PostBulkActions.CancelSchedule, [missing, deleted]);
        var results = await ReadResultsAsync(response);

        Assert.All(results, r =>
        {
            Assert.False(r.Success);
            Assert.Equal("NOT_FOUND", r.ErrorCode);
            Assert.Contains("không tìm thấy", r.Message!, StringComparison.OrdinalIgnoreCase);
        });
    }

    // --- (f) unauthenticated ---

    [Fact]
    public async Task Unauthenticated_Returns401()
    {
        var postId = await SeedPostAsync(ActorUserId, PostStatus.Scheduled);
        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/Post/bulk-action")
        {
            Content = JsonContent.Create(new PostBulkActionRequest
            {
                Action = PostBulkActions.CancelSchedule,
                PostIds = [postId]
            })
        };

        using var response = await _client.SendAsync(request);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Equal(PostStatus.Scheduled, await GetStatusAsync(postId));
    }

    // --- helpers ---

    private async Task<HttpResponseMessage> SendBulkAsync(
        string role, Guid userId, string action, IReadOnlyList<Guid> postIds)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/Post/bulk-action")
        {
            Content = JsonContent.Create(new PostBulkActionRequest
            {
                Action = action,
                PostIds = postIds.ToList()
            })
        };
        request.Headers.Authorization = new AuthenticationHeaderValue(
            BulkAuthHandler.SchemeName, $"{role}:{userId:D}");
        return await _client.SendAsync(request);
    }

    private static async Task<List<ItemResult>> ReadResultsAsync(HttpResponseMessage response)
    {
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.True(body.GetProperty("success").GetBoolean());
        var arr = body.GetProperty("data").GetProperty("results");
        var list = new List<ItemResult>();
        foreach (var item in arr.EnumerateArray())
        {
            list.Add(new ItemResult(
                item.GetProperty("postId").GetGuid(),
                item.GetProperty("success").GetBoolean(),
                item.TryGetProperty("errorCode", out var ec) && ec.ValueKind == JsonValueKind.String
                    ? ec.GetString()
                    : null,
                item.TryGetProperty("message", out var msg) && msg.ValueKind == JsonValueKind.String
                    ? msg.GetString()
                    : null));
        }
        return list;
    }

    private async Task<Guid> SeedPostAsync(
        Guid userId, PostStatus status, bool isDeleted = false)
    {
        await using var db = new AppDbContext(_options);
        var id = Guid.NewGuid();
        db.Set<PostModel>().Add(new PostModel
        {
            Id = id,
            Title = "Bulk test",
            Content = "content",
            SocialChannelId = Guid.NewGuid(),
            Status = status,
            UserId = userId,
            ScheduledPublishAt = status == PostStatus.Scheduled
                ? DateTime.UtcNow.AddHours(3)
                : null,
            IsDeleted = isDeleted,
            CreatedAt = DateTime.UtcNow,
            CreatedBy = "seed"
        });
        await db.SaveChangesAsync();
        return id;
    }

    private async Task<PostStatus> GetStatusAsync(Guid id)
    {
        await using var db = new AppDbContext(_options);
        return await db.Set<PostModel>().Where(x => x.Id == id).Select(x => x.Status).SingleAsync();
    }

    private async Task<bool> IsDeletedAsync(Guid id)
    {
        await using var db = new AppDbContext(_options);
        return await db.Set<PostModel>().Where(x => x.Id == id).Select(x => x.IsDeleted).SingleAsync();
    }

    private async Task SetStatusAsync(Guid id, PostStatus status, DateTime? scheduledAt = null)
    {
        await using var db = new AppDbContext(_options);
        var post = await db.Set<PostModel>().SingleAsync(x => x.Id == id);
        post.Status = status;
        post.ScheduledPublishAt = scheduledAt;
        await db.SaveChangesAsync();
    }

    private static Microsoft.AspNetCore.Http.DefaultHttpContext BuildHttpContext(string role, Guid userId)
    {
        var claims = new[]
        {
            new Claim(ClaimTypes.Name, "actor"),
            new Claim(ClaimTypes.NameIdentifier, userId.ToString("D")),
            new Claim(ClaimTypes.Role, role),
        };
        var ctx = new Microsoft.AspNetCore.Http.DefaultHttpContext
        {
            User = new ClaimsPrincipal(new ClaimsIdentity(claims, BulkAuthHandler.SchemeName))
        };
        return ctx;
    }

    private sealed record ItemResult(Guid PostId, bool Success, string? ErrorCode, string? Message);

    private sealed class ControllablePublishPipeline(DbContextOptions<AppDbContext> options)
        : IPublishPipelineService
    {
        public int ProcessCalls;
        public int RevertCalls;
        public bool ThrowOnProcess;

        public Task<ProcessPublishLogResponse> ProcessAsync(Guid publishLogId, CancellationToken ct = default)
            => throw new NotSupportedException();

        public Task<ProcessPublishLogResponse> ProcessRealAsync(Guid publishLogId, CancellationToken ct = default)
            => throw new NotSupportedException();

        public Task<ProcessPublishLogResponse?> ProcessPendingForPostAsync(
            Guid postId, CancellationToken ct = default)
        {
            ProcessCalls++;
            if (ThrowOnProcess)
                throw new InvalidOperationException("publish probe failed");
            return Task.FromResult<ProcessPublishLogResponse?>(null);
        }

        public async Task RevertStuckPublishingAsync(Guid postId, CancellationToken ct = default)
        {
            RevertCalls++;
            await using var db = new AppDbContext(options);
            var post = await db.Set<PostModel>().FirstOrDefaultAsync(x => x.Id == postId, ct);
            if (post is null || post.Status != PostStatus.Publishing)
                return;
            post.Status = post.ScheduledPublishAt.HasValue ? PostStatus.Scheduled : PostStatus.Approved;
            await db.SaveChangesAsync(ct);
        }

        public Task<PublishLogModel> FailAsync(
            Guid publishLogId, FailPublishLogRequest request, CancellationToken ct = default)
            => throw new NotSupportedException();

        public Task<PublishLogModel> RetryAsync(Guid publishLogId, CancellationToken ct = default)
            => throw new NotSupportedException();

        public Task<PublishLogModel> CancelAsync(Guid publishLogId, CancellationToken ct = default)
            => throw new NotSupportedException();

        public Task<ProcessDueScheduledResult> ProcessDueScheduledAsync(
            int batchSize, CancellationToken ct = default)
            => Task.FromResult(new ProcessDueScheduledResult());
    }

    private sealed class BulkAuthHandler : AuthenticationHandler<AuthenticationSchemeOptions>
    {
        public const string SchemeName = "TestPostBulk";

        public BulkAuthHandler(
            IOptionsMonitor<AuthenticationSchemeOptions> options,
            ILoggerFactory logger,
            UrlEncoder encoder) : base(options, logger, encoder) { }

        protected override Task<AuthenticateResult> HandleAuthenticateAsync()
        {
            var header = Request.Headers.Authorization.ToString();
            if (string.IsNullOrWhiteSpace(header))
                return Task.FromResult(AuthenticateResult.Fail("Missing Authorization header."));

            var value = header.StartsWith(SchemeName + " ", StringComparison.OrdinalIgnoreCase)
                ? header[(SchemeName.Length + 1)..].Trim()
                : header.Trim();
            var parts = value.Split(':', 2);
            if (parts.Length != 2 || !Guid.TryParse(parts[1], out var userId))
                return Task.FromResult(AuthenticateResult.Fail("Invalid test auth payload."));

            var claims = new[]
            {
                new Claim(ClaimTypes.Name, "actor"),
                new Claim(ClaimTypes.NameIdentifier, userId.ToString("D")),
                new Claim(ClaimTypes.Role, parts[0]),
            };
            var identity = new ClaimsIdentity(claims, SchemeName);
            return Task.FromResult(AuthenticateResult.Success(
                new AuthenticationTicket(new ClaimsPrincipal(identity), SchemeName)));
        }
    }
}
