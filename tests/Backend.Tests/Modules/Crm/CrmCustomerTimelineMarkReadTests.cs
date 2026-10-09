using System.Net.Http.Headers;
using System.Security.Claims;
using System.Text.Encodings.Web;
using Backend.Data;
using Backend.Modules.Crm.Customers;
using Backend.Modules.Crm.Reminders;
using Backend.Modules.Notification;
using Backend.Modules.PageMessage;
using Backend.Modules.SocialChannel;
using Backend.Modules.SocialChannel.Enums;
using Backend.Modules.SocialComment;
using Backend.Modules.SocialComment.Enums;
using Backend.Modules.Users;
using Backend.Shared;
using Backend.Shared.Repositories;
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
/// AC 25365fcd (a)-(c): MarkRead không vào timeline; nhãn tiếng Việt; log vẫn giữ.
/// Revert-to-prove: bỏ lọc MarkRead trên CommentActionLogs → (a) đỏ.
/// </summary>
public sealed class CrmCustomerTimelineMarkReadTests : IAsyncLifetime
{
    private readonly SqliteConnection _connection;
    private readonly DbContextOptions<AppDbContext> _options;
    private readonly IHost _host;

    public CrmCustomerTimelineMarkReadTests()
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
                    services.AddScoped<CrmCustomerCareService>();
                    services.AddScoped<CrmReminderService>();
                    services.AddScoped<NotificationService>();
                    services.AddAuthentication(TestAuthHandler.SchemeName)
                        .AddScheme<AuthenticationSchemeOptions, TestAuthHandler>(
                            TestAuthHandler.SchemeName, _ => { });
                    services.AddAuthorization();
                    services.AddControllers()
                        .AddApplicationPart(typeof(CrmCustomerController).Assembly);
                });
                web.Configure(app =>
                {
                    app.UseRouting();
                    app.UseAuthentication();
                    app.UseAuthorization();
                    app.UseEndpoints(e => e.MapControllers());
                });
            })
            .Start();
    }

    public Task InitializeAsync() => Task.CompletedTask;

    public async Task DisposeAsync()
    {
        await _host.StopAsync();
        _host.Dispose();
        await _connection.DisposeAsync();
    }

    [Fact]
    public async Task A_Timeline_ExcludesMarkRead_KeepsReplyHideAssign()
    {
        var (customerId, _) = await SeedCustomerWithMarkReadNoiseAsync();

        await using var scope = _host.Services.CreateAsyncScope();
        var care = scope.ServiceProvider.GetRequiredService<CrmCustomerCareService>();
        var timeline = await care.GetTimelineAsync(customerId);

        // 3 MarkRead comment + 2 MarkRead message đã seed → timeline chỉ còn Reply, Hide, Assign.
        var commentActions = timeline.Where(x => x.Kind == "comment-action").ToList();
        Assert.Equal(2, commentActions.Count);
        Assert.DoesNotContain(timeline, x =>
            x.Title.Contains("MarkRead", StringComparison.OrdinalIgnoreCase)
            || x.Title.Contains("(MarkRead)", StringComparison.Ordinal));

        Assert.Contains(timeline, x => x.Kind == "comment-action" && x.Title == "Trả lời bình luận");
        Assert.Contains(timeline, x => x.Kind == "comment-action" && x.Title == "Ẩn bình luận");
        Assert.Contains(timeline, x => x.Kind == "assign" && x.Title.Contains("Đổi người phụ trách", StringComparison.Ordinal));
        Assert.Equal(1, timeline.Count(x => x.Kind == "assign"));
    }

    [Fact]
    public async Task B_CommentAction_Titles_AreVietnamese_NotRawEnum()
    {
        Assert.Equal("Trả lời bình luận", CrmCustomerCareService.CommentActionTimelineTitle(CommentActionType.Reply));
        Assert.Equal("Ẩn bình luận", CrmCustomerCareService.CommentActionTimelineTitle(CommentActionType.Hide));
        Assert.Equal("Hiện bình luận", CrmCustomerCareService.CommentActionTimelineTitle(CommentActionType.Unhide));
        Assert.Equal("Xoá bình luận", CrmCustomerCareService.CommentActionTimelineTitle(CommentActionType.Delete));
        Assert.Equal("Duyệt bình luận", CrmCustomerCareService.CommentActionTimelineTitle(CommentActionType.ApprovePending));
        Assert.Equal("Đổi trạng thái", CrmCustomerCareService.CommentActionTimelineTitle(CommentActionType.SetStatus));
        Assert.Equal("Giao phụ trách", CrmCustomerCareService.CommentActionTimelineTitle(CommentActionType.Assign));
        Assert.Equal("Thêm ghi chú", CrmCustomerCareService.CommentActionTimelineTitle(CommentActionType.AddNote));
        Assert.Equal("Bỏ qua bình luận chờ duyệt", CrmCustomerCareService.CommentActionTimelineTitle(CommentActionType.IgnorePending));
        Assert.Equal("Thao tác bình luận", CrmCustomerCareService.CommentActionTimelineTitle(CommentActionType.MarkRead));

        var (customerId, _) = await SeedCustomerWithMarkReadNoiseAsync();
        await using var scope = _host.Services.CreateAsyncScope();
        var care = scope.ServiceProvider.GetRequiredService<CrmCustomerCareService>();
        var timeline = await care.GetTimelineAsync(customerId);

        Assert.DoesNotContain(timeline, x => x.Title.Contains("(Reply)", StringComparison.Ordinal));
        Assert.DoesNotContain(timeline, x => x.Title.Contains("(Hide)", StringComparison.Ordinal));
        Assert.DoesNotContain(timeline, x => x.Title.Contains("(MarkRead)", StringComparison.Ordinal));
    }

    [Fact]
    public async Task C_MarkRead_LogsRemainInDb_AfterTimeline()
    {
        var (customerId, _) = await SeedCustomerWithMarkReadNoiseAsync();

        await using var scope = _host.Services.CreateAsyncScope();
        var care = scope.ServiceProvider.GetRequiredService<CrmCustomerCareService>();
        _ = await care.GetTimelineAsync(customerId);

        await using var db = new AppDbContext(_options);
        Assert.Equal(3, await db.CommentActionLogs.CountAsync(x => x.ActionType == CommentActionType.MarkRead));
        Assert.Equal(2, await db.MessageActionLogs.CountAsync(x => x.ActionType == MessageActionType.MarkRead));
        Assert.Equal(5,
            await db.CommentActionLogs.CountAsync(x => x.ActionType == CommentActionType.MarkRead)
            + await db.MessageActionLogs.CountAsync(x => x.ActionType == MessageActionType.MarkRead));
    }

    [Fact]
    public async Task D_HttpTimeline_AlsoExcludesMarkRead()
    {
        var (customerId, _) = await SeedCustomerWithMarkReadNoiseAsync();
        var client = _host.GetTestClient();
        using var req = new HttpRequestMessage(HttpMethod.Get, $"/api/CrmCustomer/{customerId}/timeline");
        req.Headers.Authorization = new AuthenticationHeaderValue(TestAuthHandler.SchemeName, "Admin:actor");
        using var res = await client.SendAsync(req);
        res.EnsureSuccessStatusCode();
        var json = await res.Content.ReadAsStringAsync();
        Assert.DoesNotContain("MarkRead", json, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Trả lời bình luận", json, StringComparison.Ordinal);
        Assert.Contains("Ẩn bình luận", json, StringComparison.Ordinal);
    }

    private async Task<(Guid CustomerId, Guid ChannelId)> SeedCustomerWithMarkReadNoiseAsync()
    {
        var channelId = Guid.NewGuid();
        await using (var db = new AppDbContext(_options))
        {
            db.SocialChannels.Add(new SocialChannelModel
            {
                Id = channelId,
                Platform = SocialPlatform.Facebook,
                PageName = "Bán qần áo",
                ExternalPageId = $"p-{channelId:N}"[..16],
                AccessToken = "t",
                IsActive = true,
                CreatedAt = DateTime.UtcNow
            });
            await db.SaveChangesAsync();
        }

        await using var scope = _host.Services.CreateAsyncScope();
        var customers = scope.ServiceProvider.GetRequiredService<CrmCustomerService>();
        var customerId = await customers.EnsureLinkedAsync(
            SocialPlatform.Facebook, channelId, "ext-ban-quan-ao", "Bán qần áo", CrmIdentitySource.Comment);

        var now = DateTime.UtcNow;
        await using (var db = new AppDbContext(_options))
        {
            var convId = Guid.NewGuid();
            db.PageConversations.Add(new PageConversationModel
            {
                Id = convId,
                SocialChannelId = channelId,
                ExternalConversationId = "c-ban",
                ParticipantExternalId = "ext-ban-quan-ao",
                CreatedAt = now.AddHours(-5)
            });
            db.PageMessages.Add(new PageMessageModel
            {
                Id = Guid.NewGuid(),
                PageConversationId = convId,
                SocialChannelId = channelId,
                ExternalMessageId = "m-ban",
                Text = "xin chào",
                SentAt = now.AddHours(-4),
                CreatedAt = now.AddHours(-4)
            });
            for (var i = 0; i < 2; i++)
            {
                db.MessageActionLogs.Add(new MessageActionLogModel
                {
                    Id = Guid.NewGuid(),
                    PageConversationId = convId,
                    ActionType = MessageActionType.MarkRead,
                    ActorUserName = "admin",
                    CreatedAt = now.AddHours(-3).AddMinutes(i)
                });
            }

            db.MessageActionLogs.Add(new MessageActionLogModel
            {
                Id = Guid.NewGuid(),
                PageConversationId = convId,
                ActionType = MessageActionType.Assign,
                ActorUserName = "admin",
                PayloadJson = "{\"to\":\"x\"}",
                CreatedAt = now.AddHours(-2)
            });

            var postId = Guid.NewGuid();
            db.SocialPosts.Add(new SocialPostModel
            {
                Id = postId,
                SocialChannelId = channelId,
                Platform = SocialPlatform.Facebook,
                ExternalPostId = "p-ban",
                CreatedAt = now.AddHours(-5)
            });
            var commentId = Guid.NewGuid();
            db.SocialComments.Add(new SocialCommentModel
            {
                Id = commentId,
                SocialChannelId = channelId,
                SocialPostId = postId,
                Platform = SocialPlatform.Facebook,
                ExternalCommentId = "cm-ban",
                AuthorExternalId = "ext-ban-quan-ao",
                Message = "quan ao dep",
                CommentedAt = now.AddHours(-4),
                CreatedAt = now.AddHours(-4)
            });

            for (var i = 0; i < 3; i++)
            {
                db.CommentActionLogs.Add(new CommentActionLogModel
                {
                    Id = Guid.NewGuid(),
                    SocialCommentId = commentId,
                    ActionType = CommentActionType.MarkRead,
                    ActorUserName = "admin",
                    CreatedAt = now.AddHours(-3).AddMinutes(i)
                });
            }

            db.CommentActionLogs.Add(new CommentActionLogModel
            {
                Id = Guid.NewGuid(),
                SocialCommentId = commentId,
                ActionType = CommentActionType.Reply,
                ActorUserName = "admin",
                CreatedAt = now.AddHours(-1)
            });
            db.CommentActionLogs.Add(new CommentActionLogModel
            {
                Id = Guid.NewGuid(),
                SocialCommentId = commentId,
                ActionType = CommentActionType.Hide,
                ActorUserName = "admin",
                CreatedAt = now.AddMinutes(-30)
            });

            await db.SaveChangesAsync();
        }

        return (customerId, channelId);
    }

    private sealed class TestAuthHandler(
        IOptionsMonitor<AuthenticationSchemeOptions> options,
        ILoggerFactory logger,
        UrlEncoder encoder) : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
    {
        public const string SchemeName = "Test";

        protected override Task<AuthenticateResult> HandleAuthenticateAsync()
        {
            var header = Request.Headers.Authorization.ToString();
            if (string.IsNullOrWhiteSpace(header) || !header.StartsWith(SchemeName, StringComparison.Ordinal))
                return Task.FromResult(AuthenticateResult.NoResult());
            var payload = header[(SchemeName.Length + 1)..];
            var parts = payload.Split(':', 2);
            var role = parts[0];
            var name = parts.Length > 1 ? parts[1] : "actor";
            var identity = new ClaimsIdentity(
            [
                new Claim(ClaimTypes.Name, name),
                new Claim(ClaimTypes.NameIdentifier, Guid.NewGuid().ToString()),
                new Claim(ClaimTypes.Role, role)
            ], SchemeName);
            return Task.FromResult(AuthenticateResult.Success(
                new AuthenticationTicket(new ClaimsPrincipal(identity), SchemeName)));
        }
    }
}
