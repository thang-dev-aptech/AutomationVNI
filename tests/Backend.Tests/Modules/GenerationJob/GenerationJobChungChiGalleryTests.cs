using System.Reflection;
using Backend.Data;
using Backend.Modules.ContentCrawl;
using Backend.Modules.GenerationJob;
using Backend.Modules.GenerationJob.Enums;
using Backend.Modules.MediaAsset;
using Backend.Modules.MediaAsset.Enums;
using Backend.Modules.MediaFolder;
using Backend.Modules.Post;
using Backend.Modules.Post.Enums;
using Backend.Modules.SocialChannel;
using Backend.Modules.SocialChannel.Enums;
using Backend.Shared.SocialPublish;
using Backend.Tests.Modules.MediaFolder;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;

namespace Backend.Tests.Modules.GenerationJob;

/// <summary>
/// ChungChiGallery: ảnh nguyên trạng từ thư mục con chung_chi của Page, không overlay, không lấy
/// nhầm ảnh template/root, All mode không bị cap 100 item của endpoint phân trang.
/// </summary>
public class GenerationJobChungChiGalleryTests : IDisposable
{
    private readonly SqliteConnection _connection;
    private readonly AppDbContext _db;
    private readonly TestUserContext _userContext;
    private readonly GenerationJobPipelineService _pipeline;

    public GenerationJobChungChiGalleryTests()
    {
        _connection = new SqliteConnection("DataSource=:memory:");
        _connection.Open();

        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlite(_connection)
            .Options;

        _db = new AppDbContext(options);
        _db.Database.EnsureCreated();
        _userContext = new TestUserContext();
        _pipeline = CreatePipeline();
    }

    public void Dispose()
    {
        _db.Dispose();
        _connection.Dispose();
    }

    [Fact]
    public async Task GenerateFromChungChiAsync_Random_AttachesExactCountFromChungChiOnly()
    {
        var pageId = Guid.NewGuid();
        var (chungChiIds, templateIds, rootIds) = await SeedPageWithFoldersAsync(pageId,
            chungChiCount: 5, templateCount: 3, rootCount: 2);

        var post = await AddPostAsync(pageId, ChungChiSelectionMode.Random, randomCount: 3);
        await InvokeGenerateFromChungChiAsync(post);

        var links = await LoadGalleryAsync(post.Id);
        Assert.Equal(3, links.Count);
        Assert.Equal(MediaRole.Cover, links[0].MediaRole);
        Assert.All(links.Skip(1), l => Assert.Equal(MediaRole.Attachment, l.MediaRole));
        Assert.All(links, l => Assert.NotEqual(MediaRole.TemplateSource, l.MediaRole));
        Assert.All(links, l => Assert.Contains(l.MediaId, chungChiIds));
        Assert.All(links, l => Assert.DoesNotContain(l.MediaId, templateIds));
        Assert.All(links, l => Assert.DoesNotContain(l.MediaId, rootIds));

        await AssertNoOverlayJobAndWaitingReview(post.Id);
    }

    [Fact]
    public async Task GenerateFromChungChiAsync_All_AttachesEveryActiveAssetIncludingOverPageCap()
    {
        var pageId = Guid.NewGuid();
        var (chungChiIds, templateIds, _) = await SeedPageWithFoldersAsync(pageId,
            chungChiCount: 102, templateCount: 4, rootCount: 2);

        var deleted = await _db.MediaAssets.FirstAsync(a => chungChiIds.Contains(a.Id));
        deleted.IsDeleted = true;
        var liveChungChiIds = chungChiIds.Where(id => id != deleted.Id).ToList();
        await _db.SaveChangesAsync();

        var post = await AddPostAsync(pageId, ChungChiSelectionMode.All);
        await InvokeGenerateFromChungChiAsync(post);

        var links = await LoadGalleryAsync(post.Id);
        Assert.Equal(101, liveChungChiIds.Count);
        Assert.True(links.Count > 100, "All mode must not stop at a 100-item page cap");
        Assert.Equal(liveChungChiIds.Count, links.Count);
        Assert.Equal(liveChungChiIds.Count, links.Select(l => l.MediaId).Distinct().Count());
        Assert.All(links, l => Assert.Contains(l.MediaId, liveChungChiIds));
        Assert.All(links, l => Assert.DoesNotContain(l.MediaId, templateIds));
        Assert.DoesNotContain(deleted.Id, links.Select(l => l.MediaId));
        Assert.Equal(MediaRole.Cover, links[0].MediaRole);
        Assert.Equal(100, links.Count(l => l.MediaRole == MediaRole.Attachment));

        await AssertNoOverlayJobAndWaitingReview(post.Id);
    }

    [Fact]
    public async Task GenerateFromChungChiAsync_MissingFolder_SetsNeedFix()
    {
        var pageId = Guid.NewGuid();
        AddPage(pageId);
        AddFolder(pageId, "Page Root", parentId: null);
        await _db.SaveChangesAsync();

        var post = await AddPostAsync(pageId, ChungChiSelectionMode.Random, randomCount: 1);
        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => InvokeGenerateFromChungChiAsync(post));
        Assert.Contains("chung_chi", ex.Message);

        var reloaded = await _db.Posts.SingleAsync(p => p.Id == post.Id);
        Assert.Equal(PostStatus.NeedFix, reloaded.Status);
        Assert.Contains("chung_chi", reloaded.GenerationError);
        Assert.Empty(await LoadGalleryAsync(post.Id));
        Assert.Empty(await _db.GenerationJobs.Where(j => j.PostId == post.Id).ToListAsync());
    }

    [Fact]
    public async Task GenerateFromChungChiAsync_EmptyFolder_SetsNeedFix()
    {
        var pageId = Guid.NewGuid();
        AddPage(pageId);
        var root = AddFolder(pageId, "Page Root", parentId: null);
        AddFolder(pageId, "chung_chi", parentId: root.Id);
        AddFolder(pageId, "template", parentId: root.Id);
        AddImage(root.Id);
        await _db.SaveChangesAsync();

        var post = await AddPostAsync(pageId, ChungChiSelectionMode.All);
        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => InvokeGenerateFromChungChiAsync(post));
        Assert.Contains("chung_chi", ex.Message);

        var reloaded = await _db.Posts.SingleAsync(p => p.Id == post.Id);
        Assert.Equal(PostStatus.NeedFix, reloaded.Status);
        Assert.Empty(await LoadGalleryAsync(post.Id));
    }

    [Fact]
    public async Task GenerateFromChungChiAsync_RandomNotEnoughCandidates_SetsNeedFix()
    {
        var pageId = Guid.NewGuid();
        await SeedPageWithFoldersAsync(pageId, chungChiCount: 2, templateCount: 8, rootCount: 8);

        var post = await AddPostAsync(pageId, ChungChiSelectionMode.Random, randomCount: 5);
        await Assert.ThrowsAsync<InvalidOperationException>(() => InvokeGenerateFromChungChiAsync(post));

        var reloaded = await _db.Posts.SingleAsync(p => p.Id == post.Id);
        Assert.Equal(PostStatus.NeedFix, reloaded.Status);
        Assert.Contains("không đủ 5 ảnh", reloaded.GenerationError);
        Assert.Empty(await LoadGalleryAsync(post.Id));
    }

    private async Task<(List<Guid> ChungChi, List<Guid> Template, List<Guid> Root)> SeedPageWithFoldersAsync(
        Guid pageId, int chungChiCount, int templateCount, int rootCount)
    {
        AddPage(pageId);
        var root = AddFolder(pageId, "Page Root", parentId: null);
        var template = AddFolder(pageId, "template", parentId: root.Id);
        var chungChi = AddFolder(pageId, "chung_chi", parentId: root.Id);

        var rootIds = Enumerable.Range(0, rootCount).Select(_ => AddImage(root.Id).Id).ToList();
        var templateIds = Enumerable.Range(0, templateCount).Select(_ => AddImage(template.Id).Id).ToList();
        var chungChiIds = Enumerable.Range(0, chungChiCount).Select(_ => AddImage(chungChi.Id).Id).ToList();
        AddImage(chungChi.Id, mimeType: "video/mp4");
        await _db.SaveChangesAsync();
        return (chungChiIds, templateIds, rootIds);
    }

    private async Task<PostModel> AddPostAsync(
        Guid pageId, ChungChiSelectionMode mode, int? randomCount = null)
    {
        var post = new PostModel
        {
            Title = "Chung chi idea",
            Content = "Generated caption",
            SocialChannelId = pageId,
            GenerationFlow = GenerationFlow.ChungChiGallery,
            ImageCount = mode == ChungChiSelectionMode.Random ? (randomCount ?? 1) : null,
            Status = PostStatus.WaitingReview,
            ExtraJson = $"{{\"chungChi\":{{\"mode\":{(int)mode}}}}}",
            UserId = _userContext.UserId ?? Guid.NewGuid()
        };
        _db.Posts.Add(post);
        await _db.SaveChangesAsync();
        return post;
    }

    private Task InvokeGenerateFromChungChiAsync(PostModel post)
        => InvokePrivateVoidAsync(_pipeline, "GenerateFromChungChiAsync", post, CancellationToken.None);

    private async Task<List<PostMediaModel>> LoadGalleryAsync(Guid postId)
        => await _db.PostMedias
            .Where(x => x.PostId == postId && !x.IsDeleted)
            .OrderBy(x => x.SortOrder)
            .ToListAsync();

    private async Task AssertNoOverlayJobAndWaitingReview(Guid postId)
    {
        var reloaded = await _db.Posts.SingleAsync(p => p.Id == postId);
        Assert.Equal(PostStatus.WaitingReview, reloaded.Status);
        Assert.NotEqual(PostStatus.RenderingTemplate, reloaded.Status);

        var jobs = await _db.GenerationJobs.Where(j => j.PostId == postId).ToListAsync();
        Assert.DoesNotContain(jobs, j => j.JobType == JobType.ImageOverlay);
        Assert.Empty(jobs);
    }

    private GenerationJobPipelineService CreatePipeline()
    {
        var folders = new MediaFolderRepository(_db, _userContext);
        var postMedia = new PostMediaRepository(_db, _userContext);
        return new GenerationJobPipelineService(
            _db,
            null!,
            null!,
            null!,
            null!,
            postMedia,
            null!,
            folders,
            null!,
            null!,
            null!,
            null!,
            null!,
            null!,
            Options.Create(new ContentCrawlOptions()),
            Options.Create(new ReelsOptions()),
            _userContext,
            NullLogger<GenerationJobPipelineService>.Instance);
    }

    private void AddPage(Guid pageId)
    {
        _db.SocialChannels.Add(new SocialChannelModel
        {
            Id = pageId,
            Platform = SocialPlatform.Facebook,
            ChannelType = SocialChannelType.Page,
            PageName = "Page Chung Chi",
            ExternalPageId = $"fb-{pageId:N}",
            AccessToken = "token",
            IsActive = true
        });
    }

    private MediaFolderModel AddFolder(Guid pageId, string name, Guid? parentId)
    {
        var folder = new MediaFolderModel
        {
            Id = Guid.NewGuid(),
            Name = name,
            SocialChannelId = pageId,
            ParentFolderId = parentId
        };
        _db.MediaFolders.Add(folder);
        return folder;
    }

    private MediaAssetModel AddImage(Guid folderId, string mimeType = "image/jpeg")
    {
        var asset = new MediaAssetModel
        {
            Id = Guid.NewGuid(),
            FolderId = folderId,
            FileName = $"img-{Guid.NewGuid():N}.jpg",
            StoragePath = "p",
            PublicUrl = "u",
            MimeType = mimeType
        };
        _db.MediaAssets.Add(asset);
        return asset;
    }

    private static async Task InvokePrivateVoidAsync(object target, string name, params object[] args)
    {
        var method = target.GetType().GetMethod(name, BindingFlags.Instance | BindingFlags.NonPublic)
            ?? throw new InvalidOperationException($"Missing private method {name}");
        var result = method.Invoke(target, args)
            ?? throw new InvalidOperationException($"{name} returned null task");
        await (Task)result;
    }
}
