using System.Text.Json;
using Backend.Data;
using Backend.Modules.MediaAsset;
using Backend.Modules.MediaFolder;
using Backend.Modules.Post;
using Backend.Modules.Post.Enums;
using Backend.Modules.SocialChannel;
using Backend.Modules.SocialChannel.Enums;
using Backend.Tests.Modules.MediaFolder;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Backend.Tests.Modules.Post;

public class PostBulkCreateChungChiTests : IDisposable
{
    private readonly SqliteConnection _connection;
    private readonly AppDbContext _db;
    private readonly PostRepository _repo;

    public PostBulkCreateChungChiTests()
    {
        _connection = new SqliteConnection("DataSource=:memory:");
        _connection.Open();

        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlite(_connection)
            .Options;

        _db = new AppDbContext(options);
        _db.Database.EnsureCreated();
        _repo = new PostRepository(_db, new TestUserContext());
    }

    public void Dispose()
    {
        _db.Dispose();
        _connection.Dispose();
    }

    [Fact]
    public async Task BulkCreateChungChiAsync_FansOutQueuedPostsPerIdeaAndChannel()
    {
        var channelA = AddEligiblePage("Page A");
        var channelB = AddEligiblePage("Page B");

        var result = await _repo.BulkCreateChungChiAsync(new BulkCreateChungChiRequest
        {
            Items =
            [
                new BulkChungChiItem { Idea = "Idea 1" },
                new BulkChungChiItem { Idea = "Idea 2" }
            ],
            ChannelIds = [channelA, channelB],
            Mode = ChungChiSelectionMode.Random,
            RandomCount = 3
        });

        Assert.Equal(4, result.Created);
        Assert.Equal(4, result.PostIds.Count);
        Assert.NotEqual(Guid.Empty, result.BatchId);

        var posts = await _db.Posts.Where(p => result.PostIds.Contains(p.Id)).ToListAsync();
        Assert.Equal(4, posts.Count);
        Assert.All(posts, p =>
        {
            Assert.Equal(PostStatus.Queued, p.Status);
            Assert.Equal(GenerationFlow.ChungChiGallery, p.GenerationFlow);
            Assert.Equal(result.BatchId, p.BatchId);
            Assert.Null(p.TextTemplateId);
            Assert.Null(p.ImageTemplateId);
            Assert.Equal(3, p.ImageCount);
            Assert.Contains(p.SocialChannelId, new[] { channelA, channelB });
            Assert.Equal(ChungChiSelectionMode.Random, ReadMode(p.ExtraJson));
            Assert.Null(p.CategoryId);
            // Content is set to idea text during creation, not AI-generated
            Assert.NotNull(p.Content);
            Assert.NotEmpty(p.Content);
        });

        var idea1Posts = posts.Where(p => p.Title == "Idea 1").ToList();
        Assert.Equal(2, idea1Posts.Count);
        Assert.All(idea1Posts, p => Assert.Equal("Idea 1", p.Content));

        var idea2Posts = posts.Where(p => p.Title == "Idea 2").ToList();
        Assert.Equal(2, idea2Posts.Count);
        Assert.All(idea2Posts, p => Assert.Equal("Idea 2", p.Content));

        Assert.Equal(2, posts.Count(p => p.SocialChannelId == channelA));
        Assert.Equal(2, posts.Count(p => p.SocialChannelId == channelB));
    }

    [Fact]
    public async Task BulkCreateChungChiAsync_AllMode_DoesNotPersistRandomCount()
    {
        var result = await _repo.BulkCreateChungChiAsync(new BulkCreateChungChiRequest
        {
            Items = [new BulkChungChiItem { Idea = "All ideas" }],
            ChannelIds = [AddEligiblePage("Page All")],
            Mode = ChungChiSelectionMode.All,
            RandomCount = 9
        });

        var post = await _db.Posts.SingleAsync(p => p.Id == result.PostIds[0]);
        Assert.Equal(GenerationFlow.ChungChiGallery, post.GenerationFlow);
        Assert.Null(post.ImageCount);
        Assert.Equal(ChungChiSelectionMode.All, ReadMode(post.ExtraJson));
    }

    [Fact]
    public async Task BulkCreateChungChiAsync_EmptyIdeas_Throws()
    {
        await Assert.ThrowsAsync<ArgumentException>(() => _repo.BulkCreateChungChiAsync(
            new BulkCreateChungChiRequest
            {
                Items = [new BulkChungChiItem { Idea = "   " }],
                ChannelIds = [Guid.NewGuid()]
            }));
    }

    [Fact]
    public async Task BulkCreateChungChiAsync_WhenAnyPageIsIneligible_RejectsWholeBatch()
    {
        var eligiblePage = AddEligiblePage("Eligible");
        var emptyPage = AddPage("Empty chung_chi");

        var ex = await Assert.ThrowsAsync<ArgumentException>(() => _repo.BulkCreateChungChiAsync(
            new BulkCreateChungChiRequest
            {
                Items = [new BulkChungChiItem { Idea = "Chứng chỉ" }],
                ChannelIds = [eligiblePage, emptyPage]
            }));

        Assert.Contains("chung_chi", ex.Message);
        Assert.Empty(await _db.Posts.ToListAsync());
    }

    private Guid AddEligiblePage(string pageName)
    {
        var pageId = AddPage(pageName);
        var root = new MediaFolderModel
        {
            Id = Guid.NewGuid(), Name = "Root", SocialChannelId = pageId
        };
        var chungChi = new MediaFolderModel
        {
            Id = Guid.NewGuid(), Name = "chung_chi", SocialChannelId = pageId, ParentFolderId = root.Id
        };
        _db.MediaFolders.AddRange(root, chungChi);
        _db.MediaAssets.Add(new MediaAssetModel
        {
            Id = Guid.NewGuid(), FolderId = chungChi.Id, FileName = "certificate.png",
            StoragePath = $"certificates/{pageId}.png", MimeType = "image/png"
        });
        _db.SaveChanges();
        return pageId;
    }

    private Guid AddPage(string pageName)
    {
        var page = new SocialChannelModel
        {
            Id = Guid.NewGuid(), Platform = SocialPlatform.Facebook,
            ChannelType = SocialChannelType.Page, PageName = pageName,
            ExternalPageId = Guid.NewGuid().ToString(), AccessToken = "token", IsActive = true
        };
        _db.SocialChannels.Add(page);
        _db.SaveChanges();
        return page.Id;
    }

    private static ChungChiSelectionMode ReadMode(string? extraJson)
    {
        using var doc = JsonDocument.Parse(extraJson ?? "{}");
        return (ChungChiSelectionMode)doc.RootElement.GetProperty("chungChi").GetProperty("mode").GetInt32();
    }
}
