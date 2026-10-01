using System.Data.Common;
using Backend.Data;
using Backend.Modules.MediaAsset;
using Backend.Modules.MediaCaption;
using Backend.Modules.MediaFolder;
using Backend.Tests.Modules.MediaAsset;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;

namespace Backend.Tests.Modules.MediaCaption;

public sealed class MediaCaptionDeadlineTests : IAsyncLifetime
{
    private readonly SqliteConnection _connection = new("DataSource=:memory:");
    private DbContextOptions<AppDbContext> _dbOptions = null!;

    private sealed class HangSaveInterceptor : SaveChangesInterceptor
    {
        public readonly TaskCompletionSource Arrived = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public readonly TaskCompletionSource Proceed = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public override async ValueTask<InterceptionResult<int>> SavingChangesAsync(
            DbContextEventData eventData,
            InterceptionResult<int> result,
            CancellationToken cancellationToken = default)
        {
            // Set flag that we reached SaveChangesAsync
            Arrived.TrySetResult();
            
            // Wait until the test tells us to proceed
            await Proceed.Task.WaitAsync(cancellationToken);
            
            return await base.SavingChangesAsync(eventData, result, cancellationToken);
        }
    }

    public async Task InitializeAsync()
    {
        await _connection.OpenAsync();
    }

    public async Task DisposeAsync()
    {
        await _connection.CloseAsync();
        _connection.Dispose();
    }

    [Fact]
    public async Task GenerateCaptionAsync_DeadlineExpiresExactlyDuringSaveChanges_CommitsSuccessfullyAndReturnsCaption()
    {
        var interceptor = new HangSaveInterceptor();
        _dbOptions = new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlite(_connection)
            .AddInterceptors(interceptor)
            .Options;

        var assetId = Guid.NewGuid();
        var folderId = Guid.NewGuid();

        // 1. Setup DB directly without interceptor delays
        await using (var setupDb = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>().UseSqlite(_connection).Options))
        {
            await setupDb.Database.EnsureCreatedAsync();
            setupDb.MediaFolders.Add(new MediaFolderModel { Id = folderId, Name = "Folder" });
            setupDb.MediaAssets.Add(new MediaAssetModel
            {
                Id = assetId,
                FileName = "test.jpg",
                StoragePath = "test.jpg",
                FileSize = 100,
                MimeType = "image/jpeg",
                FolderId = folderId
            });
            await setupDb.SaveChangesAsync();
        }

        // 2. Mock AI HTTP call to return instantly
        var handler = new GatedChatHandler("{\"lines\":[\"Line 1\", \"Line 2\", \"Line 3\", \"Line 4\", \"Line 5\"]}");
        handler.Release(0);

        // 3. Configure short deadline
        var timeouts = new MediaAiTimeouts(
            CaptionRequest: TimeSpan.FromMilliseconds(500),
            AnalysisRequest: MediaAiTimeouts.Default.AnalysisRequest,
            CaptionPreparationAllowance: TimeSpan.FromMilliseconds(500));

        var intelligence = new MediaIntelligenceService(
            new HttpClient(handler),
            new AppDbContext(_dbOptions),
            new InMemoryImageStorage(),
            CaptionAiOptions.Create(),
            NullLogger<MediaIntelligenceService>.Instance)
        {
            Timeouts = timeouts
        };

        // 4. Start GenerateCaptionAsync
        var task = intelligence.GenerateCaptionAsync(assetId);

        // 5. Wait for it to hit SaveChangesAsync
        var arrivedTask = interceptor.Arrived.Task;
        var completed = await Task.WhenAny(arrivedTask, task);
        if (completed == task) 
        {
            await task; // This will throw if the task failed
        }
        await arrivedTask.WaitAsync(TimeSpan.FromSeconds(5));

        // 6. At this point, it is blocked inside SaveChangesAsync.
        // We wait for the deadline to expire (CaptionMaxDuration is 1000ms total, wait 1100ms to be sure)
        await Task.Delay(1100);

        // 7. Unblock SaveChangesAsync
        interceptor.Proceed.TrySetResult();

        // 8. It should complete successfully instead of throwing TimeoutException
        var media = await task.WaitAsync(TimeSpan.FromSeconds(5));

        var expectedCaption = "Line 1\nLine 2\nLine 3\nLine 4\nLine 5";
        Assert.Equal(expectedCaption, media.Caption);

        // 9. Verify in DB
        await using (var verifyDb = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>().UseSqlite(_connection).Options))
        {
            var inDb = await verifyDb.MediaAssets.SingleAsync(x => x.Id == assetId);
            Assert.Equal(expectedCaption, inDb.Caption);
        }
    }

    [Fact]
    public async Task GenerateCaptionAsync_SaveExceedsItsOwnLimit_ReportsUnknownOutcomeNotUnchanged()
    {
        var interceptor = new HangSaveInterceptor();
        _dbOptions = new DbContextOptionsBuilder<AppDbContext>().UseSqlite(_connection).AddInterceptors(interceptor).Options;
        var assetId = await SeedAsync();
        var handler = new GatedChatHandler("{\"lines\":[\"Line 1\", \"Line 2\", \"Line 3\", \"Line 4\", \"Line 5\"]}");
        handler.Release(0);
        var timeouts = MediaAiTimeouts.Default with { CaptionSave = TimeSpan.FromMilliseconds(200) };
        await using var db = new AppDbContext(_dbOptions);
        var intelligence = new MediaIntelligenceService(new HttpClient(handler), db, new InMemoryImageStorage(),
            CaptionAiOptions.Create(), NullLogger<MediaIntelligenceService>.Instance) { Timeouts = timeouts };

        var ex = await Assert.ThrowsAsync<TimeoutException>(
            () => intelligence.GenerateCaptionAsync(assetId).WaitAsync(TimeSpan.FromSeconds(10)));

        Assert.DoesNotContain("caption không đổi", ex.Message);
        Assert.Contains("không xác nhận được caption đã được lưu", ex.Message);
        await using var verify = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>().UseSqlite(_connection).Options);
        Assert.Null((await verify.MediaAssets.SingleAsync(x => x.Id == assetId)).Caption);
    }

    [Fact]
    public void WorkerDefaultWaitCoversTheLongestMarkerHoldIncludingSave()
    {
        var defaults = MediaAiTimeouts.Default;
        var wait = new DefaultWaitProbe().Wait;

        Assert.True(defaults.CaptionSave > TimeSpan.Zero);
        Assert.Equal(defaults.CaptionMaxDuration + defaults.CaptionSave, defaults.CaptionMaxMarkerHold);
        // Sau thời gian giữ dấu tối đa (deadline + lưu) worker vẫn phải còn một khoảng đệm > 0.
        Assert.True(MediaCaptionWorker.InFlightReleaseBuffer > TimeSpan.Zero);
        Assert.True(wait >= defaults.CaptionMaxMarkerHold + MediaCaptionWorker.InFlightReleaseBuffer,
            $"InFlightWait {wait} < max marker hold {defaults.CaptionMaxMarkerHold} (deadline {defaults.CaptionMaxDuration} + save {defaults.CaptionSave}) + release buffer {MediaCaptionWorker.InFlightReleaseBuffer}");
    }

    [Fact]
    public void WorkerWaitUsesInjectedTimeoutConfiguration()
    {
        var timeouts = MediaAiTimeouts.Default with
        {
            CaptionRequest = TimeSpan.FromSeconds(7),
            CaptionPreparationAllowance = TimeSpan.FromSeconds(3),
            CaptionSave = TimeSpan.FromSeconds(2)
        };

        var wait = new TimeoutWaitProbe(timeouts).Wait;

        Assert.Equal(
            timeouts.CaptionMaxMarkerHold + MediaCaptionWorker.InFlightReleaseBuffer,
            wait);
    }

    private async Task<Guid> SeedAsync()
    {
        await using var setupDb = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>().UseSqlite(_connection).Options);
        await setupDb.Database.EnsureCreatedAsync();
        var asset = new MediaAssetModel
        {
            Id = Guid.NewGuid(), FileName = "test.jpg", StoragePath = "test.jpg", FileSize = 100, MimeType = "image/jpeg"
        };
        setupDb.MediaAssets.Add(asset);
        await setupDb.SaveChangesAsync();
        return asset.Id;
    }

    private sealed class TimeoutWaitProbe(MediaAiTimeouts timeouts) : MediaCaptionWorker(
        new ServiceCollection().BuildServiceProvider().GetRequiredService<IServiceScopeFactory>(),
        Options.Create(new MediaCaptionWorkerOptions()), NullLogger<MediaCaptionWorker>.Instance, timeouts)
    {
        public TimeSpan Wait => InFlightWait;
    }

    private sealed class DefaultWaitProbe() : MediaCaptionWorker(
        new ServiceCollection().BuildServiceProvider().GetRequiredService<IServiceScopeFactory>(),
        Options.Create(new MediaCaptionWorkerOptions()), NullLogger<MediaCaptionWorker>.Instance)
    {
        public TimeSpan Wait => InFlightWait;
    }
}
