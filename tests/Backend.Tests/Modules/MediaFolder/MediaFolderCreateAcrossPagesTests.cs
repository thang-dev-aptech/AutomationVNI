using Backend.Data;
using Backend.Modules.MediaFolder;
using Backend.Modules.SocialChannel;
using Backend.Modules.SocialChannel.Enums;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Backend.Tests.Modules.MediaFolder;

/// <summary>
/// MEDIA-06 (đã sửa lại lần 2 sau khi làm rõ với người dùng): tạo 1 folder gốc ở nhiều Page
/// cùng lúc, MỖI Page có tên riêng (frontend thường điền = tên Page), best-effort — Page lỗi
/// không chặn Page khác. Đây KHÔNG phải hierarchy trong một Page (đó là MEDIA-03/BulkCreateAsync,
/// vẫn giữ nguyên, không đổi).
/// </summary>
public class MediaFolderCreateAcrossPagesTests : IDisposable
{
    private readonly SqliteConnection _connection;
    private readonly AppDbContext _db;
    private readonly TestUserContext _userContext;
    private readonly MediaFolderRepository _repo;

    private readonly Guid _pageAId = Guid.NewGuid();
    private readonly Guid _pageBId = Guid.NewGuid();
    private readonly Guid _pageCId = Guid.NewGuid(); // thuộc actor khác — không có quyền

    public MediaFolderCreateAcrossPagesTests()
    {
        _connection = new SqliteConnection("DataSource=:memory:");
        _connection.Open();

        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlite(_connection)
            .Options;

        _db = new AppDbContext(options);
        _db.Database.EnsureCreated();

        _userContext = new TestUserContext { Roles = ["ContentManager"], UserName = "owner" };
        _repo = new MediaFolderRepository(_db, _userContext);

        _db.SocialChannels.AddRange(
            new SocialChannelModel { Id = _pageAId, Platform = SocialPlatform.Facebook, ChannelType = SocialChannelType.Page, PageName = "Page A", ExternalPageId = "fb-a", AccessToken = "t", IsActive = true, CreatedBy = "owner" },
            new SocialChannelModel { Id = _pageBId, Platform = SocialPlatform.Facebook, ChannelType = SocialChannelType.Page, PageName = "Page B", ExternalPageId = "fb-b", AccessToken = "t", IsActive = true, CreatedBy = "owner" },
            new SocialChannelModel { Id = _pageCId, Platform = SocialPlatform.Facebook, ChannelType = SocialChannelType.Page, PageName = "Page C", ExternalPageId = "fb-c", AccessToken = "t", IsActive = true, CreatedBy = "someone-else" });
        _db.SaveChanges();
    }

    public void Dispose()
    {
        _db.Dispose();
        _connection.Dispose();
    }

    [Fact]
    public async Task CreateAcrossPages_MultiplePages_CreatesRootFolderNamedPerPage()
    {
        var result = await _repo.CreateAcrossPagesAsync(new CreateMediaFolderAcrossPagesRequest
        {
            Items =
            [
                new CreateMediaFolderAcrossPagesItem { SocialChannelId = _pageAId, Name = "Page A" },
                new CreateMediaFolderAcrossPagesItem { SocialChannelId = _pageBId, Name = "Page B" },
            ],
        });

        Assert.Equal(2, result.TotalRequested);
        Assert.Equal(2, result.TotalSucceeded);
        Assert.Equal(0, result.TotalFailed);
        Assert.All(result.Results, r => Assert.True(r.Success));

        // Each page should have 3 folders: root + template + chung_chi
        var foldersA = await _db.MediaFolders.Where(f => f.SocialChannelId == _pageAId).ToListAsync();
        var foldersB = await _db.MediaFolders.Where(f => f.SocialChannelId == _pageBId).ToListAsync();

        Assert.Equal(3, foldersA.Count);
        Assert.Equal(3, foldersB.Count);

        // Verify Page A root folder
        var rootA = foldersA.Single(f => f.ParentFolderId == null);
        Assert.Equal("Page A", rootA.Name);

        // Verify Page A subfolders
        var subfoldersA = foldersA.Where(f => f.ParentFolderId == rootA.Id).OrderBy(f => f.SortOrder).ToList();
        Assert.Equal(2, subfoldersA.Count);
        Assert.Equal("template", subfoldersA[0].Name);
        Assert.Equal("chung_chi", subfoldersA[1].Name);

        // Verify Page B root folder
        var rootB = foldersB.Single(f => f.ParentFolderId == null);
        Assert.Equal("Page B", rootB.Name);

        // Verify Page B subfolders
        var subfoldersB = foldersB.Where(f => f.ParentFolderId == rootB.Id).OrderBy(f => f.SortOrder).ToList();
        Assert.Equal(2, subfoldersB.Count);
        Assert.Equal("template", subfoldersB[0].Name);
        Assert.Equal("chung_chi", subfoldersB[1].Name);
    }

    [Fact]
    public async Task CreateAcrossPages_InaccessiblePage_FailsThatPageOnly_DoesNotBlockOthers()
    {
        var result = await _repo.CreateAcrossPagesAsync(new CreateMediaFolderAcrossPagesRequest
        {
            Items =
            [
                new CreateMediaFolderAcrossPagesItem { SocialChannelId = _pageAId, Name = "Page A" },
                new CreateMediaFolderAcrossPagesItem { SocialChannelId = _pageCId, Name = "Page C" },
            ],
        });

        Assert.Equal(1, result.TotalSucceeded);
        Assert.Equal(1, result.TotalFailed);

        var forA = result.Results.Single(r => r.SocialChannelId == _pageAId);
        var forC = result.Results.Single(r => r.SocialChannelId == _pageCId);
        Assert.True(forA.Success);
        Assert.NotNull(forA.FolderId);
        Assert.False(forC.Success);
        Assert.Equal("Page/Kênh không tồn tại.", forC.ErrorMessage);

        Assert.True(await _db.MediaFolders.AnyAsync(f => f.SocialChannelId == _pageAId));
        Assert.False(await _db.MediaFolders.AnyAsync(f => f.SocialChannelId == _pageCId));
    }

    [Fact]
    public async Task CreateAcrossPages_OneItemHasEmptyName_FailsThatItemOnly_DoesNotBlockOthers()
    {
        var result = await _repo.CreateAcrossPagesAsync(new CreateMediaFolderAcrossPagesRequest
        {
            Items =
            [
                new CreateMediaFolderAcrossPagesItem { SocialChannelId = _pageAId, Name = "Page A" },
                new CreateMediaFolderAcrossPagesItem { SocialChannelId = _pageBId, Name = "   " },
            ],
        });

        Assert.Equal(1, result.TotalSucceeded);
        Assert.Equal(1, result.TotalFailed);
        Assert.True(await _db.MediaFolders.AnyAsync(f => f.SocialChannelId == _pageAId));
        Assert.False(await _db.MediaFolders.AnyAsync(f => f.SocialChannelId == _pageBId));
    }

    [Fact]
    public async Task CreateAcrossPages_AdminRole_CanTargetAnyExistingPage()
    {
        _userContext.Roles = ["Admin"];
        _userContext.UserName = "admin-user";

        var result = await _repo.CreateAcrossPagesAsync(new CreateMediaFolderAcrossPagesRequest
        {
            Items = [new CreateMediaFolderAcrossPagesItem { SocialChannelId = _pageCId, Name = "Page C" }],
        });

        Assert.Equal(1, result.TotalSucceeded);
        Assert.True(await _db.MediaFolders.AnyAsync(f => f.SocialChannelId == _pageCId));
    }

    [Fact]
    public async Task CreateAcrossPages_EmptyItemList_Throws()
    {
        await Assert.ThrowsAsync<ArgumentException>(() => _repo.CreateAcrossPagesAsync(new CreateMediaFolderAcrossPagesRequest
        {
            Items = [],
        }));
    }

    [Fact]
    public async Task CreateAcrossPages_ExceedsMaxPages_Throws()
    {
        var tooMany = Enumerable.Range(0, 201)
            .Select(i => new CreateMediaFolderAcrossPagesItem { SocialChannelId = Guid.NewGuid(), Name = $"Page {i}" })
            .ToList();

        await Assert.ThrowsAsync<ArgumentException>(() => _repo.CreateAcrossPagesAsync(new CreateMediaFolderAcrossPagesRequest
        {
            Items = tooMany,
        }));
    }

    [Fact]
    public async Task CreateAcrossPages_Idempotent_RerunningDoesNotCreateDuplicates()
    {
        var result1 = await _repo.CreateAcrossPagesAsync(new CreateMediaFolderAcrossPagesRequest
        {
            Items = [new CreateMediaFolderAcrossPagesItem { SocialChannelId = _pageAId, Name = "Page A" }],
        });

        Assert.Equal(1, result1.TotalSucceeded);
        var count1 = await _db.MediaFolders.CountAsync(f => f.SocialChannelId == _pageAId);
        Assert.Equal(3, count1);

        // Re-run for the same page — should succeed but not create duplicates
        var result2 = await _repo.CreateAcrossPagesAsync(new CreateMediaFolderAcrossPagesRequest
        {
            Items = [new CreateMediaFolderAcrossPagesItem { SocialChannelId = _pageAId, Name = "Page A" }],
        });

        Assert.Equal(1, result2.TotalSucceeded);
        var count2 = await _db.MediaFolders.CountAsync(f => f.SocialChannelId == _pageAId);
        Assert.Equal(3, count2);
        Assert.Equal(count1, count2); // No duplicates created
    }

    [Fact]
    public async Task CreateAcrossPages_PerPageAtomicity_FailureLeaksNothing()
    {
        // This test verifies that when a folder creation fails within BulkCreateAsync,
        // all three folders for that page roll back (BulkCreateAsync runs in a real EF transaction).

        var result = await _repo.CreateAcrossPagesAsync(new CreateMediaFolderAcrossPagesRequest
        {
            Items =
            [
                new CreateMediaFolderAcrossPagesItem { SocialChannelId = _pageAId, Name = "Page A" },
                new CreateMediaFolderAcrossPagesItem { SocialChannelId = _pageCId, Name = "Page C (no access)" }, // Will fail
            ],
        });

        Assert.Equal(1, result.TotalSucceeded);
        Assert.Equal(1, result.TotalFailed);

        // Page A should have all 3 folders
        var countA = await _db.MediaFolders.CountAsync(f => f.SocialChannelId == _pageAId);
        Assert.Equal(3, countA);

        // Page C should have 0 folders (rollback succeeded)
        var countC = await _db.MediaFolders.CountAsync(f => f.SocialChannelId == _pageCId);
        Assert.Equal(0, countC);
    }
}
