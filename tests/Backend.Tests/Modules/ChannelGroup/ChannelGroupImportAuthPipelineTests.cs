using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using Backend.Data;
using Backend.Modules.ChannelGroup;
using Backend.Modules.SocialChannel;
using Backend.Modules.SocialChannel.Enums;
using Backend.Shared;
using Backend.Shared.Repositories;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Xunit;

namespace Backend.Tests.Modules.ChannelGroup;

/// <summary>
/// CHANNEL-GROUP-01 AC channel-group-import-backend-test (a9363817):
/// HTTP pipeline import/preview + import/commit + template; revert-to-prove (d),(f).
/// </summary>
public sealed class ChannelGroupImportAuthPipelineTests : IAsyncLifetime
{
    private readonly SqliteConnection _connection;
    private readonly DbContextOptions<AppDbContext> _options;
    private readonly IHost _host;
    private readonly HttpClient _client;
    private readonly FailAfterNSavesInterceptor _failInterceptor = new();

    public ChannelGroupImportAuthPipelineTests()
    {
        _connection = new SqliteConnection("DataSource=:memory:");
        _connection.Open();
        _options = new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlite(_connection)
            .AddInterceptors(_failInterceptor)
            .Options;
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
                    services.AddScoped<ChannelGroupRepository>();
                    services.AddScoped<ChannelGroupImportService>();
                    services.AddLogging();
                    services.AddAuthentication(TestAuthHandler.SchemeName)
                        .AddScheme<AuthenticationSchemeOptions, TestAuthHandler>(
                            TestAuthHandler.SchemeName, _ => { });
                    services.AddAuthorization();
                    services.AddControllers(o => o.Filters.Add<GlobalExceptionFilter>())
                        .AddApplicationPart(typeof(ChannelGroupImportController).Assembly);
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

    // --- (a) auth ---

    [Theory]
    [InlineData("Viewer")]
    [InlineData("Reviewer")]
    public async Task Import_ForbiddenRoles_Return403_AndDoNotPersist(string role)
    {
        var a = await SeedChannelAsync("A", "ext-a");
        var before = await CountGroupsAsync();
        var csv = "Tên nhóm,ID Page\nNhóm mới,ext-a\n";

        using var preview = await SendImportAsync(HttpMethod.Post, "/api/ChannelGroup/import/preview",
            csv, "merge", role);
        Assert.Equal(HttpStatusCode.Forbidden, preview.StatusCode);
        Assert.Equal(before, await CountGroupsAsync());

        using var commit = await SendImportAsync(HttpMethod.Post, "/api/ChannelGroup/import/commit",
            csv, "merge", role);
        Assert.Equal(HttpStatusCode.Forbidden, commit.StatusCode);
        Assert.Equal(before, await CountGroupsAsync());
        Assert.Equal(0, await CountMembersAsync(a)); // channel not in any group
        _ = a;
    }

    [Fact]
    public async Task Import_Unauthenticated_Returns401()
    {
        var csv = "Tên nhóm,ID Page\nX,1\n";
        using var preview = await SendImportAsync(HttpMethod.Post, "/api/ChannelGroup/import/preview",
            csv, "merge", role: null);
        Assert.Equal(HttpStatusCode.Unauthorized, preview.StatusCode);

        using var commit = await SendImportAsync(HttpMethod.Post, "/api/ChannelGroup/import/commit",
            csv, "merge", role: null);
        Assert.Equal(HttpStatusCode.Unauthorized, commit.StatusCode);
    }

    // --- (b) CSV formats ---

    [Theory]
    [InlineData(true, ',')]
    [InlineData(false, ',')]
    [InlineData(true, ';')]
    [InlineData(false, ';')]
    public async Task Preview_ReadsBom_Delimiter_AndCaseInsensitiveHeader(bool withBom, char delimiter)
    {
        await SeedChannelAsync("Page VN", "page-vn-1");
        var sep = delimiter.ToString();
        var header = string.Join(sep, "tÊN nHóm", "id page", "Mô tả");
        var row = string.Join(sep, "Miền Bắc", "page-vn-1", "Nhóm miền Bắc");
        var csv = header + "\n" + row + "\n";
        if (withBom) csv = "\uFEFF" + csv;

        using var response = await SendImportAsync(HttpMethod.Post, "/api/ChannelGroup/import/preview",
            csv, "merge", "Admin");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var data = await ReadDataAsync(response);
        Assert.Equal(1, data.GetProperty("validRowCount").GetInt32());
        var group = data.GetProperty("groups")[0];
        Assert.Equal("Miền Bắc", group.GetProperty("name").GetString());
        Assert.True(group.GetProperty("isNew").GetBoolean());
        Assert.Equal(0, data.GetProperty("errors").GetArrayLength());
    }

    [Fact]
    public async Task Preview_QuotedFields_AndVietnameseNamePreserved()
    {
        await SeedChannelAsync("P", "ext-q");
        var csv = "Tên nhóm,ID Page,Mô tả\n\"Nhóm \"\"Đặc biệt\"\"\",\"ext-q\",\"Mô tả, có dấu phẩy\"\n";

        using var response = await SendImportAsync(HttpMethod.Post, "/api/ChannelGroup/import/preview",
            csv, "merge", "ContentManager");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var data = await ReadDataAsync(response);
        Assert.Equal("Nhóm \"Đặc biệt\"", data.GetProperty("groups")[0].GetProperty("name").GetString());
    }

    // --- (c) validation limits ---

    [Fact]
    public async Task Preview_MissingRequiredColumns_Returns400()
    {
        var csv = "Foo,Bar\nx,y\n";
        using var response = await SendImportAsync(HttpMethod.Post, "/api/ChannelGroup/import/preview",
            csv, "merge", "Admin");
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(0, await CountGroupsAsync());
    }

    [Fact]
    public async Task Preview_TooManyRows_Returns400()
    {
        await SeedChannelAsync("P", "ext-many");
        var sb = new StringBuilder();
        sb.AppendLine("Tên nhóm,ID Page");
        for (var i = 0; i < ChannelGroupImportService.MaxDataRows + 1; i++)
            sb.AppendLine($"G{i},ext-many");

        using var response = await SendImportAsync(HttpMethod.Post, "/api/ChannelGroup/import/preview",
            sb.ToString(), "merge", "Admin");
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(0, await CountGroupsAsync());
    }

    [Fact]
    public async Task Preview_FileTooLarge_Returns400()
    {
        var big = new string('x', (int)ChannelGroupImportService.MaxFileBytes + 100);
        var csv = "Tên nhóm,ID Page\n" + big + ",ext\n";
        using var response = await SendImportAsync(HttpMethod.Post, "/api/ChannelGroup/import/preview",
            csv, "merge", "Admin", fileName: "big.csv");
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(0, await CountGroupsAsync());
    }

    [Fact]
    public async Task Preview_NonCsvFile_Returns400()
    {
        using var content = new MultipartFormDataContent();
        var bytes = Encoding.UTF8.GetBytes("not csv");
        var fileContent = new ByteArrayContent(bytes);
        fileContent.Headers.ContentType = new MediaTypeHeaderValue("application/vnd.ms-excel");
        content.Add(fileContent, "file", "data.xlsx");
        content.Add(new StringContent("merge"), "mode");

        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/ChannelGroup/import/preview")
        {
            Content = content
        };
        Authorize(request, "Admin");
        using var response = await _client.SendAsync(request);
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    // --- (d) preview errors + no DB write + revert-to-prove ---

    [Fact]
    public async Task Preview_ListsRowErrors_AndDoesNotWriteDb()
    {
        var live = await SeedChannelAsync("Live", "ext-live");
        await SeedChannelAsync("Dup1", "ext-dup");
        await SeedChannelAsync("Dup2", "ext-dup"); // ambiguous
        var deleted = await SeedChannelAsync("Gone", "ext-gone", isDeleted: true);
        await SeedGroupDirectAsync("Giữ nguyên", [live]);
        var beforeGroups = await CountGroupsAsync();
        var beforeMembers = await CountMembersForGroupNameAsync("Giữ nguyên");

        var csv = """
            Tên nhóm,ID Page
            ,ext-live
            EmptyId,
            Missing,ext-missing
            Soft,ext-gone
            Ambiguous,ext-dup
            Giữ nguyên,ext-live
            Giữ nguyên,ext-live
            """;

        using var response = await SendImportAsync(HttpMethod.Post, "/api/ChannelGroup/import/preview",
            csv, "merge", "Admin");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var data = await ReadDataAsync(response);
        var errors = data.GetProperty("errors").EnumerateArray()
            .Select(e => (e.GetProperty("line").GetInt32(), e.GetProperty("reason").GetString()!))
            .ToList();

        Assert.Contains(errors, e => e.Item2.Contains("Tên nhóm trống", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(errors, e => e.Item2.Contains("ID Page trống", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(errors, e => e.Item2.Contains("không tìm thấy", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(errors, e => e.Item2.Contains("đã xoá", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(errors, e => e.Item2.Contains("nhiều kênh", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(errors, e => e.Item2.Contains("trùng", StringComparison.OrdinalIgnoreCase));

        // Một dòng hợp lệ (Giữ nguyên,ext-live lần đầu) — preview báo cập nhật nhưng KHÔNG ghi DB.
        Assert.Equal(1, data.GetProperty("validRowCount").GetInt32());
        Assert.Equal(beforeGroups, await CountGroupsAsync());
        Assert.Equal(beforeMembers, await CountMembersForGroupNameAsync("Giữ nguyên"));
        _ = deleted;
    }

    /// <summary>
    /// Revert-to-prove (d): cùng CSV nếu gọi commit thì DB đổi — chứng minh assertion
    /// "preview không ghi DB" là có ý nghĩa (không phải vì CSV vô hiệu).
    /// </summary>
    [Fact]
    public async Task Preview_RevertToProve_CommitWouldWriteDb()
    {
        await SeedChannelAsync("Live", "ext-live-d");
        var csv = "Tên nhóm,ID Page\nNhóm chứng minh,ext-live-d\n";

        using var preview = await SendImportAsync(HttpMethod.Post, "/api/ChannelGroup/import/preview",
            csv, "merge", "Admin");
        Assert.Equal(HttpStatusCode.OK, preview.StatusCode);
        Assert.Equal(0, await CountGroupsAsync());

        using var commit = await SendImportAsync(HttpMethod.Post, "/api/ChannelGroup/import/commit",
            csv, "merge", "Admin");
        Assert.Equal(HttpStatusCode.OK, commit.StatusCode);
        Assert.Equal(1, await CountGroupsAsync());
    }

    // --- (e) commit merge ---

    [Fact]
    public async Task Commit_Merge_CreatesNew_AndKeepsExistingPlusAdds()
    {
        var a = await SeedChannelAsync("A", "ext-a-m");
        var b = await SeedChannelAsync("B", "ext-b-m");
        var c = await SeedChannelAsync("C", "ext-c-m");
        await SeedGroupDirectAsync("Cũ", [a]);

        var csv = """
            Tên nhóm,ID Page
            Cũ,ext-b-m
            Mới,ext-c-m
            """;

        using var response = await SendImportAsync(HttpMethod.Post, "/api/ChannelGroup/import/commit",
            csv, "merge", "Admin");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var data = await ReadDataAsync(response);
        Assert.Equal(1, data.GetProperty("groupsCreated").GetInt32());
        Assert.Equal(1, data.GetProperty("groupsUpdated").GetInt32());

        var oldIds = await GetMemberChannelIdsAsync("Cũ");
        Assert.Equal(new[] { a, b }.OrderBy(x => x).ToList(), oldIds.OrderBy(x => x).ToList());
        var newIds = await GetMemberChannelIdsAsync("Mới");
        Assert.Equal(new List<Guid> { c }, newIds);
    }

    // --- (f) commit replace + không làm rỗng + revert-to-prove ---

    [Fact]
    public async Task Commit_Replace_SetsExactSet_AndAllErrorGroupNotEmptied()
    {
        var a = await SeedChannelAsync("A", "ext-a-r");
        var b = await SeedChannelAsync("B", "ext-b-r");
        var c = await SeedChannelAsync("C", "ext-c-r");
        await SeedGroupDirectAsync("Thay", [a, b]);
        await SeedGroupDirectAsync("Toàn lỗi", [a]);

        var csv = """
            Tên nhóm,ID Page
            Thay,ext-c-r
            Toàn lỗi,ext-missing
            Toàn lỗi,
            """;

        using var response = await SendImportAsync(HttpMethod.Post, "/api/ChannelGroup/import/commit",
            csv, "replace", "Admin");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var thay = await GetMemberChannelIdsAsync("Thay");
        Assert.Equal([c], thay);

        var allErr = await GetMemberChannelIdsAsync("Toàn lỗi");
        Assert.Equal([a], allErr); // không bị làm rỗng
    }

    /// <summary>
    /// Revert-to-prove (f): UpdateAsync với ChannelIds rỗng SẼ làm rỗng nhóm —
    /// chứng minh guard "toàn dòng lỗi không replace" là chỗ chặn thật.
    /// </summary>
    [Fact]
    public async Task Commit_Replace_RevertToProve_EmptyChannelIdsWouldEmptyGroup()
    {
        var a = await SeedChannelAsync("A", "ext-a-f");
        var groupId = await SeedGroupDirectAsync("Giữ", [a]);

        await using (var scope = _host.Services.CreateAsyncScope())
        {
            var http = scope.ServiceProvider.GetRequiredService<IHttpContextAccessor>();
            http.HttpContext = BuildHttpContext("Admin");
            var repo = scope.ServiceProvider.GetRequiredService<ChannelGroupRepository>();
            await repo.UpdateAsync(groupId, new UpdateChannelGroupRequest { ChannelIds = [] });
        }

        Assert.Empty(await GetMemberChannelIdsAsync("Giữ"));

        // Restore rồi chứng minh import replace toàn lỗi KHÔNG làm rỗng.
        await using (var db = new AppDbContext(_options))
        {
            db.ChannelGroupMembers.Add(new ChannelGroupMemberModel
            {
                Id = Guid.NewGuid(),
                ChannelGroupId = groupId,
                SocialChannelId = a,
                CreatedAt = DateTime.UtcNow,
                CreatedBy = "restore"
            });
            await db.SaveChangesAsync();
        }

        var csv = "Tên nhóm,ID Page\nGiữ,ext-missing\n";
        using var response = await SendImportAsync(HttpMethod.Post, "/api/ChannelGroup/import/commit",
            csv, "replace", "Admin");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal([a], await GetMemberChannelIdsAsync("Giữ"));
    }

    // --- (g) untouched groups ---

    [Fact]
    public async Task Commit_DoesNotTouchGroupsAbsentFromFile()
    {
        var a = await SeedChannelAsync("A", "ext-a-g");
        var b = await SeedChannelAsync("B", "ext-b-g");
        await SeedGroupDirectAsync("Ngoài file", [a]);
        await SeedGroupDirectAsync("Trong file", [a]);

        var csv = "Tên nhóm,ID Page\nTrong file,ext-b-g\n";
        using var response = await SendImportAsync(HttpMethod.Post, "/api/ChannelGroup/import/commit",
            csv, "replace", "Admin");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        Assert.Equal([a], await GetMemberChannelIdsAsync("Ngoài file"));
        Assert.Equal([b], await GetMemberChannelIdsAsync("Trong file"));
    }

    // --- (h) transaction rollback ---

    [Fact]
    public async Task Commit_MidFailure_RollsBackEntireTransaction()
    {
        await SeedChannelAsync("A", "ext-a-h");
        await SeedChannelAsync("B", "ext-b-h");
        var csv = """
            Tên nhóm,ID Page
            G1,ext-a-h
            G2,ext-b-h
            """;

        // CreateAsync + ReplaceMembers = 2 SaveChanges mỗi nhóm mới → fail sau nhóm đầu.
        _failInterceptor.FailAfter = 2;
        try
        {
            using var response = await SendImportAsync(HttpMethod.Post, "/api/ChannelGroup/import/commit",
                csv, "merge", "Admin");
            Assert.Equal(HttpStatusCode.InternalServerError, response.StatusCode);
        }
        finally
        {
            _failInterceptor.FailAfter = null;
        }

        Assert.Equal(0, await CountGroupsAsync());
    }

    // --- (i) commit re-validates from file (not client preview) ---

    [Fact]
    public async Task Commit_UsesLatestFile_NotStalePreview()
    {
        await SeedChannelAsync("A", "ext-a-i");
        await SeedChannelAsync("B", "ext-b-i");

        var previewCsv = "Tên nhóm,ID Page\nChỉ A,ext-a-i\n";
        using var preview = await SendImportAsync(HttpMethod.Post, "/api/ChannelGroup/import/preview",
            previewCsv, "merge", "Admin");
        Assert.Equal(HttpStatusCode.OK, preview.StatusCode);
        Assert.Equal(0, await CountGroupsAsync());

        var commitCsv = "Tên nhóm,ID Page\nChỉ B,ext-b-i\n";
        using var commit = await SendImportAsync(HttpMethod.Post, "/api/ChannelGroup/import/commit",
            commitCsv, "merge", "Admin");
        Assert.Equal(HttpStatusCode.OK, commit.StatusCode);

        Assert.Null(await FindGroupIdByNameAsync("Chỉ A"));
        Assert.NotNull(await FindGroupIdByNameAsync("Chỉ B"));
        Assert.Equal(1, await CountGroupsAsync());
    }

    // --- template ---

    [Fact]
    public async Task Template_ReturnsUtf8BomCsv_WithRequiredHeaders()
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, "/api/ChannelGroup/import/template");
        Authorize(request, "Admin");
        using var response = await _client.SendAsync(request);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var bytes = await response.Content.ReadAsByteArrayAsync();
        Assert.True(bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF);
        var text = Encoding.UTF8.GetString(bytes);
        Assert.Contains("Tên nhóm", text, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("ID Page", text, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Mô tả", text, StringComparison.OrdinalIgnoreCase);
    }

    // --- helpers ---

    private async Task<HttpResponseMessage> SendImportAsync(
        HttpMethod method, string url, string csv, string mode, string? role,
        string fileName = "import.csv")
    {
        using var content = new MultipartFormDataContent();
        var bytes = Encoding.UTF8.GetBytes(csv);
        var fileContent = new ByteArrayContent(bytes);
        fileContent.Headers.ContentType = new MediaTypeHeaderValue("text/csv");
        content.Add(fileContent, "file", fileName);
        content.Add(new StringContent(mode), "mode");

        var request = new HttpRequestMessage(method, url) { Content = content };
        if (role is not null) Authorize(request, role);
        return await _client.SendAsync(request);
    }

    private static void Authorize(HttpRequestMessage request, string role)
        => request.Headers.Authorization = new AuthenticationHeaderValue(
            TestAuthHandler.SchemeName, $"{role}:actor");

    private static DefaultHttpContext BuildHttpContext(string role)
    {
        var http = new DefaultHttpContext();
        var claims = new[]
        {
            new Claim(ClaimTypes.Name, "actor"),
            new Claim(ClaimTypes.NameIdentifier, Guid.NewGuid().ToString()),
            new Claim(ClaimTypes.Role, role),
        };
        http.User = new ClaimsPrincipal(new ClaimsIdentity(claims, TestAuthHandler.SchemeName));
        return http;
    }

    private static async Task<JsonElement> ReadDataAsync(HttpResponseMessage response)
    {
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        return body.GetProperty("data");
    }

    private async Task<Guid> SeedChannelAsync(string pageName, string externalPageId, bool isDeleted = false)
    {
        await using var db = new AppDbContext(_options);
        var id = Guid.NewGuid();
        db.SocialChannels.Add(new SocialChannelModel
        {
            Id = id,
            Platform = SocialPlatform.Facebook,
            ChannelType = SocialChannelType.Page,
            PageName = pageName,
            ExternalPageId = externalPageId,
            AccessToken = "token",
            IsActive = true,
            IsDeleted = isDeleted,
            DeletedAt = isDeleted ? DateTime.UtcNow : null,
            CreatedAt = DateTime.UtcNow,
            CreatedBy = "seed"
        });
        await db.SaveChangesAsync();
        return id;
    }

    private async Task<Guid> SeedGroupDirectAsync(string name, IReadOnlyList<Guid> channelIds)
    {
        await using var db = new AppDbContext(_options);
        var groupId = Guid.NewGuid();
        db.ChannelGroups.Add(new ChannelGroupModel
        {
            Id = groupId,
            Name = name,
            CreatedAt = DateTime.UtcNow,
            CreatedBy = "seed"
        });
        foreach (var channelId in channelIds)
        {
            db.ChannelGroupMembers.Add(new ChannelGroupMemberModel
            {
                Id = Guid.NewGuid(),
                ChannelGroupId = groupId,
                SocialChannelId = channelId,
                CreatedAt = DateTime.UtcNow,
                CreatedBy = "seed"
            });
        }

        await db.SaveChangesAsync();
        return groupId;
    }

    private async Task<int> CountGroupsAsync()
    {
        await using var db = new AppDbContext(_options);
        return await db.ChannelGroups.CountAsync(x => !x.IsDeleted);
    }

    private async Task<int> CountMembersAsync(Guid channelId)
    {
        await using var db = new AppDbContext(_options);
        return await db.ChannelGroupMembers.CountAsync(x => !x.IsDeleted && x.SocialChannelId == channelId);
    }

    private async Task<int> CountMembersForGroupNameAsync(string name)
    {
        await using var db = new AppDbContext(_options);
        var id = await db.ChannelGroups.Where(g => !g.IsDeleted && g.Name == name)
            .Select(g => g.Id).FirstOrDefaultAsync();
        if (id == Guid.Empty) return 0;
        return await db.ChannelGroupMembers.CountAsync(m => !m.IsDeleted && m.ChannelGroupId == id);
    }

    private async Task<List<Guid>> GetMemberChannelIdsAsync(string groupName)
    {
        await using var db = new AppDbContext(_options);
        var id = await db.ChannelGroups.Where(g => !g.IsDeleted && g.Name == groupName)
            .Select(g => g.Id).SingleAsync();
        return await db.ChannelGroupMembers
            .Where(m => !m.IsDeleted && m.ChannelGroupId == id)
            .Select(m => m.SocialChannelId)
            .ToListAsync();
    }

    private async Task<Guid?> FindGroupIdByNameAsync(string name)
    {
        await using var db = new AppDbContext(_options);
        return await db.ChannelGroups.Where(g => !g.IsDeleted && g.Name == name)
            .Select(g => (Guid?)g.Id).FirstOrDefaultAsync();
    }

    private sealed class FailAfterNSavesInterceptor : SaveChangesInterceptor
    {
        private int? _failAfter;
        private int _count;

        public int? FailAfter
        {
            get => _failAfter;
            set
            {
                _failAfter = value;
                _count = 0;
            }
        }

        public override InterceptionResult<int> SavingChanges(
            DbContextEventData eventData, InterceptionResult<int> result)
        {
            MaybeFail();
            return base.SavingChanges(eventData, result);
        }

        public override ValueTask<InterceptionResult<int>> SavingChangesAsync(
            DbContextEventData eventData, InterceptionResult<int> result,
            CancellationToken cancellationToken = default)
        {
            MaybeFail();
            return base.SavingChangesAsync(eventData, result, cancellationToken);
        }

        private void MaybeFail()
        {
            if (_failAfter is null) return;
            _count++;
            if (_count > _failAfter.Value)
                throw new InvalidOperationException("Simulated mid-commit failure");
        }
    }

    private sealed class TestAuthHandler : AuthenticationHandler<AuthenticationSchemeOptions>
    {
        public const string SchemeName = "TestChannelGroupImport";

        public TestAuthHandler(
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
            if (parts.Length != 2)
                return Task.FromResult(AuthenticateResult.Fail("Invalid test auth payload."));

            var claims = new[]
            {
                new Claim(ClaimTypes.Name, parts[1]),
                new Claim(ClaimTypes.NameIdentifier, Guid.NewGuid().ToString()),
                new Claim(ClaimTypes.Role, parts[0]),
            };
            var identity = new ClaimsIdentity(claims, SchemeName);
            return Task.FromResult(AuthenticateResult.Success(
                new AuthenticationTicket(new ClaimsPrincipal(identity), SchemeName)));
        }
    }
}
