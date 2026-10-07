using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using Backend.Data;
using Backend.Modules.Crm.Customers;
using Backend.Modules.Crm.Reminders;
using Backend.Modules.Notification;
using Backend.Modules.PageMessage;
using Backend.Modules.SocialChannel;
using Backend.Modules.SocialChannel.Enums;
using Backend.Modules.SocialComment;
using Backend.Modules.Users;
using Backend.Shared;
using Backend.Shared.Repositories;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
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

/// <summary>CRM AC crm-profile-care-test (925780ff) phần backend.</summary>
public sealed class CrmProfileCarePipelineTests : IAsyncLifetime
{
    private readonly SqliteConnection _connection;
    private readonly DbContextOptions<AppDbContext> _options;
    private readonly IHost _host;
    private readonly HttpClient _client;

    public CrmProfileCarePipelineTests()
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
    public async Task Timeline_MergesMessagesCommentsNotesInOrder()
    {
        var channelId = await SeedChannelAsync();
        await using var scope = _host.Services.CreateAsyncScope();
        var customers = scope.ServiceProvider.GetRequiredService<CrmCustomerService>();
        var care = scope.ServiceProvider.GetRequiredService<CrmCustomerCareService>();

        var customerId = await customers.EnsureLinkedAsync(
            SocialPlatform.Facebook, channelId, "TL-1", "Timeline User", CrmIdentitySource.Message);

        var t1 = DateTime.UtcNow.AddHours(-3);
        var t2 = DateTime.UtcNow.AddHours(-2);
        var t3 = DateTime.UtcNow.AddHours(-1);

        await using (var db = new AppDbContext(_options))
        {
            var convId = Guid.NewGuid();
            db.PageConversations.Add(new PageConversationModel
            {
                Id = convId,
                SocialChannelId = channelId,
                ExternalConversationId = "c-tl",
                ParticipantExternalId = "TL-1",
                CreatedAt = t1
            });
            db.PageMessages.Add(new PageMessageModel
            {
                Id = Guid.NewGuid(),
                PageConversationId = convId,
                SocialChannelId = channelId,
                ExternalMessageId = "m1",
                Text = "hello",
                SentAt = t1,
                CreatedAt = t1
            });
            var postId = Guid.NewGuid();
            db.SocialPosts.Add(new SocialPostModel
            {
                Id = postId,
                SocialChannelId = channelId,
                Platform = SocialPlatform.Facebook,
                ExternalPostId = "p-tl",
                CreatedAt = t2
            });
            db.SocialComments.Add(new SocialCommentModel
            {
                Id = Guid.NewGuid(),
                SocialChannelId = channelId,
                SocialPostId = postId,
                Platform = SocialPlatform.Facebook,
                ExternalCommentId = "cm-tl",
                AuthorExternalId = "TL-1",
                Message = "comment body",
                CommentedAt = t2,
                CreatedAt = t2
            });
            await db.SaveChangesAsync();
        }

        await care.AddNoteAsync(customerId, new CreateCrmCustomerNoteRequest { Body = "note late" });
        await using (var db = new AppDbContext(_options))
        {
            var note = await db.CrmCustomerNotes.SingleAsync(x => x.CrmCustomerId == customerId);
            note.CreatedAt = t3;
            await db.SaveChangesAsync();
        }

        var timeline = await care.GetTimelineAsync(customerId);
        Assert.True(timeline.Count >= 3);
        Assert.Equal("note", timeline[0].Kind);
        Assert.Contains(timeline, x => x.Kind == "customer-message" || x.Kind == "page-message");
        Assert.Contains(timeline, x => x.Kind == "comment");
        for (var i = 1; i < timeline.Count; i++)
            Assert.True(timeline[i - 1].At >= timeline[i].At);
    }

    [Fact]
    public async Task Reminder_NotifyDue_OnceOnly_WhenWorkerRunsTwice()
    {
        var customerId = await CreateCustomerAsync("Notify Me");
        await using var scope = _host.Services.CreateAsyncScope();
        var reminders = scope.ServiceProvider.GetRequiredService<CrmReminderService>();

        await reminders.CreateAsync(new CreateCrmReminderRequest
        {
            CrmCustomerId = customerId,
            Title = "Gọi lại",
            DueAtUtc = DateTime.UtcNow.AddMinutes(-5)
        });

        var n1 = await reminders.NotifyDueAsync();
        var n2 = await reminders.NotifyDueAsync();
        Assert.Equal(1, n1);
        Assert.Equal(0, n2);

        await using var db = new AppDbContext(_options);
        Assert.Equal(1, await db.Set<AppNotificationModel>()
            .CountAsync(x => x.Kind == NotificationKind.CrmReminderDue && !x.IsDeleted));
        Assert.NotNull((await db.CrmCustomerReminders.SingleAsync()).NotifiedAtUtc);
    }

    [Fact]
    public async Task Reminder_Buckets_RespectVietnamDayBoundary()
    {
        var customerId = await CreateCustomerAsync("Bucket User");
        var todayStart = CrmReminderService.VietnamTodayStartUtc();
        await using var scope = _host.Services.CreateAsyncScope();
        var reminders = scope.ServiceProvider.GetRequiredService<CrmReminderService>();
        var userId = Guid.NewGuid();

        await reminders.CreateAsync(new CreateCrmReminderRequest
        {
            CrmCustomerId = customerId,
            Title = "Overdue",
            DueAtUtc = todayStart.AddHours(-2),
            AssigneeUserId = userId
        });
        await reminders.CreateAsync(new CreateCrmReminderRequest
        {
            CrmCustomerId = customerId,
            Title = "Today",
            DueAtUtc = todayStart.AddHours(3),
            AssigneeUserId = userId
        });
        await reminders.CreateAsync(new CreateCrmReminderRequest
        {
            CrmCustomerId = customerId,
            Title = "Upcoming",
            DueAtUtc = todayStart.AddDays(1).AddHours(2),
            AssigneeUserId = userId
        });

        var buckets = await reminders.ListBucketsAsync(userId);
        Assert.Single(buckets.Overdue);
        Assert.Equal("Overdue", buckets.Overdue[0].Title);
        Assert.Single(buckets.Today);
        Assert.Equal("Today", buckets.Today[0].Title);
        Assert.Single(buckets.Upcoming);
        Assert.Equal("Upcoming", buckets.Upcoming[0].Title);
    }

    [Fact]
    public async Task Csv_PreviewDoesNotWrite_CommitCreates_DuplicatePhoneSkipped()
    {
        var existingId = await CreateCustomerAsync("Existing", "+84911111111");

        var csv = "Tên,Số điện thoại,Ghi chú\nMới,0912222222,hello\nTrùng,0911111111,skip\n";
        using var content = new MultipartFormDataContent();
        content.Add(new ByteArrayContent(Encoding.UTF8.GetBytes(csv)), "file", "t.csv");

        using var previewReq = new HttpRequestMessage(HttpMethod.Post, "/api/CrmCustomer/import/preview")
        {
            Content = content
        };
        Authorize(previewReq, "Admin");
        using var previewRes = await _client.SendAsync(previewReq);
        Assert.Equal(HttpStatusCode.OK, previewRes.StatusCode);

        await using (var db = new AppDbContext(_options))
        {
            Assert.Equal(1, await db.CrmCustomers.CountAsync(x => !x.IsDeleted));
        }

        using var content2 = new MultipartFormDataContent();
        content2.Add(new ByteArrayContent(Encoding.UTF8.GetBytes(csv)), "file", "t.csv");
        using var commitReq = new HttpRequestMessage(HttpMethod.Post, "/api/CrmCustomer/import/commit")
        {
            Content = content2
        };
        Authorize(commitReq, "Admin");
        using var commitRes = await _client.SendAsync(commitReq);
        Assert.Equal(HttpStatusCode.OK, commitRes.StatusCode);
        var json = await commitRes.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(1, json.GetProperty("data").GetProperty("created").GetInt32());
        Assert.Equal(1, json.GetProperty("data").GetProperty("skippedDuplicatePhone").GetInt32());

        await using (var db = new AppDbContext(_options))
        {
            Assert.Equal(2, await db.CrmCustomers.CountAsync(x => !x.IsDeleted));
            var kept = await db.CrmCustomers.SingleAsync(x => x.Id == existingId);
            Assert.Equal("+84911111111", kept.PhoneE164);
            Assert.Equal("Existing", kept.DisplayName);
        }
    }

    [Fact]
    public async Task SoftDelete_HidesFromList_HardDelete_RemovesChildrenAndLogs()
    {
        var id = await CreateCustomerAsync("ToDelete");
        await using var scope = _host.Services.CreateAsyncScope();
        var care = scope.ServiceProvider.GetRequiredService<CrmCustomerCareService>();
        var reminders = scope.ServiceProvider.GetRequiredService<CrmReminderService>();
        await care.AddNoteAsync(id, new CreateCrmCustomerNoteRequest { Body = "n" });
        await reminders.CreateAsync(new CreateCrmReminderRequest
        {
            CrmCustomerId = id,
            Title = "r",
            DueAtUtc = DateTime.UtcNow.AddDays(1)
        });

        using var softReq = new HttpRequestMessage(HttpMethod.Delete, $"/api/CrmCustomer/{id}");
        Authorize(softReq, "ContentManager");
        using var softRes = await _client.SendAsync(softReq);
        Assert.Equal(HttpStatusCode.OK, softRes.StatusCode);

        using var filterReq = new HttpRequestMessage(HttpMethod.Post, "/api/CrmCustomer/filter")
        {
            Content = JsonContent.Create(new { index = 1, size = 20 })
        };
        Authorize(filterReq, "Viewer");
        using var filterRes = await _client.SendAsync(filterReq);
        var filterJson = await filterRes.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(0, filterJson.GetProperty("data").GetProperty("total").GetInt32());

        // Revive to hard-delete
        await using (var db = new AppDbContext(_options))
        {
            var c = await db.CrmCustomers.SingleAsync(x => x.Id == id);
            c.IsDeleted = false;
            c.DeletedAt = null;
            await db.SaveChangesAsync();
        }

        using var hardReq = new HttpRequestMessage(HttpMethod.Delete, $"/api/CrmCustomer/{id}/hard");
        Authorize(hardReq, "Admin");
        using var hardRes = await _client.SendAsync(hardReq);
        Assert.Equal(HttpStatusCode.OK, hardRes.StatusCode);

        await using (var db = new AppDbContext(_options))
        {
            Assert.False(await db.CrmCustomers.AnyAsync(x => x.Id == id));
            Assert.Equal(0, await db.CrmCustomerNotes.CountAsync(x => x.CrmCustomerId == id));
            Assert.Equal(0, await db.CrmCustomerReminders.CountAsync(x => x.CrmCustomerId == id));
            Assert.True(await db.CrmCustomerActionLogs.AnyAsync(x => x.ActionType == "HardDelete"));
        }
    }

    [Fact]
    public async Task ExportCsv_WritesAuditLog()
    {
        await CreateCustomerAsync("Export Me");
        using var req = new HttpRequestMessage(HttpMethod.Get, "/api/CrmCustomer/export");
        Authorize(req, "Reviewer");
        using var res = await _client.SendAsync(req);
        Assert.Equal(HttpStatusCode.OK, res.StatusCode);
        await using var db = new AppDbContext(_options);
        Assert.True(await db.CrmCustomerActionLogs.AnyAsync(x => x.ActionType == "CsvExport"));
    }

    private async Task<Guid> CreateCustomerAsync(string name, string? phone = null)
    {
        await using var scope = _host.Services.CreateAsyncScope();
        var care = scope.ServiceProvider.GetRequiredService<CrmCustomerCareService>();
        var created = await care.CreateManualAsync(new CreateCrmCustomerRequest
        {
            DisplayName = name,
            PhoneE164 = phone
        });
        return created.Id;
    }

    private async Task<Guid> SeedChannelAsync()
    {
        var id = Guid.NewGuid();
        await using var db = new AppDbContext(_options);
        db.SocialChannels.Add(new SocialChannelModel
        {
            Id = id,
            Platform = SocialPlatform.Facebook,
            PageName = "Page",
            ExternalPageId = $"p-{id:N}"[..16],
            AccessToken = "t",
            IsActive = true,
            CreatedAt = DateTime.UtcNow
        });
        await db.SaveChangesAsync();
        return id;
    }

    private static void Authorize(HttpRequestMessage request, string role)
        => request.Headers.Authorization = new AuthenticationHeaderValue(
            TestAuthHandler.SchemeName, $"{role}:actor");

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
            var identity = new ClaimsIdentity(new[]
            {
                new Claim(ClaimTypes.Name, name),
                new Claim(ClaimTypes.NameIdentifier, Guid.NewGuid().ToString()),
                new Claim(ClaimTypes.Role, role)
            }, SchemeName);
            return Task.FromResult(AuthenticateResult.Success(
                new AuthenticationTicket(new ClaimsPrincipal(identity), SchemeName)));
        }
    }
}
