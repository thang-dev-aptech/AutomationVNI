using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text.Encodings.Web;
using Backend.Data;
using Backend.Modules.ContentCrawl;
using Backend.Modules.GenerationJob;
using Backend.Modules.MediaAsset;
using Backend.Modules.MediaAsset.Enums;
using Backend.Modules.MediaEmbedding;
using Backend.Modules.MediaFolder;
using Backend.Modules.PageContext;
using Backend.Modules.Post;
using Backend.Modules.Post.Enums;
using Backend.Modules.PromptTemplate;
using Backend.Modules.PublishLog;
using Backend.Modules.SocialChannel;
using Backend.Modules.SocialChannel.Enums;
using Backend.Shared;
using Backend.Shared.Ai;
using Backend.Shared.Repositories;
using Backend.Shared.SocialPublish;
using Backend.Shared.Storage;
using Backend.Tests.Modules.MediaAsset;
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
/// MEDIA-POST-01 backend: auth, quyền Page, gallery đúng thứ tự. AI giả đếm request.
/// ResolvePublishMediaListAsync là private; test áp đúng quy tắc của nó: Cover trước, rồi SortOrder.
/// </summary>
public class PostFromMediaPipelineTests : IAsyncLifetime
{
    private readonly SqliteConnection _connection;
    private readonly DbContextOptions<AppDbContext> _options;
    private readonly AiProbe _probe = new();
    private readonly IHost _host;
    private readonly HttpClient _client;

    public PostFromMediaPipelineTests()
    {
        _connection = new SqliteConnection("DataSource=:memory:");
        _connection.Open();
        _options = new DbContextOptionsBuilder<AppDbContext>().UseSqlite(_connection).Options;
        using (var seed = new AppDbContext(_options))
            seed.Database.EnsureCreated();

        var options = _options;
        var probe = _probe;
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
                    services.AddScoped<PageContextRepository>();
                    services.AddScoped<PromptTemplateRepository>();
                    services.AddScoped<SocialChannelRepository>();
                    services.AddScoped<MediaEmbeddingRepository>();
                    services.AddScoped<GenerationJobRepository>();
                    services.AddScoped<PublishLogRepository>();
                    services.AddScoped<PostRepository>();
                    services.AddScoped<PostMediaRepository>();
                    services.AddScoped<PostWorkflowService>();
                    services.AddScoped<PostFromMediaService>();
                    services.AddScoped<RecycleSourcePicker>();
                    services.AddScoped<PostRecycleService>();
                    services.AddScoped<AiImageFolderService>();
                    services.AddScoped<MediaIntelligenceService>();
                    services.AddScoped<GenerationJobPipelineService>();
                    services.AddScoped<PublishPipelineService>();
                    services.AddScoped<IPublishPipelineService>(sp => sp.GetRequiredService<PublishPipelineService>());
                    services.AddSingleton<IFileStorageService, InMemoryImageStorage>();
                    services.AddSingleton<IImageOverlayService, IdleOverlayService>();
                    services.AddSingleton<IAiTextGenerationService>(new CountingTextService(probe));
                    services.AddSingleton<IAiImageGenerationService>(new CountingImageService(probe));
                    services.AddSingleton<ISocialPublishService, IdlePublishService>();
                    services.AddSingleton<IOptions<ContentCrawlOptions>>(Options.Create(new ContentCrawlOptions()));
                    services.AddSingleton<IOptions<ReelsOptions>>(Options.Create(new ReelsOptions()));
                    services.AddSingleton<IOptions<AiProvidersOptions>>(Options.Create(new AiProvidersOptions()));
                    services.AddSingleton(new HttpClient(new CountingChatHandler(probe)));
                    services.AddSingleton<SlideshowVideoRenderService>();
                    services.AddLogging();
                    services.AddAuthentication(PostFromMediaAuthHandler.SchemeName)
                        .AddScheme<AuthenticationSchemeOptions, PostFromMediaAuthHandler>(
                            PostFromMediaAuthHandler.SchemeName, _ => { });
                    services.AddAuthorization();
                    services.AddControllers()
                        .AddApplicationPart(typeof(PostController).Assembly);
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
    public async Task AllowedRoles_Return200(string role)
    {
        var channel = await SeedChannelAsync("Page mình", role == "Admin" ? "other" : "actor");
        var media = await SeedMediaAsync("cover.jpg");
        var before = _probe.Calls;

        using var response = await SendAsync(role, "actor", Payload([media], [channel], "Xin chào"));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(before, _probe.Calls);
    }

    [Theory]
    [InlineData("Viewer")]
    [InlineData("Reviewer")]
    public async Task ForbiddenRoles_Return403AndCreateNothing(string role)
    {
        var channel = await SeedChannelAsync("Page cấm", "actor");
        var media = await SeedMediaAsync("nope.jpg");
        var postsBefore = await CountPostsAsync();

        using var response = await SendAsync(role, "actor", Payload([media], [channel], "Không được"));

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Equal(postsBefore, await CountPostsAsync());
        Assert.Equal(0, await CountPostMediaAsync());
    }

    [Fact]
    public async Task Unauthenticated_Returns401AndCreatesNothing()
    {
        var postsBefore = await CountPostsAsync();

        using var response = await SendAsync(null, null, Payload([Guid.NewGuid()], [Guid.NewGuid()], "Ẩn danh"));

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Equal(postsBefore, await CountPostsAsync());
    }

    [Fact]
    public async Task ForeignPage_Returns404WithoutLeakingNameOrId_AndCreatesNothing()
    {
        const string secretName = "Ten Bi Mat Khong Lo";
        var foreign = await SeedChannelAsync(secretName, "someone-else");
        var media = await SeedMediaAsync("a.jpg");
        var postsBefore = await CountPostsAsync();

        using var response = await SendAsync("ContentManager", "owner", Payload([media], [foreign], "Bài"));
        var body = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Contains("Page/Kênh không tồn tại.", body);
        Assert.DoesNotContain(secretName, body);
        Assert.DoesNotContain(foreign.ToString(), body);
        Assert.Equal(postsBefore, await CountPostsAsync());
    }

    [Fact]
    public async Task MixedWritableAndForeignPage_Returns404AndCreatesNothing()
    {
        var own = await SeedChannelAsync("Page của owner", "owner");
        var foreign = await SeedChannelAsync("Page người khác", "someone-else");
        var media = await SeedMediaAsync("a.jpg");
        var postsBefore = await CountPostsAsync();
        var linksBefore = await CountPostMediaAsync();

        using var response = await SendAsync(
            "ContentManager", "owner", Payload([media], [own, foreign], "Hai page"));

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Equal(postsBefore, await CountPostsAsync());
        Assert.Equal(linksBefore, await CountPostMediaAsync());
    }

    [Fact]
    public async Task Admin_CanCreateOnAnotherUsersPage()
    {
        var foreign = await SeedChannelAsync("Page người khác", "someone-else");
        var media = await SeedMediaAsync("a.jpg");

        using var response = await SendAsync("Admin", "admin-user", Payload([media], [foreign], "Admin tạo"));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(1, await CountPostsForChannelAsync(foreign));
    }

    [Fact]
    public async Task MissingPage_Returns404()
    {
        var media = await SeedMediaAsync("a.jpg");

        using var response = await SendAsync(
            "Admin", "admin-user", Payload([media], [Guid.NewGuid()], "Thiếu page"));

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Contains("Page/Kênh không tồn tại.", await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task OnePage_ThreeImages_ApprovesExactOrder_WithoutChangingAssetsOrCallingAi()
    {
        var channel = await SeedChannelAsync("Page A", "actor");
        var imageB = await SeedMediaAsync("B.jpg", caption: "cap-b", tags: "tag-b", alt: "alt-b", description: "desc-b");
        var imageA = await SeedMediaAsync("A.jpg", caption: "cap-a", tags: "tag-a", alt: "alt-a", description: "desc-a");
        var imageC = await SeedMediaAsync("C.jpg", caption: "cap-c", tags: "tag-c", alt: "alt-c", description: "desc-c");
        var before = _probe.Calls;

        using var response = await SendAsync(
            "ContentManager", "actor", Payload([imageB, imageA, imageC], [channel], "Caption người dùng"));
        var body = await response.Content.ReadFromJsonAsync<System.Text.Json.JsonElement>();
        var data = body.GetProperty("data");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal((int)GenerationFlow.UserSelectedMedia, data.GetProperty("generationFlow").GetInt32());
        Assert.Equal((int)PostStatus.Approved, data.GetProperty("status").GetInt32());
        Assert.Equal("Caption người dùng", data.GetProperty("content").GetString());
        Assert.Equal(before, _probe.Calls);

        var postId = data.GetProperty("id").GetGuid();
        var ordered = await LoadPublishOrderAsync(postId);
        Assert.Equal([imageB, imageA, imageC], ordered);
        await AssertAssetUnchangedAsync(imageB, "cap-b", "tag-b", "alt-b", "desc-b");
        await AssertAssetUnchangedAsync(imageA, "cap-a", "tag-a", "alt-a", "desc-a");
        await AssertAssetUnchangedAsync(imageC, "cap-c", "tag-c", "alt-c", "desc-c");
    }

    [Fact]
    public async Task ThreePages_ShareBatchId_AndEachPostHasTheGallery()
    {
        var channels = new[]
        {
            await SeedChannelAsync("P1", "actor"),
            await SeedChannelAsync("P2", "actor"),
            await SeedChannelAsync("P3", "actor"),
        };
        var images = new[]
        {
            await SeedMediaAsync("B.jpg"),
            await SeedMediaAsync("A.jpg"),
            await SeedMediaAsync("C.jpg"),
        };

        using var response = await SendAsync("ContentManager", "actor", Payload(images, channels, "Nhiều page"));
        var data = (await response.Content.ReadFromJsonAsync<System.Text.Json.JsonElement>()).GetProperty("data");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(3, data.GetProperty("created").GetInt32());
        var batchId = data.GetProperty("batchId").GetGuid();
        var postIds = data.GetProperty("postIds").EnumerateArray().Select(x => x.GetGuid()).ToList();
        Assert.Equal(3, postIds.Count);

        await using var db = new AppDbContext(_options);
        foreach (var postId in postIds)
        {
            var post = await db.Set<PostModel>().SingleAsync(x => x.Id == postId);
            Assert.Equal(batchId, post.BatchId);
            Assert.Equal(PostStatus.Approved, post.Status);
            Assert.Equal(images, await LoadPublishOrderAsync(postId));
        }
    }

    [Fact]
    public async Task InvalidMediaOrContent_Returns400AndCreatesNothing()
    {
        var channel = await SeedChannelAsync("Page A", "actor");
        var ok = await SeedMediaAsync("ok.jpg");
        var deleted = await SeedMediaAsync("gone.jpg", isDeleted: true);
        var pdf = await SeedMediaAsync("file.pdf", mime: "application/pdf");
        var noPath = await SeedMediaAsync("empty.jpg", storagePath: " ");
        var postsBefore = await CountPostsAsync();

        var cases = new List<CreatePostFromMediaRequest>
        {
            Payload([deleted], [channel], "X"),
            Payload([pdf], [channel], "X"),
            Payload([noPath], [channel], "X"),
            Payload([ok, ok], [channel], "X"),
            Payload(Enumerable.Range(0, 11).Select(_ => Guid.NewGuid()).ToList(), [channel], "X"),
            Payload([], [channel], "X"),
            Payload([ok], [channel], "   "),
        };

        foreach (var payload in cases)
        {
            using var response = await SendAsync("ContentManager", "actor", payload);
            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
            Assert.Contains("VALIDATION_ERROR", await response.Content.ReadAsStringAsync());
        }

        Assert.Equal(postsBefore, await CountPostsAsync());
    }

    [Fact]
    public async Task MissingOrDeletedCategory_Returns400AndCreatesNothing()
    {
        var channel = await SeedChannelAsync("Page A", "actor");
        var media = await SeedMediaAsync("ok.jpg");
        var deletedCategory = await SeedCategoryAsync(isDeleted: true);
        var postsBefore = await CountPostsAsync();

        foreach (var categoryId in new[] { Guid.NewGuid(), deletedCategory })
        {
            var payload = Payload([media], [channel], "Nội dung");
            payload.CategoryId = categoryId;
            using var response = await SendAsync("ContentManager", "actor", payload);
            var body = await response.Content.ReadAsStringAsync();
            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
            Assert.Contains("VALIDATION_ERROR", body);
        }

        Assert.Equal(postsBefore, await CountPostsAsync());
    }

    private async Task<HttpResponseMessage> SendAsync(
        string? role, string? user, CreatePostFromMediaRequest payload)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/Post/from-media")
        {
            Content = JsonContent.Create(payload),
        };
        if (role is not null)
        {
            request.Headers.Authorization = new AuthenticationHeaderValue(
                PostFromMediaAuthHandler.SchemeName, $"{role}:{user}");
        }

        return await _client.SendAsync(request);
    }

    private static CreatePostFromMediaRequest Payload(
        IReadOnlyList<Guid> mediaIds, IReadOnlyList<Guid> channelIds, string content) => new()
    {
        MediaIds = mediaIds.ToList(),
        SocialChannelIds = channelIds.ToList(),
        Content = content,
    };

    private async Task<Guid> SeedCategoryAsync(bool isDeleted = false)
    {
        await using var db = new AppDbContext(_options);
        var category = new Backend.Modules.Category.CategoryModel
        {
            Id = Guid.NewGuid(),
            Name = "Đã xoá",
            Slug = "da-xoa",
            IsDeleted = isDeleted,
            CreatedAt = DateTime.UtcNow,
        };
        db.Set<Backend.Modules.Category.CategoryModel>().Add(category);
        await db.SaveChangesAsync();
        return category.Id;
    }

    private async Task<Guid> SeedChannelAsync(string name, string createdBy)
    {
        await using var db = new AppDbContext(_options);
        var channel = new SocialChannelModel
        {
            Id = Guid.NewGuid(),
            Platform = SocialPlatform.Facebook,
            ChannelType = SocialChannelType.Page,
            PageName = name,
            ExternalPageId = Guid.NewGuid().ToString("N"),
            AccessToken = "token",
            IsActive = true,
            CreatedAt = DateTime.UtcNow,
            CreatedBy = createdBy,
        };
        db.Set<SocialChannelModel>().Add(channel);
        await db.SaveChangesAsync();
        return channel.Id;
    }

    private async Task<Guid> SeedMediaAsync(
        string name,
        string mime = "image/jpeg",
        string storagePath = "media/a.jpg",
        string caption = "caption",
        string tags = "tags",
        string alt = "alt",
        string description = "description",
        bool isDeleted = false)
    {
        await using var db = new AppDbContext(_options);
        var asset = new MediaAssetModel
        {
            Id = Guid.NewGuid(),
            FileName = name,
            StoragePath = storagePath,
            MimeType = mime,
            Caption = caption,
            Tags = tags,
            AltText = alt,
            Description = description,
            IsDeleted = isDeleted,
            CreatedAt = DateTime.UtcNow,
        };
        db.Set<MediaAssetModel>().Add(asset);
        await db.SaveChangesAsync();
        return asset.Id;
    }

    private async Task<List<Guid>> LoadPublishOrderAsync(Guid postId)
    {
        await using var db = new AppDbContext(_options);
        var rows = await db.Set<PostMediaModel>()
            .Where(x => x.PostId == postId && !x.IsDeleted)
            .ToListAsync();
        var cover = rows.First(x => x.MediaRole == MediaRole.Cover);
        Assert.Equal(0, cover.SortOrder);
        var ordered = new List<Guid> { cover.MediaId };
        ordered.AddRange(rows.Where(x => x.Id != cover.Id).OrderBy(x => x.SortOrder).Select(x => x.MediaId));
        Assert.All(rows.Where(x => x.Id != cover.Id), row => Assert.Equal(MediaRole.Attachment, row.MediaRole));
        return ordered;
    }

    private async Task AssertAssetUnchangedAsync(
        Guid id, string caption, string tags, string alt, string description)
    {
        await using var db = new AppDbContext(_options);
        var asset = await db.Set<MediaAssetModel>().SingleAsync(x => x.Id == id);
        Assert.Equal(caption, asset.Caption);
        Assert.Equal(tags, asset.Tags);
        Assert.Equal(alt, asset.AltText);
        Assert.Equal(description, asset.Description);
    }

    private async Task<int> CountPostsAsync()
    {
        await using var db = new AppDbContext(_options);
        return await db.Set<PostModel>().CountAsync(x => !x.IsDeleted);
    }

    private async Task<int> CountPostsForChannelAsync(Guid channelId)
    {
        await using var db = new AppDbContext(_options);
        return await db.Set<PostModel>().CountAsync(x => !x.IsDeleted && x.SocialChannelId == channelId);
    }

    private async Task<int> CountPostMediaAsync()
    {
        await using var db = new AppDbContext(_options);
        return await db.Set<PostMediaModel>().CountAsync(x => !x.IsDeleted);
    }
}

internal sealed class AiProbe
{
    public int Calls;
    public void Hit() => Interlocked.Increment(ref Calls);
}

internal sealed class CountingChatHandler(AiProbe probe) : HttpMessageHandler
{
    protected override Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request, CancellationToken cancellationToken)
    {
        probe.Hit();
        return Task.FromResult(new HttpResponseMessage(HttpStatusCode.InternalServerError));
    }
}

internal sealed class CountingTextService(AiProbe probe) : IAiTextGenerationService
{
    public bool IsAvailable(string? provider = null) => false;
    public Task<AiTextGenerationResult> GenerateAsync(AiTextGenerationRequest request, CancellationToken ct = default)
    {
        probe.Hit();
        throw new InvalidOperationException("AI text không được gọi");
    }
    public Task<List<string>> SuggestIdeasAsync(string topic, int count, string? category, CancellationToken ct = default)
    {
        probe.Hit();
        throw new InvalidOperationException("AI text không được gọi");
    }
    public Task<string> ComposeImagePromptAsync(AiImagePromptRequest request, CancellationToken ct = default)
    {
        probe.Hit();
        throw new InvalidOperationException("AI text không được gọi");
    }
}

internal sealed class CountingImageService(AiProbe probe) : IAiImageGenerationService
{
    public bool IsAvailable(string? provider = null) => false;
    public Task<AiImageGenerationResult> GenerateAsync(AiImageGenerationRequest request, CancellationToken ct = default)
    {
        probe.Hit();
        throw new InvalidOperationException("AI image không được gọi");
    }
}

internal sealed class IdlePublishService : ISocialPublishService
{
    public Task<SocialPublishResult> PublishAsync(SocialPublishRequest request, CancellationToken ct = default)
        => throw new InvalidOperationException("Không đăng trong from-media");
    public Task<SocialPublishResult> CommentAsync(SocialCommentPublishRequest request, CancellationToken ct = default)
        => throw new InvalidOperationException("Không đăng trong from-media");
}

internal sealed class IdleOverlayService : IImageOverlayService
{
    public Task<ImageOverlayResult> RenderAsync(ImageOverlayRequest request, CancellationToken ct = default)
        => throw new InvalidOperationException("Không overlay trong from-media");
}

internal sealed class PostFromMediaAuthHandler : AuthenticationHandler<AuthenticationSchemeOptions>
{
    public const string SchemeName = "TestPostFromMedia";

    public PostFromMediaAuthHandler(
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
        return Task.FromResult(AuthenticateResult.Success(new AuthenticationTicket(new ClaimsPrincipal(identity), SchemeName)));
    }
}
