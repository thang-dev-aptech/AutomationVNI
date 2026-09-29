using Backend.Data;
using Backend.Modules.GoogleDrive;
using Backend.Modules.MediaFolder;
using Backend.Modules.SocialChannel;
using Backend.Modules.SocialChannel.Enums;
using Xunit;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace Backend.Tests.Modules.MediaFolder;

/// <summary>
/// GDRIVE-04 (t6) — AC gdrive04-pageless-folder-test (44ad3f88): folder chuyên dụng page-less
/// browse được qua GetPageRoots/GetChildren/GetBreadcrumb, ghim đầu danh sách.
/// </summary>
public class MediaFolderRepositoryTests : IDisposable
{
    private readonly SqliteConnection _connection;
    private readonly AppDbContext _db;
    private readonly Guid _pageAId = Guid.NewGuid();
    private readonly MediaFolderRepository _folderRepo;
    private readonly GoogleDriveRepository _driveRepo;

    public MediaFolderRepositoryTests()
    {
        _connection = new SqliteConnection("DataSource=:memory:");
        _connection.Open();
        var options = new DbContextOptionsBuilder<AppDbContext>().UseSqlite(_connection).Options;
        _db = new AppDbContext(options);
        _db.Database.EnsureCreated();

        _db.SocialChannels.Add(new SocialChannelModel
        {
            Id = _pageAId,
            Platform = SocialPlatform.Facebook,
            ChannelType = SocialChannelType.Page,
            PageName = "Page A",
            ExternalPageId = "fb-page-a",
            AccessToken = "token-a",
            IsActive = true,
            CreatedAt = DateTime.UtcNow,
        });
        _db.MediaFolders.Add(new MediaFolderModel
        {
            Id = Guid.NewGuid(),
            Name = "Root A",
            SocialChannelId = _pageAId,
            ParentFolderId = null,
            CreatedAt = DateTime.UtcNow,
        });
        _db.SaveChanges();

        var user = new TestUserContext { Roles = ["Admin"] };
        _folderRepo = new MediaFolderRepository(_db, user);
        _driveRepo = new GoogleDriveRepository(_db, user);
    }

    public void Dispose()
    {
        _db.Dispose();
        _connection.Dispose();
    }

    [Fact]
    public async Task DedicatedFolder_AppearsFirstInPageRoots_AndChildrenBreadcrumbDoNotThrow()
    {
        var dedicatedId = await _driveRepo.GetOrCreateDedicatedFolderAsync("test-drive-root");

        var roots = await _folderRepo.GetPageRootsAsync(new GetMediaFolderPageRootsRequest
        {
            Index = 1,
            Size = 20,
        });

        Assert.True(roots.Items.Count >= 2);
        Assert.Equal(dedicatedId, roots.Items[0].Id);
        Assert.Equal("Google Drive", roots.Items[0].Name);
        Assert.Null(roots.Items[0].SocialChannelId);
        Assert.Contains(roots.Items.Skip(1), x => x.SocialChannelId == _pageAId);

        // Guid.Empty SocialChannelId — không throw khi ParentFolderId = dedicated.
        var children = await _folderRepo.GetChildrenAsync(new GetMediaFolderChildrenRequest
        {
            SocialChannelId = Guid.Empty,
            ParentFolderId = dedicatedId,
            Index = 1,
            Size = 20,
        });
        Assert.Empty(children.Items);
        Assert.Equal(0, children.Total);

        var breadcrumb = await _folderRepo.GetBreadcrumbAsync(new GetMediaFolderBreadcrumbRequest
        {
            SocialChannelId = Guid.Empty,
            FolderId = dedicatedId,
        });
        Assert.Single(breadcrumb.Ancestors);
        Assert.Equal(dedicatedId, breadcrumb.Ancestors[0].Id);
        Assert.Equal("Google Drive", breadcrumb.Ancestors[0].Name);
    }

    /// <summary>
    /// Fix bug d6c04d1e: GDRIVE-05 thêm cây MediaFolder con thật (page-less) bên dưới dedicated
    /// root — GetChildrenAsync/GetBreadcrumbAsync phải browse đúng ở MỌI cấp, không chỉ đúng root
    /// như hành vi GDRIVE-04 cũ (children luôn rỗng).
    /// </summary>
    [Fact]
    public async Task PageLessTree_BrowsesRealSubfoldersAtEveryDepth_ChildrenAndBreadcrumb()
    {
        var dedicatedId = await _driveRepo.GetOrCreateDedicatedFolderAsync("test-drive-root");

        var folderA = new MediaFolderModel
        {
            Id = Guid.NewGuid(),
            Name = "Thư mục A",
            SocialChannelId = null,
            ParentFolderId = dedicatedId,
            CreatedAt = DateTime.UtcNow,
        };
        var folderB = new MediaFolderModel
        {
            Id = Guid.NewGuid(),
            Name = "Thư mục B",
            SocialChannelId = null,
            ParentFolderId = folderA.Id,
            CreatedAt = DateTime.UtcNow,
        };
        _db.MediaFolders.AddRange(folderA, folderB);
        await _db.SaveChangesAsync();

        var rootChildren = await _folderRepo.GetChildrenAsync(new GetMediaFolderChildrenRequest
        {
            SocialChannelId = Guid.Empty,
            ParentFolderId = dedicatedId,
            Index = 1,
            Size = 20,
        });
        Assert.Single(rootChildren.Items);
        Assert.Equal(folderA.Id, rootChildren.Items[0].Id);
        Assert.True(rootChildren.Items[0].HasChildren);

        var aChildren = await _folderRepo.GetChildrenAsync(new GetMediaFolderChildrenRequest
        {
            SocialChannelId = Guid.Empty,
            ParentFolderId = folderA.Id,
            Index = 1,
            Size = 20,
        });
        Assert.Single(aChildren.Items);
        Assert.Equal(folderB.Id, aChildren.Items[0].Id);

        var breadcrumbB = await _folderRepo.GetBreadcrumbAsync(new GetMediaFolderBreadcrumbRequest
        {
            SocialChannelId = Guid.Empty,
            FolderId = folderB.Id,
        });
        Assert.Equal(3, breadcrumbB.Ancestors.Count);
        Assert.Equal(dedicatedId, breadcrumbB.Ancestors[0].Id);
        Assert.Equal(folderA.Id, breadcrumbB.Ancestors[1].Id);
        Assert.Equal(folderB.Id, breadcrumbB.Ancestors[2].Id);
    }

    /// <summary>
    /// Ranh giới an ninh: folder con thuộc Page A (SocialChannelId có giá trị) tuyệt đối KHÔNG
    /// được lọt vào nhánh page-less mới, dù caller cố tình gọi với SocialChannelId=Guid.Empty —
    /// vẫn phải đòi hỏi SocialChannelId thật/throw như trước, không bị GDRIVE-05 nới lỏng nhầm.
    /// </summary>
    [Fact]
    public async Task PageOwnedFolder_StillRequiresSocialChannelId_NotTreatedAsPageLess()
    {
        var pageAFolder = await _db.MediaFolders
            .FirstAsync(f => f.SocialChannelId == _pageAId && f.ParentFolderId == null);
        var pageAChild = new MediaFolderModel
        {
            Id = Guid.NewGuid(),
            Name = "Con của Page A",
            SocialChannelId = _pageAId,
            ParentFolderId = pageAFolder.Id,
            CreatedAt = DateTime.UtcNow,
        };
        _db.MediaFolders.Add(pageAChild);
        await _db.SaveChangesAsync();

        await Assert.ThrowsAsync<ArgumentException>(() => _folderRepo.GetChildrenAsync(
            new GetMediaFolderChildrenRequest
            {
                SocialChannelId = Guid.Empty,
                ParentFolderId = pageAFolder.Id,
                Index = 1,
                Size = 20,
            }));

        await Assert.ThrowsAsync<ArgumentException>(() => _folderRepo.GetBreadcrumbAsync(
            new GetMediaFolderBreadcrumbRequest
            {
                SocialChannelId = Guid.Empty,
                FolderId = pageAChild.Id,
            }));

        // Với đúng SocialChannelId thì vẫn hoạt động bình thường như trước.
        var children = await _folderRepo.GetChildrenAsync(new GetMediaFolderChildrenRequest
        {
            SocialChannelId = _pageAId,
            ParentFolderId = pageAFolder.Id,
            Index = 1,
            Size = 20,
        });
        Assert.Single(children.Items);
        Assert.Equal(pageAChild.Id, children.Items[0].Id);
    }
}
