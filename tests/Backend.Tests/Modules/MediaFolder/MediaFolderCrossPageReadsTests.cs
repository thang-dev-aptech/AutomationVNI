using Backend.Data;
using Backend.Modules.MediaAsset;
using Backend.Modules.MediaFolder;
using Backend.Modules.SocialChannel;
using Backend.Modules.SocialChannel.Enums;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Backend.Tests.Modules.MediaFolder;

/// <summary>
/// Cross-Page reads (page-roots, search-global, writable-pages?withoutRoot) must go through
/// QueryWritableChannels — never /tree or /filter.
/// </summary>
public sealed class MediaFolderCrossPageReadsTests : IDisposable
{
    private const string Owner = "page-a-owner";
    private const string OtherOwner = "page-b-owner";

    private readonly SqliteConnection _connection;
    private readonly AppDbContext _db;
    private readonly TestUserContext _userContext;
    private readonly MediaFolderRepository _repo;

    private readonly Guid _ownedPageId = Guid.NewGuid();
    private readonly Guid _otherPageId = Guid.NewGuid();
    private readonly Guid _ownedNoRootPageId = Guid.NewGuid();

    public MediaFolderCrossPageReadsTests()
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
            UserName = Owner,
            Roles = ["ContentManager"]
        };
        _repo = new MediaFolderRepository(_db, _userContext);

        _db.SocialChannels.AddRange(
            new SocialChannelModel
            {
                Id = _ownedPageId,
                Platform = SocialPlatform.Facebook,
                ChannelType = SocialChannelType.Page,
                PageName = "Owned Page",
                ExternalPageId = "fb-owned",
                AccessToken = "token-owned",
                IsActive = true,
                CreatedBy = Owner
            },
            new SocialChannelModel
            {
                Id = _otherPageId,
                Platform = SocialPlatform.Facebook,
                ChannelType = SocialChannelType.Page,
                PageName = "Other User Page",
                ExternalPageId = "fb-other",
                AccessToken = "token-other",
                IsActive = true,
                CreatedBy = OtherOwner
            },
            new SocialChannelModel
            {
                Id = _ownedNoRootPageId,
                Platform = SocialPlatform.Facebook,
                ChannelType = SocialChannelType.Page,
                PageName = "Owned No Root",
                ExternalPageId = "fb-owned-empty",
                AccessToken = "token-empty",
                IsActive = true,
                CreatedBy = Owner
            });
        _db.SaveChanges();
    }

    public void Dispose()
    {
        _db.Dispose();
        _connection.Dispose();
    }

    [Fact]
    public async Task PageRoots_NonAdmin_SeesOnlyOwnWritablePages_WhenSecondUsersPagePresent()
    {
        var ownedRoot = new MediaFolderModel
        {
            Id = Guid.NewGuid(),
            Name = "Campaign",
            SocialChannelId = _ownedPageId
        };
        var otherRoot = new MediaFolderModel
        {
            Id = Guid.NewGuid(),
            Name = "Campaign",
            SocialChannelId = _otherPageId
        };
        _db.MediaFolders.AddRange(ownedRoot, otherRoot);
        await _db.SaveChangesAsync();

        var result = await _repo.GetPageRootsAsync(new GetMediaFolderPageRootsRequest());

        Assert.Single(result.Items);
        Assert.Equal(ownedRoot.Id, result.Items[0].Id);
        Assert.Equal(_ownedPageId, result.Items[0].SocialChannelId);
        Assert.DoesNotContain(result.Items, x => x.SocialChannelId == _otherPageId);
        Assert.DoesNotContain(result.Items, x => x.Id == otherRoot.Id);
    }

    [Fact]
    public async Task PageRoots_ReturnsPageName_SoSameNamedFoldersAreDistinguishable()
    {
        _userContext.Roles = ["Admin"];
        var ownedRoot = new MediaFolderModel
        {
            Id = Guid.NewGuid(),
            Name = "Campaign",
            SocialChannelId = _ownedPageId
        };
        var otherRoot = new MediaFolderModel
        {
            Id = Guid.NewGuid(),
            Name = "Campaign",
            SocialChannelId = _otherPageId
        };
        _db.MediaFolders.AddRange(ownedRoot, otherRoot);
        await _db.SaveChangesAsync();

        var result = await _repo.GetPageRootsAsync(new GetMediaFolderPageRootsRequest());

        Assert.Equal(2, result.Total);
        Assert.Equal(2, result.Items.Count);
        Assert.All(result.Items, item => Assert.Equal("Campaign", item.Name));

        var byPage = result.Items.ToDictionary(x => x.SocialChannelId!.Value);
        Assert.Equal("Owned Page", byPage[_ownedPageId].PageName);
        Assert.Equal("Other User Page", byPage[_otherPageId].PageName);
        Assert.NotEqual(byPage[_ownedPageId].PageName, byPage[_otherPageId].PageName);
    }

    [Fact]
    public async Task PageRoots_OmitsWritablePagesThatHaveNoActiveRoot()
    {
        _db.MediaFolders.Add(new MediaFolderModel
        {
            Id = Guid.NewGuid(),
            Name = "Only Root",
            SocialChannelId = _ownedPageId
        });
        await _db.SaveChangesAsync();

        var result = await _repo.GetPageRootsAsync(new GetMediaFolderPageRootsRequest());

        Assert.Single(result.Items);
        Assert.Equal(_ownedPageId, result.Items[0].SocialChannelId);
        Assert.DoesNotContain(result.Items, x => x.SocialChannelId == _ownedNoRootPageId);
    }

    [Fact]
    public async Task GlobalSearch_ReturnsNoFolderFromPageCallerCannotWrite()
    {
        var owned = new MediaFolderModel
        {
            Id = Guid.NewGuid(),
            Name = "Shared Name",
            SocialChannelId = _ownedPageId
        };
        var secret = new MediaFolderModel
        {
            Id = Guid.NewGuid(),
            Name = "Shared Name",
            SocialChannelId = _otherPageId
        };
        _db.MediaFolders.AddRange(owned, secret);
        await _db.SaveChangesAsync();

        var result = await _repo.SearchFoldersGlobalAsync(new SearchMediaFoldersGlobalRequest
        {
            Keyword = "Shared"
        });

        Assert.Single(result.Items);
        Assert.Equal(owned.Id, result.Items[0].Id);
        Assert.Equal("Owned Page", result.Items[0].PageName);
        Assert.DoesNotContain(result.Items, x => x.Id == secret.Id);
        Assert.DoesNotContain(result.Items, x => x.SocialChannelId == _otherPageId);
    }

    [Fact]
    public async Task PageScopedSearch_StillReturnsOnlyRequestedPage_AfterGlobalHelperExtract()
    {
        var owned = new MediaFolderModel
        {
            Id = Guid.NewGuid(),
            Name = "Duplicate",
            SocialChannelId = _ownedPageId
        };
        var other = new MediaFolderModel
        {
            Id = Guid.NewGuid(),
            Name = "Duplicate",
            SocialChannelId = _otherPageId
        };
        _db.MediaFolders.AddRange(owned, other);
        await _db.SaveChangesAsync();

        var result = await _repo.SearchFoldersAsync(new SearchMediaFoldersRequest
        {
            SocialChannelId = _ownedPageId,
            Keyword = "Duplicate"
        });

        Assert.Single(result.Items);
        Assert.Equal(owned.Id, result.Items[0].Id);
        Assert.Null(result.Items[0].PageName);
        Assert.Equal("Duplicate", result.Items[0].FullPath);
    }

    [Fact]
    public async Task GlobalSearch_BrokenCrossPageParent_DoesNotResolveViaOtherPageIdMap()
    {
        _userContext.Roles = ["Admin"];
        var otherRoot = new MediaFolderModel
        {
            Id = Guid.NewGuid(),
            Name = "Other Root",
            SocialChannelId = _otherPageId
        };
        var orphan = new MediaFolderModel
        {
            Id = Guid.NewGuid(),
            Name = "Orphan Match",
            SocialChannelId = _ownedPageId,
            ParentFolderId = otherRoot.Id
        };
        var intact = new MediaFolderModel
        {
            Id = Guid.NewGuid(),
            Name = "Orphan Intact",
            SocialChannelId = _ownedPageId
        };
        _db.MediaFolders.AddRange(otherRoot, orphan, intact);
        await _db.SaveChangesAsync();

        var result = await _repo.SearchFoldersGlobalAsync(new SearchMediaFoldersGlobalRequest
        {
            Keyword = "Orphan"
        });

        Assert.Single(result.Items);
        Assert.Equal(intact.Id, result.Items[0].Id);
        Assert.DoesNotContain(result.Items, x => x.Id == orphan.Id);
        Assert.DoesNotContain(result.Items, x => x.FullPath.Contains("Other Root"));
    }

    [Fact]
    public async Task WritablePages_WithoutRoot_ReturnsOnlyPagesWithNoActiveRoot()
    {
        var activeRoot = new MediaFolderModel
        {
            Id = Guid.NewGuid(),
            Name = "Root",
            SocialChannelId = _ownedPageId
        };
        var deletedRoot = new MediaFolderModel
        {
            Id = Guid.NewGuid(),
            Name = "Deleted Root",
            SocialChannelId = _ownedNoRootPageId,
            IsDeleted = true
        };
        _db.MediaFolders.AddRange(activeRoot, deletedRoot);
        await _db.SaveChangesAsync();

        var all = await _repo.GetWritablePagesAsync();
        var withoutRoot = await _repo.GetWritablePagesAsync(withoutRoot: true);

        Assert.Equal(2, all.Count);
        Assert.Contains(all, p => p.Id == _ownedPageId);
        Assert.Contains(all, p => p.Id == _ownedNoRootPageId);
        Assert.DoesNotContain(all, p => p.Id == _otherPageId);

        Assert.Single(withoutRoot);
        Assert.Equal(_ownedNoRootPageId, withoutRoot[0].Id);
        Assert.DoesNotContain(withoutRoot, p => p.Id == _ownedPageId);
        Assert.DoesNotContain(withoutRoot, p => p.Id == _otherPageId);
    }

    [Fact]
    public async Task ChungChiEligiblePages_RequiresOwnedPageDirectActiveImageInNamedChild()
    {
        var ownedRoot = new MediaFolderModel
        {
            Id = Guid.NewGuid(), Name = "Owned Root", SocialChannelId = _ownedPageId
        };
        var ownedChungChi = new MediaFolderModel
        {
            Id = Guid.NewGuid(), Name = "chung_chi", SocialChannelId = _ownedPageId,
            ParentFolderId = ownedRoot.Id
        };
        var nonImageRoot = new MediaFolderModel
        {
            Id = Guid.NewGuid(), Name = "Non-image Root", SocialChannelId = _ownedNoRootPageId
        };
        var nonImageChungChi = new MediaFolderModel
        {
            Id = Guid.NewGuid(), Name = "chung_chi", SocialChannelId = _ownedNoRootPageId,
            ParentFolderId = nonImageRoot.Id
        };
        var otherRoot = new MediaFolderModel
        {
            Id = Guid.NewGuid(), Name = "Other Root", SocialChannelId = _otherPageId
        };
        var otherChungChi = new MediaFolderModel
        {
            Id = Guid.NewGuid(), Name = "chung_chi", SocialChannelId = _otherPageId,
            ParentFolderId = otherRoot.Id
        };
        _db.MediaFolders.AddRange(
            ownedRoot, ownedChungChi, nonImageRoot, nonImageChungChi, otherRoot, otherChungChi);
        _db.MediaAssets.AddRange(
            new MediaAssetModel
            {
                Id = Guid.NewGuid(), FolderId = ownedChungChi.Id, FileName = "owned.jpg",
                StoragePath = "owned.jpg", MimeType = "image/jpeg"
            },
            new MediaAssetModel
            {
                Id = Guid.NewGuid(), FolderId = nonImageChungChi.Id, FileName = "notes.pdf",
                StoragePath = "notes.pdf", MimeType = "application/pdf"
            },
            new MediaAssetModel
            {
                Id = Guid.NewGuid(), FolderId = nonImageChungChi.Id, FileName = "deleted.png",
                StoragePath = "deleted.png", MimeType = "image/png", IsDeleted = true
            },
            new MediaAssetModel
            {
                Id = Guid.NewGuid(), FolderId = otherChungChi.Id, FileName = "other.png",
                StoragePath = "other.png", MimeType = "image/png"
            });
        await _db.SaveChangesAsync();

        var result = await _repo.GetChungChiEligiblePagesAsync();

        Assert.Single(result);
        Assert.Equal(_ownedPageId, result[0].Id);
    }
}
