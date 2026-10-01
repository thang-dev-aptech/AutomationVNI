using Backend.Data;
using Backend.Modules.GoogleDrive;
using Backend.Modules.MediaAsset;
using Backend.Modules.MediaCaption;
using Backend.Modules.MediaFolder;
using Backend.Shared.Repositories;
using Backend.Tests.Modules.MediaAsset;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;

namespace Backend.Tests.Modules.MediaCaption;

/// <summary>R-024 B1: single generate-caption đang chạy → job được tạo → worker cũng gọi AI.
/// Caption người dùng ghi trước phải được giữ, item của worker thành Skipped.</summary>
public sealed class MediaCaptionRaceTests : IAsyncLifetime
{
    private const string UserCaption = "u1\nu2\nu3\nu4\nu5";
    private const string WorkerCaption = "w1\nw2\nw3\nw4\nw5";

    private readonly SqliteConnection _connection = new("DataSource=:memory:");
    private DbContextOptions<AppDbContext> _dbOptions = null!;

    public async Task InitializeAsync()
    {
        await _connection.OpenAsync();
        _dbOptions = new DbContextOptionsBuilder<AppDbContext>().UseSqlite(_connection).Options;
        await using var db = new AppDbContext(_dbOptions);
        await db.Database.EnsureCreatedAsync();
    }

    public async Task DisposeAsync() => await _connection.DisposeAsync();

    [Fact]
    public async Task WorkerDoesNotOverwriteCaptionWrittenBySingleGenerateWhileBothAiCallsAreInFlight()
    {
        var (folderId, assetId) = await SeedAsync();
        var handler = new GatedChatHandler(
            ScriptedChatHandler.Lines(UserCaption.Split('\n')),
            ScriptedChatHandler.Lines(WorkerCaption.Split('\n')));

        await using var userDb = new AppDbContext(_dbOptions);
        var userService = CreateIntelligence(userDb, handler);
        var userTask = userService.GenerateCaptionAsync(assetId);
        await handler.Arrived(0).WaitAsync(TimeSpan.FromSeconds(10));

        await using (var jobDb = new AppDbContext(_dbOptions))
        {
            var jobs = new MediaCaptionJobService(jobDb, new MediaFolderRepository(jobDb, new StubUserContext()));
            Assert.Equal(1, (await jobs.CreateAsync(folderId)).Total);
        }

        var services = new ServiceCollection()
            .AddScoped(_ => new AppDbContext(_dbOptions))
            .AddScoped(sp => new MediaCaptionJobService(
                sp.GetRequiredService<AppDbContext>(),
                new MediaFolderRepository(sp.GetRequiredService<AppDbContext>(), new StubUserContext())))
            .AddScoped(sp => CreateIntelligence(sp.GetRequiredService<AppDbContext>(), handler))
            .BuildServiceProvider();
        var worker = new RaceWorker(services.GetRequiredService<IServiceScopeFactory>());
        var workerTask = worker.RunOnceAsync();
        await handler.Arrived(1).WaitAsync(TimeSpan.FromSeconds(10));

        handler.Release(0);
        await userTask.WaitAsync(TimeSpan.FromSeconds(10));
        handler.Release(1);
        await workerTask.WaitAsync(TimeSpan.FromSeconds(10));

        await using var db = new AppDbContext(_dbOptions);
        Assert.Equal(UserCaption, (await db.MediaAssets.SingleAsync(x => x.Id == assetId)).Caption);
        var item = await db.MediaCaptionJobItems.SingleAsync();
        Assert.Equal(MediaCaptionJobItemStatus.Skipped, item.Status);
        var job = await db.MediaCaptionJobs.SingleAsync();
        Assert.Equal(0, job.Succeeded);
        Assert.Equal(1, job.Skipped);
        Assert.Equal(MediaCaptionJobStatus.Completed, job.Status);
        Assert.Equal(2, handler.RequestCount);
    }

    [Fact]
    public async Task WorkerStillWritesCaptionWhenNobodyElseDid()
    {
        var (folderId, assetId) = await SeedAsync();
        var handler = new GatedChatHandler(ScriptedChatHandler.Lines(WorkerCaption.Split('\n')));
        handler.Release(0);
        await using (var jobDb = new AppDbContext(_dbOptions))
            await new MediaCaptionJobService(jobDb, new MediaFolderRepository(jobDb, new StubUserContext())).CreateAsync(folderId);
        var services = new ServiceCollection()
            .AddScoped(_ => new AppDbContext(_dbOptions))
            .AddScoped(sp => new MediaCaptionJobService(
                sp.GetRequiredService<AppDbContext>(),
                new MediaFolderRepository(sp.GetRequiredService<AppDbContext>(), new StubUserContext())))
            .AddScoped(sp => CreateIntelligence(sp.GetRequiredService<AppDbContext>(), handler))
            .BuildServiceProvider();

        await new RaceWorker(services.GetRequiredService<IServiceScopeFactory>()).RunOnceAsync();

        await using var db = new AppDbContext(_dbOptions);
        Assert.Equal(WorkerCaption, (await db.MediaAssets.SingleAsync(x => x.Id == assetId)).Caption);
        Assert.Equal(MediaCaptionJobItemStatus.Succeeded, (await db.MediaCaptionJobItems.SingleAsync()).Status);
    }

    private static MediaIntelligenceService CreateIntelligence(AppDbContext db, HttpMessageHandler handler) => new(
        new HttpClient(handler), db, new InMemoryImageStorage(), CaptionAiOptions.Create(),
        NullLogger<MediaIntelligenceService>.Instance);

    private async Task<(Guid FolderId, Guid AssetId)> SeedAsync()
    {
        await using var db = new AppDbContext(_dbOptions);
        var root = new MediaFolderModel { Id = Guid.NewGuid(), Name = "Google Drive" };
        var folder = new MediaFolderModel { Id = Guid.NewGuid(), Name = "Child", ParentFolderId = root.Id };
        var asset = new MediaAssetModel
        {
            Id = Guid.NewGuid(), FolderId = folder.Id, FileName = "a.png", StoragePath = "a.png",
            MimeType = "image/png", CreatedAt = DateTime.UtcNow
        };
        db.AddRange(root, folder, asset);
        (await db.GoogleDriveSyncStates.SingleAsync(x => x.Id == GoogleDriveSyncStateModel.SingletonId))
            .DedicatedFolderId = root.Id;
        await db.SaveChangesAsync();
        return (folder.Id, asset.Id);
    }

    private sealed class RaceWorker(IServiceScopeFactory scopeFactory)
        : MediaCaptionWorker(scopeFactory, Options.Create(new MediaCaptionWorkerOptions()), NullLogger<MediaCaptionWorker>.Instance)
    {
        public Task RunOnceAsync() => ProcessOneAsync(CancellationToken.None);
    }

    private sealed class StubUserContext : IUserContext
    {
        public Guid? GetCurrentUserId() => null;
        public string? GetCurrentUserName() => "test";
        public IReadOnlyList<string> GetCurrentUserRoles() => ["Admin"];
    }
}
