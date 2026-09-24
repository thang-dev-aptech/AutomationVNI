using System.Security.Claims;
using Backend.Data;
using Backend.Modules.MediaAsset;
using Backend.Modules.MediaFolder;
using Backend.Modules.SocialChannel;
using Backend.Modules.SocialChannel.Enums;
using Backend.Shared;
using Backend.Shared.Repositories;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Backend.Tests.Modules.MediaFolder;

public class MediaFolderBreadcrumbSearchTests : IDisposable
{
    private readonly SqliteConnection _connection;
    private readonly AppDbContext _db;
    private readonly TestUserContext _userContext;
    private readonly MediaFolderRepository _repo;
    private readonly MediaFolderController _controller;

    private readonly Guid _pageAId = Guid.NewGuid();
    private readonly Guid _pageBId = Guid.NewGuid();

    public MediaFolderBreadcrumbSearchTests()
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
        _controller = new MediaFolderController(_repo);

        SeedChannels();
        AttachAuthorizedUser();
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

    private void AttachAuthorizedUser()
    {
        var user = new ClaimsPrincipal(new ClaimsIdentity(new[]
        {
            new Claim(ClaimTypes.NameIdentifier, Guid.NewGuid().ToString()),
            new Claim(ClaimTypes.Name, "tester"),
            new Claim(ClaimTypes.Role, "Viewer")
        }, "TestAuth"));

        _controller.ControllerContext = new ControllerContext
        {
            HttpContext = new DefaultHttpContext { User = user }
        };
    }

    public void Dispose()
    {
        _db.Dispose();
        _connection.Dispose();
    }

    [Fact]
    public async Task BreadcrumbAndSearch_PageAccess_HidesUnauthorizedMetadata()
    {
        _userContext.Roles = ["Viewer"];
        _userContext.UserName = "page-a-owner";
        var pageA = await _db.SocialChannels.FindAsync(_pageAId);
        var pageB = await _db.SocialChannels.FindAsync(_pageBId);
        pageA!.CreatedBy = _userContext.UserName;
        pageB!.CreatedBy = "other-owner";
        var secret = new MediaFolderModel
        {
            Id = Guid.NewGuid(), Name = "Secret Page B", SocialChannelId = _pageBId
        };
        _db.MediaFolders.Add(secret);
        await _db.SaveChangesAsync();

        await _repo.SearchFoldersAsync(new SearchMediaFoldersRequest
        {
            SocialChannelId = _pageAId, Keyword = "anything"
        });

        var breadcrumbDenied = await Assert.ThrowsAsync<KeyNotFoundException>(() =>
            _repo.GetBreadcrumbAsync(new GetMediaFolderBreadcrumbRequest
            {
                SocialChannelId = _pageBId, FolderId = secret.Id
            }));
        var searchDenied = await Assert.ThrowsAsync<KeyNotFoundException>(() =>
            _repo.SearchFoldersAsync(new SearchMediaFoldersRequest
            {
                SocialChannelId = _pageBId, Keyword = "Secret"
            }));

        Assert.Equal("Page/Kênh không tồn tại.", breadcrumbDenied.Message);
        Assert.Equal(breadcrumbDenied.Message, searchDenied.Message);
        Assert.DoesNotContain(secret.Name, breadcrumbDenied.Message);
        Assert.DoesNotContain(secret.Name, searchDenied.Message);
    }

    /// <summary>AC 1: Breadcrumb đúng thứ tự root đến folder.</summary>
    [Fact]
    public async Task GetBreadcrumb_OrderIsRootToTarget_Inclusive()
    {
        var root = new MediaFolderModel { Id = Guid.NewGuid(), Name = "Root", SocialChannelId = _pageAId, ParentFolderId = null };
        var child = new MediaFolderModel { Id = Guid.NewGuid(), Name = "Child", SocialChannelId = _pageAId, ParentFolderId = root.Id };
        var leaf = new MediaFolderModel { Id = Guid.NewGuid(), Name = "Leaf", SocialChannelId = _pageAId, ParentFolderId = child.Id };
        _db.MediaFolders.AddRange(root, child, leaf);
        await _db.SaveChangesAsync();

        var result = await _repo.GetBreadcrumbAsync(new GetMediaFolderBreadcrumbRequest
        {
            SocialChannelId = _pageAId,
            FolderId = leaf.Id
        });

        Assert.Equal(3, result.Ancestors.Count);
        Assert.Equal(["Root", "Child", "Leaf"], result.Ancestors.Select(x => x.Name).ToArray());
        Assert.Equal(leaf.Id, result.Ancestors[^1].Id);
    }

    /// <summary>
    /// AC 2: Parent chain sang Page khác dùng cùng not-found với folder thiếu;
    /// message không lộ tên/ID của parent ngoài Page.
    /// </summary>
    [Fact]
    public async Task GetBreadcrumb_BrokenCrossPageParentChain_IsIndistinguishableFromMissing()
    {
        var rootB = new MediaFolderModel { Id = Guid.NewGuid(), Name = "Root B Secret", SocialChannelId = _pageBId, ParentFolderId = null };
        var orphanOnA = new MediaFolderModel
        {
            Id = Guid.NewGuid(),
            Name = "Orphan A",
            SocialChannelId = _pageAId,
            ParentFolderId = rootB.Id
        };
        _db.MediaFolders.AddRange(rootB, orphanOnA);
        await _db.SaveChangesAsync();

        var missing = await Assert.ThrowsAsync<KeyNotFoundException>(() => _repo.GetBreadcrumbAsync(new GetMediaFolderBreadcrumbRequest
        {
            SocialChannelId = _pageAId,
            FolderId = Guid.NewGuid()
        }));

        var brokenChain = await Assert.ThrowsAsync<KeyNotFoundException>(() => _repo.GetBreadcrumbAsync(new GetMediaFolderBreadcrumbRequest
        {
            SocialChannelId = _pageAId,
            FolderId = orphanOnA.Id
        }));

        Assert.Equal(missing.Message, brokenChain.Message);
        Assert.DoesNotContain(rootB.Name, brokenChain.Message, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(rootB.Id.ToString(), brokenChain.Message, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(orphanOnA.Name, brokenChain.Message, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(orphanOnA.Id.ToString(), brokenChain.Message, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>AC 3: Search chỉ trả folder trong Page được phép.</summary>
    [Fact]
    public async Task SearchFolders_OnlyReturnsFoldersOnRequestedPage()
    {
        var a = new MediaFolderModel { Id = Guid.NewGuid(), Name = "Hình ảnh chung", SocialChannelId = _pageAId };
        var b = new MediaFolderModel { Id = Guid.NewGuid(), Name = "Hình ảnh chung", SocialChannelId = _pageBId };
        _db.MediaFolders.AddRange(a, b);
        await _db.SaveChangesAsync();

        var result = await _repo.SearchFoldersAsync(new SearchMediaFoldersRequest
        {
            SocialChannelId = _pageAId,
            Keyword = "hinh"
        });

        Assert.Single(result.Items);
        Assert.Equal(a.Id, result.Items[0].Id);
        Assert.Equal(_pageAId, result.Items[0].SocialChannelId);
    }

    /// <summary>AC 4: Tìm có dấu và không dấu.</summary>
    [Fact]
    public async Task SearchFolders_MatchesWithAndWithoutVietnameseDiacritics()
    {
        var folder = new MediaFolderModel { Id = Guid.NewGuid(), Name = "Thư viện ảnh", SocialChannelId = _pageAId };
        _db.MediaFolders.Add(folder);
        await _db.SaveChangesAsync();

        var withDiacritics = await _repo.SearchFoldersAsync(new SearchMediaFoldersRequest
        {
            SocialChannelId = _pageAId,
            Keyword = "Thư viện"
        });
        var withoutDiacritics = await _repo.SearchFoldersAsync(new SearchMediaFoldersRequest
        {
            SocialChannelId = _pageAId,
            Keyword = "thu vien"
        });

        Assert.Single(withDiacritics.Items);
        Assert.Single(withoutDiacritics.Items);
        Assert.Equal(folder.Id, withDiacritics.Items[0].Id);
        Assert.Equal(folder.Id, withoutDiacritics.Items[0].Id);
    }

    /// <summary>AC 5: Folder trùng tên được phân biệt bằng full path.</summary>
    [Fact]
    public async Task SearchFolders_DistinguishesDuplicateNamesByFullPath()
    {
        var root1 = new MediaFolderModel { Id = Guid.NewGuid(), Name = "A", SocialChannelId = _pageAId, ParentFolderId = null };
        var root2 = new MediaFolderModel { Id = Guid.NewGuid(), Name = "B", SocialChannelId = _pageAId, ParentFolderId = null };
        var dup1 = new MediaFolderModel { Id = Guid.NewGuid(), Name = "Ảnh", SocialChannelId = _pageAId, ParentFolderId = root1.Id };
        var dup2 = new MediaFolderModel { Id = Guid.NewGuid(), Name = "Ảnh", SocialChannelId = _pageAId, ParentFolderId = root2.Id };
        _db.MediaFolders.AddRange(root1, root2, dup1, dup2);
        await _db.SaveChangesAsync();

        var result = await _repo.SearchFoldersAsync(new SearchMediaFoldersRequest
        {
            SocialChannelId = _pageAId,
            Keyword = "anh"
        });

        Assert.Equal(2, result.Total);
        var paths = result.Items.Select(x => x.FullPath).OrderBy(x => x).ToArray();
        Assert.Equal(["A / Ảnh", "B / Ảnh"], paths);
    }

    /// <summary>AC 6: Pagination ổn định.</summary>
    [Fact]
    public async Task SearchFolders_PaginationIsStable()
    {
        var folders = new List<MediaFolderModel>();
        for (var i = 1; i <= 30; i++)
        {
            folders.Add(new MediaFolderModel
            {
                Id = Guid.NewGuid(),
                Name = $"Match_{i:D2}",
                SocialChannelId = _pageAId,
                SortOrder = 30 - i
            });
        }
        _db.MediaFolders.AddRange(folders);
        await _db.SaveChangesAsync();

        var page1 = await _repo.SearchFoldersAsync(new SearchMediaFoldersRequest
        {
            SocialChannelId = _pageAId,
            Keyword = "Match",
            Index = 1,
            Size = 10,
            SortBy = "name",
            SortDirection = "asc"
        });
        var page2 = await _repo.SearchFoldersAsync(new SearchMediaFoldersRequest
        {
            SocialChannelId = _pageAId,
            Keyword = "Match",
            Index = 2,
            Size = 10,
            SortBy = "name",
            SortDirection = "asc"
        });

        Assert.Equal(30, page1.Total);
        Assert.Equal(10, page1.Items.Count);
        Assert.Equal("Match_01", page1.Items[0].Name);
        Assert.Equal("Match_10", page1.Items[9].Name);
        Assert.Equal("Match_11", page2.Items[0].Name);

        var page1Ids = page1.Items.Select(x => x.Id).ToHashSet();
        Assert.DoesNotContain(page2.Items, x => page1Ids.Contains(x.Id));
    }

    /// <summary>AC 7: Counts nhất quán với MEDIA-01; bỏ soft-deleted và chỉ quan hệ trực tiếp.</summary>
    [Fact]
    public async Task SearchFolders_CountsMatchChildrenApi_ExcludingSoftDeleted()
    {
        var root = new MediaFolderModel { Id = Guid.NewGuid(), Name = "Root", SocialChannelId = _pageAId, ParentFolderId = null };
        var child = new MediaFolderModel { Id = Guid.NewGuid(), Name = "Child", SocialChannelId = _pageAId, ParentFolderId = root.Id };
        var grand = new MediaFolderModel { Id = Guid.NewGuid(), Name = "Grand", SocialChannelId = _pageAId, ParentFolderId = child.Id };
        var deletedChild = new MediaFolderModel
        {
            Id = Guid.NewGuid(),
            Name = "Deleted Child",
            SocialChannelId = _pageAId,
            ParentFolderId = root.Id,
            IsDeleted = true
        };
        _db.MediaFolders.AddRange(root, child, grand, deletedChild);
        _db.MediaAssets.AddRange(
            new MediaAssetModel { Id = Guid.NewGuid(), FolderId = root.Id, FileName = "a.jpg", StoragePath = "p", PublicUrl = "u" },
            new MediaAssetModel { Id = Guid.NewGuid(), FolderId = root.Id, FileName = "b.jpg", StoragePath = "p2", PublicUrl = "u2" },
            new MediaAssetModel
            {
                Id = Guid.NewGuid(),
                FolderId = root.Id,
                FileName = "gone.jpg",
                StoragePath = "pd",
                PublicUrl = "ud",
                IsDeleted = true
            },
            new MediaAssetModel
            {
                Id = Guid.NewGuid(),
                FolderId = child.Id,
                FileName = "nested.jpg",
                StoragePath = "pn",
                PublicUrl = "un"
            }
        );
        await _db.SaveChangesAsync();

        var children = await _repo.GetChildrenAsync(new GetMediaFolderChildrenRequest
        {
            SocialChannelId = _pageAId,
            ParentFolderId = null
        });
        var search = await _repo.SearchFoldersAsync(new SearchMediaFoldersRequest
        {
            SocialChannelId = _pageAId,
            Keyword = "Root"
        });

        var fromChildren = children.Items.First(x => x.Id == root.Id);
        var fromSearch = search.Items.First(x => x.Id == root.Id);

        Assert.Equal(1, fromSearch.ChildFolderCount);
        Assert.Equal(2, fromSearch.DirectAssetCount);
        Assert.Equal(fromChildren.ChildFolderCount, fromSearch.ChildFolderCount);
        Assert.Equal(fromChildren.DirectAssetCount, fromSearch.DirectAssetCount);
        Assert.Equal(fromChildren.HasChildren, fromSearch.HasChildren);
        Assert.DoesNotContain(search.Items, x => x.Id == deletedChild.Id || x.Name.Contains("Deleted", StringComparison.Ordinal));
    }

    /// <summary>AC 8: Folder không tồn tại, sai Page hoặc soft-deleted đều cùng not-found, không lộ metadata.</summary>
    [Fact]
    public async Task GetBreadcrumb_MissingCrossPageOrSoftDeleted_ReturnsNotFoundWithoutLeak()
    {
        var folderB = new MediaFolderModel { Id = Guid.NewGuid(), Name = "Secret B", SocialChannelId = _pageBId };
        var softDeleted = new MediaFolderModel
        {
            Id = Guid.NewGuid(),
            Name = "Soft Deleted A",
            SocialChannelId = _pageAId,
            IsDeleted = true
        };
        _db.MediaFolders.AddRange(folderB, softDeleted);
        await _db.SaveChangesAsync();

        var missing = await Assert.ThrowsAsync<KeyNotFoundException>(() => _repo.GetBreadcrumbAsync(new GetMediaFolderBreadcrumbRequest
        {
            SocialChannelId = _pageAId,
            FolderId = Guid.NewGuid()
        }));

        var crossPage = await Assert.ThrowsAsync<KeyNotFoundException>(() => _repo.GetBreadcrumbAsync(new GetMediaFolderBreadcrumbRequest
        {
            SocialChannelId = _pageAId,
            FolderId = folderB.Id
        }));

        var softDeletedTarget = await Assert.ThrowsAsync<KeyNotFoundException>(() => _repo.GetBreadcrumbAsync(new GetMediaFolderBreadcrumbRequest
        {
            SocialChannelId = _pageAId,
            FolderId = softDeleted.Id
        }));

        Assert.Equal(missing.Message, crossPage.Message);
        Assert.Equal(missing.Message, softDeletedTarget.Message);
        Assert.DoesNotContain(folderB.Name, crossPage.Message, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(folderB.Id.ToString(), crossPage.Message, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(softDeleted.Name, softDeletedTarget.Message, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(softDeleted.Id.ToString(), softDeletedTarget.Message, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// AC 8b: Search không trả soft-deleted; folder Page A có parent chain sang Page B
    /// bị loại khỏi kết quả (cùng semantics breadcrumb not-found), không lộ tên/ID parent Page B.
    /// </summary>
    [Fact]
    public async Task SearchFolders_ExcludesSoftDeletedAndBrokenCrossPageParentChain()
    {
        var rootB = new MediaFolderModel { Id = Guid.NewGuid(), Name = "Hidden Parent B", SocialChannelId = _pageBId };
        var softDeleted = new MediaFolderModel
        {
            Id = Guid.NewGuid(),
            Name = "Ảnh đã xóa",
            SocialChannelId = _pageAId,
            IsDeleted = true
        };
        var brokenChain = new MediaFolderModel
        {
            Id = Guid.NewGuid(),
            Name = "Ảnh còn",
            SocialChannelId = _pageAId,
            ParentFolderId = rootB.Id
        };
        var intact = new MediaFolderModel
        {
            Id = Guid.NewGuid(),
            Name = "Ảnh sạch",
            SocialChannelId = _pageAId,
            ParentFolderId = null
        };
        _db.MediaFolders.AddRange(rootB, softDeleted, brokenChain, intact);
        await _db.SaveChangesAsync();

        var result = await _repo.SearchFoldersAsync(new SearchMediaFoldersRequest
        {
            SocialChannelId = _pageAId,
            Keyword = "anh"
        });

        Assert.Single(result.Items);
        Assert.Equal(intact.Id, result.Items[0].Id);
        Assert.Equal(intact.Name, result.Items[0].FullPath);
        Assert.DoesNotContain(result.Items, x => x.Id == brokenChain.Id || x.Id == softDeleted.Id);
        Assert.DoesNotContain(result.Items, x =>
            (x.FullPath ?? string.Empty).Contains(rootB.Name, StringComparison.OrdinalIgnoreCase));

        // Breadcrumb của broken chain vẫn cùng not-found với missing, không lộ parent Page B.
        var missing = await Assert.ThrowsAsync<KeyNotFoundException>(() =>
            _repo.GetBreadcrumbAsync(new GetMediaFolderBreadcrumbRequest
            {
                SocialChannelId = _pageAId,
                FolderId = Guid.NewGuid()
            }));
        var breadcrumbBroken = await Assert.ThrowsAsync<KeyNotFoundException>(() =>
            _repo.GetBreadcrumbAsync(new GetMediaFolderBreadcrumbRequest
            {
                SocialChannelId = _pageAId,
                FolderId = brokenChain.Id
            }));
        Assert.Equal(missing.Message, breadcrumbBroken.Message);
        Assert.DoesNotContain(rootB.Name, breadcrumbBroken.Message, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(rootB.Id.ToString(), breadcrumbBroken.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Controller_SearchAndBreadcrumb_ReturnOk()
    {
        var root = new MediaFolderModel { Id = Guid.NewGuid(), Name = "API Root", SocialChannelId = _pageAId };
        _db.MediaFolders.Add(root);
        await _db.SaveChangesAsync();

        var breadcrumb = await _controller.GetBreadcrumb(new GetMediaFolderBreadcrumbRequest
        {
            SocialChannelId = _pageAId,
            FolderId = root.Id
        }, CancellationToken.None);
        var search = await _controller.SearchFolders(new SearchMediaFoldersRequest
        {
            SocialChannelId = _pageAId,
            Keyword = "API"
        }, CancellationToken.None);

        var breadcrumbOk = Assert.IsType<OkObjectResult>(breadcrumb);
        var searchOk = Assert.IsType<OkObjectResult>(search);
        Assert.IsAssignableFrom<ApiResponse<MediaFolderBreadcrumbResponse>>(breadcrumbOk.Value);
        Assert.IsAssignableFrom<ApiResponse<PagedResult<MediaFolderSearchResultItem>>>(searchOk.Value);
    }
}
