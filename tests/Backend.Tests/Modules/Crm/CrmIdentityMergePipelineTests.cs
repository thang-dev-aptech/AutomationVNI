using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text.Encodings.Web;
using System.Text.Json;
using Backend.Data;
using Backend.Modules.Crm.Customers;
using Backend.Modules.Crm.Tags;
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
/// CRM AC crm-identity-merge-test (665a3057): auto-link, no auto-merge, merge/split,
/// unique index, phone extract; revert-to-prove (b).
/// </summary>
public sealed class CrmIdentityMergePipelineTests : IAsyncLifetime
{
    private readonly SqliteConnection _connection;
    private readonly DbContextOptions<AppDbContext> _options;
    private readonly IHost _host;
    private readonly HttpClient _client;

    public CrmIdentityMergePipelineTests()
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
                            o.User.RequireUniqueEmail = true;
                        })
                        .AddRoles<ApplicationRole>()
                        .AddEntityFrameworkStores<AppDbContext>()
                        .AddDefaultTokenProviders();
                    services.AddScoped<UsersService>();
                    services.AddScoped<CrmCustomerService>();
                    services.AddScoped<CrmCustomerCareService>();
                    services.AddScoped<CrmTagService>();
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
    public async Task EnsureLinked_NewIdentity_CreatesOneCustomer_Idempotent()
    {
        var channelId = await SeedChannelAsync("Page A");
        await using var scope = _host.Services.CreateAsyncScope();
        var svc = scope.ServiceProvider.GetRequiredService<CrmCustomerService>();

        var id1 = await svc.EnsureLinkedAsync(
            SocialPlatform.Facebook, channelId, "PSID-1", "Alice", CrmIdentitySource.Message);
        var id2 = await svc.EnsureLinkedAsync(
            SocialPlatform.Facebook, channelId, "PSID-1", "Alice", CrmIdentitySource.Comment);
        var id3 = await svc.EnsureLinkedAsync(
            SocialPlatform.Facebook, channelId, "PSID-1", "Alice Updated", CrmIdentitySource.Message);

        Assert.Equal(id1, id2);
        Assert.Equal(id1, id3);

        await using var db = new AppDbContext(_options);
        Assert.Equal(1, await db.CrmCustomers.CountAsync(x => !x.IsDeleted));
        Assert.Equal(1, await db.CrmCustomerIdentities.CountAsync(x => !x.IsDeleted));
    }

    [Fact]
    public async Task SamePage_MessageAndComment_SameExternalId_OneCustomer()
    {
        var channelId = await SeedChannelAsync("Page A");
        await using var scope = _host.Services.CreateAsyncScope();
        var svc = scope.ServiceProvider.GetRequiredService<CrmCustomerService>();

        var msgCustomer = await svc.EnsureLinkedAsync(
            SocialPlatform.Facebook, channelId, "SAME-PSID", "Bob", CrmIdentitySource.Message);
        var cmtCustomer = await svc.EnsureLinkedAsync(
            SocialPlatform.Facebook, channelId, "SAME-PSID", "Bob", CrmIdentitySource.Comment);

        Assert.Equal(msgCustomer, cmtCustomer);
        await using var db = new AppDbContext(_options);
        Assert.Equal(1, await db.CrmCustomers.CountAsync(x => !x.IsDeleted));
    }

    [Fact]
    public async Task DifferentPage_SameExternalId_DoesNotAutoMerge_AppearsInSuggestions()
    {
        var pageA = await SeedChannelAsync("Page A");
        var pageB = await SeedChannelAsync("Page B");
        await using var scope = _host.Services.CreateAsyncScope();
        var svc = scope.ServiceProvider.GetRequiredService<CrmCustomerService>();

        var a = await svc.EnsureLinkedAsync(
            SocialPlatform.Facebook, pageA, "CROSS-PSID", "Carol", CrmIdentitySource.Message);
        var b = await svc.EnsureLinkedAsync(
            SocialPlatform.Facebook, pageB, "CROSS-PSID", "Carol", CrmIdentitySource.Comment);

        Assert.NotEqual(a, b);

        var suggestions = await svc.ListMergeSuggestionsAsync();
        Assert.Contains(suggestions, s =>
            s.Reason == "same-external-id-different-page"
            && ((s.CustomerAId == a && s.CustomerBId == b)
                || (s.CustomerAId == b && s.CustomerBId == a)));
    }

    [Fact]
    public async Task SameNameOrPhone_DoesNotAutoMerge_AppearsInSuggestions()
    {
        var pageA = await SeedChannelAsync("Page A");
        var pageB = await SeedChannelAsync("Page B");
        await using var scope = _host.Services.CreateAsyncScope();
        var svc = scope.ServiceProvider.GetRequiredService<CrmCustomerService>();

        var a = await svc.EnsureLinkedAsync(
            SocialPlatform.Facebook, pageA, "ID-A", "Nguyen Van A", CrmIdentitySource.Message);
        var b = await svc.EnsureLinkedAsync(
            SocialPlatform.Facebook, pageB, "ID-B", "Nguyen Van A", CrmIdentitySource.Comment);

        Assert.NotEqual(a, b);

        await using (var db = new AppDbContext(_options))
        {
            var ca = await db.CrmCustomers.SingleAsync(x => x.Id == a);
            var cb = await db.CrmCustomers.SingleAsync(x => x.Id == b);
            ca.PhoneE164 = "+84912345678";
            cb.PhoneE164 = "+84912345678";
            await db.SaveChangesAsync();
        }

        var suggestions = await svc.ListMergeSuggestionsAsync();
        Assert.Contains(suggestions, s => s.Reason == "same-name");
        Assert.Contains(suggestions, s => s.Reason == "same-phone" && s.SharedPhoneE164 == "+84912345678");
    }

    /// <summary>
    /// Revert-to-prove (b): nếu auto-gộp khi khác Page cùng ExternalId thì chỉ còn 1 khách;
    /// luật đúng giữ 2 khách + gợi ý.
    /// </summary>
    [Fact]
    public async Task RevertToProve_B_UnsafeCrossPageAutoMergeWouldCollapseCustomers()
    {
        var pageA = await SeedChannelAsync("Page A");
        var pageB = await SeedChannelAsync("Page B");

        // Unsafe: gắn theo ExternalId toàn cục, bỏ SocialChannelId
        async Task<Guid> UnsafeLinkIgnorePageAsync(string externalId, string name, Guid channelId)
        {
            await using var db = new AppDbContext(_options);
            var existing = await db.CrmCustomerIdentities
                .FirstOrDefaultAsync(x => !x.IsDeleted && x.ExternalId == externalId);
            if (existing is not null) return existing.CrmCustomerId;

            var customer = new CrmCustomerModel
            {
                Id = Guid.NewGuid(),
                DisplayName = name,
                CreatedAt = DateTime.UtcNow
            };
            db.CrmCustomers.Add(customer);
            db.CrmCustomerIdentities.Add(new CrmCustomerIdentityModel
            {
                Id = Guid.NewGuid(),
                CrmCustomerId = customer.Id,
                Platform = SocialPlatform.Facebook,
                SocialChannelId = channelId,
                ExternalId = externalId,
                Source = CrmIdentitySource.Message,
                CreatedAt = DateTime.UtcNow
            });
            await db.SaveChangesAsync();
            return customer.Id;
        }

        // Simulate unsafe second link finding first by ExternalId only
        var first = await UnsafeLinkIgnorePageAsync("UNSAFE-PSID", "Dana", pageA);
        await using (var db = new AppDbContext(_options))
        {
            var hit = await db.CrmCustomerIdentities
                .FirstAsync(x => !x.IsDeleted && x.ExternalId == "UNSAFE-PSID");
            // Unsafe "ensure" for page B: reuse customer by ExternalId alone
            db.CrmCustomerIdentities.Add(new CrmCustomerIdentityModel
            {
                Id = Guid.NewGuid(),
                CrmCustomerId = hit.CrmCustomerId,
                Platform = SocialPlatform.Facebook,
                SocialChannelId = pageB,
                ExternalId = "UNSAFE-PSID",
                Source = CrmIdentitySource.Comment,
                CreatedAt = DateTime.UtcNow
            });
            await db.SaveChangesAsync();
        }

        await using (var db = new AppDbContext(_options))
        {
            Assert.Equal(1, await db.CrmCustomers.CountAsync(x => !x.IsDeleted));
            Assert.Equal(2, await db.CrmCustomerIdentities.CountAsync(x =>
                !x.IsDeleted && x.ExternalId == "UNSAFE-PSID"));
        }

        // Safe path: khác Page → 2 khách
        await using var scope = _host.Services.CreateAsyncScope();
        var svc = scope.ServiceProvider.GetRequiredService<CrmCustomerService>();
        var safeA = await svc.EnsureLinkedAsync(
            SocialPlatform.Facebook, pageA, "SAFE-PSID", "Eve", CrmIdentitySource.Message);
        var safeB = await svc.EnsureLinkedAsync(
            SocialPlatform.Facebook, pageB, "SAFE-PSID", "Eve", CrmIdentitySource.Comment);
        Assert.NotEqual(safeA, safeB);
        Assert.NotEqual(first, safeA);
    }

    [Fact]
    public async Task Merge_TransfersIdentitiesNotesRemindersTags_SplitRestores()
    {
        var pageA = await SeedChannelAsync("Page A");
        var pageB = await SeedChannelAsync("Page B");
        await using var scope = _host.Services.CreateAsyncScope();
        var svc = scope.ServiceProvider.GetRequiredService<CrmCustomerService>();

        var keepId = await svc.EnsureLinkedAsync(
            SocialPlatform.Facebook, pageA, "KEEP", "Keep Me", CrmIdentitySource.Message);
        var sourceId = await svc.EnsureLinkedAsync(
            SocialPlatform.Facebook, pageB, "SRC", "Source Me", CrmIdentitySource.Comment);

        Guid noteId, reminderId, tagId;
        await using (var db = new AppDbContext(_options))
        {
            noteId = Guid.NewGuid();
            reminderId = Guid.NewGuid();
            tagId = Guid.NewGuid();
            db.CrmTags.Add(new CrmTagModel
            {
                Id = tagId, Name = "VIP", Color = "#FF0000", CreatedAt = DateTime.UtcNow
            });
            db.CrmCustomerNotes.Add(new CrmCustomerNoteModel
            {
                Id = noteId, CrmCustomerId = sourceId, Body = "note", CreatedAt = DateTime.UtcNow
            });
            db.CrmCustomerReminders.Add(new CrmCustomerReminderModel
            {
                Id = reminderId,
                CrmCustomerId = sourceId,
                Title = "call",
                DueAtUtc = DateTime.UtcNow.AddDays(1),
                CreatedAt = DateTime.UtcNow
            });
            db.CrmCustomerTagLinks.Add(new CrmCustomerTagLinkModel
            {
                Id = Guid.NewGuid(),
                CrmCustomerId = sourceId,
                CrmTagId = tagId,
                CreatedAt = DateTime.UtcNow
            });
            await db.SaveChangesAsync();
        }

        using var mergeReq = new HttpRequestMessage(
            HttpMethod.Post, $"/api/CrmCustomer/{keepId}/merge")
        {
            Content = JsonContent.Create(new { sourceCustomerId = sourceId })
        };
        Authorize(mergeReq, "Admin");
        using var mergeRes = await _client.SendAsync(mergeReq);
        Assert.Equal(HttpStatusCode.OK, mergeRes.StatusCode);
        var mergeJson = await mergeRes.Content.ReadFromJsonAsync<JsonElement>();
        var mergeRecordId = mergeJson.GetProperty("data").GetProperty("mergeRecordId").GetGuid();

        await using (var db = new AppDbContext(_options))
        {
            Assert.False(await db.CrmCustomers.AnyAsync(x => x.Id == sourceId && !x.IsDeleted));
            Assert.Equal(2, await db.CrmCustomerIdentities.CountAsync(x =>
                !x.IsDeleted && x.CrmCustomerId == keepId));
            Assert.Equal(keepId, (await db.CrmCustomerNotes.SingleAsync(x => x.Id == noteId)).CrmCustomerId);
            Assert.Equal(keepId, (await db.CrmCustomerReminders.SingleAsync(x => x.Id == reminderId)).CrmCustomerId);
            Assert.Equal(keepId, (await db.CrmCustomerTagLinks.SingleAsync(x =>
                !x.IsDeleted && x.CrmTagId == tagId)).CrmCustomerId);
            Assert.True(await db.CrmCustomerActionLogs.AnyAsync(x =>
                x.ActionType == "Merge" && x.CrmCustomerId == keepId));
        }

        using var splitReq = new HttpRequestMessage(
            HttpMethod.Post, $"/api/CrmCustomer/merge/{mergeRecordId}/split");
        Authorize(splitReq, "Admin");
        using var splitRes = await _client.SendAsync(splitReq);
        Assert.Equal(HttpStatusCode.OK, splitRes.StatusCode);

        await using (var db = new AppDbContext(_options))
        {
            Assert.True(await db.CrmCustomers.AnyAsync(x => x.Id == sourceId && !x.IsDeleted));
            Assert.Equal(1, await db.CrmCustomerIdentities.CountAsync(x =>
                !x.IsDeleted && x.CrmCustomerId == keepId));
            Assert.Equal(1, await db.CrmCustomerIdentities.CountAsync(x =>
                !x.IsDeleted && x.CrmCustomerId == sourceId));
            Assert.Equal(sourceId, (await db.CrmCustomerNotes.SingleAsync(x => x.Id == noteId)).CrmCustomerId);
            Assert.Equal(sourceId, (await db.CrmCustomerReminders.SingleAsync(x => x.Id == reminderId)).CrmCustomerId);
            Assert.True(await db.CrmCustomerActionLogs.AnyAsync(x => x.ActionType == "Split"));
        }
    }

    [Fact]
    public async Task DuplicateIdentity_UniqueIndex_BlocksSecondRow()
    {
        var channelId = await SeedChannelAsync("Page A");
        await using var scope = _host.Services.CreateAsyncScope();
        var svc = scope.ServiceProvider.GetRequiredService<CrmCustomerService>();
        var customerId = await svc.EnsureLinkedAsync(
            SocialPlatform.Facebook, channelId, "UNIQ", "Frank", CrmIdentitySource.Message);

        await using var db = new AppDbContext(_options);
        db.CrmCustomerIdentities.Add(new CrmCustomerIdentityModel
        {
            Id = Guid.NewGuid(),
            CrmCustomerId = customerId,
            Platform = SocialPlatform.Facebook,
            SocialChannelId = channelId,
            ExternalId = "UNIQ",
            Source = CrmIdentitySource.Comment,
            CreatedAt = DateTime.UtcNow
        });
        await Assert.ThrowsAnyAsync<Exception>(() => db.SaveChangesAsync());
    }

    [Theory]
    [InlineData("0912 345 678", "+84912345678")]
    [InlineData("+84912345678", "+84912345678")]
    [InlineData("0912.345.678", "+84912345678")]
    public void PhoneNormalize_AcceptsMobileVariants(string raw, string expected)
        => Assert.Equal(expected, VietnamesePhoneNormalizer.TryNormalize(raw));

    [Theory]
    [InlineData("5.000.000đ")]
    [InlineData("2026")]
    [InlineData("0241234567")]
    public void PhoneNormalize_RejectsMoneyYearLandline(string raw)
        => Assert.Null(VietnamesePhoneNormalizer.TryNormalize(raw));

    [Fact]
    public async Task PhoneSuggestion_ExtractedButNotAutoSaved_UntilConfirm()
    {
        var channelId = await SeedChannelAsync("Page A");
        await using var scope = _host.Services.CreateAsyncScope();
        var svc = scope.ServiceProvider.GetRequiredService<CrmCustomerService>();
        var customerId = await svc.EnsureLinkedAsync(
            SocialPlatform.Facebook, channelId, "PHONE-1", "Gina", CrmIdentitySource.Message);

        await svc.SuggestPhonesFromTextAsync(customerId, "Liên hệ mình 0912 345 678 nhé");

        await using (var db = new AppDbContext(_options))
        {
            var customer = await db.CrmCustomers.SingleAsync(x => x.Id == customerId);
            Assert.Null(customer.PhoneE164);
            Assert.Equal("+84912345678", (await db.CrmCustomerPhoneSuggestions
                .SingleAsync(x => x.CrmCustomerId == customerId && !x.IsDeleted)).PhoneE164);
        }

        var suggestionId = await new AppDbContext(_options).CrmCustomerPhoneSuggestions
            .Where(x => x.CrmCustomerId == customerId)
            .Select(x => x.Id)
            .SingleAsync();

        using var req = new HttpRequestMessage(
            HttpMethod.Post, $"/api/CrmCustomer/{customerId}/phone/confirm")
        {
            Content = JsonContent.Create(new { suggestionId })
        };
        Authorize(req, "Reviewer");
        using var res = await _client.SendAsync(req);
        Assert.Equal(HttpStatusCode.OK, res.StatusCode);

        await using (var db = new AppDbContext(_options))
        {
            Assert.Equal("+84912345678",
                (await db.CrmCustomers.SingleAsync(x => x.Id == customerId)).PhoneE164);
        }
    }

    [Fact]
    public async Task Backfill_IsIdempotent()
    {
        var channelId = await SeedChannelAsync("Page A");
        await using (var db = new AppDbContext(_options))
        {
            db.PageConversations.Add(new PageConversationModel
            {
                Id = Guid.NewGuid(),
                SocialChannelId = channelId,
                ExternalConversationId = "c1",
                ParticipantExternalId = "BF-1",
                ParticipantName = "Backfill User",
                CreatedAt = DateTime.UtcNow
            });
            var postId = Guid.NewGuid();
            db.SocialPosts.Add(new SocialPostModel
            {
                Id = postId,
                SocialChannelId = channelId,
                Platform = SocialPlatform.Facebook,
                ExternalPostId = "p1",
                CreatedAt = DateTime.UtcNow
            });
            db.SocialComments.Add(new SocialCommentModel
            {
                Id = Guid.NewGuid(),
                SocialChannelId = channelId,
                SocialPostId = postId,
                Platform = SocialPlatform.Facebook,
                ExternalCommentId = "cm1",
                AuthorExternalId = "BF-1",
                AuthorName = "Backfill User",
                Message = "hi",
                CreatedAt = DateTime.UtcNow
            });
            await db.SaveChangesAsync();
        }

        using var req1 = new HttpRequestMessage(HttpMethod.Post, "/api/CrmCustomer/backfill");
        Authorize(req1, "Admin");
        using var res1 = await _client.SendAsync(req1);
        Assert.Equal(HttpStatusCode.OK, res1.StatusCode);

        using var req2 = new HttpRequestMessage(HttpMethod.Post, "/api/CrmCustomer/backfill");
        Authorize(req2, "Admin");
        using var res2 = await _client.SendAsync(req2);
        Assert.Equal(HttpStatusCode.OK, res2.StatusCode);

        await using var check = new AppDbContext(_options);
        Assert.Equal(1, await check.CrmCustomers.CountAsync(x => !x.IsDeleted));
        Assert.Equal(1, await check.CrmCustomerIdentities.CountAsync(x => !x.IsDeleted));
    }

    [Fact]
    public async Task Filter_FindsByNameAndPhone()
    {
        var channelId = await SeedChannelAsync("Page A");
        await using var scope = _host.Services.CreateAsyncScope();
        var svc = scope.ServiceProvider.GetRequiredService<CrmCustomerService>();
        var id = await svc.EnsureLinkedAsync(
            SocialPlatform.Facebook, channelId, "F1", "Searchable Name", CrmIdentitySource.Message);
        await using (var db = new AppDbContext(_options))
        {
            var c = await db.CrmCustomers.SingleAsync(x => x.Id == id);
            c.PhoneE164 = "+84987654321";
            await db.SaveChangesAsync();
        }

        using var req = new HttpRequestMessage(HttpMethod.Post, "/api/CrmCustomer/filter")
        {
            Content = JsonContent.Create(new { keyword = "Searchable", index = 1, size = 20 })
        };
        Authorize(req, "Viewer");
        using var res = await _client.SendAsync(req);
        Assert.Equal(HttpStatusCode.OK, res.StatusCode);
        var json = await res.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(1, json.GetProperty("data").GetProperty("total").GetInt32());
    }

    private static void Authorize(HttpRequestMessage request, string role)
        => request.Headers.Authorization = new AuthenticationHeaderValue(
            TestAuthHandler.SchemeName, $"{role}:actor");

    private async Task<Guid> SeedChannelAsync(string pageName)
    {
        var id = Guid.NewGuid();
        await using var db = new AppDbContext(_options);
        db.SocialChannels.Add(new SocialChannelModel
        {
            Id = id,
            Platform = SocialPlatform.Facebook,
            PageName = pageName,
            ExternalPageId = $"page-{id:N}"[..20],
            AccessToken = "token",
            IsActive = true,
            CreatedAt = DateTime.UtcNow
        });
        await db.SaveChangesAsync();
        return id;
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
            var claims = new[]
            {
                new Claim(ClaimTypes.Name, name),
                new Claim(ClaimTypes.NameIdentifier, Guid.NewGuid().ToString()),
                new Claim(ClaimTypes.Role, role)
            };
            var identity = new ClaimsIdentity(claims, SchemeName);
            return Task.FromResult(AuthenticateResult.Success(
                new AuthenticationTicket(new ClaimsPrincipal(identity), SchemeName)));
        }
    }
}
