using Backend.Data;
using Backend.Modules.ContentCrawl;
using Backend.Modules.GenerationJob;
using Backend.Modules.GenerationJob.Enums;
using Backend.Modules.MediaAsset;
using Backend.Modules.MediaAsset.Enums;
using Backend.Modules.MediaFolder;
using Backend.Modules.PageContext;
using Backend.Modules.Post;
using Backend.Modules.Post.Enums;
using Backend.Modules.PromptTemplate;
using Backend.Shared.Ai;
using Backend.Shared.SocialPublish;
using Backend.Shared.Storage;
using Backend.Tests.Modules.MediaFolder;
using Microsoft.AspNetCore.Http;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;

namespace Backend.Tests.Modules.GenerationJob;

public sealed class AiImageFolderTests : IDisposable
{
    private readonly SqliteConnection _connection = new("DataSource=:memory:");
    private readonly DbContextOptions<AppDbContext> _options;

    public AiImageFolderTests()
    {
        _connection.Open();
        _options = new DbContextOptionsBuilder<AppDbContext>().UseSqlite(_connection).Options;
        using var db = new AppDbContext(_options);
        db.Database.EnsureCreated();
    }

    public void Dispose() => _connection.Dispose();

    [Fact]
    public async Task SequentialCalls_CreateOnlyOneAiFolder()
    {
        var page = Guid.NewGuid();
        var root = await SeedRootAsync(page);
        var template = Guid.NewGuid();
        await using (var db = NewDb())
        {
            db.MediaFolders.Add(new MediaFolderModel
            {
                Id = template, Name = "template", ParentFolderId = root, SocialChannelId = page
            });
            await db.SaveChangesAsync();
        }

        var first = await NewService().GetOrCreateAiFolderIdAsync(page);
        var second = await NewService().GetOrCreateAiFolderIdAsync(page);

        Assert.Equal(first, second);
        await using var verify = NewDb();
        var ai = await verify.MediaFolders.Where(x => !x.IsDeleted && x.Name == "Ảnh AI").ToListAsync();
        Assert.Single(ai);
        Assert.Equal(root, ai[0].ParentFolderId);
        Assert.Equal(page, ai[0].SocialChannelId);
        Assert.Equal("template", (await verify.MediaFolders.SingleAsync(x => x.Id == template)).Name);
    }

    [Fact]
    public async Task ParallelCalls_CreateOnlyOneAiFolder()
    {
        var page = Guid.NewGuid();
        await SeedRootAsync(page);

        var ids = await Task.WhenAll(
            Task.Run(() => NewService().GetOrCreateAiFolderIdAsync(page)),
            Task.Run(() => NewService().GetOrCreateAiFolderIdAsync(page)));

        Assert.Equal(ids[0], ids[1]);
        await using var verify = NewDb();
        Assert.Equal(1, await verify.MediaFolders.CountAsync(x => !x.IsDeleted && x.Name == "Ảnh AI"));
    }

    [Fact]
    public async Task ExistingUserFolder_IsReused()
    {
        var page = Guid.NewGuid();
        var root = await SeedRootAsync(page);
        var existing = Guid.NewGuid();
        await using (var db = NewDb())
        {
            db.MediaFolders.Add(new MediaFolderModel
            {
                Id = existing, Name = "Ảnh AI", ParentFolderId = root, SocialChannelId = page
            });
            await db.SaveChangesAsync();
        }

        var id = await NewService().GetOrCreateAiFolderIdAsync(page);

        Assert.Equal(existing, id);
    }

    [Fact]
    public async Task MissingRoot_ReturnsNullAndCreatesNothing()
    {
        var page = Guid.NewGuid();

        var id = await NewService().GetOrCreateAiFolderIdAsync(page);

        Assert.Null(id);
        await using var verify = NewDb();
        Assert.Equal(0, await verify.MediaFolders.CountAsync());
    }

    [Fact]
    public async Task Backfill_MovesOnlyUnambiguousAiImages()
    {
        var pageA = Guid.NewGuid();
        var pageB = Guid.NewGuid();
        var rootA = await SeedRootAsync(pageA);
        await SeedRootAsync(pageB);
        var pageWithoutRoot = Guid.NewGuid();

        var tagged = await SeedScenarioAsync(pageA, pageB, pageWithoutRoot, rootA);

        await RunBackfillAsync();
        await AssertScenarioAsync(tagged, pageA, pageB);

        var folderCount = 0;
        await using (var before = NewDb())
            folderCount = await before.MediaFolders.CountAsync(x => x.Name == "Ảnh AI");
        await RunBackfillAsync();
        await AssertScenarioAsync(tagged, pageA, pageB);
        await using var after = NewDb();
        Assert.Equal(folderCount, await after.MediaFolders.CountAsync(x => x.Name == "Ảnh AI"));
    }

    private async Task<Dictionary<string, Guid>> SeedScenarioAsync(
        Guid pageA, Guid pageB, Guid pageWithoutRoot, Guid rootA)
    {
        await using var db = NewDb();
        var ids = new Dictionary<string, Guid>();
        Guid AddAsset(string key, MediaSource source, Guid? folderId, string tags)
        {
            var id = Guid.NewGuid();
            ids[key] = id;
            db.MediaAssets.Add(new MediaAssetModel
            {
                Id = id,
                Source = source,
                FolderId = folderId,
                FileName = key,
                StoragePath = key,
                MimeType = "image/png",
                Tags = tags,
                AltText = "alt",
                Description = "desc",
                Caption = "caption"
            });
            return id;
        }

        void Link(Guid mediaId, Guid pageId, bool deletedLink = false, bool deletedPost = false)
        {
            var postId = Guid.NewGuid();
            db.Posts.Add(new PostModel
            {
                Id = postId,
                SocialChannelId = pageId,
                IsDeleted = deletedPost,
                Title = "post"
            });
            db.PostMedias.Add(new PostMediaModel
            {
                Id = Guid.NewGuid(),
                MediaId = mediaId,
                PostId = postId,
                IsDeleted = deletedLink
            });
        }

        Link(AddAsset("a", MediaSource.AIGenerated, null, "keep"), pageA);
        Link(AddAsset("b", MediaSource.AIGenerated, null, "keep"), pageB);
        AddAsset("orphan", MediaSource.AIGenerated, null, "keep");
        var both = AddAsset("both", MediaSource.AIGenerated, null, "keep");
        Link(both, pageA);
        Link(both, pageB);
        Link(AddAsset("deleted-link", MediaSource.AIGenerated, null, "keep"), pageA, deletedLink: true);
        Link(AddAsset("deleted-post", MediaSource.AIGenerated, null, "keep"), pageA, deletedPost: true);
        var already = AddAsset("already", MediaSource.AIGenerated, rootA, "keep");
        Link(already, pageA);
        Link(AddAsset("upload", MediaSource.Upload, null, "keep"), pageA);
        Link(AddAsset("no-root", MediaSource.AIGenerated, null, "keep"), pageWithoutRoot);
        await db.SaveChangesAsync();
        return ids;
    }

    private async Task AssertScenarioAsync(Dictionary<string, Guid> ids, Guid pageA, Guid pageB)
    {
        await using var db = NewDb();
        async Task<MediaAssetModel> Load(string key)
            => await db.MediaAssets.SingleAsync(x => x.Id == ids[key]);

        var a = await Load("a");
        var b = await Load("b");
        var folderA = await db.MediaFolders.SingleAsync(x => x.Name == "Ảnh AI" && x.SocialChannelId == pageA);
        var folderB = await db.MediaFolders.SingleAsync(x => x.Name == "Ảnh AI" && x.SocialChannelId == pageB);
        Assert.Equal(folderA.Id, a.FolderId);
        Assert.Equal(folderB.Id, b.FolderId);
        Assert.NotEqual(folderA.Id, folderB.Id);

        foreach (var key in new[] { "orphan", "both", "deleted-link", "deleted-post", "upload", "no-root" })
            Assert.Null((await Load(key)).FolderId);

        Assert.NotNull((await Load("already")).FolderId);
        foreach (var asset in await db.MediaAssets.ToListAsync())
        {
            Assert.Equal("keep", asset.Tags);
            Assert.Equal("alt", asset.AltText);
            Assert.Equal("desc", asset.Description);
            Assert.Equal("caption", asset.Caption);
        }
    }

    private async Task RunBackfillAsync()
    {
        var services = new ServiceCollection();
        services.AddScoped(_ => NewDb());
        services.AddScoped<AiImageFolderService>();
        services.AddLogging();
        var provider = services.BuildServiceProvider();
        var service = new AiImageFolderBackfillService(
            provider.GetRequiredService<IServiceScopeFactory>(),
            NullLogger<AiImageFolderBackfillService>.Instance);
        await service.StartAsync(CancellationToken.None);
    }

    [Fact]
    public async Task ProcessImageGeneration_AssignsPageAiFolder_AndKeepsNullWithoutRoot()
    {
        var pageA = Guid.NewGuid();
        var pageB = Guid.NewGuid();
        var rootA = await SeedRootAsync(pageA);
        var rootB = await SeedRootAsync(pageB);
        await using (var seed = NewDb())
        {
            seed.MediaFolders.Add(new MediaFolderModel
            {
                Id = Guid.NewGuid(), Name = "Ảnh AI", ParentFolderId = rootB, SocialChannelId = pageB
            });
            seed.MediaFolders.Add(new MediaFolderModel
            {
                Id = Guid.NewGuid(), Name = "template", ParentFolderId = rootA, SocialChannelId = pageA
            });
            await seed.SaveChangesAsync();
        }

        var postA = await SeedPostAsync(pageA);
        var jobA = await SeedImageJobAsync(postA);
        await using (var db = NewDb())
        {
            var result = await Pipeline(db, new MemoryStorage()).ProcessAsync(jobA);
            Assert.Equal(JobStatus.Completed, result.JobStatus);
            var asset = await db.MediaAssets.SingleAsync(x => x.Id == result.MediaAssetId);
            var folder = await db.MediaFolders.SingleAsync(x => x.Id == asset.FolderId);
            Assert.Equal("Ảnh AI", folder.Name);
            Assert.Equal(rootA, folder.ParentFolderId);
            Assert.Equal(pageA, folder.SocialChannelId);
            Assert.Equal(PostStatus.WaitingReview, (await db.Posts.SingleAsync(x => x.Id == postA)).Status);
            Assert.NotNull(await db.PostMedias.SingleOrDefaultAsync(x => x.PostId == postA && x.MediaId == asset.Id));
            Assert.Equal("template", (await db.MediaFolders.SingleAsync(x => x.Name == "template")).Name);
        }

        var pageNoRoot = Guid.NewGuid();
        var postNoRoot = await SeedPostAsync(pageNoRoot);
        var jobNoRoot = await SeedImageJobAsync(postNoRoot);
        var foldersBefore = 0;
        await using (var before = NewDb())
            foldersBefore = await before.MediaFolders.CountAsync();
        await using (var db = NewDb())
        {
            var result = await Pipeline(db, new MemoryStorage()).ProcessAsync(jobNoRoot);
            Assert.Equal(JobStatus.Completed, result.JobStatus);
            Assert.Null((await db.MediaAssets.SingleAsync(x => x.Id == result.MediaAssetId)).FolderId);
        }
        await using var after = NewDb();
        Assert.Equal(foldersBefore, await after.MediaFolders.CountAsync());

        var postFail = await SeedPostAsync(pageA);
        var jobFail = await SeedImageJobAsync(postFail);
        await using (var db = NewDb())
        {
            var result = await Pipeline(db, new MemoryStorage(), new ThrowingFolderService(Scopes())).ProcessAsync(jobFail);
            Assert.Equal(JobStatus.Completed, result.JobStatus);
            Assert.Null((await db.MediaAssets.SingleAsync(x => x.Id == result.MediaAssetId)).FolderId);
            Assert.Equal(PostStatus.WaitingReview, (await db.Posts.SingleAsync(x => x.Id == postFail)).Status);
            Assert.NotNull(await db.PostMedias.SingleOrDefaultAsync(
                x => x.PostId == postFail && x.MediaId == result.MediaAssetId));
        }
    }

    private async Task<Guid> SeedPostAsync(Guid page)
    {
        await using var db = NewDb();
        var id = Guid.NewGuid();
        db.Posts.Add(new PostModel
        {
            Id = id, SocialChannelId = page, Title = "Bai test", Status = PostStatus.Approved
        });
        await db.SaveChangesAsync();
        return id;
    }

    private async Task<Guid> SeedImageJobAsync(Guid postId)
    {
        await using var db = NewDb();
        var id = Guid.NewGuid();
        db.GenerationJobs.Add(new GenerationJobModel
        {
            Id = id, PostId = postId, JobType = JobType.ImageGeneration, Status = JobStatus.Pending
        });
        await db.SaveChangesAsync();
        return id;
    }

    private GenerationJobPipelineService Pipeline(
        AppDbContext db, IFileStorageService storage, AiImageFolderService? folders = null)
    {
        var user = new TestUserContext();
        return new GenerationJobPipelineService(
            db,
            new PostRepository(db, user),
            new GenerationJobRepository(db, user),
            new MediaAssetRepository(db, user, storage),
            null!,
            new PostMediaRepository(db, user),
            new PageContextRepository(db, user),
            new MediaFolderRepository(db, user),
            new PromptTemplateRepository(db, user),
            storage,
            null!,
            new UnavailableTextAi(),
            new UnavailableImageAi(),
            null!,
            Options.Create(new ContentCrawlOptions()),
            Options.Create(new ReelsOptions()),
            user,
            folders ?? NewService(),
            NullLogger<GenerationJobPipelineService>.Instance);
    }

    private sealed class ThrowingFolderService(IServiceScopeFactory scopes)
        : AiImageFolderService(scopes, NullLogger<AiImageFolderService>.Instance)
    {
        public override Task<Guid?> GetOrCreateAiFolderIdAsync(Guid socialChannelId, CancellationToken ct = default)
            => throw new InvalidOperationException("folder failed");
    }

    private sealed class UnavailableTextAi : IAiTextGenerationService
    {
        public bool IsAvailable(string? provider = null) => false;
        public Task<AiTextGenerationResult> GenerateAsync(AiTextGenerationRequest request, CancellationToken ct = default)
            => throw new NotSupportedException();
        public Task<List<string>> SuggestIdeasAsync(string topic, int count, string? category, CancellationToken ct = default)
            => throw new NotSupportedException();
        public Task<string> ComposeImagePromptAsync(AiImagePromptRequest request, CancellationToken ct = default)
            => throw new NotSupportedException();
    }

    private sealed class UnavailableImageAi : IAiImageGenerationService
    {
        public bool IsAvailable(string? provider = null) => false;
        public Task<AiImageGenerationResult> GenerateAsync(AiImageGenerationRequest request, CancellationToken ct = default)
            => throw new NotSupportedException();
    }

    private sealed class MemoryStorage : IFileStorageService
    {
        public Task<FileSaveResult> SaveAsync(IFormFile file, string folder, CancellationToken ct = default)
            => throw new NotSupportedException();
        public Task<FileSaveResult> SaveBytesAsync(byte[] data, string folder, string extension, string contentType, CancellationToken ct = default)
            => Task.FromResult(new FileSaveResult
            {
                StorageKey = $"{folder}/a{extension}",
                OriginalFileName = "a.png",
                ContentType = contentType,
                SizeBytes = data.Length
            });
        public Task<Stream> OpenReadAsync(string storageKey, CancellationToken ct = default)
            => Task.FromResult<Stream>(new MemoryStream([1, 2, 3]));
        public Task<bool> ExistsAsync(string storageKey, CancellationToken ct = default) => Task.FromResult(true);
        public Task DeleteAsync(string storageKey, CancellationToken ct = default) => Task.CompletedTask;
    }

    private async Task<Guid> SeedRootAsync(Guid page)
    {
        await using var db = NewDb();
        var id = Guid.NewGuid();
        db.MediaFolders.Add(new MediaFolderModel
        {
            Id = id, Name = "root", SocialChannelId = page, ParentFolderId = null
        });
        await db.SaveChangesAsync();
        return id;
    }

    private AppDbContext NewDb() => new(_options);

    private AiImageFolderService NewService() => new(Scopes(), NullLogger<AiImageFolderService>.Instance);

    private IServiceScopeFactory Scopes()
    {
        var services = new ServiceCollection();
        services.AddScoped(_ => NewDb());
        return services.BuildServiceProvider().GetRequiredService<IServiceScopeFactory>();
    }
}
