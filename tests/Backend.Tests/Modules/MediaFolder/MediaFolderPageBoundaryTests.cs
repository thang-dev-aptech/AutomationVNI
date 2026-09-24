using Backend.Data;
using Backend.Modules.MediaFolder;
using Backend.Modules.SocialChannel;
using Backend.Modules.SocialChannel.Enums;
using Backend.Shared.Repositories;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Backend.Tests.Modules.MediaFolder;

/// <summary>
/// MEDIA-EPIC-AC1: một actor chỉ có quyền Page A không thể đọc hoặc ghi Page B
/// qua bất kỳ MediaFolder API cấp repository nào.
/// </summary>
public sealed class MediaFolderPageBoundaryTests : IDisposable
{
    private const string PageAOwner = "page-a-manager";
    private readonly SqliteConnection _connection;
    private readonly AppDbContext _db;
    private readonly TestUserContext _userContext;
    private readonly MediaFolderRepository _repo;
    private readonly Guid _pageAId = Guid.NewGuid();
    private readonly Guid _pageBId = Guid.NewGuid();

    public MediaFolderPageBoundaryTests()
    {
        _connection = new SqliteConnection("DataSource=:memory:");
        _connection.Open();

        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlite(_connection)
            .Options;
        _db = new AppDbContext(options);
        _db.Database.EnsureCreated();

        _userContext = new TestUserContext
        {
            UserName = PageAOwner,
            Roles = ["ContentManager"]
        };
        _repo = new MediaFolderRepository(_db, _userContext);

        _db.SocialChannels.AddRange(
            new SocialChannelModel
            {
                Id = _pageAId,
                Platform = SocialPlatform.Facebook,
                ChannelType = SocialChannelType.Page,
                PageName = "Page A",
                ExternalPageId = "page-a",
                AccessToken = "token-a",
                IsActive = true,
                CreatedBy = PageAOwner
            },
            new SocialChannelModel
            {
                Id = _pageBId,
                Platform = SocialPlatform.Facebook,
                ChannelType = SocialChannelType.Page,
                PageName = "Page B Secret",
                ExternalPageId = "page-b",
                AccessToken = "token-b",
                IsActive = true,
                CreatedBy = "page-b-owner"
            });
        _db.SaveChanges();
    }

    [Fact]
    public async Task PageAActor_CanOnlyReadAndWritePageA_AcrossChildrenSearchBreadcrumbAndBulk()
    {
        var folderA = new MediaFolderModel
        {
            Id = Guid.NewGuid(),
            Name = "Folder A",
            SocialChannelId = _pageAId
        };
        var secretFolderB = new MediaFolderModel
        {
            Id = Guid.NewGuid(),
            Name = "Secret Folder B",
            SocialChannelId = _pageBId
        };
        _db.MediaFolders.AddRange(folderA, secretFolderB);
        await _db.SaveChangesAsync();

        var childrenA = await _repo.GetChildrenAsync(new GetMediaFolderChildrenRequest
        {
            SocialChannelId = _pageAId
        });
        var searchA = await _repo.SearchFoldersAsync(new SearchMediaFoldersRequest
        {
            SocialChannelId = _pageAId,
            Keyword = "Folder"
        });
        var breadcrumbA = await _repo.GetBreadcrumbAsync(new GetMediaFolderBreadcrumbRequest
        {
            SocialChannelId = _pageAId,
            FolderId = folderA.Id
        });
        var createA = await _repo.BulkCreateAsync(new BulkCreateMediaFolderRequest
        {
            SocialChannelId = _pageAId,
            Folders = [new BulkCreateMediaFolderItem { ClientRef = "a-new", Name = "Created on A" }]
        });

        Assert.Contains(childrenA.Items, x => x.Id == folderA.Id);
        Assert.DoesNotContain(childrenA.Items, x => x.Id == secretFolderB.Id);
        Assert.Contains(searchA.Items, x => x.Id == folderA.Id);
        Assert.DoesNotContain(searchA.Items, x => x.Id == secretFolderB.Id);
        Assert.Equal([folderA.Id], breadcrumbA.Ancestors.Select(x => x.Id).ToArray());
        Assert.Single(createA.Folders);
        Assert.Equal(_pageAId, createA.Folders[0].SocialChannelId);

        var pageBCountBefore = await _db.MediaFolders.CountAsync(x => x.SocialChannelId == _pageBId);

        var childrenB = await Assert.ThrowsAsync<KeyNotFoundException>(() =>
            _repo.GetChildrenAsync(new GetMediaFolderChildrenRequest { SocialChannelId = _pageBId }));
        var searchB = await Assert.ThrowsAsync<KeyNotFoundException>(() =>
            _repo.SearchFoldersAsync(new SearchMediaFoldersRequest { SocialChannelId = _pageBId, Keyword = "Secret" }));
        var breadcrumbB = await Assert.ThrowsAsync<KeyNotFoundException>(() =>
            _repo.GetBreadcrumbAsync(new GetMediaFolderBreadcrumbRequest
            {
                SocialChannelId = _pageBId,
                FolderId = secretFolderB.Id
            }));
        var bulkB = await Assert.ThrowsAsync<KeyNotFoundException>(() =>
            _repo.BulkCreateAsync(new BulkCreateMediaFolderRequest
            {
                SocialChannelId = _pageBId,
                Folders = [new BulkCreateMediaFolderItem { ClientRef = "b-denied", Name = "Denied Page B Folder" }]
            }));

        Assert.Equal("Page/Kênh không tồn tại.", childrenB.Message);
        Assert.Equal(childrenB.Message, searchB.Message);
        Assert.Equal(childrenB.Message, breadcrumbB.Message);
        Assert.Equal(childrenB.Message, bulkB.Message);

        var pageB = await _db.SocialChannels.FindAsync(_pageBId);
        Assert.NotNull(pageB);
        foreach (var message in new[] { childrenB.Message, searchB.Message, breadcrumbB.Message, bulkB.Message })
        {
            Assert.DoesNotContain(secretFolderB.Name, message, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain(secretFolderB.Id.ToString(), message, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("Denied Page B Folder", message, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain(pageB!.PageName!, message, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain(_pageBId.ToString(), message, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain(pageB.ExternalPageId!, message, StringComparison.OrdinalIgnoreCase);
        }

        Assert.Equal(pageBCountBefore, await _db.MediaFolders.CountAsync(x => x.SocialChannelId == _pageBId));
        Assert.False(await _db.MediaFolders.AnyAsync(x => x.Name == "Denied Page B Folder"));
        Assert.True(await _db.MediaFolders.AnyAsync(x =>
            x.Name == "Created on A" && x.SocialChannelId == _pageAId && !x.IsDeleted));
        Assert.Equal(1, await _db.MediaFolders.CountAsync(x => x.SocialChannelId == _pageBId && !x.IsDeleted));
    }

    public void Dispose()
    {
        _db.Dispose();
        _connection.Dispose();
    }
}
