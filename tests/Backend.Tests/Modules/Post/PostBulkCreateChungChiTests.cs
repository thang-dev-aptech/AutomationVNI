using System.Text.Json;
using Backend.Data;
using Backend.Modules.Post;
using Backend.Modules.Post.Enums;
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
        var channelA = Guid.NewGuid();
        var channelB = Guid.NewGuid();
        var categoryId = Guid.NewGuid();

        var result = await _repo.BulkCreateChungChiAsync(new BulkCreateChungChiRequest
        {
            Items =
            [
                new BulkChungChiItem { Idea = "Idea 1", CategoryId = categoryId },
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
        });

        Assert.Equal(2, posts.Count(p => p.Title == "Idea 1" && p.CategoryId == categoryId));
        Assert.Equal(2, posts.Count(p => p.Title == "Idea 2" && p.CategoryId is null));
        Assert.Equal(2, posts.Count(p => p.SocialChannelId == channelA));
        Assert.Equal(2, posts.Count(p => p.SocialChannelId == channelB));
    }

    [Fact]
    public async Task BulkCreateChungChiAsync_AllMode_DoesNotPersistRandomCount()
    {
        var result = await _repo.BulkCreateChungChiAsync(new BulkCreateChungChiRequest
        {
            Items = [new BulkChungChiItem { Idea = "All ideas" }],
            ChannelIds = [Guid.NewGuid()],
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

    private static ChungChiSelectionMode ReadMode(string? extraJson)
    {
        using var doc = JsonDocument.Parse(extraJson ?? "{}");
        return (ChungChiSelectionMode)doc.RootElement.GetProperty("chungChi").GetProperty("mode").GetInt32();
    }
}
