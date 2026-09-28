using Backend.Data;
using Backend.Modules.MediaAsset;
using Backend.Modules.MediaFolder;
using Backend.Modules.SocialChannel;
using Backend.Modules.SocialChannel.Enums;
using Backend.Shared.Storage;
using Backend.Tests.Modules.MediaFolder;
using Microsoft.AspNetCore.Http;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Backend.Tests.Modules.MediaAsset;

/// <summary>
/// R-010 (MEDIA-08-AC3): regression cho MediaAssetRepository.MoveAsync — thao tác duy nhất
/// của MediaAsset can thiệp trực tiếp vào FolderId, nên phải xác nhận không hồi quy sau các
/// thay đổi MediaFolder (R-005..R-008). Trước phiên này module MediaAsset chưa có test nào.
/// </summary>
public class MediaAssetMoveTests : IDisposable
{
    private readonly SqliteConnection _connection;
    private readonly AppDbContext _db;
    private readonly TestUserContext _userContext;
    private readonly MediaAssetRepository _repo;

    public MediaAssetMoveTests()
    {
        _connection = new SqliteConnection("DataSource=:memory:");
        _connection.Open();

        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlite(_connection)
            .Options;

        _db = new AppDbContext(options);
        _db.Database.EnsureCreated();

        _userContext = new TestUserContext();
        _repo = new MediaAssetRepository(_db, _userContext, new NoopFileStorageService());
    }

    public void Dispose()
    {
        _db.Dispose();
        _connection.Dispose();
    }

    private MediaAssetModel AddAsset(Guid? folderId = null, bool isDeleted = false)
    {
        var asset = new MediaAssetModel
        {
            Id = Guid.NewGuid(),
            FolderId = folderId,
            FileName = $"a-{Guid.NewGuid():N}.jpg",
            StoragePath = "p",
            PublicUrl = "u",
            IsDeleted = isDeleted,
        };
        _db.MediaAssets.Add(asset);
        return asset;
    }

    private MediaFolderModel AddFolder(Guid? socialChannelId = null, string name = "Folder")
    {
        var folder = new MediaFolderModel
        {
            Id = Guid.NewGuid(),
            Name = name,
            SocialChannelId = socialChannelId
        };
        _db.MediaFolders.Add(folder);
        return folder;
    }

    private SocialChannelModel AddPage(string createdBy, string pageName = "Page")
    {
        var page = new SocialChannelModel
        {
            Id = Guid.NewGuid(),
            Platform = SocialPlatform.Facebook,
            ChannelType = SocialChannelType.Page,
            PageName = pageName,
            ExternalPageId = $"fb-{Guid.NewGuid():N}",
            AccessToken = "token",
            IsActive = true,
            CreatedBy = createdBy
        };
        _db.SocialChannels.Add(page);
        return page;
    }

    [Fact]
    public async Task MoveAsync_ToExistingFolder_SetsFolderIdOnAllGivenAssets()
    {
        var folder = AddFolder();
        var a1 = AddAsset();
        var a2 = AddAsset();
        await _db.SaveChangesAsync();

        var moved = await _repo.MoveAsync([a1.Id, a2.Id], folder.Id);

        Assert.Equal(2, moved);
        Assert.Equal(folder.Id, (await _db.MediaAssets.FindAsync(a1.Id))!.FolderId);
        Assert.Equal(folder.Id, (await _db.MediaAssets.FindAsync(a2.Id))!.FolderId);
    }

    [Fact]
    public async Task MoveAsync_ToNull_ReturnsAssetsToUnclassified()
    {
        var folder = AddFolder();
        var asset = AddAsset(folder.Id);
        await _db.SaveChangesAsync();

        var moved = await _repo.MoveAsync([asset.Id], null);

        Assert.Equal(1, moved);
        Assert.Null((await _db.MediaAssets.FindAsync(asset.Id))!.FolderId);
    }

    [Fact]
    public async Task MoveAsync_NonExistentTargetFolder_ThrowsAndMovesNothing()
    {
        var asset = AddAsset();
        await _db.SaveChangesAsync();

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => _repo.MoveAsync([asset.Id], Guid.NewGuid()));

        Assert.Null((await _db.MediaAssets.FindAsync(asset.Id))!.FolderId);
    }

    [Fact]
    public async Task MoveAsync_SoftDeletedTargetFolder_ThrowsAndMovesNothing()
    {
        var folder = AddFolder();
        folder.IsDeleted = true;
        var asset = AddAsset();
        await _db.SaveChangesAsync();

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => _repo.MoveAsync([asset.Id], folder.Id));

        Assert.Null((await _db.MediaAssets.FindAsync(asset.Id))!.FolderId);
    }

    [Fact]
    public async Task MoveAsync_EmptyIdList_ReturnsZero_NoOp()
    {
        var moved = await _repo.MoveAsync([], Guid.NewGuid());

        Assert.Equal(0, moved);
    }

    [Fact]
    public async Task MoveAsync_IgnoresNonExistentOrSoftDeletedAssetIds_ReturnsCountActuallyMoved()
    {
        var folder = AddFolder();
        var real = AddAsset();
        var deleted = AddAsset(isDeleted: true);
        await _db.SaveChangesAsync();

        var moved = await _repo.MoveAsync([real.Id, deleted.Id, Guid.NewGuid()], folder.Id);

        Assert.Equal(1, moved);
        Assert.Equal(folder.Id, (await _db.MediaAssets.FindAsync(real.Id))!.FolderId);
        Assert.Null((await _db.MediaAssets.FindAsync(deleted.Id))!.FolderId);
    }

    [Fact]
    public async Task MoveAsync_DeduplicatesRepeatedIds_CountsEachAssetOnce()
    {
        var folder = AddFolder();
        var asset = AddAsset();
        await _db.SaveChangesAsync();

        var moved = await _repo.MoveAsync([asset.Id, asset.Id], folder.Id);

        Assert.Equal(1, moved);
    }

    [Fact]
    public async Task MoveAsync_OwnedPageFolder_SucceedsForContentManager()
    {
        _userContext.Roles = ["ContentManager"];
        _userContext.UserName = "page-a-owner";
        var page = AddPage("page-a-owner", "Owned Page");
        var folder = AddFolder(page.Id, "Owned Folder");
        var asset = AddAsset();
        await _db.SaveChangesAsync();

        var moved = await _repo.MoveAsync([asset.Id], folder.Id);

        Assert.Equal(1, moved);
        Assert.Equal(folder.Id, (await _db.MediaAssets.FindAsync(asset.Id))!.FolderId);
    }

    [Fact]
    public async Task MoveAsync_UnwritablePageFolder_ThrowsKeyNotFound_AndMovesNothing()
    {
        _userContext.Roles = ["ContentManager"];
        _userContext.UserName = "page-a-owner";
        var otherPage = AddPage("other-owner", "Secret Page");
        var folder = AddFolder(otherPage.Id, "SecretTargetFolder");
        var asset = AddAsset();
        await _db.SaveChangesAsync();

        var ex = await Assert.ThrowsAsync<KeyNotFoundException>(
            () => _repo.MoveAsync([asset.Id], folder.Id));

        Assert.Equal("Page/Kênh không tồn tại.", ex.Message);
        Assert.DoesNotContain(folder.Name, ex.Message);
        Assert.DoesNotContain(folder.Id.ToString(), ex.Message);
        Assert.Null((await _db.MediaAssets.FindAsync(asset.Id))!.FolderId);
    }

    [Fact]
    public async Task MoveAsync_OtherUsersPage_SucceedsForAdmin()
    {
        _userContext.Roles = ["Admin"];
        _userContext.UserName = "admin-user";
        var otherPage = AddPage("other-owner", "Other Page");
        var folder = AddFolder(otherPage.Id, "Other Folder");
        var asset = AddAsset();
        await _db.SaveChangesAsync();

        var moved = await _repo.MoveAsync([asset.Id], folder.Id);

        Assert.Equal(1, moved);
        Assert.Equal(folder.Id, (await _db.MediaAssets.FindAsync(asset.Id))!.FolderId);
    }
}

/// <summary>MoveAsync không chạm file storage — chỉ cần một stub ném lỗi nếu lỡ được gọi.</summary>
file sealed class NoopFileStorageService : IFileStorageService
{
    public Task<FileSaveResult> SaveAsync(IFormFile file, string folder, CancellationToken ct = default)
        => throw new NotImplementedException();

    public Task<FileSaveResult> SaveBytesAsync(
        byte[] data, string folder, string extension, string contentType, CancellationToken ct = default)
        => throw new NotImplementedException();

    public Task<Stream> OpenReadAsync(string storageKey, CancellationToken ct = default)
        => throw new NotImplementedException();

    public Task<bool> ExistsAsync(string storageKey, CancellationToken ct = default)
        => throw new NotImplementedException();

    public Task DeleteAsync(string storageKey, CancellationToken ct = default)
        => throw new NotImplementedException();
}
