using System.Security.Claims;
using Backend.Data;
using Backend.Modules.MediaFolder;
using Backend.Modules.SocialChannel;
using Backend.Modules.SocialChannel.Enums;
using Backend.Shared;
using Backend.Shared.Repositories;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Xunit;

namespace Backend.Tests.Modules.MediaFolder;

public class MediaFolderBulkCreateTests : IDisposable
{
    private readonly SqliteConnection _connection;
    private readonly AppDbContext _db;
    private readonly TestUserContext _userContext;
    private readonly MediaFolderRepository _repo;
    private readonly MediaFolderController _controller;

    private readonly Guid _pageAId = Guid.NewGuid();
    private readonly Guid _pageBId = Guid.NewGuid();

    public MediaFolderBulkCreateTests()
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

    [Fact]
    public async Task BulkCreate_PageAccess_AllowsOwnerAndRejectsOtherPageAtomically()
    {
        _userContext.Roles = ["ContentManager"];
        _userContext.UserName = "page-a-owner";
        var pageA = await _db.SocialChannels.FindAsync(_pageAId);
        var pageB = await _db.SocialChannels.FindAsync(_pageBId);
        pageA!.CreatedBy = _userContext.UserName;
        pageB!.CreatedBy = "other-owner";
        await _db.SaveChangesAsync();

        var allowed = await _repo.BulkCreateAsync(new BulkCreateMediaFolderRequest
        {
            SocialChannelId = _pageAId,
            Folders = [new BulkCreateMediaFolderItem { ClientRef = "allowed", Name = "Allowed" }]
        });
        Assert.Equal(1, allowed.TotalCreated);

        var countBefore = await _db.MediaFolders.CountAsync();
        var denied = await Assert.ThrowsAsync<KeyNotFoundException>(() =>
            _repo.BulkCreateAsync(new BulkCreateMediaFolderRequest
            {
                SocialChannelId = _pageBId,
                Folders = [new BulkCreateMediaFolderItem { ClientRef = "denied", Name = "Secret write" }]
            }));

        Assert.Equal("Page/Kênh không tồn tại.", denied.Message);
        Assert.Equal(countBefore, await _db.MediaFolders.CountAsync());
        Assert.False(await _db.MediaFolders.AnyAsync(x => x.Name == "Secret write"));
    }

    /// <summary>
    /// AC 1: Tạo hierarchy đúng qua clientRef/parentRef (Level 1 -> Level 2 -> Level 3).
    /// </summary>
    [Fact]
    public async Task BulkCreate_Hierarchy_CreatesCorrectParentChildTree()
    {
        var request = new BulkCreateMediaFolderRequest
        {
            SocialChannelId = _pageAId,
            ParentFolderId = null,
            Folders =
            [
                new BulkCreateMediaFolderItem { ClientRef = "r1", Name = "Level 1" },
                new BulkCreateMediaFolderItem { ClientRef = "c1_1", Name = "Level 2 A", ParentRef = "r1" },
                new BulkCreateMediaFolderItem { ClientRef = "c1_2", Name = "Level 2 B", ParentRef = "r1" },
                new BulkCreateMediaFolderItem { ClientRef = "g1", Name = "Level 3", ParentRef = "c1_1" }
            ]
        };

        var response = await _repo.BulkCreateAsync(request);

        Assert.True(response.Success);
        Assert.Equal(4, response.TotalCreated);
        Assert.Equal(4, response.Folders.Count);

        var r1Item = response.Folders.First(x => x.ClientRef == "r1");
        var c1Item = response.Folders.First(x => x.ClientRef == "c1_1");
        var c2Item = response.Folders.First(x => x.ClientRef == "c1_2");
        var g1Item = response.Folders.First(x => x.ClientRef == "g1");

        // Kiểm tra trong database
        var dbR1 = await _db.MediaFolders.FindAsync(r1Item.Id);
        var dbC1 = await _db.MediaFolders.FindAsync(c1Item.Id);
        var dbC2 = await _db.MediaFolders.FindAsync(c2Item.Id);
        var dbG1 = await _db.MediaFolders.FindAsync(g1Item.Id);

        Assert.NotNull(dbR1);
        Assert.Null(dbR1.ParentFolderId);

        Assert.NotNull(dbC1);
        Assert.Equal(dbR1.Id, dbC1.ParentFolderId);

        Assert.NotNull(dbC2);
        Assert.Equal(dbR1.Id, dbC2.ParentFolderId);

        Assert.NotNull(dbG1);
        Assert.Equal(dbC1.Id, dbG1.ParentFolderId);
    }

    /// <summary>
    /// AC 2 & 3: Tất cả node kế thừa đúng SocialChannelId, DTO node không nhận Page riêng.
    /// </summary>
    [Fact]
    public async Task BulkCreate_AllNodes_InheritSocialChannelId_NoPerNodePageField()
    {
        // Kiểm tra kiểu BulkCreateMediaFolderItem không có property SocialChannelId
        var itemProps = typeof(BulkCreateMediaFolderItem).GetProperties();
        Assert.DoesNotContain(itemProps, p => p.Name == "SocialChannelId");

        var request = new BulkCreateMediaFolderRequest
        {
            SocialChannelId = _pageAId,
            Folders =
            [
                new BulkCreateMediaFolderItem { ClientRef = "1", Name = "Folder 1" },
                new BulkCreateMediaFolderItem { ClientRef = "2", Name = "Folder 2", ParentRef = "1" }
            ]
        };

        var response = await _repo.BulkCreateAsync(request);

        Assert.All(response.Folders, f => Assert.Equal(_pageAId, f.SocialChannelId));

        var dbFolders = await _db.MediaFolders.Where(x => x.SocialChannelId == _pageAId).ToListAsync();
        Assert.Equal(2, dbFolders.Count);
        Assert.All(dbFolders, f => Assert.Equal(_pageAId, f.SocialChannelId));
    }

    /// <summary>
    /// AC 4: Parent thuộc Page khác bị từ chối với ArgumentException.
    /// </summary>
    [Fact]
    public async Task BulkCreate_ParentBelongingToDifferentPage_IsRejected()
    {
        // Tạo folder thuộc Page B
        var folderB = new MediaFolderModel
        {
            Id = Guid.NewGuid(),
            Name = "Folder Page B",
            SocialChannelId = _pageBId
        };
        _db.MediaFolders.Add(folderB);
        await _db.SaveChangesAsync();

        var request = new BulkCreateMediaFolderRequest
        {
            SocialChannelId = _pageAId,
            ParentFolderId = folderB.Id, // Parent thuộc Page B nhưng request Page A
            Folders =
            [
                new BulkCreateMediaFolderItem { ClientRef = "1", Name = "New Folder" }
            ]
        };

        var ex = await Assert.ThrowsAsync<ArgumentException>(() => _repo.BulkCreateAsync(request));
        Assert.Contains("không thuộc Page", ex.Message);
    }

    /// <summary>
    /// AC 5: Cycle trong batch (A -> B -> A hoặc tự trỏ) bị từ chối.
    /// </summary>
    [Fact]
    public async Task BulkCreate_Cycle_IsRejected()
    {
        // Case 1: Tự trỏ chính nó
        var selfCycleReq = new BulkCreateMediaFolderRequest
        {
            SocialChannelId = _pageAId,
            Folders =
            [
                new BulkCreateMediaFolderItem { ClientRef = "a", Name = "A", ParentRef = "a" }
            ]
        };
        await Assert.ThrowsAsync<ArgumentException>(() => _repo.BulkCreateAsync(selfCycleReq));

        // Case 2: Vòng lặp A -> B -> A
        var loopReq = new BulkCreateMediaFolderRequest
        {
            SocialChannelId = _pageAId,
            Folders =
            [
                new BulkCreateMediaFolderItem { ClientRef = "a", Name = "A", ParentRef = "b" },
                new BulkCreateMediaFolderItem { ClientRef = "b", Name = "B", ParentRef = "a" }
            ]
        };
        var ex = await Assert.ThrowsAsync<ArgumentException>(() => _repo.BulkCreateAsync(loopReq));
        Assert.Contains("vòng lặp", ex.Message);
    }

    /// <summary>
    /// AC 6: clientRef / parentRef không hợp lệ (trùng clientRef, parentRef không tồn tại) bị từ chối.
    /// </summary>
    [Fact]
    public async Task BulkCreate_InvalidClientRefOrParentRef_IsRejected()
    {
        // Trùng clientRef
        var duplicateClientRefReq = new BulkCreateMediaFolderRequest
        {
            SocialChannelId = _pageAId,
            Folders =
            [
                new BulkCreateMediaFolderItem { ClientRef = "dup", Name = "Folder 1" },
                new BulkCreateMediaFolderItem { ClientRef = "dup", Name = "Folder 2" }
            ]
        };
        var exDup = await Assert.ThrowsAsync<ArgumentException>(() => _repo.BulkCreateAsync(duplicateClientRefReq));
        Assert.Contains("trùng lặp", exDup.Message);

        // parentRef không tồn tại trong batch
        var missingParentRefReq = new BulkCreateMediaFolderRequest
        {
            SocialChannelId = _pageAId,
            Folders =
            [
                new BulkCreateMediaFolderItem { ClientRef = "child", Name = "Child", ParentRef = "non_existent" }
            ]
        };
        var exMissing = await Assert.ThrowsAsync<ArgumentException>(() => _repo.BulkCreateAsync(missingParentRefReq));
        Assert.Contains("không tồn tại trong batch", exMissing.Message);
    }

    /// <summary>
    /// AC 7: Duplicate cùng parent xử lý theo policy (Error vs Skip).
    /// </summary>
    [Fact]
    public async Task BulkCreate_DuplicatePolicy_Error_ThrowsException()
    {
        // Đã có folder "Marketing" trong DB dưới root của Page A
        _db.MediaFolders.Add(new MediaFolderModel
        {
            Id = Guid.NewGuid(),
            Name = "Marketing",
            SocialChannelId = _pageAId,
            ParentFolderId = null
        });
        await _db.SaveChangesAsync();

        var request = new BulkCreateMediaFolderRequest
        {
            SocialChannelId = _pageAId,
            ParentFolderId = null,
            DuplicatePolicy = BulkDuplicatePolicy.Error,
            Folders =
            [
                new BulkCreateMediaFolderItem { ClientRef = "1", Name = "Marketing" }
            ]
        };

        var ex = await Assert.ThrowsAsync<ArgumentException>(() => _repo.BulkCreateAsync(request));
        Assert.Contains("đã tồn tại", ex.Message);
    }

    /// <summary>
    /// AC 7b: Duplicate cùng parent với policy = Skip sẽ bỏ qua tạo mới và map sang folder có sẵn.
    /// </summary>
    [Fact]
    public async Task BulkCreate_DuplicatePolicy_Skip_ReusesExistingAndSkips()
    {
        var existing = new MediaFolderModel
        {
            Id = Guid.NewGuid(),
            Name = "Marketing",
            SocialChannelId = _pageAId,
            ParentFolderId = null
        };
        _db.MediaFolders.Add(existing);
        await _db.SaveChangesAsync();

        var request = new BulkCreateMediaFolderRequest
        {
            SocialChannelId = _pageAId,
            ParentFolderId = null,
            DuplicatePolicy = BulkDuplicatePolicy.Skip,
            Folders =
            [
                new BulkCreateMediaFolderItem { ClientRef = "ref_existing", Name = "Marketing" },
                new BulkCreateMediaFolderItem { ClientRef = "ref_new_child", Name = "Sub Marketing", ParentRef = "ref_existing" }
            ]
        };

        var response = await _repo.BulkCreateAsync(request);

        Assert.True(response.Success);
        Assert.Equal(1, response.TotalCreated);
        Assert.Equal(1, response.TotalSkipped);

        var skippedItem = response.Folders.First(x => x.ClientRef == "ref_existing");
        Assert.True(skippedItem.IsSkipped);
        Assert.Equal(existing.Id, skippedItem.Id);

        var createdChild = response.Folders.First(x => x.ClientRef == "ref_new_child");
        Assert.False(createdChild.IsSkipped);
        Assert.Equal(existing.Id, createdChild.ParentFolderId);
    }

    /// <summary>
    /// AC 8: Vượt giới hạn batch/depth/name bị từ chối.
    /// </summary>
    [Fact]
    public async Task BulkCreate_LimitsExceeded_IsRejected()
    {
        // 1. Tên vượt 200 ký tự
        var longNameReq = new BulkCreateMediaFolderRequest
        {
            SocialChannelId = _pageAId,
            Folders = [new BulkCreateMediaFolderItem { ClientRef = "1", Name = new string('A', 201) }]
        };
        await Assert.ThrowsAsync<ArgumentException>(() => _repo.BulkCreateAsync(longNameReq));

        // 2. Batch vượt 200 items
        var largeBatch = Enumerable.Range(1, 201).Select(i => new BulkCreateMediaFolderItem
        {
            ClientRef = i.ToString(),
            Name = $"Folder {i}"
        }).ToList();
        var largeBatchReq = new BulkCreateMediaFolderRequest
        {
            SocialChannelId = _pageAId,
            Folders = largeBatch
        };
        await Assert.ThrowsAsync<ArgumentException>(() => _repo.BulkCreateAsync(largeBatchReq));

        // 3. Độ sâu vượt 10 cấp
        var deepList = new List<BulkCreateMediaFolderItem>();
        deepList.Add(new BulkCreateMediaFolderItem { ClientRef = "lvl_1", Name = "Level 1" });
        for (int i = 2; i <= 11; i++)
        {
            deepList.Add(new BulkCreateMediaFolderItem
            {
                ClientRef = $"lvl_{i}",
                Name = $"Level {i}",
                ParentRef = $"lvl_{i - 1}"
            });
        }
        var deepReq = new BulkCreateMediaFolderRequest
        {
            SocialChannelId = _pageAId,
            Folders = deepList
        };
        var exDepth = await Assert.ThrowsAsync<ArgumentException>(() => _repo.BulkCreateAsync(deepReq));
        Assert.Contains("Độ sâu thư mục", exDepth.Message);
    }

    /// <summary>
    /// AC 9: Một node lỗi rollback toàn bộ batch (Transaction nguyên tử).
    /// </summary>
    [Fact]
    public async Task BulkCreate_Atomicity_RollbackOnFailure()
    {
        var countBefore = await _db.MediaFolders.CountAsync();

        // Node 1 hợp lệ, Node 2 gây lỗi (trùng tên trong DB khi policy = Error)
        _db.MediaFolders.Add(new MediaFolderModel
        {
            Id = Guid.NewGuid(),
            Name = "Existing Conflict",
            SocialChannelId = _pageAId
        });
        await _db.SaveChangesAsync();

        var request = new BulkCreateMediaFolderRequest
        {
            SocialChannelId = _pageAId,
            DuplicatePolicy = BulkDuplicatePolicy.Error,
            Folders =
            [
                new BulkCreateMediaFolderItem { ClientRef = "good_1", Name = "Valid Folder 1" },
                new BulkCreateMediaFolderItem { ClientRef = "bad_2", Name = "Existing Conflict" }
            ]
        };

        await Assert.ThrowsAsync<ArgumentException>(() => _repo.BulkCreateAsync(request));

        // Xác nhận "Valid Folder 1" không được lưu vào DB (đã rollback hoàn toàn)
        var existsGood = await _db.MediaFolders.AnyAsync(x => x.Name == "Valid Folder 1");
        Assert.False(existsGood);
    }

    /// <summary>
    /// AC 10: Phân quyền controller [Authorize(Roles = "Admin,ContentManager")]
    /// </summary>
    [Fact]
    public void Controller_BulkCreate_HasAuthorizeAdminOrContentManager()
    {
        var method = typeof(MediaFolderController).GetMethod("BulkCreate");
        Assert.NotNull(method);

        var authAttrs = method.GetCustomAttributes(typeof(Microsoft.AspNetCore.Authorization.AuthorizeAttribute), true)
            .Cast<Microsoft.AspNetCore.Authorization.AuthorizeAttribute>()
            .ToList();

        Assert.NotEmpty(authAttrs);
        var attr = authAttrs.First();
        Assert.Contains("Admin", attr.Roles);
        Assert.Contains("ContentManager", attr.Roles);
        Assert.DoesNotContain("Viewer", attr.Roles);
        Assert.DoesNotContain("Reviewer", attr.Roles);
    }

    /// <summary>
    /// AC 11: Response mapping clientRef sang ID chính xác.
    /// </summary>
    [Fact]
    public async Task BulkCreate_ResponseMapping_MatchesClientRefToId()
    {
        var request = new BulkCreateMediaFolderRequest
        {
            SocialChannelId = _pageAId,
            Folders =
            [
                new BulkCreateMediaFolderItem { ClientRef = "client-alpha", Name = "Alpha" },
                new BulkCreateMediaFolderItem { ClientRef = "client-beta", Name = "Beta", ParentRef = "client-alpha" }
            ]
        };

        var response = await _repo.BulkCreateAsync(request);

        Assert.True(response.Success);
        var alpha = response.Folders.First(x => x.ClientRef == "client-alpha");
        var beta = response.Folders.First(x => x.ClientRef == "client-beta");

        var dbAlpha = await _db.MediaFolders.FindAsync(alpha.Id);
        var dbBeta = await _db.MediaFolders.FindAsync(beta.Id);

        Assert.NotNull(dbAlpha);
        Assert.Equal("Alpha", dbAlpha.Name);

        Assert.NotNull(dbBeta);
        Assert.Equal("Beta", dbBeta.Name);
        Assert.Equal(dbAlpha.Id, dbBeta.ParentFolderId);
    }

    /// <summary>
    /// MEDIA-03-AC1: ValidateOnly hierarchy nhiều cấp — DB không đổi và ParentFolderId preview đúng clientRef/parentRef.
    /// </summary>
    [Fact]
    public async Task BulkCreate_ValidateOnly_ReturnsPreviewWithoutSaving()
    {
        var countBefore = await _db.MediaFolders.CountAsync();

        var request = new BulkCreateMediaFolderRequest
        {
            SocialChannelId = _pageAId,
            ValidateOnly = true,
            Folders =
            [
                new BulkCreateMediaFolderItem { ClientRef = "p1", Name = "Preview 1" },
                new BulkCreateMediaFolderItem { ClientRef = "p2", Name = "Preview 2", ParentRef = "p1" },
                new BulkCreateMediaFolderItem { ClientRef = "p3", Name = "Preview 3", ParentRef = "p2" }
            ]
        };

        var response = await _repo.BulkCreateAsync(request);

        Assert.True(response.Success);
        Assert.True(response.ValidateOnly);
        Assert.Equal(3, response.TotalCreated);
        Assert.Equal(0, response.TotalSkipped);
        Assert.Equal(3, response.Folders.Count);

        var p1 = response.Folders.First(x => x.ClientRef == "p1");
        var p2 = response.Folders.First(x => x.ClientRef == "p2");
        var p3 = response.Folders.First(x => x.ClientRef == "p3");

        Assert.Null(p1.ParentFolderId);
        Assert.Equal(p1.Id, p2.ParentFolderId);
        Assert.Equal(p2.Id, p3.ParentFolderId);
        Assert.All(response.Folders, x => Assert.Equal(_pageAId, x.SocialChannelId));
        Assert.Equal(countBefore, await _db.MediaFolders.CountAsync());
        Assert.False(await _db.MediaFolders.AnyAsync(x => x.Name.StartsWith("Preview ")));
    }

    /// <summary>
    /// MEDIA-03-AC1: ValidateOnly + DuplicatePolicy.Error với folder đã có trong DB → từ chối, DB không đổi.
    /// </summary>
    [Fact]
    public async Task BulkCreate_ValidateOnly_DuplicatePolicy_Error_RejectsExistingWithoutSaving()
    {
        _db.MediaFolders.Add(new MediaFolderModel
        {
            Id = Guid.NewGuid(),
            Name = "Marketing",
            SocialChannelId = _pageAId,
            ParentFolderId = null
        });
        await _db.SaveChangesAsync();
        var countBefore = await _db.MediaFolders.CountAsync();

        var request = new BulkCreateMediaFolderRequest
        {
            SocialChannelId = _pageAId,
            ValidateOnly = true,
            DuplicatePolicy = BulkDuplicatePolicy.Error,
            Folders =
            [
                new BulkCreateMediaFolderItem { ClientRef = "1", Name = "Marketing" },
                new BulkCreateMediaFolderItem { ClientRef = "2", Name = "Child", ParentRef = "1" }
            ]
        };

        var ex = await Assert.ThrowsAsync<ArgumentException>(() => _repo.BulkCreateAsync(request));
        Assert.Contains("đã tồn tại", ex.Message);
        Assert.Equal(countBefore, await _db.MediaFolders.CountAsync());
        Assert.False(await _db.MediaFolders.AnyAsync(x => x.Name == "Child"));
    }

    /// <summary>
    /// MEDIA-03-AC1: ValidateOnly + DuplicatePolicy.Skip tái sử dụng existing ID; descendants trỏ đúng; DB không đổi.
    /// </summary>
    [Fact]
    public async Task BulkCreate_ValidateOnly_DuplicatePolicy_Skip_ReusesExistingIdsWithoutSaving()
    {
        var existing = new MediaFolderModel
        {
            Id = Guid.NewGuid(),
            Name = "Marketing",
            SocialChannelId = _pageAId,
            ParentFolderId = null
        };
        _db.MediaFolders.Add(existing);
        await _db.SaveChangesAsync();
        var countBefore = await _db.MediaFolders.CountAsync();

        var request = new BulkCreateMediaFolderRequest
        {
            SocialChannelId = _pageAId,
            ValidateOnly = true,
            DuplicatePolicy = BulkDuplicatePolicy.Skip,
            Folders =
            [
                new BulkCreateMediaFolderItem { ClientRef = "ref_existing", Name = "Marketing" },
                new BulkCreateMediaFolderItem { ClientRef = "ref_new_child", Name = "Sub Marketing", ParentRef = "ref_existing" }
            ]
        };

        var response = await _repo.BulkCreateAsync(request);

        Assert.True(response.Success);
        Assert.True(response.ValidateOnly);
        Assert.Equal(1, response.TotalCreated);
        Assert.Equal(1, response.TotalSkipped);

        var skipped = response.Folders.First(x => x.ClientRef == "ref_existing");
        Assert.True(skipped.IsSkipped);
        Assert.Equal(existing.Id, skipped.Id);

        var child = response.Folders.First(x => x.ClientRef == "ref_new_child");
        Assert.False(child.IsSkipped);
        Assert.Equal(existing.Id, child.ParentFolderId);
        Assert.Equal(_pageAId, child.SocialChannelId);

        Assert.Equal(countBefore, await _db.MediaFolders.CountAsync());
        Assert.False(await _db.MediaFolders.AnyAsync(x => x.Name == "Sub Marketing"));
    }

    /// <summary>
    /// MEDIA-03-AC1: persistence failure giữa transaction phải rollback toàn bộ (không chỉ lỗi validation trước khi ghi).
    /// </summary>
    [Fact]
    public async Task BulkCreate_PersistenceFailure_RollsBackEntireBatch()
    {
        await using var connection = new SqliteConnection("DataSource=:memory:");
        await connection.OpenAsync();

        var interceptor = new FailAfterFirstSaveInterceptor();
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlite(connection)
            .AddInterceptors(interceptor)
            .Options;

        await using var db = new AppDbContext(options);
        await db.Database.EnsureCreatedAsync();
        db.SocialChannels.Add(new SocialChannelModel
        {
            Id = _pageAId,
            Platform = SocialPlatform.Facebook,
            ChannelType = SocialChannelType.Page,
            PageName = "Page A",
            ExternalPageId = "fb-page-a-persist",
            AccessToken = "token-a",
            IsActive = true
        });
        await db.SaveChangesAsync();
        interceptor.Reset();

        var repo = new MediaFolderRepository(db, _userContext);
        var countBefore = await db.MediaFolders.CountAsync();

        var request = new BulkCreateMediaFolderRequest
        {
            SocialChannelId = _pageAId,
            Folders =
            [
                new BulkCreateMediaFolderItem { ClientRef = "good_1", Name = "Persist Good" },
                new BulkCreateMediaFolderItem { ClientRef = "good_2", Name = "Persist Also", ParentRef = "good_1" }
            ]
        };

        await Assert.ThrowsAsync<InvalidOperationException>(() => repo.BulkCreateAsync(request));

        Assert.Equal(countBefore, await db.MediaFolders.CountAsync());
        Assert.False(await db.MediaFolders.AnyAsync(x => x.Name == "Persist Good"));
        Assert.False(await db.MediaFolders.AnyAsync(x => x.Name == "Persist Also"));
    }
}

/// <summary>
/// Gây lỗi persistence thật ở lần SaveChanges thứ N trong batch (sau khi node trước đã Add).
/// </summary>
file sealed class FailAfterFirstSaveInterceptor : Microsoft.EntityFrameworkCore.Diagnostics.SaveChangesInterceptor
{
    private int _saves;

    public void Reset() => _saves = 0;

    public override InterceptionResult<int> SavingChanges(
        DbContextEventData eventData,
        InterceptionResult<int> result)
    {
        _saves++;
        if (_saves > 1)
            throw new InvalidOperationException("Simulated persistence failure after first SaveChanges.");
        return base.SavingChanges(eventData, result);
    }

    public override ValueTask<InterceptionResult<int>> SavingChangesAsync(
        DbContextEventData eventData,
        InterceptionResult<int> result,
        CancellationToken cancellationToken = default)
    {
        _saves++;
        if (_saves > 1)
            throw new InvalidOperationException("Simulated persistence failure after first SaveChanges.");
        return base.SavingChangesAsync(eventData, result, cancellationToken);
    }
}
