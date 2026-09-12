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

public class MediaFolderChildrenTests : IDisposable
{
    private readonly SqliteConnection _connection;
    private readonly AppDbContext _db;
    private readonly TestUserContext _userContext;
    private readonly MediaFolderRepository _repo;
    private readonly MediaFolderController _controller;

    private readonly Guid _pageAId = Guid.NewGuid();
    private readonly Guid _pageBId = Guid.NewGuid();

    public MediaFolderChildrenTests()
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

    /// <summary>
    /// AC 1: Root chỉ thuộc Page yêu cầu. Không rò rỉ root của Page khác và không lẫn folder con.
    /// </summary>
    [Fact]
    public async Task GetChildren_Root_OnlyBelongsToRequestedPage()
    {
        // Arrange
        var rootA1 = new MediaFolderModel { Id = Guid.NewGuid(), Name = "Root A1", SocialChannelId = _pageAId, ParentFolderId = null, SortOrder = 1 };
        var rootA2 = new MediaFolderModel { Id = Guid.NewGuid(), Name = "Root A2", SocialChannelId = _pageAId, ParentFolderId = null, SortOrder = 2 };
        var childA1 = new MediaFolderModel { Id = Guid.NewGuid(), Name = "Child A1_1", SocialChannelId = _pageAId, ParentFolderId = rootA1.Id, SortOrder = 1 };

        var rootB1 = new MediaFolderModel { Id = Guid.NewGuid(), Name = "Root B1", SocialChannelId = _pageBId, ParentFolderId = null, SortOrder = 1 };

        _db.MediaFolders.AddRange(rootA1, rootA2, childA1, rootB1);
        await _db.SaveChangesAsync();

        // Act
        var request = new GetMediaFolderChildrenRequest
        {
            SocialChannelId = _pageAId,
            ParentFolderId = null,
            Index = 1,
            Size = 20
        };
        var result = await _repo.GetChildrenAsync(request);

        // Assert
        Assert.Equal(2, result.Total);
        Assert.Equal(2, result.Items.Count);
        Assert.Contains(result.Items, x => x.Id == rootA1.Id && x.SocialChannelId == _pageAId && x.ParentFolderId == null);
        Assert.Contains(result.Items, x => x.Id == rootA2.Id && x.SocialChannelId == _pageAId && x.ParentFolderId == null);
        Assert.DoesNotContain(result.Items, x => x.Id == childA1.Id);
        Assert.DoesNotContain(result.Items, x => x.Id == rootB1.Id);
    }

    /// <summary>
    /// AC 2: Children chỉ gồm một cấp trực tiếp. Không tải cháu (grandchildren) hay folder nhánh khác.
    /// </summary>
    [Fact]
    public async Task GetChildren_Children_OnlyDirectOneLevel()
    {
        // Arrange
        var root = new MediaFolderModel { Id = Guid.NewGuid(), Name = "Root", SocialChannelId = _pageAId, ParentFolderId = null };
        var child1 = new MediaFolderModel { Id = Guid.NewGuid(), Name = "Child 1", SocialChannelId = _pageAId, ParentFolderId = root.Id };
        var child2 = new MediaFolderModel { Id = Guid.NewGuid(), Name = "Child 2", SocialChannelId = _pageAId, ParentFolderId = root.Id };
        var grandChild = new MediaFolderModel { Id = Guid.NewGuid(), Name = "Grandchild 1", SocialChannelId = _pageAId, ParentFolderId = child1.Id };
        var greatGrandChild = new MediaFolderModel { Id = Guid.NewGuid(), Name = "Great Grandchild 1", SocialChannelId = _pageAId, ParentFolderId = grandChild.Id };

        var otherRoot = new MediaFolderModel { Id = Guid.NewGuid(), Name = "Other Root", SocialChannelId = _pageAId, ParentFolderId = null };
        var otherChild = new MediaFolderModel { Id = Guid.NewGuid(), Name = "Other Child", SocialChannelId = _pageAId, ParentFolderId = otherRoot.Id };

        _db.MediaFolders.AddRange(root, child1, child2, grandChild, greatGrandChild, otherRoot, otherChild);
        await _db.SaveChangesAsync();

        // Act
        var request = new GetMediaFolderChildrenRequest
        {
            SocialChannelId = _pageAId,
            ParentFolderId = root.Id,
            Index = 1,
            Size = 20
        };
        var result = await _repo.GetChildrenAsync(request);

        // Assert
        Assert.Equal(2, result.Total);
        Assert.Equal(2, result.Items.Count);
        Assert.All(result.Items, item => Assert.Equal(root.Id, item.ParentFolderId));
        Assert.Contains(result.Items, x => x.Id == child1.Id);
        Assert.Contains(result.Items, x => x.Id == child2.Id);
        Assert.DoesNotContain(result.Items, x => x.Id == grandChild.Id);
        Assert.DoesNotContain(result.Items, x => x.Id == greatGrandChild.Id);
        Assert.DoesNotContain(result.Items, x => x.Id == otherChild.Id);
    }

    /// <summary>
    /// AC 3: ChildFolderCount đúng và chỉ đếm thư mục con trực tiếp của folder đó.
    /// </summary>
    [Fact]
    public async Task GetChildren_ChildFolderCount_IsAccurate()
    {
        // Arrange
        var rootA1 = new MediaFolderModel { Id = Guid.NewGuid(), Name = "Root A1", SocialChannelId = _pageAId, ParentFolderId = null };
        var rootA2 = new MediaFolderModel { Id = Guid.NewGuid(), Name = "Root A2", SocialChannelId = _pageAId, ParentFolderId = null };

        // rootA1 có 3 con trực tiếp
        var child1 = new MediaFolderModel { Id = Guid.NewGuid(), Name = "Child 1", SocialChannelId = _pageAId, ParentFolderId = rootA1.Id };
        var child2 = new MediaFolderModel { Id = Guid.NewGuid(), Name = "Child 2", SocialChannelId = _pageAId, ParentFolderId = rootA1.Id };
        var child3 = new MediaFolderModel { Id = Guid.NewGuid(), Name = "Child 3", SocialChannelId = _pageAId, ParentFolderId = rootA1.Id };

        // child1 có 2 con (cháu của rootA1) -> không được tính vào ChildFolderCount của rootA1
        var grandChild1 = new MediaFolderModel { Id = Guid.NewGuid(), Name = "GC 1", SocialChannelId = _pageAId, ParentFolderId = child1.Id };
        var grandChild2 = new MediaFolderModel { Id = Guid.NewGuid(), Name = "GC 2", SocialChannelId = _pageAId, ParentFolderId = child1.Id };

        // 1 folder con đã bị xóa mềm -> không được đếm
        var childDeleted = new MediaFolderModel { Id = Guid.NewGuid(), Name = "Child Deleted", SocialChannelId = _pageAId, ParentFolderId = rootA1.Id, IsDeleted = true };

        _db.MediaFolders.AddRange(rootA1, rootA2, child1, child2, child3, grandChild1, grandChild2, childDeleted);
        await _db.SaveChangesAsync();

        // Act 1: Lấy root folders
        var rootResult = await _repo.GetChildrenAsync(new GetMediaFolderChildrenRequest
        {
            SocialChannelId = _pageAId,
            ParentFolderId = null
        });

        // Assert 1
        var respRootA1 = rootResult.Items.First(x => x.Id == rootA1.Id);
        var respRootA2 = rootResult.Items.First(x => x.Id == rootA2.Id);
        Assert.Equal(3, respRootA1.ChildFolderCount);
        Assert.True(respRootA1.HasChildren);
        Assert.Equal(0, respRootA2.ChildFolderCount);
        Assert.False(respRootA2.HasChildren);

        // Act 2: Lấy children của rootA1
        var childResult = await _repo.GetChildrenAsync(new GetMediaFolderChildrenRequest
        {
            SocialChannelId = _pageAId,
            ParentFolderId = rootA1.Id
        });

        // Assert 2
        var respChild1 = childResult.Items.First(x => x.Id == child1.Id);
        var respChild2 = childResult.Items.First(x => x.Id == child2.Id);
        Assert.Equal(2, respChild1.ChildFolderCount);
        Assert.True(respChild1.HasChildren);
        Assert.Equal(0, respChild2.ChildFolderCount);
        Assert.False(respChild2.HasChildren);
    }

    /// <summary>
    /// AC 4: DirectAssetCount đúng, chỉ đếm asset trực tiếp trong folder, không tính asset ở folder con.
    /// </summary>
    [Fact]
    public async Task GetChildren_DirectAssetCount_IsAccurate()
    {
        // Arrange
        var root = new MediaFolderModel { Id = Guid.NewGuid(), Name = "Root", SocialChannelId = _pageAId, ParentFolderId = null };
        var child = new MediaFolderModel { Id = Guid.NewGuid(), Name = "Child", SocialChannelId = _pageAId, ParentFolderId = root.Id };

        _db.MediaFolders.AddRange(root, child);

        // 3 assets trực tiếp trong root
        _db.MediaAssets.AddRange(
            new MediaAssetModel { Id = Guid.NewGuid(), FolderId = root.Id, FileName = "img1.jpg", StoragePath = "p1", PublicUrl = "u1" },
            new MediaAssetModel { Id = Guid.NewGuid(), FolderId = root.Id, FileName = "img2.jpg", StoragePath = "p2", PublicUrl = "u2" },
            new MediaAssetModel { Id = Guid.NewGuid(), FolderId = root.Id, FileName = "img3.jpg", StoragePath = "p3", PublicUrl = "u3" }
        );

        // 1 asset trong root bị soft deleted -> không được tính
        _db.MediaAssets.Add(
            new MediaAssetModel { Id = Guid.NewGuid(), FolderId = root.Id, FileName = "deleted.jpg", StoragePath = "pd", PublicUrl = "ud", IsDeleted = true }
        );

        // 2 assets trong child -> không được tính vào DirectAssetCount của root
        _db.MediaAssets.AddRange(
            new MediaAssetModel { Id = Guid.NewGuid(), FolderId = child.Id, FileName = "c1.jpg", StoragePath = "pc1", PublicUrl = "uc1" },
            new MediaAssetModel { Id = Guid.NewGuid(), FolderId = child.Id, FileName = "c2.jpg", StoragePath = "pc2", PublicUrl = "uc2" }
        );

        await _db.SaveChangesAsync();

        // Act
        var result = await _repo.GetChildrenAsync(new GetMediaFolderChildrenRequest
        {
            SocialChannelId = _pageAId,
            ParentFolderId = null
        });

        // Assert
        var folderResp = result.Items.First(x => x.Id == root.Id);
        Assert.Equal(3, folderResp.DirectAssetCount);
        Assert.Equal(3, folderResp.AssetCount); // Tương thích ngược
        Assert.Equal(1, folderResp.ChildFolderCount);
    }

    /// <summary>
    /// AC 5: Pagination và sorting ổn định với dữ liệu lớn (500 folder/Page).
    /// </summary>
    [Fact]
    public async Task GetChildren_PaginationAndSorting_StableWith500Folders()
    {
        // Arrange: Seed 500 folder trong Page A
        var folders = new List<MediaFolderModel>();
        for (int i = 1; i <= 500; i++)
        {
            folders.Add(new MediaFolderModel
            {
                Id = Guid.NewGuid(),
                Name = $"Folder_{i:D4}",
                SocialChannelId = _pageAId,
                ParentFolderId = null,
                SortOrder = 500 - i, // nghịch đảo để kiểm tra sắp xếp
                CreatedAt = DateTime.UtcNow.AddMinutes(-i)
            });
        }
        _db.MediaFolders.AddRange(folders);
        await _db.SaveChangesAsync();

        // Act 1: Phân trang trang 1 và trang 2 với SortBy = "name", SortDirection = "asc"
        var page1 = await _repo.GetChildrenAsync(new GetMediaFolderChildrenRequest
        {
            SocialChannelId = _pageAId,
            ParentFolderId = null,
            Index = 1,
            Size = 25,
            SortBy = "name",
            SortDirection = "asc"
        });

        var page2 = await _repo.GetChildrenAsync(new GetMediaFolderChildrenRequest
        {
            SocialChannelId = _pageAId,
            ParentFolderId = null,
            Index = 2,
            Size = 25,
            SortBy = "name",
            SortDirection = "asc"
        });

        // Assert 1: Tổng số bản ghi là 500, kích thước trang đúng
        Assert.Equal(500, page1.Total);
        Assert.Equal(25, page1.Items.Count);
        Assert.Equal(500, page2.Total);
        Assert.Equal(25, page2.Items.Count);

        // Thứ tự sắp xếp theo Name strictly ascending
        Assert.Equal("Folder_0001", page1.Items[0].Name);
        Assert.Equal("Folder_0025", page1.Items[24].Name);
        Assert.Equal("Folder_0026", page2.Items[0].Name);
        Assert.Equal("Folder_0050", page2.Items[24].Name);

        // Đảm bảo không có bản ghi nào trùng lặp giữa trang 1 và trang 2
        var page1Ids = page1.Items.Select(x => x.Id).ToHashSet();
        Assert.DoesNotContain(page2.Items, x => page1Ids.Contains(x.Id));

        // Act 2: Sắp xếp theo SortOrder desc
        var sortOrderDesc = await _repo.GetChildrenAsync(new GetMediaFolderChildrenRequest
        {
            SocialChannelId = _pageAId,
            ParentFolderId = null,
            Index = 1,
            Size = 10,
            SortBy = "sortOrder",
            SortDirection = "desc"
        });

        // Item đầu tiên phải có SortOrder cao nhất (499)
        Assert.Equal(499, sortOrderDesc.Items[0].SortOrder);
        Assert.Equal("Folder_0001", sortOrderDesc.Items[0].Name);
    }

    /// <summary>
    /// AC 6: Parent thuộc Page khác bị từ chối với ArgumentException (400 Bad Request).
    /// </summary>
    [Fact]
    public async Task GetChildren_ParentBelongingToDifferentPage_IsRejected()
    {
        // Arrange: Tạo thư mục cha thuộc Page B
        var parentB = new MediaFolderModel
        {
            Id = Guid.NewGuid(),
            Name = "Root of Page B",
            SocialChannelId = _pageBId,
            ParentFolderId = null
        };
        _db.MediaFolders.Add(parentB);
        await _db.SaveChangesAsync();

        // Act & Assert: Yêu cầu lấy con của parentB nhưng truyền SocialChannelId = Page A
        var request = new GetMediaFolderChildrenRequest
        {
            SocialChannelId = _pageAId,
            ParentFolderId = parentB.Id
        };

        var ex = await Assert.ThrowsAsync<ArgumentException>(() => _repo.GetChildrenAsync(request));
        Assert.Contains("không thuộc Page", ex.Message);
    }

    /// <summary>
    /// AC 6b: Parent không tồn tại hoặc SocialChannelId rỗng bị từ chối.
    /// </summary>
    [Fact]
    public async Task GetChildren_NonExistentParentOrChannel_IsRejected()
    {
        // SocialChannelId rỗng
        await Assert.ThrowsAsync<ArgumentException>(() => _repo.GetChildrenAsync(new GetMediaFolderChildrenRequest
        {
            SocialChannelId = Guid.Empty
        }));

        // SocialChannel không tồn tại
        await Assert.ThrowsAsync<KeyNotFoundException>(() => _repo.GetChildrenAsync(new GetMediaFolderChildrenRequest
        {
            SocialChannelId = Guid.NewGuid()
        }));

        // Parent không tồn tại
        await Assert.ThrowsAsync<KeyNotFoundException>(() => _repo.GetChildrenAsync(new GetMediaFolderChildrenRequest
        {
            SocialChannelId = _pageAId,
            ParentFolderId = Guid.NewGuid()
        }));
    }

    /// <summary>
    /// AC 7: User không có quyền không đọc được folder.
    /// Controller được bảo vệ bởi Authorize và từ chối truy cập trái phép.
    /// </summary>
    [Fact]
    public void Controller_HasAuthorizeAttribute_WithRequiredRoles()
    {
        var type = typeof(MediaFolderController);

        var authorizeAttributes = type.GetCustomAttributes(typeof(Microsoft.AspNetCore.Authorization.AuthorizeAttribute), true)
            .Cast<Microsoft.AspNetCore.Authorization.AuthorizeAttribute>()
            .ToList();

        Assert.NotEmpty(authorizeAttributes);
        var authAttr = authorizeAttributes.First();
        Assert.Contains("Admin", authAttr.Roles);
        Assert.Contains("ContentManager", authAttr.Roles);
        Assert.Contains("Reviewer", authAttr.Roles);
        Assert.Contains("Viewer", authAttr.Roles);
    }

    /// <summary>
    /// AC 7b: Controller trả về 200 OK khi người dùng hợp lệ và trả về ApiResponse chuẩn.
    /// </summary>
    [Fact]
    public async Task Controller_GetChildren_ReturnsOkResult()
    {
        // Arrange
        var root = new MediaFolderModel
        {
            Id = Guid.NewGuid(),
            Name = "Folder Test",
            SocialChannelId = _pageAId,
            ParentFolderId = null
        };
        _db.MediaFolders.Add(root);
        await _db.SaveChangesAsync();

        // Gắn ClaimsPrincipal vào ControllerContext
        var user = new ClaimsPrincipal(new ClaimsIdentity(new[]
        {
            new Claim(ClaimTypes.NameIdentifier, Guid.NewGuid().ToString()),
            new Claim(ClaimTypes.Name, "tester"),
            new Claim(ClaimTypes.Role, "Admin")
        }, "TestAuth"));

        _controller.ControllerContext = new ControllerContext
        {
            HttpContext = new DefaultHttpContext { User = user }
        };

        // Act
        var actionResult = await _controller.GetChildren(new GetMediaFolderChildrenRequest
        {
            SocialChannelId = _pageAId,
            ParentFolderId = null
        }, CancellationToken.None);

        // Assert
        var okResult = Assert.IsType<OkObjectResult>(actionResult);
        var apiResp = Assert.IsAssignableFrom<ApiResponse<PagedResult<MediaFolderResponse>>>(okResult.Value);
        Assert.True(apiResp.Success);
        Assert.NotNull(apiResp.Data);
        Assert.Single(apiResp.Data!.Items);
        Assert.Equal("Folder Test", apiResp.Data.Items[0].Name);
    }

    /// <summary>
    /// AC 8: Không có truy vấn hoặc response chứa descendants ngoài yêu cầu.
    /// Response DTO MediaFolderResponse là flat record, không có trường List con, và chỉ chứa counts.
    /// </summary>
    [Fact]
    public async Task GetChildren_NoDescendantsLoaded_InResponse()
    {
        // Arrange: Cây 4 cấp
        var l1 = new MediaFolderModel { Id = Guid.NewGuid(), Name = "Level 1", SocialChannelId = _pageAId, ParentFolderId = null };
        var l2 = new MediaFolderModel { Id = Guid.NewGuid(), Name = "Level 2", SocialChannelId = _pageAId, ParentFolderId = l1.Id };
        var l3 = new MediaFolderModel { Id = Guid.NewGuid(), Name = "Level 3", SocialChannelId = _pageAId, ParentFolderId = l2.Id };
        var l4 = new MediaFolderModel { Id = Guid.NewGuid(), Name = "Level 4", SocialChannelId = _pageAId, ParentFolderId = l3.Id };

        _db.MediaFolders.AddRange(l1, l2, l3, l4);
        await _db.SaveChangesAsync();

        // Act
        var result = await _repo.GetChildrenAsync(new GetMediaFolderChildrenRequest
        {
            SocialChannelId = _pageAId,
            ParentFolderId = null
        });

        // Assert: Chỉ có Level 1 trong kết quả, không có l2, l3, l4 trong items
        Assert.Single(result.Items);
        var rootItem = result.Items[0];
        Assert.Equal(l1.Id, rootItem.Id);
        Assert.Equal(1, rootItem.ChildFolderCount); // chỉ đếm l2

        // Kiểm tra kiểu của MediaFolderResponse không có thuộc tính chứa danh sách con (descendants list)
        var responseProps = typeof(MediaFolderResponse).GetProperties();
        var collectionProps = responseProps.Where(p =>
            p.PropertyType != typeof(string) &&
            typeof(System.Collections.IEnumerable).IsAssignableFrom(p.PropertyType)).ToList();

        Assert.Empty(collectionProps);
    }

    /// <summary>
    /// Kiểm tra endpoint POST /api/MediaFolder/children trả về cùng kết quả chuẩn với request body.
    /// </summary>
    [Fact]
    public async Task Controller_PostChildren_ReturnsOkResult()
    {
        // Arrange
        var root = new MediaFolderModel
        {
            Id = Guid.NewGuid(),
            Name = "Folder Test Post",
            SocialChannelId = _pageAId,
            ParentFolderId = null
        };
        _db.MediaFolders.Add(root);
        await _db.SaveChangesAsync();

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

        // Act
        var actionResult = await _controller.PostChildren(new GetMediaFolderChildrenRequest
        {
            SocialChannelId = _pageAId,
            ParentFolderId = null
        }, CancellationToken.None);

        // Assert
        var okResult = Assert.IsType<OkObjectResult>(actionResult);
        var apiResp = Assert.IsAssignableFrom<ApiResponse<PagedResult<MediaFolderResponse>>>(okResult.Value);
        Assert.True(apiResp.Success);
        Assert.NotNull(apiResp.Data);
        Assert.Contains(apiResp.Data!.Items, x => x.Id == root.Id);
    }

    /// <summary>
    /// Kiểm tra FilterAsync áp dụng lọc SocialChannelId đúng và không rò rỉ dữ liệu giữa các Page.
    /// </summary>
    [Fact]
    public async Task FilterAsync_ScopesBySocialChannelId()
    {
        var fA = new MediaFolderModel { Id = Guid.NewGuid(), Name = "Folder Page A", SocialChannelId = _pageAId };
        var fB = new MediaFolderModel { Id = Guid.NewGuid(), Name = "Folder Page B", SocialChannelId = _pageBId };
        _db.MediaFolders.AddRange(fA, fB);
        await _db.SaveChangesAsync();

        var result = await _repo.FilterAsync(new MediaFolderFilterRequest
        {
            SocialChannelId = _pageAId
        });

        Assert.Contains(result.Items, x => x.Id == fA.Id);
        Assert.DoesNotContain(result.Items, x => x.Id == fB.Id);
    }
}

public class TestUserContext : IUserContext
{
    public Guid? UserId { get; set; } = Guid.NewGuid();
    public string? UserName { get; set; } = "testuser";
    public List<string> Roles { get; set; } = ["Admin"];

    public Guid? GetCurrentUserId() => UserId;
    public string? GetCurrentUserName() => UserName;
    public IReadOnlyList<string> GetCurrentUserRoles() => Roles;
}
