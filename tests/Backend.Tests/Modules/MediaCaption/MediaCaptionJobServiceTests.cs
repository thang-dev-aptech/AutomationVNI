using Backend.Data;
using Backend.Modules.GoogleDrive;
using Backend.Modules.MediaAsset;
using Backend.Modules.MediaCaption;
using Backend.Modules.MediaFolder;
using Backend.Shared.Repositories;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Backend.Tests.Modules.MediaCaption;

public sealed class MediaCaptionJobServiceTests : IDisposable
{
    private readonly SqliteConnection _connection = new("DataSource=:memory:");
    private readonly AppDbContext _db;
    private readonly MediaCaptionJobService _service;

    public MediaCaptionJobServiceTests()
    {
        _connection.Open();
        _db = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>().UseSqlite(_connection).Options);
        _db.Database.EnsureCreated();
        var user = new StubUserContext();
        _service = new MediaCaptionJobService(_db, new MediaFolderRepository(_db, user));
    }

    [Fact]
    public async Task CreateAsync_CollectsOnlyActiveImagesInDriveSubtree_AndSkipsExistingCaption()
    {
        var root = new MediaFolderModel { Id = Guid.NewGuid(), Name = "Google Drive" };
        var child = new MediaFolderModel { Id = Guid.NewGuid(), Name = "Child", ParentFolderId = root.Id };
        var grandchild = new MediaFolderModel { Id = Guid.NewGuid(), Name = "Grandchild", ParentFolderId = child.Id };
        var sibling = new MediaFolderModel { Id = Guid.NewGuid(), Name = "Sibling", ParentFolderId = root.Id };
        var deleted = new MediaFolderModel { Id = Guid.NewGuid(), Name = "Deleted", ParentFolderId = child.Id, IsDeleted = true };
        _db.MediaFolders.AddRange(root, child, grandchild, sibling, deleted);
        await SetDedicatedRootAsync(root.Id);
        _db.MediaAssets.AddRange(
            Asset(child.Id, "direct.png"), Asset(grandchild.Id, "nested.jpg"),
            Asset(sibling.Id, "outside.png"), Asset(child.Id, "video.mp4", "video/mp4"),
            Asset(deleted.Id, "deleted-folder.png"), Asset(child.Id, "already.png", caption: "Giữ nguyên"),
            Asset(child.Id, "deleted.png", deleted: true));
        await _db.SaveChangesAsync();

        var job = await _service.CreateAsync(child.Id);

        Assert.Equal(2, job.Total);
        Assert.Equal(1, job.Skipped);
        Assert.Equal(MediaCaptionJobStatus.Queued, job.Status);
        var items = await _db.MediaCaptionJobItems.Where(x => x.JobId == job.Id).ToListAsync();
        Assert.Equal(2, items.Count);
        Assert.All(items, x => Assert.Equal(MediaCaptionJobItemStatus.Pending, x.Status));
        Assert.DoesNotContain(items, x => x.FileName == "outside.png");
    }

    [Fact]
    public async Task CreateAsync_RejectsPageFolderMissingAndDeleted()
    {
        var root = new MediaFolderModel { Id = Guid.NewGuid(), Name = "Google Drive" };
        var pageFolder = new MediaFolderModel { Id = Guid.NewGuid(), Name = "Page", SocialChannelId = Guid.NewGuid() };
        var deleted = new MediaFolderModel { Id = Guid.NewGuid(), Name = "Deleted", IsDeleted = true };
        _db.MediaFolders.AddRange(root, pageFolder, deleted);
        await SetDedicatedRootAsync(root.Id);
        await _db.SaveChangesAsync();

        await Assert.ThrowsAsync<ArgumentException>(() => _service.CreateAsync(pageFolder.Id));
        await Assert.ThrowsAsync<KeyNotFoundException>(() => _service.CreateAsync(deleted.Id));
        await Assert.ThrowsAsync<KeyNotFoundException>(() => _service.CreateAsync(Guid.NewGuid()));
        Assert.Empty(_db.MediaCaptionJobs);
    }

    [Fact]
    public async Task CreateAsync_ReusesActiveJob_AndRetryOnlyAllowsFailedItems()
    {
        var root = new MediaFolderModel { Id = Guid.NewGuid(), Name = "Google Drive" };
        _db.MediaFolders.Add(root);
        await SetDedicatedRootAsync(root.Id);
        _db.MediaAssets.Add(Asset(root.Id, "one.png"));
        await _db.SaveChangesAsync();

        var first = await _service.CreateAsync(root.Id);
        var second = await _service.CreateAsync(root.Id);
        Assert.Equal(first.Id, second.Id);
        var item = await _db.MediaCaptionJobItems.SingleAsync();
        await Assert.ThrowsAsync<InvalidOperationException>(() => _service.RetryItemAsync(item.Id));
        item.Status = MediaCaptionJobItemStatus.Failed;
        await _db.SaveChangesAsync();
        await _service.RetryItemAsync(item.Id);
        Assert.Equal(MediaCaptionJobItemStatus.Pending, item.Status);
    }

    private static MediaAssetModel Asset(Guid folderId, string name, string mime = "image/png", string? caption = null, bool deleted = false) => new()
    {
        Id = Guid.NewGuid(), FolderId = folderId, FileName = name, StoragePath = name, MimeType = mime,
        Caption = caption, IsDeleted = deleted, CreatedAt = DateTime.UtcNow
    };

    private async Task SetDedicatedRootAsync(Guid rootId)
    {
        var state = await _db.GoogleDriveSyncStates.SingleAsync(x => x.Id == GoogleDriveSyncStateModel.SingletonId);
        state.DedicatedFolderId = rootId;
    }

    public void Dispose() { _db.Dispose(); _connection.Dispose(); }

    private sealed class StubUserContext : IUserContext
    {
        public Guid? GetCurrentUserId() => null;
        public string? GetCurrentUserName() => "test";
        public IReadOnlyList<string> GetCurrentUserRoles() => ["Admin"];
    }
}
