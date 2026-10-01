using System.Runtime.CompilerServices;
using Backend.Data;
using Backend.Modules.MediaAsset;
using Backend.Modules.MediaCaption;
using Backend.Modules.MediaFolder;
using Backend.Shared.Repositories;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;

namespace Backend.Tests.Modules.MediaCaption;

public sealed class MediaCaptionWorkerTests : IAsyncLifetime
{
    private readonly SqliteConnection _connection = new("DataSource=:memory:");
    private DbContextOptions<AppDbContext> _dbOptions = null!;
    private ServiceProvider _services = null!;

    public async Task InitializeAsync()
    {
        await _connection.OpenAsync();
        _dbOptions = new DbContextOptionsBuilder<AppDbContext>().UseSqlite(_connection).Options;
        await using var db = new AppDbContext(_dbOptions);
        await db.Database.EnsureCreatedAsync();
        _services = new ServiceCollection()
            .AddScoped(_ => new AppDbContext(_dbOptions))
            .AddScoped<MediaCaptionJobService>(sp => new MediaCaptionJobService(
                sp.GetRequiredService<AppDbContext>(),
                new MediaFolderRepository(sp.GetRequiredService<AppDbContext>(), new StubUserContext())))
            .AddScoped<MediaIntelligenceService>(_ =>
                (MediaIntelligenceService)RuntimeHelpers.GetUninitializedObject(typeof(MediaIntelligenceService)))
            .BuildServiceProvider();
    }

    [Fact]
    public async Task RecoverRunningAsync_ReturnsInterruptedItemsToPending()
    {
        var (job, item, _) = await SeedAsync(MediaCaptionJobItemStatus.Running);
        var worker = CreateWorker();

        await worker.RecoverAsync();

        await using var db = new AppDbContext(_dbOptions);
        var recovered = await db.MediaCaptionJobItems.SingleAsync(x => x.Id == item.Id);
        Assert.Equal(MediaCaptionJobItemStatus.Pending, recovered.Status);
        Assert.Null(recovered.StartedAt);
        Assert.Equal(MediaCaptionJobStatus.Running, (await db.MediaCaptionJobs.SingleAsync(x => x.Id == job.Id)).Status);
    }

    [Fact]
    public async Task ProcessOneAsync_FailureDoesNotStopLaterItems_AndReconcilesJob()
    {
        var (job, first, _) = await SeedAsync(MediaCaptionJobItemStatus.Pending);
        var (_, second, _) = await SeedItemAsync(job.Id, MediaCaptionJobItemStatus.Pending, "second.png");
        var (_, third, _) = await SeedItemAsync(job.Id, MediaCaptionJobItemStatus.Pending, "third.png");
        var calls = new List<Guid>();
        var worker = CreateWorker((id, _) =>
        {
            calls.Add(id);
            if (id == second.MediaAssetId) throw new InvalidOperationException("AI unavailable");
            return SaveCaptionAsync(id, "one\ntwo\nthree\nfour\nfive");
        });

        await worker.ProcessUntilIdleAsync();

        await using var db = new AppDbContext(_dbOptions);
        var items = await db.MediaCaptionJobItems.Where(x => x.JobId == job.Id).OrderBy(x => x.CreatedAt).ToListAsync();
        var persistedJob = await db.MediaCaptionJobs.SingleAsync(x => x.Id == job.Id);
        Assert.Equal(new[] { first.MediaAssetId, second.MediaAssetId, third.MediaAssetId }, calls);
        Assert.Equal([MediaCaptionJobItemStatus.Succeeded, MediaCaptionJobItemStatus.Failed, MediaCaptionJobItemStatus.Succeeded], items.Select(x => x.Status));
        Assert.Equal("AI unavailable", items[1].Error);
        Assert.Equal(MediaCaptionJobStatus.Completed, persistedJob.Status);
        Assert.Equal(3, persistedJob.Total);
        Assert.Equal(2, persistedJob.Succeeded);
        Assert.Equal(1, persistedJob.Failed);
        Assert.Equal(0, persistedJob.Skipped);
    }

    [Fact]
    public async Task RetryFailedAsync_CompletedJobReturnsToRunningAndProcessesAgain()
    {
        var (job, item, _) = await SeedAsync(MediaCaptionJobItemStatus.Failed, jobStatus: MediaCaptionJobStatus.Completed);
        await using (var db = new AppDbContext(_dbOptions))
        {
            var service = new MediaCaptionJobService(db, new MediaFolderRepository(db, new StubUserContext()));
            await service.RetryFailedAsync(job.Id);
        }
        var worker = CreateWorker((id, _) => SaveCaptionAsync(id, "one\ntwo\nthree\nfour\nfive"));

        await worker.ProcessUntilIdleAsync();

        await using var verify = new AppDbContext(_dbOptions);
        var retried = await verify.MediaCaptionJobItems.SingleAsync(x => x.Id == item.Id);
        var completed = await verify.MediaCaptionJobs.SingleAsync(x => x.Id == job.Id);
        Assert.Equal(MediaCaptionJobItemStatus.Succeeded, retried.Status);
        Assert.Equal(1, retried.Attempts);
        Assert.Equal(MediaCaptionJobStatus.Completed, completed.Status);
    }

    [Fact]
    public async Task ProcessOneAsync_UserCaptionAddedWhileQueued_SkipsWithoutAiCall()
    {
        var (job, item, asset) = await SeedAsync(MediaCaptionJobItemStatus.Pending);
        await SaveCaptionAsync(asset.Id, "user caption");
        var calls = 0;
        var worker = CreateWorker((_, _) => { calls++; return Task.CompletedTask; });

        await worker.ProcessUntilIdleAsync();

        await using var db = new AppDbContext(_dbOptions);
        Assert.Equal(0, calls);
        Assert.Equal(MediaCaptionJobItemStatus.Skipped, (await db.MediaCaptionJobItems.SingleAsync(x => x.Id == item.Id)).Status);
        Assert.Equal("user caption", (await db.MediaAssets.SingleAsync(x => x.Id == asset.Id)).Caption);
        Assert.Equal(1, (await db.MediaCaptionJobs.SingleAsync(x => x.Id == job.Id)).Skipped);
    }

    [Fact]
    public async Task ProcessOneAsync_ItemSkipAddsToInitialSkippedCount()
    {
        // Job tạo ra đã bỏ qua sẵn 2 ảnh có caption (không có item); sau đó người dùng tự viết
        // caption cho ảnh đang chờ → Skipped phải là 2 + 1, và giữ nguyên qua các lần tính lại.
        var (job, first, firstAsset) = await SeedAsync(MediaCaptionJobItemStatus.Pending, initialSkipped: 2);
        var (_, second, _) = await SeedItemAsync(job.Id, MediaCaptionJobItemStatus.Pending, "second.png");
        await SaveCaptionAsync(firstAsset.Id, "user caption");
        var worker = CreateWorker((id, _) => SaveCaptionAsync(id, "one\ntwo\nthree\nfour\nfive"));

        await worker.ProcessUntilIdleAsync();

        await using var db = new AppDbContext(_dbOptions);
        var persisted = await db.MediaCaptionJobs.SingleAsync(x => x.Id == job.Id);
        Assert.Equal(MediaCaptionJobItemStatus.Skipped, (await db.MediaCaptionJobItems.SingleAsync(x => x.Id == first.Id)).Status);
        Assert.Equal(MediaCaptionJobItemStatus.Succeeded, (await db.MediaCaptionJobItems.SingleAsync(x => x.Id == second.Id)).Status);
        Assert.Equal(3, persisted.Skipped);
        Assert.Equal(1, persisted.Succeeded);
        Assert.Equal(2, persisted.Total);
        Assert.Equal(MediaCaptionJobStatus.Completed, persisted.Status);
    }

    [Fact]
    public async Task ProcessOneAsync_ValidCaptionChangesOnlyCaption_AndCallsAreSequential()
    {
        var (job, first, firstAsset) = await SeedAsync(MediaCaptionJobItemStatus.Pending);
        firstAsset.Tags = "{\"keyword\":\"keep\"}";
        firstAsset.AltText = "keep alt";
        firstAsset.Description = "keep description";
        await SaveAssetAsync(firstAsset);
        var (_, second, _) = await SeedItemAsync(job.Id, MediaCaptionJobItemStatus.Pending, "second.png");
        var active = 0;
        var maxActive = 0;
        var calls = new List<Guid>();
        var worker = CreateWorker(async (id, ct) =>
        {
            calls.Add(id);
            maxActive = Math.Max(maxActive, ++active);
            try
            {
                await Task.Delay(10, ct);
                await SaveCaptionAsync(id, "line 1\nline 2\nline 3\nline 4\nline 5");
            }
            finally { active--; }
        });

        await worker.ProcessUntilIdleAsync();

        await using var db = new AppDbContext(_dbOptions);
        var saved = await db.MediaAssets.SingleAsync(x => x.Id == first.MediaAssetId);
        Assert.Equal(new[] { first.MediaAssetId, second.MediaAssetId }, calls);
        Assert.Equal(1, maxActive);
        Assert.Equal("line 1\nline 2\nline 3\nline 4\nline 5", saved.Caption);
        Assert.Equal("{\"keyword\":\"keep\"}", saved.Tags);
        Assert.Equal("keep alt", saved.AltText);
        Assert.Equal("keep description", saved.Description);
    }

    [Fact]
    public async Task ProcessOneAsync_InvalidCaptionFailurePreservesExistingFields()
    {
        var (_, item, asset) = await SeedAsync(MediaCaptionJobItemStatus.Pending);
        asset.Tags = "tags";
        asset.AltText = "alt";
        asset.Description = "description";
        await SaveAssetAsync(asset);
        var worker = CreateWorker((_, _) => throw new InvalidOperationException("AI không trả đúng 5 dòng caption"));

        await worker.ProcessUntilIdleAsync();

        await using var db = new AppDbContext(_dbOptions);
        var persistedItem = await db.MediaCaptionJobItems.SingleAsync(x => x.Id == item.Id);
        var persistedAsset = await db.MediaAssets.SingleAsync(x => x.Id == asset.Id);
        Assert.Equal(MediaCaptionJobItemStatus.Failed, persistedItem.Status);
        Assert.Equal("AI không trả đúng 5 dòng caption", persistedItem.Error);
        Assert.Null(persistedAsset.Caption);
        Assert.Equal("tags", persistedAsset.Tags);
        Assert.Equal("alt", persistedAsset.AltText);
        Assert.Equal("description", persistedAsset.Description);
    }

    private TestWorker CreateWorker(Func<Guid, CancellationToken, Task>? generate = null) => new(
        _services.GetRequiredService<IServiceScopeFactory>(), generate);

    private async Task<(MediaCaptionJobModel Job, MediaCaptionJobItemModel Item, MediaAssetModel Asset)> SeedAsync(
        MediaCaptionJobItemStatus status, MediaCaptionJobStatus jobStatus = MediaCaptionJobStatus.Running, int initialSkipped = 0)
    {
        await using var db = new AppDbContext(_dbOptions);
        var job = new MediaCaptionJobModel { Id = Guid.NewGuid(), FolderId = Guid.NewGuid(), FolderName = "folder", Status = jobStatus, Total = 1, Skipped = initialSkipped, InitialSkipped = initialSkipped, CreatedAt = DateTime.UtcNow };
        var asset = new MediaAssetModel { Id = Guid.NewGuid(), FileName = "image.png", StoragePath = "image.png", MimeType = "image/png", CreatedAt = DateTime.UtcNow };
        var item = new MediaCaptionJobItemModel { Id = Guid.NewGuid(), JobId = job.Id, MediaAssetId = asset.Id, FileName = asset.FileName, Status = status, CreatedAt = DateTime.UtcNow, StartedAt = status == MediaCaptionJobItemStatus.Running ? DateTime.UtcNow : null };
        db.AddRange(job, asset, item);
        await db.SaveChangesAsync();
        return (job, item, asset);
    }

    private async Task<(MediaCaptionJobModel Job, MediaCaptionJobItemModel Item, MediaAssetModel Asset)> SeedItemAsync(Guid jobId, MediaCaptionJobItemStatus status, string name)
    {
        await using var db = new AppDbContext(_dbOptions);
        var asset = new MediaAssetModel { Id = Guid.NewGuid(), FileName = name, StoragePath = name, MimeType = "image/png", CreatedAt = DateTime.UtcNow };
        var item = new MediaCaptionJobItemModel { Id = Guid.NewGuid(), JobId = jobId, MediaAssetId = asset.Id, FileName = name, Status = status, CreatedAt = DateTime.UtcNow.AddMilliseconds(1) };
        db.AddRange(asset, item);
        await db.SaveChangesAsync();
        var job = await db.MediaCaptionJobs.SingleAsync(x => x.Id == jobId);
        return (job, item, asset);
    }

    private async Task SaveCaptionAsync(Guid id, string caption)
    {
        await using var db = new AppDbContext(_dbOptions);
        (await db.MediaAssets.SingleAsync(x => x.Id == id)).Caption = caption;
        await db.SaveChangesAsync();
    }

    private async Task SaveAssetAsync(MediaAssetModel asset)
    {
        await using var db = new AppDbContext(_dbOptions);
        var persisted = await db.MediaAssets.SingleAsync(x => x.Id == asset.Id);
        persisted.Caption = asset.Caption;
        persisted.Tags = asset.Tags;
        persisted.AltText = asset.AltText;
        persisted.Description = asset.Description;
        await db.SaveChangesAsync();
    }

    public async Task DisposeAsync()
    {
        await _services.DisposeAsync();
        await _connection.DisposeAsync();
    }

    private sealed class TestWorker(IServiceScopeFactory scopeFactory, Func<Guid, CancellationToken, Task>? generate)
        : MediaCaptionWorker(scopeFactory, Options.Create(new MediaCaptionWorkerOptions()), NullLogger<MediaCaptionWorker>.Instance)
    {
        public Task RecoverAsync() => RecoverRunningAsync(CancellationToken.None);
        public async Task ProcessUntilIdleAsync()
        {
            while (true)
            {
                await ProcessOneAsync(CancellationToken.None);
                using var scope = scopeFactory.CreateScope();
                var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
                if (!await db.MediaCaptionJobItems.AnyAsync(x => !x.IsDeleted && x.Status == MediaCaptionJobItemStatus.Pending)) return;
            }
        }

        protected override Task GenerateCaptionAsync(MediaIntelligenceService intelligence, Guid mediaAssetId, CancellationToken ct) =>
            generate?.Invoke(mediaAssetId, ct) ?? Task.CompletedTask;
    }

    private sealed class StubUserContext : IUserContext
    {
        public Guid? GetCurrentUserId() => null;
        public string? GetCurrentUserName() => "test";
        public IReadOnlyList<string> GetCurrentUserRoles() => ["Admin"];
    }
}
