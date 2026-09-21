using Backend.Data;
using Backend.Modules.MediaAsset;
using Backend.Modules.MediaFolder;
using Backend.Modules.SocialChannel;
using Backend.Modules.SocialChannel.Enums;
using Backend.Tests.Modules.MediaFolder;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Backend.Tests.Modules.MediaAsset;

/// <summary>
/// FilterAsync giờ join FolderId -> MediaFolder.SocialChannelId theo lô (không round-trip
/// từng ảnh) để frontend điều hướng "mở đúng Page" từ kết quả tìm kiếm chung (search box gộp
/// thư mục + tệp). Regression cho phần join đó — không lặp lại các case filter khác vốn không
/// thay đổi (giữ nguyên trong FilterAsync).
/// </summary>
public class MediaAssetFilterSocialChannelTests : IDisposable
{
    private readonly SqliteConnection _connection;
    private readonly AppDbContext _db;
    private readonly MediaAssetRepository _repo;

    public MediaAssetFilterSocialChannelTests()
    {
        _connection = new SqliteConnection("DataSource=:memory:");
        _connection.Open();

        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlite(_connection)
            .Options;

        _db = new AppDbContext(options);
        _db.Database.EnsureCreated();
        _repo = new MediaAssetRepository(_db, new TestUserContext());
    }

    public void Dispose()
    {
        _db.Dispose();
        _connection.Dispose();
    }

    private MediaAssetModel AddAsset(Guid? folderId, string fileName)
    {
        var asset = new MediaAssetModel
        {
            Id = Guid.NewGuid(), FolderId = folderId, FileName = fileName,
            StoragePath = "p", PublicUrl = "u",
        };
        _db.MediaAssets.Add(asset);
        return asset;
    }

    private MediaFolderModel AddFolder(Guid socialChannelId, string name = "Folder")
    {
        var folder = new MediaFolderModel { Id = Guid.NewGuid(), Name = name, SocialChannelId = socialChannelId };
        _db.MediaFolders.Add(folder);
        return folder;
    }

    private Guid AddPage(string pageName = "Page")
    {
        var page = new SocialChannelModel
        {
            Id = Guid.NewGuid(), Platform = SocialPlatform.Facebook, ChannelType = SocialChannelType.Page,
            PageName = pageName, ExternalPageId = $"fb-{Guid.NewGuid():N}", AccessToken = "token", IsActive = true,
        };
        _db.SocialChannels.Add(page);
        return page.Id;
    }

    [Fact]
    public async Task FilterAsync_AssetInFolder_ReturnsFolderSocialChannelId()
    {
        var pageId = AddPage();
        var folder = AddFolder(pageId);
        AddAsset(folder.Id, "banner.jpg");
        await _db.SaveChangesAsync();

        var result = await _repo.FilterAsync(new MediaAssetFilterRequest { Keyword = "banner", Index = 1, Size = 20 });

        var item = Assert.Single(result.Items);
        Assert.Equal(pageId, item.SocialChannelId);
    }

    [Fact]
    public async Task FilterAsync_UnassignedAsset_SocialChannelIdIsNull()
    {
        AddAsset(null, "loose.png");
        await _db.SaveChangesAsync();

        var result = await _repo.FilterAsync(new MediaAssetFilterRequest { Keyword = "loose", Index = 1, Size = 20 });

        var item = Assert.Single(result.Items);
        Assert.Null(item.SocialChannelId);
    }

    [Fact]
    public async Task FilterAsync_AssetsAcrossDifferentPages_EachGetsItsOwnFoldersChannelId()
    {
        var pageA = AddPage("Page A");
        var pageB = AddPage("Page B");
        var folderA = AddFolder(pageA, "Folder A");
        var folderB = AddFolder(pageB, "Folder B");
        AddAsset(folderA.Id, "shared-name.jpg");
        AddAsset(folderB.Id, "shared-name.jpg");
        await _db.SaveChangesAsync();

        var result = await _repo.FilterAsync(new MediaAssetFilterRequest { Keyword = "shared-name", Index = 1, Size = 20 });

        Assert.Equal(2, result.Items.Count);
        Assert.Contains(result.Items, i => i.SocialChannelId == pageA);
        Assert.Contains(result.Items, i => i.SocialChannelId == pageB);
    }
}
