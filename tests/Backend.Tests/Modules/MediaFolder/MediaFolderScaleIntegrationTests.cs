using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text.Encodings.Web;
using System.Text.Json;
using Backend.Data;
using Backend.Modules.MediaFolder;
using Backend.Modules.SocialChannel;
using Backend.Modules.SocialChannel.Enums;
using Backend.Shared;
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

namespace Backend.Tests.Modules.MediaFolder;

/// <summary>
/// R-010 (MEDIA-08): 2 Page x 500 folder/Page qua HTTP thật (TestServer), chứng minh
/// data boundary vẫn đúng ở scale lớn (AC1) và explorer/picker chỉ tải theo trang, không
/// dump cả cây (AC2 — request count/payload).
/// </summary>
public class MediaFolderScaleIntegrationTests : IAsyncLifetime
{
    private const string Owner = "scale-owner";
    private const int FolderCountPerPage = 500;

    private readonly SqliteConnection _connection;
    private readonly IHost _host;
    private readonly HttpClient _client;
    private readonly Guid _pageAId = Guid.NewGuid();
    private readonly Guid _pageBId = Guid.NewGuid();
    private Guid _pageAExpandableFolderId;
    private Guid _pageAExpandableChildId;
    private Guid _pageBFolderId;

    public MediaFolderScaleIntegrationTests()
    {
        _connection = new SqliteConnection("DataSource=:memory:");
        _connection.Open();

        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlite(_connection)
            .Options;

        using (var seedDb = new AppDbContext(options))
        {
            seedDb.Database.EnsureCreated();
            seedDb.SocialChannels.AddRange(
                new SocialChannelModel
                {
                    Id = _pageAId, Platform = SocialPlatform.Facebook, ChannelType = SocialChannelType.Page,
                    PageName = "Scale Page A", ExternalPageId = "fb-scale-a", AccessToken = "token-a",
                    IsActive = true, CreatedBy = Owner,
                },
                new SocialChannelModel
                {
                    Id = _pageBId, Platform = SocialPlatform.Facebook, ChannelType = SocialChannelType.Page,
                    PageName = "Scale Page B", ExternalPageId = "fb-scale-b", AccessToken = "token-b",
                    IsActive = true, CreatedBy = Owner,
                });

            var folders = new List<MediaFolderModel>();

            // Page A: 1 root có 1 con (để test expand), phần còn lại là root phẳng, cộng 1 folder
            // "Duplicate Name" để test search full path phân biệt theo Page.
            _pageAExpandableFolderId = Guid.NewGuid();
            _pageAExpandableChildId = Guid.NewGuid();
            folders.Add(new MediaFolderModel { Id = _pageAExpandableFolderId, Name = "Expandable A", SocialChannelId = _pageAId, ParentFolderId = null });
            folders.Add(new MediaFolderModel { Id = _pageAExpandableChildId, Name = "Duplicate Name", SocialChannelId = _pageAId, ParentFolderId = _pageAExpandableFolderId });
            for (var i = 0; i < FolderCountPerPage - 2; i++)
            {
                folders.Add(new MediaFolderModel { Id = Guid.NewGuid(), Name = $"PageA_Root_{i:D4}", SocialChannelId = _pageAId, ParentFolderId = null });
            }

            // Page B: 500 root folders, gồm 1 "Duplicate Name" trùng tên với Page A nhưng path khác.
            _pageBFolderId = Guid.NewGuid();
            folders.Add(new MediaFolderModel { Id = _pageBFolderId, Name = "Duplicate Name", SocialChannelId = _pageBId, ParentFolderId = null });
            for (var i = 0; i < FolderCountPerPage - 1; i++)
            {
                folders.Add(new MediaFolderModel { Id = Guid.NewGuid(), Name = $"PageB_Root_{i:D4}", SocialChannelId = _pageBId, ParentFolderId = null });
            }

            seedDb.MediaFolders.AddRange(folders);
            seedDb.SaveChanges();
        }

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
                    services.AddAuthentication(TestAuthHandler.SchemeName)
                        .AddScheme<AuthenticationSchemeOptions, TestAuthHandler>(
                            TestAuthHandler.SchemeName, _ => { });
                    services.AddAuthorization();
                    services.AddControllers(options => options.Filters.Add<Backend.Shared.GlobalExceptionFilter>())
                        .AddApplicationPart(typeof(MediaFolderController).Assembly);
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

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private void AuthorizeAs(HttpRequestMessage request, string role, string userName) =>
        request.Headers.Authorization = new AuthenticationHeaderValue(TestAuthHandler.SchemeName, $"{role}:{userName}");

    private async Task<T> GetJsonAsync<T>(string url, string role = "ContentManager", string userName = Owner)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        AuthorizeAs(request, role, userName);
        var response = await _client.SendAsync(request);
        response.EnsureSuccessStatusCode();
        var body = await response.Content.ReadFromJsonAsync<ApiResponse<T>>(JsonOptions);
        return body!.Data!;
    }

    [Fact]
    public async Task MEDIA_08_AC1_Children_AtScale_OnlyReturnsRequestedPage()
    {
        var pageA = await GetJsonAsync<PagedResult<MediaFolderResponse>>(
            $"/api/MediaFolder/children?socialChannelId={_pageAId}&index=1&size=20");
        Assert.Equal(FolderCountPerPage - 1, pageA.Total); // 499 roots (Expandable A is a root; its child is not)
        Assert.All(pageA.Items, item => Assert.DoesNotContain("PageB_", item.Name));

        var pageB = await GetJsonAsync<PagedResult<MediaFolderResponse>>(
            $"/api/MediaFolder/children?socialChannelId={_pageBId}&index=1&size=20");
        Assert.Equal(FolderCountPerPage, pageB.Total);
        Assert.All(pageB.Items, item => Assert.DoesNotContain("PageA_", item.Name));
    }

    [Fact]
    public async Task MEDIA_08_AC2_OpeningRootThenExpandingOneFolder_Issues2PaginatedRequests_NotFullTreeDump()
    {
        var root = await GetJsonAsync<PagedResult<MediaFolderResponse>>(
            $"/api/MediaFolder/children?socialChannelId={_pageAId}&index=1&size=20");
        // Call #1: payload is one page (20), never the full 499 roots.
        Assert.Equal(20, root.Items.Count);
        Assert.Equal(FolderCountPerPage - 1, root.Total);

        var expanded = await GetJsonAsync<PagedResult<MediaFolderResponse>>(
            $"/api/MediaFolder/children?socialChannelId={_pageAId}&parentFolderId={_pageAExpandableFolderId}&index=1&size=20");
        // Call #2: expanding one node returns only ITS direct children (1), not the sibling 499 roots
        // and not a recursive dump of the whole Page.
        Assert.Single(expanded.Items);
        Assert.Equal("Duplicate Name", expanded.Items[0].Name);
    }

    [Fact]
    public async Task MEDIA_08_AC1_Search_AtScale_DistinguishesSameNameAcrossPages_WithFullPath()
    {
        var resultsA = await GetJsonAsync<PagedResult<MediaFolderSearchResultItem>>(
            $"/api/MediaFolder/search?socialChannelId={_pageAId}&keyword=Duplicate");
        var resultsB = await GetJsonAsync<PagedResult<MediaFolderSearchResultItem>>(
            $"/api/MediaFolder/search?socialChannelId={_pageBId}&keyword=Duplicate");

        Assert.Single(resultsA.Items);
        Assert.Equal("Expandable A / Duplicate Name", resultsA.Items[0].FullPath);

        Assert.Single(resultsB.Items);
        Assert.Equal("Duplicate Name", resultsB.Items[0].FullPath);
        Assert.NotEqual(resultsA.Items[0].Id, resultsB.Items[0].Id);
    }

    [Fact]
    public async Task MEDIA_08_AC1_Breadcrumb_AtScale_CrossPageIsNotFound_NoMetadataLeak()
    {
        using var request = new HttpRequestMessage(
            HttpMethod.Get, $"/api/MediaFolder/breadcrumb?socialChannelId={_pageAId}&folderId={_pageBFolderId}");
        AuthorizeAs(request, "ContentManager", Owner);
        var response = await _client.SendAsync(request);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync();
        Assert.DoesNotContain("Duplicate Name", body);
        Assert.DoesNotContain(_pageBFolderId.ToString(), body, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData("Viewer")]
    [InlineData("Reviewer")]
    public async Task MEDIA_08_AC1_UnauthorizedActor_AtScale_GetsUniformNotFound_AcrossChildrenSearchBreadcrumb(string role)
    {
        const string stranger = "stranger-not-owner";

        using var childrenReq = new HttpRequestMessage(HttpMethod.Get, $"/api/MediaFolder/children?socialChannelId={_pageAId}");
        AuthorizeAs(childrenReq, role, stranger);
        var childrenResp = await _client.SendAsync(childrenReq);
        var childrenBody = await childrenResp.Content.ReadAsStringAsync();

        using var searchReq = new HttpRequestMessage(HttpMethod.Get, $"/api/MediaFolder/search?socialChannelId={_pageAId}&keyword=Duplicate");
        AuthorizeAs(searchReq, role, stranger);
        var searchResp = await _client.SendAsync(searchReq);
        var searchBody = await searchResp.Content.ReadAsStringAsync();

        using var breadcrumbReq = new HttpRequestMessage(HttpMethod.Get, $"/api/MediaFolder/breadcrumb?socialChannelId={_pageAId}&folderId={_pageAExpandableFolderId}");
        AuthorizeAs(breadcrumbReq, role, stranger);
        var breadcrumbResp = await _client.SendAsync(breadcrumbReq);
        var breadcrumbBody = await breadcrumbResp.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.NotFound, childrenResp.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, searchResp.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, breadcrumbResp.StatusCode);
        Assert.DoesNotContain("Duplicate Name", childrenBody);
        Assert.DoesNotContain("Duplicate Name", searchBody);
        Assert.DoesNotContain("Duplicate Name", breadcrumbBody);
        Assert.DoesNotContain("Expandable A", breadcrumbBody);
    }

    [Fact]
    public async Task MEDIA_08_AC1_BulkCreate_AtScale_StaysWithinRequestedPage()
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/MediaFolder/bulk")
        {
            Content = JsonContent.Create(new BulkCreateMediaFolderRequest
            {
                SocialChannelId = _pageAId,
                Folders = [new BulkCreateMediaFolderItem { ClientRef = "n1", Name = "New Under Scale" }],
            }),
        };
        AuthorizeAs(request, "ContentManager", Owner);
        var response = await _client.SendAsync(request);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var pageA = await GetJsonAsync<PagedResult<MediaFolderResponse>>(
            $"/api/MediaFolder/children?socialChannelId={_pageAId}&index=1&size=1");
        Assert.Equal(FolderCountPerPage, pageA.Total); // 499 + 1 new = 500; Page B untouched (asserted below).

        var pageB = await GetJsonAsync<PagedResult<MediaFolderResponse>>(
            $"/api/MediaFolder/children?socialChannelId={_pageBId}&index=1&size=1");
        Assert.Equal(FolderCountPerPage, pageB.Total);
    }
}

/// <summary>Scheme test: Authorization header value = "{role}:{userName}".</summary>
file sealed class TestAuthHandler : AuthenticationHandler<AuthenticationSchemeOptions>
{
    public const string SchemeName = "TestScaleAuth";

    public TestAuthHandler(
        IOptionsMonitor<AuthenticationSchemeOptions> options,
        ILoggerFactory logger,
        UrlEncoder encoder)
        : base(options, logger, encoder)
    {
    }

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

        var claims = new List<Claim>
        {
            new(ClaimTypes.Name, parts[1]),
            new(ClaimTypes.NameIdentifier, Guid.NewGuid().ToString()),
            new(ClaimTypes.Role, parts[0]),
        };
        var identity = new ClaimsIdentity(claims, SchemeName);
        var ticket = new AuthenticationTicket(new ClaimsPrincipal(identity), SchemeName);
        return Task.FromResult(AuthenticateResult.Success(ticket));
    }
}
