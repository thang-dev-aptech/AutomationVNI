using Backend.Data;
using Backend.Modules.MediaFolder;
using Backend.Modules.SocialChannel;
using Backend.Modules.SocialChannel.Enums;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Backend.Tests.Modules.MediaFolder;

/// <summary>
/// MEDIA-06 (đã sửa lại sau khi làm rõ với người dùng): tạo 1 folder gốc cùng tên ở nhiều
/// Page cùng lúc, best-effort — Page lỗi không chặn Page khác. Đây KHÔNG phải hierarchy
/// trong một Page (đó là MEDIA-03/BulkCreateAsync, vẫn giữ nguyên, không đổi).
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
    public async Task CreateAcrossPages_MultiplePages_CreatesRootFolderInEach()
    {
        var result = await _repo.CreateAcrossPagesAsync(new CreateMediaFolderAcrossPagesRequest
        {
            Name = "Campaign X",
            SocialChannelIds = [_pageAId, _pageBId],
        });

        Assert.Equal(2, result.TotalRequested);
        Assert.Equal(2, result.TotalSucceeded);
        Assert.Equal(0, result.TotalFailed);
        Assert.All(result.Results, r => Assert.True(r.Success));

        var folderA = await _db.MediaFolders.SingleAsync(f => f.SocialChannelId == _pageAId);
        var folderB = await _db.MediaFolders.SingleAsync(f => f.SocialChannelId == _pageBId);
        Assert.Equal("Campaign X", folderA.Name);
        Assert.Null(folderA.ParentFolderId);
        Assert.Equal("Campaign X", folderB.Name);
        Assert.Null(folderB.ParentFolderId);
    }

    [Fact]
    public async Task CreateAcrossPages_InaccessiblePage_FailsThatPageOnly_DoesNotBlockOthers()
    {
        var result = await _repo.CreateAcrossPagesAsync(new CreateMediaFolderAcrossPagesRequest
        {
            Name = "Campaign X",
            SocialChannelIds = [_pageAId, _pageCId],
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
    public async Task CreateAcrossPages_AdminRole_CanTargetAnyExistingPage()
    {
        _userContext.Roles = ["Admin"];
        _userContext.UserName = "admin-user";

        var result = await _repo.CreateAcrossPagesAsync(new CreateMediaFolderAcrossPagesRequest
        {
            Name = "Campaign X",
            SocialChannelIds = [_pageCId],
        });

        Assert.Equal(1, result.TotalSucceeded);
        Assert.True(await _db.MediaFolders.AnyAsync(f => f.SocialChannelId == _pageCId));
    }

    [Fact]
    public async Task CreateAcrossPages_EmptyName_ThrowsBeforeTouchingAnyPage()
    {
        await Assert.ThrowsAsync<ArgumentException>(() => _repo.CreateAcrossPagesAsync(new CreateMediaFolderAcrossPagesRequest
        {
            Name = "   ",
            SocialChannelIds = [_pageAId, _pageBId],
        }));

        Assert.False(await _db.MediaFolders.AnyAsync());
    }

    [Fact]
    public async Task CreateAcrossPages_EmptyPageList_Throws()
    {
        await Assert.ThrowsAsync<ArgumentException>(() => _repo.CreateAcrossPagesAsync(new CreateMediaFolderAcrossPagesRequest
        {
            Name = "Campaign X",
            SocialChannelIds = [],
        }));
    }

    [Fact]
    public async Task CreateAcrossPages_ExceedsMaxPages_Throws()
    {
        var tooMany = Enumerable.Range(0, 201).Select(_ => Guid.NewGuid()).ToList();

        await Assert.ThrowsAsync<ArgumentException>(() => _repo.CreateAcrossPagesAsync(new CreateMediaFolderAcrossPagesRequest
        {
            Name = "Campaign X",
            SocialChannelIds = tooMany,
        }));
    }

    [Fact]
    public async Task CreateAcrossPages_DeduplicatesRepeatedPageIds_OnlyCreatesOnce()
    {
        var result = await _repo.CreateAcrossPagesAsync(new CreateMediaFolderAcrossPagesRequest
        {
            Name = "Campaign X",
            SocialChannelIds = [_pageAId, _pageAId],
        });

        Assert.Equal(1, result.TotalRequested);
        Assert.Equal(1, await _db.MediaFolders.CountAsync(f => f.SocialChannelId == _pageAId));
    }
}
