using Backend.Data;
using Backend.Modules.GoogleDrive;
using Backend.Modules.MediaAsset;
using Backend.Modules.MediaFolder;
using Backend.Shared;
using Backend.Shared.Repositories;
using Backend.Shared.Storage;
using Microsoft.AspNetCore.Http;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;

namespace Backend.Tests.Modules.GoogleDrive;

/// <summary>
/// GDRIVE-01 (t3): hành vi của GoogleDriveImportWorker chạy trên một IGoogleDriveClient fake —
/// gdrive-toggle-test, gdrive-cursor-dedup-test, gdrive-zip-rejected-test, gdrive-bounded-retry-test.
/// </summary>
public class GoogleDriveImportWorkerTests
{
    [Fact]
    public async Task RunTickAsync_SkipsAllPhasesWhenDisabled_ThenResumesWithoutRestart()
    {
        await using var fixture = await Fixture.CreateAsync();
        var worker = fixture.CreateWorker();

        await fixture.SetEnabledAsync(false);
        await worker.RunSingleTickAsync(fixture.Settings);
        await worker.RunSingleTickAsync(fixture.Settings);

        Assert.Equal(0, fixture.Client.ListChangesCalls);
        Assert.Empty(fixture.Client.DownloadedFileIds);

        await fixture.SetEnabledAsync(true);
        fixture.Client.Pages.Enqueue(new GoogleDriveChangesPage { NextPageToken = "after-enable" });
        await worker.RunSingleTickAsync(fixture.Settings);

        Assert.True(fixture.Client.ListChangesCalls > 0);
        Assert.Equal("after-enable", (await fixture.GetStateAsync()).PageToken);
    }

    [Fact]
    public async Task RunTickAsync_AdvancesPageTokenOnEmptyPage_AndSkipsAlreadyImportedFile()
    {
        await using var fixture = await Fixture.CreateAsync();
        var worker = fixture.CreateWorker();
        await fixture.SetEnabledAsync(true);

        // Seed: file đã import từ trước.
        var existing = await fixture.CreateExistingAssetAsync("existing-1");

        fixture.Client.Pages.Enqueue(new GoogleDriveChangesPage
        {
            NextPageToken = "token-2",
            Files =
            [
                new GoogleDriveFileInfo { FileId = "existing-1", Name = "old.jpg", MimeType = "image/jpeg", SizeBytes = 10 },
                new GoogleDriveFileInfo { FileId = "new-1", Name = "new.jpg", MimeType = "image/jpeg", SizeBytes = 10 },
            ],
        });

        await worker.RunSingleTickAsync(fixture.Settings);

        Assert.DoesNotContain("existing-1", fixture.Client.DownloadedFileIds);
        Assert.Contains("new-1", fixture.Client.DownloadedFileIds);
        Assert.Equal("token-2", (await fixture.GetStateAsync()).PageToken);
        // 1 asset đã seed sẵn (existing-1) + 1 asset mới nhập (new-1) = 2. existing-1 KHÔNG được
        // tải/nhập lại — chỉ new-1 mới tạo thêm MediaAsset.
        Assert.Equal(2, await fixture.CountMediaAssetsAsync());

        // Lượt kế tiếp: danh sách rỗng — PageToken vẫn phải advance.
        fixture.Client.Pages.Enqueue(new GoogleDriveChangesPage { NextPageToken = "token-3", Files = [] });
        await worker.RunSingleTickAsync(fixture.Settings);

        Assert.Equal("token-3", (await fixture.GetStateAsync()).PageToken);
        // Không có file google-apps/mới nào lọt qua — tổng số MediaAsset không đổi.
        Assert.Equal(2, await fixture.CountMediaAssetsAsync());
        Assert.NotEqual(Guid.Empty, existing.Id);
    }

    [Fact]
    public async Task RunTickAsync_RejectsZipAtExtensionValidation_NoAssetNoFailureRow()
    {
        await using var fixture = await Fixture.CreateAsync();
        var worker = fixture.CreateWorker();
        await fixture.SetEnabledAsync(true);

        fixture.Client.Pages.Enqueue(new GoogleDriveChangesPage
        {
            NextPageToken = "token-2",
            Files = [new GoogleDriveFileInfo { FileId = "zip-1", Name = "malware.zip", MimeType = "application/zip", SizeBytes = 10 }],
        });

        await worker.RunSingleTickAsync(fixture.Settings);

        Assert.DoesNotContain("zip-1", fixture.Client.DownloadedFileIds);
        Assert.Equal(0, await fixture.CountMediaAssetsAsync());
        Assert.Empty(await fixture.GetRetryableFailuresAsync());
    }

    [Fact]
    public async Task RunTickAsync_BoundedRetry_StopsAppearingAfterMaxAttempts()
    {
        await using var fixture = await Fixture.CreateAsync(maxRetryAttempts: 3);
        var worker = fixture.CreateWorker();
        await fixture.SetEnabledAsync(true);

        const string fileId = "flaky-1";
        fixture.Client.FailDownload(fileId, times: 10);

        // Tick 1: file "mới" xuất hiện qua ListChangesAsync, tải lỗi -> AttemptCount=1.
        fixture.Client.Pages.Enqueue(new GoogleDriveChangesPage
        {
            NextPageToken = "t2",
            Files = [new GoogleDriveFileInfo { FileId = fileId, Name = "flaky.jpg", MimeType = "image/jpeg", SizeBytes = 10 }],
        });
        await worker.RunSingleTickAsync(fixture.Settings);
        var failures = await fixture.GetRetryableFailuresAsync();
        Assert.Single(failures);
        Assert.Equal(1, failures[0].AttemptCount);

        // Tick 2 & 3: không còn "mới" (đã tiêu thụ), pha retry thử lại và vẫn lỗi -> AttemptCount 2 rồi 3.
        fixture.Client.Pages.Enqueue(new GoogleDriveChangesPage { NextPageToken = "t3", Files = [] });
        await worker.RunSingleTickAsync(fixture.Settings);
        Assert.Equal(2, (await fixture.GetRetryableFailuresAsync()).Single().AttemptCount);

        fixture.Client.Pages.Enqueue(new GoogleDriveChangesPage { NextPageToken = "t4", Files = [] });
        await worker.RunSingleTickAsync(fixture.Settings);
        Assert.Equal(3, (await fixture.GetFailureAsync(fileId))!.AttemptCount);

        // Đạt MaxRetryAttempts=3: không còn được chọn để thử lại nữa, nhưng dòng vẫn còn trong DB.
        Assert.Empty(await fixture.GetRetryableFailuresAsync());
        Assert.NotNull(await fixture.GetFailureAsync(fileId));
    }

    [Fact]
    public async Task RunTickAsync_BoundedRetry_SuccessMidwayDeletesFailureAndCreatesAsset()
    {
        await using var fixture = await Fixture.CreateAsync(maxRetryAttempts: 5);
        var worker = fixture.CreateWorker();
        await fixture.SetEnabledAsync(true);

        const string fileId = "flaky-2";
        fixture.Client.FailDownload(fileId, times: 2); // lỗi 2 lần đầu, lần thứ 3 thành công.

        fixture.Client.Pages.Enqueue(new GoogleDriveChangesPage
        {
            NextPageToken = "t2",
            Files = [new GoogleDriveFileInfo { FileId = fileId, Name = "flaky2.jpg", MimeType = "image/jpeg", SizeBytes = 10 }],
        });
        await worker.RunSingleTickAsync(fixture.Settings); // AttemptCount=1
        Assert.Equal(1, (await fixture.GetFailureAsync(fileId))!.AttemptCount);

        fixture.Client.Pages.Enqueue(new GoogleDriveChangesPage { NextPageToken = "t3", Files = [] });
        await worker.RunSingleTickAsync(fixture.Settings); // AttemptCount=2
        Assert.Equal(2, (await fixture.GetFailureAsync(fileId))!.AttemptCount);

        fixture.Client.Pages.Enqueue(new GoogleDriveChangesPage { NextPageToken = "t4", Files = [] });
        await worker.RunSingleTickAsync(fixture.Settings); // thành công -> xoá dòng + tạo MediaAsset

        Assert.Null(await fixture.GetFailureAsync(fileId));
        Assert.Equal(1, await fixture.CountMediaAssetsAsync());
    }

    /// <summary>
    /// AC gdrive-no-file-loss-when-page-exceeds-cap (9382d03b): trước bản sửa t7, worker tự
    /// Take(MaxFilesPerTick) SAU KHI đã nhận nguyên trang từ ListChangesAsync, rồi vẫn advance
    /// PageToken theo trang ĐẦY ĐỦ — phần bị cắt bỏ mất vĩnh viễn. Giờ việc giới hạn xảy ra ở
    /// tầng gọi API (maxResults truyền vào ListChangesAsync), nên PageToken luôn khớp đúng với
    /// phần đã xử lý — backlog nhiều hơn 1 trang phải được xử lý HẾT qua nhiều tick, không rơi
    /// file nào.
    /// </summary>
    [Fact]
    public async Task RunTickAsync_BacklogLargerThanMaxFilesPerTick_ProcessesEveryFileAcrossTicks()
    {
        await using var fixture = await Fixture.CreateAsync(maxFilesPerTick: 20);
        var worker = fixture.CreateWorker();
        await fixture.SetEnabledAsync(true);

        const int backlogSize = 45; // > 2x MaxFilesPerTick (20) để buộc phải trải qua >= 3 tick.
        var expectedFileIds = new List<string>();
        for (var i = 0; i < backlogSize; i++)
        {
            var fileId = $"backlog-{i}";
            expectedFileIds.Add(fileId);
            fixture.Client.Backlog.Enqueue(new GoogleDriveFileInfo
            {
                FileId = fileId,
                Name = $"backlog-{i}.jpg",
                MimeType = "image/jpeg",
                SizeBytes = 10,
            });
        }

        // 3 tick là đủ để rút cạn 45 file ở mức trần 20 file/tick (20 + 20 + 5); tick thứ 4 xác
        // nhận trạng thái ổn định — không có gì mất, không có gì lặp lại.
        await worker.RunSingleTickAsync(fixture.Settings);
        await worker.RunSingleTickAsync(fixture.Settings);
        await worker.RunSingleTickAsync(fixture.Settings);
        await worker.RunSingleTickAsync(fixture.Settings);

        Assert.Equal(backlogSize, await fixture.CountMediaAssetsAsync());
        Assert.Equal(expectedFileIds.OrderBy(x => x), fixture.Client.DownloadedFileIds.OrderBy(x => x));
        // Mỗi lượt gọi Drive phải xin đúng maxResults = MaxFilesPerTick — việc giới hạn xảy ra ở
        // tầng gọi API, không phải Take() sau khi nhận về nguyên trang.
        Assert.All(fixture.Client.MaxResultsSeen, n => Assert.Equal(20, n));
        Assert.Empty(await fixture.GetRetryableFailuresAsync());
    }

    private sealed class Fixture : IAsyncDisposable
    {
        private readonly SqliteConnection _connection;
        private readonly ServiceProvider _services;

        public DbContextOptions<AppDbContext> DbOptions { get; }
        public FakeGoogleDriveClient Client { get; }
        public GoogleDriveOptions Settings { get; }
        public FileStorageOptions FileStorageSettings { get; }

        private Fixture(
            SqliteConnection connection,
            DbContextOptions<AppDbContext> dbOptions,
            FakeGoogleDriveClient client,
            GoogleDriveOptions settings,
            FileStorageOptions fileStorageOptions,
            ServiceProvider services)
        {
            _connection = connection;
            DbOptions = dbOptions;
            Client = client;
            Settings = settings;
            FileStorageSettings = fileStorageOptions;
            _services = services;
        }

        public static async Task<Fixture> CreateAsync(int maxRetryAttempts = 5, int maxFilesPerTick = 20)
        {
            var connection = new SqliteConnection("DataSource=:memory:");
            await connection.OpenAsync();
            var dbOptions = new DbContextOptionsBuilder<AppDbContext>().UseSqlite(connection).Options;

            await using (var db = new AppDbContext(dbOptions))
                await db.Database.EnsureCreatedAsync();

            var client = new FakeGoogleDriveClient();
            var gdriveOptions = new GoogleDriveOptions
            {
                Enabled = true,
                IntervalSeconds = 180,
                MaxFilesPerTick = maxFilesPerTick,
                MaxRetryAttempts = maxRetryAttempts,
            };
            var fileStorageOptions = new FileStorageOptions();
            var fileStorage = new RecordingFileStorageService();

            var services = new ServiceCollection()
                .AddSingleton(dbOptions)
                .AddScoped(_ => new AppDbContext(dbOptions))
                .AddScoped<IUserContext, StubUserContext>()
                .AddScoped<MediaFolderRepository>()
                .AddSingleton<IFileStorageService>(fileStorage)
                .AddScoped<MediaAssetRepository>()
                .AddScoped<Backend.Modules.GoogleDrive.GoogleDriveRepository>()
                .AddSingleton<Backend.Modules.GoogleDrive.IGoogleDriveClient>(client)
                .AddSingleton<IOptions<Backend.Modules.GoogleDrive.GoogleDriveOptions>>(Options.Create(gdriveOptions))
                .AddSingleton<IOptions<FileStorageOptions>>(Options.Create(fileStorageOptions))
                .AddLogging()
                .BuildServiceProvider();

            return new Fixture(connection, dbOptions, client, gdriveOptions, fileStorageOptions, services);
        }

        public TestableWorker CreateWorker() => new(
            _services.GetRequiredService<IServiceScopeFactory>(),
            _services.GetRequiredService<IOptions<Backend.Modules.GoogleDrive.GoogleDriveOptions>>(),
            NullLogger<Backend.Modules.GoogleDrive.GoogleDriveImportWorker>.Instance);

        public async Task SetEnabledAsync(bool enabled)
        {
            await using var db = new AppDbContext(DbOptions);
            await CreateRepository(db).SetEnabledAsync(enabled, "test");
        }

        public async Task<Backend.Modules.GoogleDrive.GoogleDriveSyncStateModel> GetStateAsync()
        {
            await using var db = new AppDbContext(DbOptions);
            return await CreateRepository(db).GetSyncStateAsync();
        }

        public async Task<List<Backend.Modules.GoogleDrive.GoogleDriveImportFailureModel>> GetRetryableFailuresAsync()
        {
            await using var db = new AppDbContext(DbOptions);
            return await CreateRepository(db).GetRetryableFailuresAsync(Settings.MaxRetryAttempts, Settings.MaxFilesPerTick);
        }

        public async Task<Backend.Modules.GoogleDrive.GoogleDriveImportFailureModel?> GetFailureAsync(string fileId)
        {
            await using var db = new AppDbContext(DbOptions);
            return await db.Set<Backend.Modules.GoogleDrive.GoogleDriveImportFailureModel>()
                .FirstOrDefaultAsync(x => x.GoogleDriveFileId == fileId);
        }

        public async Task<int> CountMediaAssetsAsync()
        {
            await using var db = new AppDbContext(DbOptions);
            return await db.MediaAssets.CountAsync(x => !x.IsDeleted);
        }

        public async Task<MediaAssetModel> CreateExistingAssetAsync(string googleDriveFileId)
        {
            await using var db = new AppDbContext(DbOptions);
            var repo = new MediaAssetRepository(db, new StubUserContext(), new RecordingFileStorageService());
            return await repo.CreateFromGoogleDriveAsync(
                [1, 2, 3],
                new Backend.Modules.GoogleDrive.GoogleDriveFileInfo
                {
                    FileId = googleDriveFileId,
                    Name = "old.jpg",
                    MimeType = "image/jpeg",
                    SizeBytes = 10,
                });
        }

        private static Backend.Modules.GoogleDrive.GoogleDriveRepository CreateRepository(AppDbContext db) =>
            new(db, new StubUserContext());

        public async ValueTask DisposeAsync()
        {
            await _services.DisposeAsync();
            await _connection.DisposeAsync();
        }
    }

    public sealed class TestableWorker(
        IServiceScopeFactory scopeFactory,
        IOptions<Backend.Modules.GoogleDrive.GoogleDriveOptions> options,
        ILogger<Backend.Modules.GoogleDrive.GoogleDriveImportWorker> logger)
        : Backend.Modules.GoogleDrive.GoogleDriveImportWorker(scopeFactory, options, logger)
    {
        public Task RunSingleTickAsync(Backend.Modules.GoogleDrive.GoogleDriveOptions settings) =>
            RunTickAsync(settings, CancellationToken.None);
    }

    private sealed class FakeGoogleDriveClient : Backend.Modules.GoogleDrive.IGoogleDriveClient
    {
        public string StartToken { get; set; } = "start-token";
        public Queue<Backend.Modules.GoogleDrive.GoogleDriveChangesPage> Pages { get; } = new();
        /// <summary>
        /// Backlog "thô" — khi Pages rỗng, mỗi ListChangesAsync tự cắt tối đa maxResults phần tử
        /// từ đây, mô phỏng đúng cách Drive API thật giới hạn PageSize ở tầng SERVER, không phải
        /// worker tự Take() sau khi nhận nguyên trang (đây chính là bug đã sửa ở t7).
        /// </summary>
        public Queue<Backend.Modules.GoogleDrive.GoogleDriveFileInfo> Backlog { get; } = new();
        public List<string> DownloadedFileIds { get; } = [];
        public int ListChangesCalls { get; private set; }
        public List<int> MaxResultsSeen { get; } = [];

        private readonly Dictionary<string, Queue<Func<byte[]>>> _downloadBehaviors = new();
        private int _backlogTokenCounter;

        public void FailDownload(string fileId, int times)
        {
            var queue = new Queue<Func<byte[]>>();
            for (var i = 0; i < times; i++)
                queue.Enqueue(() => throw new IOException($"simulated failure #{i + 1} for {fileId}"));
            _downloadBehaviors[fileId] = queue;
        }

        public bool IsConfigured() => true;
        public string? DescribeConfigIssue() => null;

        public Task<string> GetStartPageTokenAsync(CancellationToken ct = default) => Task.FromResult(StartToken);

        public Task<Backend.Modules.GoogleDrive.GoogleDriveChangesPage> ListChangesAsync(
            string? pageToken, int maxResults, CancellationToken ct = default)
        {
            ListChangesCalls++;
            MaxResultsSeen.Add(maxResults);

            if (Pages.Count > 0)
                return Task.FromResult(Pages.Dequeue());

            if (Backlog.Count > 0)
            {
                var batch = new List<Backend.Modules.GoogleDrive.GoogleDriveFileInfo>();
                while (batch.Count < maxResults && Backlog.Count > 0)
                    batch.Add(Backlog.Dequeue());
                _backlogTokenCounter++;
                return Task.FromResult(new Backend.Modules.GoogleDrive.GoogleDriveChangesPage
                {
                    Files = batch,
                    NextPageToken = $"backlog-token-{_backlogTokenCounter}",
                });
            }

            return Task.FromResult(new Backend.Modules.GoogleDrive.GoogleDriveChangesPage { NextPageToken = pageToken });
        }

        public Task<byte[]> DownloadFileAsync(string fileId, CancellationToken ct = default)
        {
            DownloadedFileIds.Add(fileId);
            if (_downloadBehaviors.TryGetValue(fileId, out var queue) && queue.Count > 0)
                return Task.FromResult(queue.Dequeue()());
            return Task.FromResult(new byte[] { 1, 2, 3 });
        }
    }

    private sealed class RecordingFileStorageService : IFileStorageService
    {
        public Task<FileSaveResult> SaveAsync(IFormFile file, string folder, CancellationToken ct = default)
            => throw new NotImplementedException();

        public Task<FileSaveResult> SaveBytesAsync(
            byte[] data, string folder, string extension, string contentType, CancellationToken ct = default)
            => Task.FromResult(new FileSaveResult
            {
                StorageKey = $"{folder}/2026/09/28/{Guid.NewGuid():N}{extension}",
                OriginalFileName = $"{Guid.NewGuid():N}{extension}",
                ContentType = contentType,
                SizeBytes = data.Length,
            });

        public Task<Stream> OpenReadAsync(string storageKey, CancellationToken ct = default)
            => throw new NotImplementedException();

        public Task<bool> ExistsAsync(string storageKey, CancellationToken ct = default) => Task.FromResult(true);

        public Task DeleteAsync(string storageKey, CancellationToken ct = default) => Task.CompletedTask;
    }

    private sealed class StubUserContext : IUserContext
    {
        public Guid? GetCurrentUserId() => null;
        public string? GetCurrentUserName() => "test";
        public IReadOnlyList<string> GetCurrentUserRoles() => [];
    }
}
