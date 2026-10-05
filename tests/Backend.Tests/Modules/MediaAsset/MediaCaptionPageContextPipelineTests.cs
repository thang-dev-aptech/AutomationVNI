using System.Net;
using System.Net.Http.Headers;
using System.Security.Claims;
using System.Text.Encodings.Web;
using System.Text.Json;
using Backend.Data;
using Backend.Modules.GoogleDrive;
using Backend.Modules.MediaAsset;
using Backend.Modules.MediaCaption;
using Backend.Modules.MediaFolder;
using Backend.Modules.PageContext;
using Backend.Modules.SocialChannel;
using Backend.Modules.SocialChannel.Enums;
using Backend.Shared.Repositories;
using Backend.Shared.Storage;
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

namespace Backend.Tests.Modules.MediaAsset;

/// <summary>
/// R-029 AC 42a00e9e image-caption-context-test: pipeline thật (Authorize + QueryWritableChannels) cho
/// POST api/MediaAsset/{id}/generate-caption?socialChannelId=… — PageContext của đúng MỘT Page đi tới AI,
/// Page ngoài quyền là 404 chung và KHÔNG gọi AI.
/// </summary>
public class MediaCaptionPageContextPipelineTests : IAsyncLifetime
{
    private const string Owner = "owner-user";
    private const string Foreign = "someone-else";

    private readonly SqliteConnection _connection;
    private readonly DbContextOptions<AppDbContext> _options;
    private readonly ScriptedChatHandler _ai;
    private readonly IHost _host;
    private readonly HttpClient _client;

    private readonly Guid _pageA = Guid.NewGuid();
    private readonly Guid _pageB = Guid.NewGuid();
    private readonly Guid _pageForeign = Guid.NewGuid();
    private Guid _assetInA, _assetInForeign, _assetInDrive, _assetPlain;

    public MediaCaptionPageContextPipelineTests()
    {
        _connection = new SqliteConnection("DataSource=:memory:");
        _connection.Open();
        _options = new DbContextOptionsBuilder<AppDbContext>().UseSqlite(_connection).Options;
        using (var seedDb = new AppDbContext(_options))
            seedDb.Database.EnsureCreated();

        _ai = new ScriptedChatHandler(Enumerable.Repeat(CaptionAi.Json("Bài thử"), 10).ToArray());
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
                    services.AddScoped<MediaFolderRepository>();
                    services.AddScoped<MediaAssetRepository>();
                    services.AddSingleton<IFileStorageService, InMemoryImageStorage>();
                    services.AddSingleton(CaptionAiOptions.Create());
                    services.AddSingleton(_ => new HttpClient(ai));
                    services.AddScoped<MediaIntelligenceService>();
                    services.AddAuthentication(PipelineAuth.SchemeName)
                        .AddScheme<AuthenticationSchemeOptions, PipelineAuth>(PipelineAuth.SchemeName, _ => { });
                    services.AddAuthorization();
                    services.AddControllers().AddApplicationPart(typeof(MediaAssetController).Assembly);
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

    public async Task InitializeAsync() => await SeedAsync();

    public async Task DisposeAsync()
    {
        _client.Dispose();
        await _host.StopAsync();
        _host.Dispose();
        await _connection.DisposeAsync();
    }

    // ── (a) Page chọn tường minh ───────────────────────────────────────────

    [Fact]
    public async Task ExplicitPage_SendsThatPagesContext_NotTheFolderPages()
    {
        var response = await PostAsync(_assetInA, "ContentManager", Owner, socialChannelId: _pageB);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var text = UserText();
        Assert.Contains("Thương hiệu / Page: Thương hiệu Beta", text);
        Assert.Contains("Giọng điệu: giọng Beta", text);
        Assert.Contains("CTA gợi ý: CTA Beta", text);
        Assert.Contains("Hashtag gợi ý: #beta1 #beta2", text);
        Assert.DoesNotContain("Alpha", text);
        Assert.Equal(CaptionAi.Expected("Bài thử"), await GetCaptionAsync(_assetInA));
    }

    // ── (b) không truyền ⇒ Page của thư mục ───────────────────────────────

    [Fact]
    public async Task NoPageGiven_UsesTheFolderPagesContext()
    {
        var response = await PostAsync(_assetInA, "ContentManager", Owner);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var text = UserText();
        Assert.Contains("Thương hiệu / Page: Thương hiệu Alpha", text);
        Assert.Contains("CTA gợi ý: CTA Alpha", text);
        Assert.Contains("Hashtag gợi ý: #alpha1 #alpha2", text);
        Assert.DoesNotContain("Beta", text);
    }

    // ── (c) ảnh Drive / không thư mục ⇒ mặc định chung ────────────────────

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task ImageOutsideAnyPage_UsesGenericDefaultsAndNoPagesContext(bool inDriveTree)
    {
        var response = await PostAsync(inDriveTree ? _assetInDrive : _assetPlain, "ContentManager", Owner);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var text = UserText();
        Assert.DoesNotContain("Thương hiệu / Page:", text);
        Assert.DoesNotContain("Alpha", text);
        Assert.DoesNotContain("Beta", text);
        Assert.DoesNotContain("Ngoài quyền", text);
        Assert.Contains("Giọng điệu: thân thiện, rõ ràng, chuyên nghiệp", text);
        Assert.Contains("CTA gợi ý: Inbox ngay để được tư vấn chi tiết nhé", text);
        Assert.Contains("Hashtag gợi ý: #Chung #Facebook #Marketing #BanHang", text);
    }

    // ── (d) Page ngoài quyền ⇒ 404 chung, không gọi AI ────────────────────

    [Fact]
    public async Task ForeignPage_Returns404WithoutLeakingAnythingAndWithoutCallingAi()
    {
        var response = await PostAsync(_assetInA, "ContentManager", Owner, socialChannelId: _pageForeign);
        var body = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Contains("Page/Kênh không tồn tại.", body);
        Assert.DoesNotContain("Ngoài quyền", body);
        Assert.DoesNotContain(_pageForeign.ToString(), body, StringComparison.OrdinalIgnoreCase);
        Assert.Empty(_ai.RequestBodies);
        Assert.Equal("cũ", await GetCaptionAsync(_assetInA));
    }

    [Fact]
    public async Task NonexistentPage_IsIndistinguishableFromAForeignOne()
    {
        var foreign = await PostAsync(_assetInA, "ContentManager", Owner, socialChannelId: _pageForeign);
        var missing = await PostAsync(_assetInA, "ContentManager", Owner, socialChannelId: Guid.NewGuid());

        Assert.Equal(foreign.StatusCode, missing.StatusCode);
        Assert.Equal(
            Normalize(await foreign.Content.ReadAsStringAsync(), _pageForeign),
            Normalize(await missing.Content.ReadAsStringAsync(), Guid.Empty));
        Assert.Empty(_ai.RequestBodies);
    }

    [Fact]
    public async Task AdminMayUseAnyPage()
    {
        var response = await PostAsync(_assetInA, "Admin", "admin-user", socialChannelId: _pageForeign);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("Thương hiệu / Page: Brand Ngoài Quyền", UserText());
    }

    [Fact]
    public async Task FolderPageOutsideTheCallersRights_FallsBackToGenericContext()
    {
        var response = await PostAsync(_assetInForeign, "ContentManager", Owner);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var text = UserText();
        Assert.DoesNotContain("Ngoài quyền", text);
        Assert.DoesNotContain("Thương hiệu / Page:", text);
        Assert.Contains("Hashtag gợi ý: #Chung #Facebook #Marketing #BanHang", text);
    }

    // ── (e) vai trò ───────────────────────────────────────────────────────

    [Theory]
    [InlineData("Viewer")]
    [InlineData("Reviewer")]
    public async Task ForbiddenRoles_Return403(string role)
    {
        var response = await PostAsync(_assetInA, role, Owner, socialChannelId: _pageA);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Empty(_ai.RequestBodies);
        Assert.Equal("cũ", await GetCaptionAsync(_assetInA));
    }

    [Fact]
    public async Task Unauthenticated_Returns401()
    {
        var response = await PostAsync(_assetInA, role: null, Owner, socialChannelId: _pageA);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Empty(_ai.RequestBodies);
    }

    // ── (f) 409 khi ảnh trong queue ───────────────────────────────────────

    [Fact]
    public async Task ImageInACaptionJob_Returns409WithoutCallingAi()
    {
        await using (var db = new AppDbContext(_options))
        {
            var job = new MediaCaptionJobModel
            {
                Id = Guid.NewGuid(), FolderId = Guid.NewGuid(), FolderName = "folder",
                Status = MediaCaptionJobStatus.Running, Total = 1, CreatedAt = DateTime.UtcNow,
            };
            db.MediaCaptionJobs.Add(job);
            db.MediaCaptionJobItems.Add(new MediaCaptionJobItemModel
            {
                Id = Guid.NewGuid(), JobId = job.Id, MediaAssetId = _assetInA, FileName = "a.png",
                Status = MediaCaptionJobItemStatus.Pending, CreatedAt = DateTime.UtcNow,
            });
            await db.SaveChangesAsync();
        }

        var response = await PostAsync(_assetInA, "ContentManager", Owner, socialChannelId: _pageA);

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Contains("MEDIA_CAPTION_QUEUED", await response.Content.ReadAsStringAsync());
        Assert.Empty(_ai.RequestBodies);
        Assert.Equal("cũ", await GetCaptionAsync(_assetInA));
    }

    // ── helpers ───────────────────────────────────────────────────────────

    private static string Normalize(string body, Guid id) => body.Replace(id.ToString(), "<id>", StringComparison.OrdinalIgnoreCase);

    private string UserText()
    {
        using var doc = JsonDocument.Parse(_ai.RequestBodies.Single());
        return doc.RootElement.GetProperty("messages")[1].GetProperty("content")[0].GetProperty("text").GetString()!;
    }

    private async Task<HttpResponseMessage> PostAsync(Guid assetId, string? role, string user, Guid? socialChannelId = null)
    {
        var url = $"/api/MediaAsset/{assetId}/generate-caption" + (socialChannelId is Guid id ? $"?socialChannelId={id}" : "");
        using var request = new HttpRequestMessage(HttpMethod.Post, url);
        if (role is not null)
            request.Headers.Authorization = new AuthenticationHeaderValue(PipelineAuth.SchemeName, $"{role}:{user}");
        return await _client.SendAsync(request);
    }

    private async Task<string?> GetCaptionAsync(Guid id)
    {
        await using var db = new AppDbContext(_options);
        return (await db.MediaAssets.AsNoTracking().SingleAsync(x => x.Id == id)).Caption;
    }

    private async Task SeedAsync()
    {
        await using var db = new AppDbContext(_options);
        SocialChannelModel Channel(Guid id, string name, string createdBy) => new()
        {
            Id = id, Platform = SocialPlatform.Facebook, ChannelType = SocialChannelType.Page, PageName = name,
            ExternalPageId = $"fb-{id:N}", AccessToken = "token", IsActive = true, CreatedBy = createdBy,
        };
        db.SocialChannels.AddRange(
            Channel(_pageA, "Page Alpha", Owner), Channel(_pageB, "Page Beta", Owner), Channel(_pageForeign, "Page Ngoài Quyền", Foreign));
        db.PageContexts.AddRange(
            Context(_pageA, "Alpha"), Context(_pageB, "Beta"),
            new PageContextModel
            {
                Id = Guid.NewGuid(), SocialChannelId = _pageForeign, BrandName = "Brand Ngoài Quyền",
                ToneOfVoice = "giọng Ngoài Quyền", CtaText = "CTA Ngoài Quyền", DefaultHashtags = "#ngoai1 #ngoai2",
            });

        var folderA = new MediaFolderModel { Id = Guid.NewGuid(), Name = "Folder A", SocialChannelId = _pageA };
        var folderForeign = new MediaFolderModel { Id = Guid.NewGuid(), Name = "Folder F", SocialChannelId = _pageForeign };
        var drive = new MediaFolderModel { Id = Guid.NewGuid(), Name = GoogleDriveRepository.DedicatedFolderName };
        db.MediaFolders.AddRange(folderA, folderForeign, drive);
        (await db.GoogleDriveSyncStates.SingleAsync(x => x.Id == GoogleDriveSyncStateModel.SingletonId))
            .DedicatedFolderId = drive.Id;

        MediaAssetModel Asset(Guid? folderId) => new()
        {
            Id = Guid.NewGuid(), FileName = "a.png", StoragePath = "media/a.png", MimeType = "image/png",
            FolderId = folderId, Caption = "cũ",
        };
        var inA = Asset(folderA.Id); var inForeign = Asset(folderForeign.Id);
        var inDrive = Asset(drive.Id); var plain = Asset(null);
        db.MediaAssets.AddRange(inA, inForeign, inDrive, plain);
        await db.SaveChangesAsync();
        (_assetInA, _assetInForeign, _assetInDrive, _assetPlain) = (inA.Id, inForeign.Id, inDrive.Id, plain.Id);
    }

    private static PageContextModel Context(Guid pageId, string name) => new()
    {
        Id = Guid.NewGuid(), SocialChannelId = pageId, BrandName = $"Thương hiệu {name}",
        ToneOfVoice = $"giọng {name}", CtaText = $"CTA {name}", DefaultHashtags = $"#{name.ToLowerInvariant()}1 #{name.ToLowerInvariant()}2",
    };
}

file sealed class PipelineAuth(
    IOptionsMonitor<AuthenticationSchemeOptions> options, ILoggerFactory logger, UrlEncoder encoder)
    : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
{
    public const string SchemeName = "TestPageContextCaptionAuth";

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

        var identity = new ClaimsIdentity(
            [new Claim(ClaimTypes.Name, parts[1]), new Claim(ClaimTypes.NameIdentifier, Guid.NewGuid().ToString()), new Claim(ClaimTypes.Role, parts[0])],
            SchemeName);
        return Task.FromResult(AuthenticateResult.Success(
            new AuthenticationTicket(new ClaimsPrincipal(identity), SchemeName)));
    }
}
