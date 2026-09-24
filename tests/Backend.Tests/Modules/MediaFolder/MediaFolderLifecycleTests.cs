using Backend.Data;
using Backend.Modules.MediaAsset;
using Backend.Modules.MediaFolder;
using Backend.Modules.SocialChannel;
using Backend.Modules.SocialChannel.Enums;
using Backend.Shared.Repositories;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Backend.Tests.Modules.MediaFolder;

/// <summary>
/// R-005 (MEDIA-EPIC): create/update/reparent/soft-delete lifecycle của MediaFolder.
/// Khoá lại SocialChannel inheritance, chống cycle, cross-Page reparent, chặn xóa còn con
/// và đưa media về "chưa phân loại" (FolderId = null) khi xóa folder.
/// </summary>
public class MediaFolderLifecycleTests : IDisposable
{
    private readonly SqliteConnection _connection;
    private readonly AppDbContext _db;
    private readonly TestUserContext _userContext;
    private readonly MediaFolderRepository _repo;

    private readonly Guid _pageAId = Guid.NewGuid();
    private readonly Guid _pageBId = Guid.NewGuid();

    public MediaFolderLifecycleTests()
    {
        _connection = new SqliteConnection("DataSource=:memory:");
        _connection.Open();

        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlite(_connection)
            .Options;

        _db = new AppDbContext(options);
        _db.Database.EnsureCreated();

        _userContext = new TestUserContext();
        _repo = new MediaFolderRepository(_db, _userContext);

        SeedChannels();
    }

    private void SeedChannels()
    {
        _db.SocialChannels.AddRange(
            new SocialChannelModel
            {
                Id = _pageAId,
                Platform = SocialPlatform.Facebook,
                ChannelType = SocialChannelType.Page,
                PageName = "Page A",
                ExternalPageId = "fb-page-a",
                AccessToken = "token-a",
                IsActive = true
            },
            new SocialChannelModel
            {
                Id = _pageBId,
                Platform = SocialPlatform.Facebook,
                ChannelType = SocialChannelType.Page,
                PageName = "Page B",
                ExternalPageId = "fb-page-b",
                AccessToken = "token-b",
                IsActive = true
            }
        );
        _db.SaveChanges();
    }

    public void Dispose()
    {
        _db.Dispose();
        _connection.Dispose();
    }

    // ---------- CreateAsync ----------

    [Fact]
    public async Task CreateAsync_Root_UsesGivenSocialChannelId()
    {
        var entity = await _repo.CreateAsync(new CreateMediaFolderRequest
        {
            Name = "Root",
            SocialChannelId = _pageAId
        });

        Assert.Null(entity.ParentFolderId);
        Assert.Equal(_pageAId, entity.SocialChannelId);
    }

    [Fact]
    public async Task CreateAsync_Child_NoSocialChannelGiven_InheritsFromParent()
    {
        var parent = await _repo.CreateAsync(new CreateMediaFolderRequest { Name = "Parent", SocialChannelId = _pageAId });

        var child = await _repo.CreateAsync(new CreateMediaFolderRequest
        {
            Name = "Child",
            ParentFolderId = parent.Id
        });

        Assert.Equal(_pageAId, child.SocialChannelId);
    }

    [Fact]
    public async Task CreateAsync_Child_SocialChannelMismatchWithParent_IsRejected()
    {
        var parent = await _repo.CreateAsync(new CreateMediaFolderRequest { Name = "Parent", SocialChannelId = _pageAId });

        await Assert.ThrowsAsync<InvalidOperationException>(() => _repo.CreateAsync(new CreateMediaFolderRequest
        {
            Name = "Child",
            ParentFolderId = parent.Id,
            SocialChannelId = _pageBId
        }));
    }

    [Fact]
    public async Task CreateAsync_MissingParent_IsRejected()
    {
        await Assert.ThrowsAsync<InvalidOperationException>(() => _repo.CreateAsync(new CreateMediaFolderRequest
        {
            Name = "Orphan",
            ParentFolderId = Guid.NewGuid(),
            SocialChannelId = _pageAId
        }));
    }

    [Fact]
    public async Task CreateAsync_EmptyName_IsRejected()
    {
        await Assert.ThrowsAsync<InvalidOperationException>(() => _repo.CreateAsync(new CreateMediaFolderRequest
        {
            Name = "   ",
            SocialChannelId = _pageAId
        }));
    }

    // ---------- UpdateAsync ----------

    [Fact]
    public async Task UpdateAsync_NotFound_ReturnsNull()
    {
        var result = await _repo.UpdateAsync(Guid.NewGuid(), new UpdateMediaFolderRequest { Name = "X" });

        Assert.Null(result);
    }

    [Fact]
    public async Task UpdateAsync_NameDescriptionSortOrder_UpdateFields_WithoutTouchingParent()
    {
        var parent = await _repo.CreateAsync(new CreateMediaFolderRequest { Name = "Parent", SocialChannelId = _pageAId });
        var folder = await _repo.CreateAsync(new CreateMediaFolderRequest { Name = "Old", ParentFolderId = parent.Id });

        var updated = await _repo.UpdateAsync(folder.Id, new UpdateMediaFolderRequest
        {
            Name = "New",
            Description = "desc",
            SortOrder = 5,
            ParentFolderId = parent.Id
        });

        Assert.NotNull(updated);
        Assert.Equal("New", updated!.Name);
        Assert.Equal("desc", updated.Description);
        Assert.Equal(5, updated.SortOrder);
        Assert.Equal(parent.Id, updated.ParentFolderId);
    }

    [Fact]
    public async Task UpdateAsync_Reparent_ToNonExistentParent_IsRejected()
    {
        var folder = await _repo.CreateAsync(new CreateMediaFolderRequest { Name = "Folder", SocialChannelId = _pageAId });

        await Assert.ThrowsAsync<InvalidOperationException>(() => _repo.UpdateAsync(folder.Id, new UpdateMediaFolderRequest
        {
            ParentFolderId = Guid.NewGuid()
        }));
    }

    [Fact]
    public async Task UpdateAsync_Reparent_ToDifferentPageParent_IsRejected()
    {
        var folderA = await _repo.CreateAsync(new CreateMediaFolderRequest { Name = "Folder A", SocialChannelId = _pageAId });
        var rootB = await _repo.CreateAsync(new CreateMediaFolderRequest { Name = "Root B", SocialChannelId = _pageBId });

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => _repo.UpdateAsync(folderA.Id, new UpdateMediaFolderRequest
        {
            ParentFolderId = rootB.Id
        }));
        Assert.Equal("Thư mục cha không thuộc Page yêu cầu.", ex.Message);
    }

    [Fact]
    public async Task UpdateAsync_Reparent_SelfAsParent_IsRejectedAsCycle()
    {
        var folder = await _repo.CreateAsync(new CreateMediaFolderRequest { Name = "Folder", SocialChannelId = _pageAId });

        await Assert.ThrowsAsync<InvalidOperationException>(() => _repo.UpdateAsync(folder.Id, new UpdateMediaFolderRequest
        {
            ParentFolderId = folder.Id
        }));
    }

    [Fact]
    public async Task UpdateAsync_Reparent_DescendantAsParent_IsRejectedAsCycle()
    {
        var root = await _repo.CreateAsync(new CreateMediaFolderRequest { Name = "Root", SocialChannelId = _pageAId });
        var child = await _repo.CreateAsync(new CreateMediaFolderRequest { Name = "Child", ParentFolderId = root.Id });
        var grandchild = await _repo.CreateAsync(new CreateMediaFolderRequest { Name = "Grandchild", ParentFolderId = child.Id });

        // Đặt root làm con của chính grandchild của nó -> phải bị chặn.
        await Assert.ThrowsAsync<InvalidOperationException>(() => _repo.UpdateAsync(root.Id, new UpdateMediaFolderRequest
        {
            ParentFolderId = grandchild.Id
        }));
    }

    [Fact]
    public async Task UpdateAsync_Reparent_ToValidSiblingBranch_Succeeds()
    {
        var root = await _repo.CreateAsync(new CreateMediaFolderRequest { Name = "Root", SocialChannelId = _pageAId });
        var siblingA = await _repo.CreateAsync(new CreateMediaFolderRequest { Name = "Sibling A", ParentFolderId = root.Id });
        var siblingB = await _repo.CreateAsync(new CreateMediaFolderRequest { Name = "Sibling B", ParentFolderId = root.Id });
        var moving = await _repo.CreateAsync(new CreateMediaFolderRequest { Name = "Moving", ParentFolderId = siblingA.Id });

        var updated = await _repo.UpdateAsync(moving.Id, new UpdateMediaFolderRequest { ParentFolderId = siblingB.Id });

        Assert.NotNull(updated);
        Assert.Equal(siblingB.Id, updated!.ParentFolderId);
    }

    [Fact]
    public async Task UpdateAsync_Reparent_ToRoot_SettingParentNull_Succeeds()
    {
        var root = await _repo.CreateAsync(new CreateMediaFolderRequest { Name = "Root", SocialChannelId = _pageAId });
        var child = await _repo.CreateAsync(new CreateMediaFolderRequest { Name = "Child", ParentFolderId = root.Id });

        // Move child to page B (which has no root) and set it as root
        var updated = await _repo.UpdateAsync(child.Id, new UpdateMediaFolderRequest { ParentFolderId = null, SocialChannelId = _pageBId });

        Assert.NotNull(updated);
        Assert.Null(updated!.ParentFolderId);
        Assert.Equal(_pageBId, updated.SocialChannelId);
    }

    // ---------- Single-root-per-page guard tests ----------

    [Fact]
    public async Task CreateAsync_SecondRoot_IsRejected()
    {
        var root1 = await _repo.CreateAsync(new CreateMediaFolderRequest { Name = "Root 1", SocialChannelId = _pageAId });

        await Assert.ThrowsAsync<InvalidOperationException>(() => _repo.CreateAsync(new CreateMediaFolderRequest { Name = "Root 2", SocialChannelId = _pageAId }));
    }

    [Fact]
    public async Task UpdateAsync_Reparent_ToRoot_WhenRootExists_IsRejected()
    {
        var root = await _repo.CreateAsync(new CreateMediaFolderRequest { Name = "Root", SocialChannelId = _pageAId });
        var child = await _repo.CreateAsync(new CreateMediaFolderRequest { Name = "Child", ParentFolderId = root.Id });

        await Assert.ThrowsAsync<InvalidOperationException>(() => _repo.UpdateAsync(child.Id, new UpdateMediaFolderRequest { ParentFolderId = null }));
    }

    // ---------- SoftDeleteAsync ----------

    [Fact]
    public async Task SoftDeleteAsync_FolderWithChildren_IsBlocked_AndFolderRemainsActive()
    {
        var root = await _repo.CreateAsync(new CreateMediaFolderRequest { Name = "Root", SocialChannelId = _pageAId });
        await _repo.CreateAsync(new CreateMediaFolderRequest { Name = "Child", ParentFolderId = root.Id });

        await Assert.ThrowsAsync<InvalidOperationException>(() => _repo.SoftDeleteAsync(root.Id));

        var stillActive = await _repo.GetByIdAsync(root.Id);
        Assert.NotNull(stillActive);
        Assert.False(stillActive!.IsDeleted);
    }

    [Fact]
    public async Task SoftDeleteAsync_FolderWithDirectAssets_SetsAssetsFolderIdToNull_AndKeepsAssetsUndeleted()
    {
        var folder = await _repo.CreateAsync(new CreateMediaFolderRequest { Name = "Folder", SocialChannelId = _pageAId });
        var asset = new MediaAssetModel
        {
            Id = Guid.NewGuid(),
            FolderId = folder.Id,
            FileName = "a.jpg",
            StoragePath = "p",
            PublicUrl = "u"
        };
        _db.MediaAssets.Add(asset);
        await _db.SaveChangesAsync();

        var deleted = await _repo.SoftDeleteAsync(folder.Id);

        Assert.True(deleted);
        var reloadedAsset = await _db.MediaAssets.FindAsync(asset.Id);
        Assert.NotNull(reloadedAsset);
        Assert.Null(reloadedAsset!.FolderId);
        Assert.False(reloadedAsset.IsDeleted);
    }

    [Fact]
    public async Task SoftDeleteAsync_FolderWithoutChildrenOrAssets_SoftDeletesSuccessfully()
    {
        var folder = await _repo.CreateAsync(new CreateMediaFolderRequest { Name = "Folder", SocialChannelId = _pageAId });

        var deleted = await _repo.SoftDeleteAsync(folder.Id);

        Assert.True(deleted);
        Assert.Null(await _repo.GetByIdAsync(folder.Id));
    }

    [Fact]
    public async Task SoftDeleteAsync_NotFound_ReturnsFalse()
    {
        var deleted = await _repo.SoftDeleteAsync(Guid.NewGuid());

        Assert.False(deleted);
    }

    // ---------- Known gaps locked as documentation for follow-up (see pcs bug entries) ----------

    /// <summary>
    /// UpdateAsync chỉ validate SocialChannel khớp parent khi ParentFolderId đổi.
    /// Đổi SocialChannelId riêng lẻ (không kèm đổi parent) không so khớp lại với parent hiện tại,
    /// có thể làm folder lệch Page so với cha. Khoá lại hành vi hiện tại; xem bug entry đã ghi trong pcs.
    /// </summary>
    [Fact]
    public async Task UpdateAsync_ChangingSocialChannelId_WithoutReparent_DoesNotRevalidateAgainstCurrentParent()
    {
        var parent = await _repo.CreateAsync(new CreateMediaFolderRequest { Name = "Parent", SocialChannelId = _pageAId });
        var child = await _repo.CreateAsync(new CreateMediaFolderRequest { Name = "Child", ParentFolderId = parent.Id });

        var updated = await _repo.UpdateAsync(child.Id, new UpdateMediaFolderRequest
        {
            SocialChannelId = _pageBId,
            ParentFolderId = parent.Id
        });

        Assert.NotNull(updated);
        Assert.Equal(_pageBId, updated!.SocialChannelId);
        Assert.Equal(parent.Id, updated.ParentFolderId);
        Assert.NotEqual(updated.SocialChannelId, parent.SocialChannelId);
    }
}
