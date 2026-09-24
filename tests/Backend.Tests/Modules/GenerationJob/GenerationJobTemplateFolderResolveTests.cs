using System.Reflection;
using Backend.Data;
using Backend.Modules.ContentCrawl;
using Backend.Modules.GenerationJob;
using Backend.Modules.MediaAsset;
using Backend.Modules.MediaFolder;
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
/// Regression: sau media revamp mỗi Page có root + "template" + "chung_chi" cùng SocialChannelId.
/// FirstOrDefault theo channel lấy folder bất kỳ; Template flow phải resolve root rồi child "template".
/// </summary>
public class GenerationJobTemplateFolderResolveTests : IDisposable
{
    private readonly SqliteConnection _connection;
    private readonly AppDbContext _db;
    private readonly TestUserContext _userContext;
    private readonly GenerationJobPipelineService _pipeline;

    public GenerationJobTemplateFolderResolveTests()
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
    public async Task ResolvePageSubfolderAsync_WithRootTemplateAndChungChi_ReturnsTemplateNotRootOrChungChi()
    {
        var pageId = Guid.NewGuid();
        _db.SocialChannels.Add(new SocialChannelModel
        {
            Id = pageId,
            Platform = SocialPlatform.Facebook,
            ChannelType = SocialChannelType.Page,
            PageName = "Page Template",
            ExternalPageId = "fb-template-page",
            AccessToken = "token",
            IsActive = true
        });

        // Insert root first so the old FirstOrDefault(SocialChannelId) would pick it.
        var root = AddFolder(pageId, "Page Template", parentId: null);
        var chungChi = AddFolder(pageId, "chung_chi", parentId: root.Id);
        var template = AddFolder(pageId, "template", parentId: root.Id);
        var nestedTemplate = AddFolder(pageId, "template", parentId: chungChi.Id);

        var otherPageId = Guid.NewGuid();
        _db.SocialChannels.Add(new SocialChannelModel
        {
            Id = otherPageId,
            Platform = SocialPlatform.Facebook,
            ChannelType = SocialChannelType.Page,
            PageName = "Other Page",
            ExternalPageId = "fb-other-page",
            AccessToken = "token",
            IsActive = true
        });
        var otherRoot = AddFolder(otherPageId, "Other Page", parentId: null);
        var otherTemplate = AddFolder(otherPageId, "template", parentId: otherRoot.Id);

        var rootImage = AddImage(root.Id);
        var chungChiImage = AddImage(chungChi.Id);
        var templateImage = AddImage(template.Id);
        AddImage(nestedTemplate.Id);
        AddImage(otherTemplate.Id);
        await _db.SaveChangesAsync();

        var resolved = await InvokePrivateAsync<MediaFolderModel>(
            _pipeline, "ResolvePageSubfolderAsync", pageId, "template", CancellationToken.None);

        Assert.NotNull(resolved);
        Assert.Equal(template.Id, resolved.Id);
        Assert.NotEqual(root.Id, resolved.Id);
        Assert.NotEqual(chungChi.Id, resolved.Id);
        Assert.NotEqual(nestedTemplate.Id, resolved.Id);
        Assert.NotEqual(otherTemplate.Id, resolved.Id);

        var candidateIds = await InvokePrivateAsync<List<Guid>>(
            _pipeline, "LoadFolderImageCandidateIdsAsync", resolved, CancellationToken.None);

        Assert.NotNull(candidateIds);
        Assert.Equal([templateImage.Id], candidateIds);
        Assert.DoesNotContain(rootImage.Id, candidateIds);
        Assert.DoesNotContain(chungChiImage.Id, candidateIds);
    }

    [Fact]
    public async Task ResolvePageSubfolderAsync_MissingTemplateChild_ReturnsNull()
    {
        var pageId = Guid.NewGuid();
        var root = AddFolder(pageId, "Page Only Root", parentId: null);
        AddFolder(pageId, "chung_chi", parentId: root.Id);
        await _db.SaveChangesAsync();

        var resolved = await InvokePrivateAsync<MediaFolderModel>(
            _pipeline, "ResolvePageSubfolderAsync", pageId, "template", CancellationToken.None);

        Assert.Null(resolved);
    }

    private GenerationJobPipelineService CreatePipeline()
    {
        var folders = new MediaFolderRepository(_db, _userContext);
        return new GenerationJobPipelineService(
            _db,
            null!,
            null!,
            null!,
            null!,
            null!,
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

    private MediaAssetModel AddImage(Guid folderId)
    {
        var asset = new MediaAssetModel
        {
            Id = Guid.NewGuid(),
            FolderId = folderId,
            FileName = $"img-{Guid.NewGuid():N}.jpg",
            StoragePath = "p",
            PublicUrl = "u",
            MimeType = "image/jpeg"
        };
        _db.MediaAssets.Add(asset);
        return asset;
    }

    private static async Task<T?> InvokePrivateAsync<T>(object target, string name, params object[] args)
        where T : class
    {
        var method = target.GetType().GetMethod(name, BindingFlags.Instance | BindingFlags.NonPublic)
            ?? throw new InvalidOperationException($"Missing private method {name}");
        var result = method.Invoke(target, args)
            ?? throw new InvalidOperationException($"{name} returned null task");
        return await (Task<T?>)result;
    }
}
