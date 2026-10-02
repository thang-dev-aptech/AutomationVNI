using System.Net;
using System.Text;
using System.Text.Json;
using Backend.Data;
using Backend.Modules.MediaAsset;
using Backend.Modules.MediaFolder;
using Backend.Shared.Ai;
using Backend.Shared.Storage;
using Microsoft.AspNetCore.Http;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;

namespace Backend.Tests.Modules.MediaAsset;

public sealed class MediaBulkStorageCircuitTests : IDisposable
{
    private static readonly TimeSpan StorageTimeout = TimeSpan.FromMilliseconds(80);
    private const string AnalysisJson = "{\"keywords\":[\"khai giảng\",\"học sinh\",\"sân trường\",\"lớp học\",\"bài giảng\"],\"altText\":\"alt\",\"description\":\"description\"}";
    private const string LayoutJson = "{\"layoutStyle\":\"TopBottomSplit\",\"safeTextRegion\":{\"x\":10,\"y\":20,\"width\":80,\"height\":30},\"logoShape\":\"Square\",\"colorSlot\":\"Primary\"}";

    private readonly SqliteConnection _connection = new("DataSource=:memory:");
    private readonly DbContextOptions<AppDbContext> _options;
    private readonly List<IDisposable> _disposables = [];
    private readonly List<SelectiveStorage> _storages = [];

    public MediaBulkStorageCircuitTests()
    {
        _connection.Open();
        _options = new DbContextOptionsBuilder<AppDbContext>().UseSqlite(_connection).Options;
        using var db = new AppDbContext(_options);
        db.Database.EnsureCreated();
    }

    public void Dispose()
    {
        foreach (var storage in _storages) storage.Release();
        foreach (var disposable in _disposables) disposable.Dispose();
        _connection.Dispose();
    }

    [Fact(Timeout = 8000)]
    public async Task AnalyzeAll_ThreeConsecutiveStorageTimeouts_SkipsRestAndDoesNotTouchThem()
    {
        var hung = new HashSet<string>(["b.png", "c.png", "d.png"]);
        var storage = Track(new SelectiveStorage(hung));
        var folder = Guid.NewGuid();
        await SeedAsync(folder, "a.png", "b.png", "c.png", "d.png", "e.png", "f.png");
        var service = Create(storage, ImmediateHandler(AnalysisJson), maxConsecutive: 3);

        var result = await service.AnalyzeAllAsync(force: true);

        Assert.Equal(6, result.Total);
        Assert.Equal(1, result.Analyzed);
        Assert.Equal(3, result.Failed);
        Assert.Equal(2, result.Skipped);
        Assert.Equal(result.Total, result.Analyzed + result.Failed + result.Skipped);
        Assert.Equal(new[] { "a.png", "b.png", "c.png", "d.png" }, storage.Opened.ToArray());
        Assert.Contains(result.Errors, x => x.Contains("đã dừng sau 3 ảnh liên tiếp", StringComparison.Ordinal)
            && x.Contains("còn 2 ảnh chưa xử lý", StringComparison.Ordinal));
        Assert.Equal(1, result.Errors.Count(x => x.Contains("đã dừng sau", StringComparison.Ordinal)));
    }

    [Fact(Timeout = 8000)]
    public async Task AnalyzeLayout_ThreeConsecutiveStorageTimeouts_SkipsRest()
    {
        var hung = new HashSet<string>(["b.png", "c.png", "d.png"]);
        var storage = Track(new SelectiveStorage(hung));
        var folder = Guid.NewGuid();
        await SeedAsync(folder, "a.png", "b.png", "c.png", "d.png", "e.png", "f.png");
        var service = Create(storage, ImmediateHandler(LayoutJson), maxConsecutive: 3);

        var result = await service.AnalyzeLayoutFolderAsync(folder);

        Assert.Equal(6, result.Total);
        Assert.Equal(1, result.Analyzed);
        Assert.Equal(3, result.Failed);
        Assert.Equal(2, result.Skipped);
        Assert.Equal(new[] { "a.png", "b.png", "c.png", "d.png" }, storage.Opened.ToArray());
        Assert.Equal(result.Total, result.Analyzed + result.Failed + result.Skipped);
        Assert.Contains(result.Errors, x => x.Contains("đã dừng sau 3 ảnh liên tiếp", StringComparison.Ordinal)
            && x.Contains("còn 2 ảnh chưa xử lý", StringComparison.Ordinal));
        Assert.Equal(1, result.Errors.Count(x => x.Contains("đã dừng sau", StringComparison.Ordinal)));
    }

    [Fact(Timeout = 8000)]
    public async Task AnalyzeLayout_ConsecutiveAiTimeouts_DoNotTripCircuit()
    {
        var storage = Track(new SelectiveStorage(new HashSet<string>()));
        var folder = Guid.NewGuid();
        await SeedAsync(folder, "a.png", "b.png", "c.png", "d.png");
        var service = Create(storage, new HangingAiHandler(), maxConsecutive: 3, imageTimeout: TimeSpan.FromMilliseconds(40));

        var result = await service.AnalyzeLayoutFolderAsync(folder);

        Assert.Equal(4, result.Total);
        Assert.Equal(0, result.Analyzed);
        Assert.Equal(4, result.Failed);
        Assert.Equal(0, result.Skipped);
        Assert.Equal(result.Total, result.Analyzed + result.Failed + result.Skipped);
        Assert.Equal(4, storage.Opened.Count);
        Assert.DoesNotContain(result.Errors, x => x.Contains("đã dừng sau", StringComparison.Ordinal));
    }

    [Fact(Timeout = 8000)]
    public async Task AnalyzeAll_CallerCancellationDuringStorageHang_PropagatesAndStops()
    {
        var storage = Track(new SelectiveStorage(new HashSet<string>(["a.png"])));
        await SeedAsync(Guid.NewGuid(), "a.png", "b.png", "c.png");
        var service = Create(storage, ImmediateHandler(AnalysisJson), maxConsecutive: 3, storageTimeout: TimeSpan.FromSeconds(30));
        using var caller = new CancellationTokenSource();

        var task = service.AnalyzeAllAsync(force: true, caller.Token);
        await storage.WaitUntilOpened("a.png");
        caller.Cancel();

        var exception = await Assert.ThrowsAnyAsync<OperationCanceledException>(() => task);
        Assert.IsNotType<TimeoutException>(exception);
        Assert.Equal(new[] { "a.png" }, storage.Opened.ToArray());
    }

    [Fact(Timeout = 8000)]
    public async Task AnalyzeLayout_CallerCancellationDuringStorageHang_PropagatesAndStops()
    {
        var storage = Track(new SelectiveStorage(new HashSet<string>(["a.png"])));
        var folder = Guid.NewGuid();
        await SeedAsync(folder, "a.png", "b.png", "c.png");
        var service = Create(storage, ImmediateHandler(LayoutJson), maxConsecutive: 3, storageTimeout: TimeSpan.FromSeconds(30));
        using var caller = new CancellationTokenSource();

        var task = service.AnalyzeLayoutFolderAsync(folder, caller.Token);
        await storage.WaitUntilOpened("a.png");
        caller.Cancel();

        var exception = await Assert.ThrowsAnyAsync<OperationCanceledException>(() => task);
        Assert.IsNotType<TimeoutException>(exception);
        Assert.Equal(new[] { "a.png" }, storage.Opened.ToArray());
    }

    [Fact(Timeout = 8000)]
    public async Task AnalyzeAll_InterleavedStorageTimeouts_DoNotTripCircuit()
    {
        var hung = new HashSet<string>(["a.png", "c.png", "e.png"]);
        var storage = Track(new SelectiveStorage(hung));
        var folder = Guid.NewGuid();
        await SeedAsync(folder, "a.png", "b.png", "c.png", "d.png", "e.png");
        var service = Create(storage, ImmediateHandler(AnalysisJson), maxConsecutive: 3);

        var result = await service.AnalyzeAllAsync(force: true);

        Assert.Equal(5, result.Total);
        Assert.Equal(2, result.Analyzed);
        Assert.Equal(3, result.Failed);
        Assert.Equal(0, result.Skipped);
        Assert.Equal(new[] { "a.png", "b.png", "c.png", "d.png", "e.png" }, storage.Opened.ToArray());
        Assert.DoesNotContain(result.Errors, x => x.Contains("đã dừng sau", StringComparison.Ordinal));
    }

    [Fact(Timeout = 8000)]
    public async Task AnalyzeAll_ConsecutiveAiTimeouts_DoNotTripCircuit()
    {
        var storage = Track(new SelectiveStorage(new HashSet<string>()));
        var folder = Guid.NewGuid();
        await SeedAsync(folder, "a.png", "b.png", "c.png", "d.png");
        var service = Create(storage, new HangingAiHandler(), maxConsecutive: 3, imageTimeout: TimeSpan.FromMilliseconds(40));

        var result = await service.AnalyzeAllAsync(force: true);

        Assert.Equal(4, result.Total);
        Assert.Equal(0, result.Analyzed);
        Assert.Equal(4, result.Failed);
        Assert.Equal(0, result.Skipped);
        Assert.Equal(4, storage.Opened.Count);
        Assert.DoesNotContain(result.Errors, x => x.Contains("đã dừng sau", StringComparison.Ordinal));
    }

    private SelectiveStorage Track(SelectiveStorage storage)
    {
        _storages.Add(storage);
        return storage;
    }

    private MediaIntelligenceService Create(
        IFileStorageService storage,
        HttpMessageHandler handler,
        int maxConsecutive,
        TimeSpan? imageTimeout = null,
        TimeSpan? storageTimeout = null)
    {
        var db = new AppDbContext(_options);
        _disposables.Add(db);
        return new MediaIntelligenceService(
            new HttpClient(handler) { Timeout = MediaIntelligenceService.HttpClientTimeout },
            db,
            storage,
            Options.Create(new AiProvidersOptions
            {
                DefaultProvider = "test",
                Providers = new(StringComparer.OrdinalIgnoreCase)
                {
                    ["test"] = new AiProviderConfig
                    {
                        BaseUrl = "http://ai.test",
                        ApiKey = "key",
                        DefaultVisionModel = "vision-test",
                        DefaultTextModel = "text-test"
                    }
                }
            }),
            NullLogger<MediaIntelligenceService>.Instance)
        {
            Timeouts = MediaAiTimeouts.Default with
            {
                StorageReadTimeout = storageTimeout ?? StorageTimeout,
                MaxConsecutiveStorageTimeouts = maxConsecutive,
                ImageAnalysisRequest = imageTimeout ?? TimeSpan.FromSeconds(5),
                LayoutAnalysisRequest = imageTimeout ?? TimeSpan.FromSeconds(5)
            }
        };
    }

    private async Task SeedAsync(Guid folderId, params string[] files)
    {
        await using var db = new AppDbContext(_options);
        db.MediaFolders.Add(new MediaFolderModel
        {
            Id = folderId,
            Name = "folder"
        });
        var created = DateTime.UtcNow;
        foreach (var file in files)
        {
            created = created.AddMilliseconds(1);
            db.MediaAssets.Add(new MediaAssetModel
            {
                Id = Guid.NewGuid(),
                FolderId = folderId,
                FileName = file,
                OriginalFileName = file,
                StoragePath = file,
                MimeType = "image/png",
                CreatedAt = created
            });
        }
        await db.SaveChangesAsync();
    }

    private static HttpMessageHandler ImmediateHandler(string content) => new ImmediateChatHandler(content);

    private sealed class ImmediateChatHandler(string content) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var body = JsonSerializer.Serialize(new { choices = new[] { new { message = new { content } } } });
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(body, Encoding.UTF8, "application/json")
            });
        }
    }

    private sealed class HangingAiHandler : HttpMessageHandler
    {
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            throw new InvalidOperationException("unreachable");
        }
    }

    private sealed class SelectiveStorage(IReadOnlySet<string> hung) : IFileStorageService
    {
        private readonly TaskCompletionSource _release = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly object _gate = new();
        private readonly Dictionary<string, TaskCompletionSource> _opened = new(StringComparer.Ordinal);
        public List<string> Opened { get; } = [];

        public Task WaitUntilOpened(string storageKey)
        {
            lock (_gate)
            {
                if (!_opened.TryGetValue(storageKey, out var opened))
                {
                    opened = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
                    _opened[storageKey] = opened;
                }
                return opened.Task.WaitAsync(TimeSpan.FromSeconds(2));
            }
        }

        public void Release() => _release.TrySetResult();

        public Task<FileSaveResult> SaveAsync(IFormFile file, string folder, CancellationToken ct = default)
            => throw new NotSupportedException();

        public Task<FileSaveResult> SaveBytesAsync(byte[] data, string folder, string extension, string contentType, CancellationToken ct = default)
            => throw new NotSupportedException();

        public Task<bool> ExistsAsync(string storageKey, CancellationToken ct = default)
            => Task.FromResult(true);

        public async Task<Stream> OpenReadAsync(string storageKey, CancellationToken ct = default)
        {
            Opened.Add(storageKey);
            TaskCompletionSource opened;
            lock (_gate)
            {
                if (!_opened.TryGetValue(storageKey, out opened!))
                {
                    opened = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
                    _opened[storageKey] = opened;
                }
            }
            opened.TrySetResult();
            if (hung.Contains(storageKey))
                await _release.Task;
            return new MemoryStream(InMemoryImageStorage.ImageBytes);
        }

        public Task DeleteAsync(string storageKey, CancellationToken ct = default) => Task.CompletedTask;
    }
}
