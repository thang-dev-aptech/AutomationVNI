using System.Net;
using System.Text;
using System.Text.Json;
using Backend.Data;
using Backend.Modules.MediaAsset;
using Backend.Shared.Ai;
using Backend.Shared.Storage;
using Microsoft.Data.Sqlite;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;

namespace Backend.Tests.Modules.MediaAsset;

public sealed class MediaLayoutStorageTimeoutTests : IDisposable
{
    private static readonly TimeSpan StorageTimeout = TimeSpan.FromMilliseconds(100);
    private const string LayoutJson = "{\"layoutStyle\":\"TopBottomSplit\",\"safeTextRegion\":{\"x\":10,\"y\":20,\"width\":80,\"height\":30},\"logoShape\":\"Square\",\"colorSlot\":\"Primary\"}";

    private readonly SqliteConnection _connection = new("DataSource=:memory:");
    private readonly DbContextOptions<AppDbContext> _options;
    private readonly List<IDisposable> _disposables = new();

    public MediaLayoutStorageTimeoutTests()
    {
        _connection.Open();
        _options = new DbContextOptionsBuilder<AppDbContext>().UseSqlite(_connection).Options;
        using var db = new AppDbContext(_options);
        db.Database.EnsureCreated();
    }

    public void Dispose()
    {
        foreach (var d in _disposables) d.Dispose();
        _connection.Dispose();
    }

    [Fact(Timeout = 5000)]
    public async Task AnalyzeLayoutAsync_StorageReadTimeout_ThrowsAndKeepsTags()
    {
        var storage = new GatedAnalysisStorage("hung.png");
        var id = (await SeedAssetsAsync(Guid.Empty, ("hung.png", "{\"existing\":\"tag\"}")))[0];
        var service = Create(storage, StorageTimeout);

        var task = service.AnalyzeLayoutAsync(id);
        await storage.OpenReadStarted.WaitAsync(TimeSpan.FromSeconds(2));

        try
        {
            var exception = await Assert.ThrowsAsync<TimeoutException>(() => task);
            Assert.Contains("storage", exception.Message, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            storage.Release();
        }

        await using var verify = new AppDbContext(_options);
        var asset = await verify.MediaAssets.SingleAsync(x => x.Id == id);
        Assert.Equal("{\"existing\":\"tag\"}", asset.Tags);
        Assert.Null(asset.UpdatedBy);
    }

    [Fact(Timeout = 5000)]
    public async Task AnalyzeLayoutFolderAsync_OneStorageReadTimeout_FailsOneAndContinues()
    {
        var storage = new GatedAnalysisStorage("hung.png");
        var folderId = Guid.NewGuid();
        var ids = await SeedAssetsAsync(folderId,
            ("first.png", null),
            ("hung.png", "{\"existing\":\"tag\"}"),
            ("last.png", null));
        var service = Create(storage, StorageTimeout);

        BulkMediaAnalysisResult result;
        try
        {
            result = await service.AnalyzeLayoutFolderAsync(folderId);
        }
        finally
        {
            storage.Release();
        }

        Assert.Equal(2, result.Analyzed);
        Assert.Equal(1, result.Failed);
        var error = Assert.Single(result.Errors);
        Assert.StartsWith("hung.png", error, StringComparison.OrdinalIgnoreCase);

        await using var verify = new AppDbContext(_options);
        var assets = await verify.MediaAssets.OrderBy(x => x.FileName).ToListAsync();
        
        var hung = assets.Single(x => x.Id == ids[1]);
        Assert.Equal("{\"existing\":\"tag\"}", hung.Tags);
        Assert.Null(hung.UpdatedBy);

        Assert.All(assets.Where(x => x.Id != ids[1]), asset =>
        {
            Assert.Contains("TopBottomSplit", asset.Tags ?? "");
            Assert.Contains("safeTextRegion", asset.Tags ?? "");
            Assert.Contains("Square", asset.Tags ?? "");
            Assert.Contains("Primary", asset.Tags ?? "");
            Assert.Equal("AI", asset.UpdatedBy);
        });
    }

    [Fact(Timeout = 5000)]
    public async Task AnalyzeLayoutAsync_CallerCancellation_IsNotReportedAsStorageTimeout()
    {
        var storage = new GatedAnalysisStorage("hung.png");
        var folderId = Guid.NewGuid();
        var id = (await SeedAssetsAsync(folderId, ("hung.png", "old-tags")))[0];
            
        var service = Create(storage, TimeSpan.FromSeconds(10));
        using var caller = new CancellationTokenSource();

        var task = service.AnalyzeLayoutAsync(id, caller.Token);
        await storage.OpenReadStarted.WaitAsync(TimeSpan.FromSeconds(2));
        caller.Cancel();

        try
        {
            var exception = await Assert.ThrowsAnyAsync<OperationCanceledException>(() => task);
            Assert.IsNotType<TimeoutException>(exception);
        }
        finally
        {
            storage.Release();
        }
    }

    [Fact(Timeout = 5000)]
    public async Task AnalyzeLayoutFolderAsync_CallerCancellation_StopsAndDoesNotSwallow()
    {
        var storage = new GatedAnalysisStorage("hung.png");
        var folderId = Guid.NewGuid();
        await SeedAssetsAsync(folderId, ("hung.png", "old-tags"));
            
        var service = Create(storage, TimeSpan.FromSeconds(10));
        using var caller = new CancellationTokenSource();

        var task = service.AnalyzeLayoutFolderAsync(folderId, caller.Token);
        await storage.OpenReadStarted.WaitAsync(TimeSpan.FromSeconds(2));
        caller.Cancel();

        try
        {
            var exception = await Assert.ThrowsAnyAsync<OperationCanceledException>(() => task);
            Assert.IsNotType<TimeoutException>(exception);
        }
        finally
        {
            storage.Release();
        }
        
        await using var verify = new AppDbContext(_options);
        var hung = await verify.MediaAssets.SingleAsync(x => x.FileName == "hung.png");
        Assert.Equal("old-tags", hung.Tags);
    }

    private MediaIntelligenceService Create(IFileStorageService storage, TimeSpan timeout)
    {
        var handler = new ImmediateChatHandler(LayoutJson);
        var client = new HttpClient(handler) { Timeout = MediaIntelligenceService.HttpClientTimeout };
        var db = new AppDbContext(_options);
        _disposables.Add(client);
        _disposables.Add(db);

        var service = new MediaIntelligenceService(
            client,
            db,
            storage,
            AnalysisOptions.Create(),
            NullLogger<MediaIntelligenceService>.Instance)
        {
            Timeouts = MediaAiTimeouts.Default with { StorageReadTimeout = timeout }
        };
        return service;
    }

    private async Task<Guid[]> SeedAssetsAsync(Guid folderId, params (string FileName, string? Tags)[] assets)
    {
        await using var db = new AppDbContext(_options);
        var models = assets.Select(item => new MediaAssetModel
        {
            Id = Guid.NewGuid(),
            FolderId = folderId == Guid.Empty ? null : folderId,
            FileName = item.FileName,
            OriginalFileName = item.FileName,
            StoragePath = item.FileName,
            MimeType = "image/png",
            Tags = item.Tags
        }).ToArray();
        db.MediaAssets.AddRange(models);
        await db.SaveChangesAsync();
        return models.Select(x => x.Id).ToArray();
    }

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

    private sealed class GatedAnalysisStorage(string hangingFile) : IFileStorageService
    {
        private readonly TaskCompletionSource _openReadStarted = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly TaskCompletionSource _release = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public Task OpenReadStarted => _openReadStarted.Task;

        public void Release() => _release.TrySetResult();

        public Task<FileSaveResult> SaveAsync(IFormFile file, string folder, CancellationToken ct = default)
            => throw new NotSupportedException();

        public Task<FileSaveResult> SaveBytesAsync(byte[] data, string folder, string extension, string contentType, CancellationToken ct = default)
            => throw new NotSupportedException();

        public Task<bool> ExistsAsync(string storageKey, CancellationToken ct = default)
            => Task.FromResult(true);

        public async Task<Stream> OpenReadAsync(string storageKey, CancellationToken ct = default)
        {
            if (!string.Equals(storageKey, hangingFile, StringComparison.Ordinal))
                return new MemoryStream(InMemoryImageStorage.ImageBytes);

            _openReadStarted.TrySetResult();
            await _release.Task;
            return new MemoryStream(InMemoryImageStorage.ImageBytes);
        }

        public Task DeleteAsync(string storageKey, CancellationToken ct = default) => Task.CompletedTask;
    }

    private static class AnalysisOptions
    {
        public static IOptions<AiProvidersOptions> Create() => Options.Create(new AiProvidersOptions
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
        });
    }
}
