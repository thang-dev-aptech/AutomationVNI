using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using Backend.Data;
using Backend.Modules.Crm.Customers;
using Backend.Modules.Crm.Opportunities;
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

/// <summary>AC 176ae13f (a)(b) + AC 894baa0e (d) Activity: nhắc việc/ghi chú gắn Cơ hội.</summary>
public sealed class CrmOpportunityActivityLinksTests : IAsyncLifetime
{
    private readonly SqliteConnection _connection;
    private readonly DbContextOptions<AppDbContext> _options;
    private readonly IHost _host;
    private readonly HttpClient _client;
    private static readonly Guid Stage = CrmOpportunityStageIds.Moi;

    public CrmOpportunityActivityLinksTests()
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
                    services.AddScoped<CrmOpportunityService>();
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
    public async Task Reminder_WithOpportunityOfOtherCustomer_Is400_AndNoRow()
    {
        var a = await SeedCustomerAsync("A");
        var b = await SeedCustomerAsync("B");
        var oppOfB = await SeedOppAsync(b);

        var res = await PostAsync("/api/CrmReminder", new
        {
            crmCustomerId = a, crmOpportunityId = oppOfB, title = "Gọi", dueAtUtc = DateTime.UtcNow.AddDays(1)
        });

        Assert.Equal(HttpStatusCode.BadRequest, res.StatusCode);
        await using var db = new AppDbContext(_options);
        Assert.Equal(0, await db.CrmCustomerReminders.CountAsync());
    }

    [Fact]
    public async Task Reminder_WithMissingOrDeletedOpportunity_Is400()
    {
        var a = await SeedCustomerAsync("A");
        var deleted = await SeedOppAsync(a, isDeleted: true);

        foreach (var oppId in new[] { Guid.NewGuid(), deleted })
        {
            var res = await PostAsync("/api/CrmReminder", new
            {
                crmCustomerId = a, crmOpportunityId = oppId, title = "Gọi", dueAtUtc = DateTime.UtcNow.AddDays(1)
            });
            Assert.Equal(HttpStatusCode.BadRequest, res.StatusCode);
        }
    }

    [Fact]
    public async Task Reminder_SameCustomerOpportunity_Ok_TouchesLastActivity_AndResponseHasOpportunity()
    {
        var a = await SeedCustomerAsync("A");
        var opp = await SeedOppAsync(a, title: "Cơ hội A", lastActivity: DateTime.UtcNow.AddDays(-10));
        var before = DateTime.UtcNow.AddSeconds(-1);

        var res = await PostAsync("/api/CrmReminder", new
        {
            crmCustomerId = a, crmOpportunityId = opp, title = "Gọi", dueAtUtc = DateTime.UtcNow.AddDays(1)
        });

        Assert.Equal(HttpStatusCode.OK, res.StatusCode);
        var data = (await ReadAsync(res)).GetProperty("data");
        Assert.Equal(opp, data.GetProperty("crmOpportunityId").GetGuid());
        Assert.Equal("Cơ hội A", data.GetProperty("opportunityTitle").GetString());
        await using var db = new AppDbContext(_options);
        Assert.True((await db.CrmOpportunities.SingleAsync()).LastActivityAtUtc >= before);
    }

    [Fact]
    public async Task CompleteReminder_AndAddNote_WithOpportunity_TouchLastActivity()
    {
        var a = await SeedCustomerAsync("A");
        var opp = await SeedOppAsync(a, lastActivity: DateTime.UtcNow.AddDays(-10));
        var reminderId = await SeedReminderAsync(a, opp);

        await SetLastActivityAsync(opp, DateTime.UtcNow.AddDays(-10));
        var t0 = DateTime.UtcNow.AddSeconds(-1);
        Assert.Equal(HttpStatusCode.OK, (await PostAsync($"/api/CrmReminder/{reminderId}/complete", new { })).StatusCode);
        Assert.True(await GetLastActivityAsync(opp) >= t0);

        await SetLastActivityAsync(opp, DateTime.UtcNow.AddDays(-10));
        var t1 = DateTime.UtcNow.AddSeconds(-1);
        var noteRes = await PostAsync($"/api/CrmCustomer/{a}/notes", new { body = "Ghi chú", crmOpportunityId = opp });
        Assert.Equal(HttpStatusCode.OK, noteRes.StatusCode);
        Assert.Equal(opp, (await ReadAsync(noteRes)).GetProperty("data").GetProperty("crmOpportunityId").GetGuid());
        Assert.True(await GetLastActivityAsync(opp) >= t1);
    }

    [Fact]
    public async Task Note_WithOpportunityOfOtherCustomer_Is400_AndNoRow()
    {
        var a = await SeedCustomerAsync("A");
        var b = await SeedCustomerAsync("B");
        var oppOfB = await SeedOppAsync(b);

        var res = await PostAsync($"/api/CrmCustomer/{a}/notes", new { body = "x", crmOpportunityId = oppOfB });

        Assert.Equal(HttpStatusCode.BadRequest, res.StatusCode);
        await using var db = new AppDbContext(_options);
        Assert.Equal(0, await db.CrmCustomerNotes.CountAsync());
    }

    [Fact]
    public async Task LegacyReminderWithoutOpportunity_StillInBuckets_AndNotInByOpportunity()
    {
        var a = await SeedCustomerAsync("A");
        var opp = await SeedOppAsync(a);
        await SeedReminderAsync(a, null, title: "Cũ", due: DateTime.UtcNow.AddDays(3));
        await SeedReminderAsync(a, opp, title: "Mới", due: DateTime.UtcNow.AddDays(4));

        var buckets = (await ReadAsync(await GetAsync("/api/CrmReminder/buckets?all=true"))).GetProperty("data");
        var titles = buckets.GetProperty("upcoming").EnumerateArray().Select(x => x.GetProperty("title").GetString()).ToList();
        Assert.Equal(["Cũ", "Mới"], titles);

        var byOpp = (await ReadAsync(await GetAsync($"/api/CrmReminder/by-opportunity/{opp}"))).GetProperty("data")
            .EnumerateArray().ToList();
        Assert.Single(byOpp);
        Assert.Equal("Mới", byOpp[0].GetProperty("title").GetString());
        Assert.Equal(HttpStatusCode.NotFound, (await GetAsync($"/api/CrmReminder/by-opportunity/{Guid.NewGuid()}")).StatusCode);
    }

    [Fact]
    public async Task Stats_Activity_CountsLinkedNotesOnly_NotRemindersOrDeletedOrUnlinked()
    {
        var a = await SeedCustomerAsync("A");
        var o1 = await SeedOppAsync(a);
        var o2 = await SeedOppAsync(a);
        await SeedReminderAsync(a, o1);
        await SeedReminderAsync(a, o1);
        await SeedReminderAsync(a, o2);
        await SeedReminderAsync(a, o2);
        await SeedReminderAsync(a, null);                 // không gắn cơ hội
        await SeedReminderAsync(a, o2, isDeleted: true);  // đã xoá
        await SeedNoteAsync(a, o1);
        await SeedNoteAsync(a, null);                     // không gắn cơ hội
        await SeedNoteAsync(a, o2, isDeleted: true);      // đã xoá — không đếm

        var stats = (await ReadAsync(await PostAsync("/api/CrmOpportunity/stats", new { }))).GetProperty("data");

        // Giữ ô Hoạt động = chỉ ghi chú gắn cơ hội (4 nhắc + 1 ghi chú gắn → Activity = 1).
        Assert.Equal(1, stats.GetProperty("activity").GetInt32());
    }

    // ── helpers ──────────────────────────────────────────────────────────────
    private Task<HttpResponseMessage> PostAsync(string url, object body)
    {
        var req = new HttpRequestMessage(HttpMethod.Post, url) { Content = JsonContent.Create(body) };
        req.Headers.Authorization = new AuthenticationHeaderValue(TestAuthHandler.SchemeName, "Admin:actor");
        return _client.SendAsync(req);
    }

    private Task<HttpResponseMessage> GetAsync(string url)
    {
        var req = new HttpRequestMessage(HttpMethod.Get, url);
        req.Headers.Authorization = new AuthenticationHeaderValue(TestAuthHandler.SchemeName, "Admin:actor");
        return _client.SendAsync(req);
    }

    private static async Task<JsonElement> ReadAsync(HttpResponseMessage res)
        => JsonDocument.Parse(await res.Content.ReadAsStringAsync()).RootElement.Clone();

    private async Task<Guid> SeedCustomerAsync(string name)
    {
        await using var db = new AppDbContext(_options);
        var id = Guid.NewGuid();
        db.CrmCustomers.Add(new CrmCustomerModel { Id = id, DisplayName = name, CreatedAt = DateTime.UtcNow, CreatedBy = "seed" });
        await db.SaveChangesAsync();
        return id;
    }

    private async Task<Guid> SeedOppAsync(Guid customerId, string title = "Cơ hội", DateTime? lastActivity = null, bool isDeleted = false)
    {
        await using var db = new AppDbContext(_options);
        var id = Guid.NewGuid();
        db.CrmOpportunities.Add(new CrmOpportunityModel
        {
            Id = id, CrmCustomerId = customerId, Title = title, StageId = Stage, Status = CrmOpportunityStatus.Open,
            LastActivityAtUtc = lastActivity, IsDeleted = isDeleted, CreatedAt = DateTime.UtcNow, CreatedBy = "seed"
        });
        await db.SaveChangesAsync();
        return id;
    }

    private async Task<Guid> SeedReminderAsync(Guid customerId, Guid? oppId, string title = "R", DateTime? due = null, bool isDeleted = false)
    {
        await using var db = new AppDbContext(_options);
        var id = Guid.NewGuid();
        db.CrmCustomerReminders.Add(new CrmCustomerReminderModel
        {
            Id = id, CrmCustomerId = customerId, CrmOpportunityId = oppId, Title = title,
            DueAtUtc = due ?? DateTime.UtcNow.AddDays(1), IsDeleted = isDeleted, CreatedAt = DateTime.UtcNow, CreatedBy = "seed"
        });
        await db.SaveChangesAsync();
        return id;
    }

    private async Task SeedNoteAsync(Guid customerId, Guid? oppId, bool isDeleted = false)
    {
        await using var db = new AppDbContext(_options);
        db.CrmCustomerNotes.Add(new CrmCustomerNoteModel
        {
            Id = Guid.NewGuid(), CrmCustomerId = customerId, CrmOpportunityId = oppId, Body = "n",
            IsDeleted = isDeleted, CreatedAt = DateTime.UtcNow, CreatedBy = "seed"
        });
        await db.SaveChangesAsync();
    }

    private async Task SetLastActivityAsync(Guid oppId, DateTime value)
    {
        await using var db = new AppDbContext(_options);
        (await db.CrmOpportunities.SingleAsync(x => x.Id == oppId)).LastActivityAtUtc = value;
        await db.SaveChangesAsync();
    }

    private async Task<DateTime?> GetLastActivityAsync(Guid oppId)
    {
        await using var db = new AppDbContext(_options);
        return (await db.CrmOpportunities.AsNoTracking().SingleAsync(x => x.Id == oppId)).LastActivityAtUtc;
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
