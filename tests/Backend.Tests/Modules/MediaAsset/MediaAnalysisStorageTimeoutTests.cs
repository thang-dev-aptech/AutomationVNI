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

public sealed class MediaAnalysisStorageTimeoutTests : IDisposable
{
    private static readonly TimeSpan StorageTimeout = TimeSpan.FromMilliseconds(100);
    private const string AnalysisJson = "{\"keywords\":[\"khai giảng\",\"học sinh\",\"sân trường\"],\"altText\":\"alt\",\"description\":\"description\"}";

    private readonly SqliteConnection _connection = new("DataSource=:memory:");
    private readonly DbContextOptions<AppDbContext> _options;

    public MediaAnalysisStorageTimeoutTests()
    {
        _connection.Open();
        _options = new DbContextOptionsBuilder<AppDbContext>().UseSqlite(_connection).Options;
        using var db = new AppDbContext(_options);
        db.Database.EnsureCreated();
    }

    public void Dispose() => _connection.Dispose();

    [Fact]
    public async Task AnalyzeAndSave_StorageReadTimeout_ThrowsAndKeepsMetadata()
    {
        var latency = StorageTimeout * 0.6;
        var storage = new SlowStorage(latency, "hung.png");
        var id = (await SeedAssetsAsync(("hung.png", "old-tags", "old-alt", "old-description")))[0];
        var service = Create(storage, StorageTimeout);

        var task = service.AnalyzeAndSaveAsync(id);

        var exception = await Assert.ThrowsAsync<TimeoutException>(() => task);
        Assert.Contains("storage", exception.Message, StringComparison.OrdinalIgnoreCase);

        await using var verify = new AppDbContext(_options);
        var asset = await verify.MediaAssets.SingleAsync(x => x.Id == id);
        Assert.Equal("old-tags", asset.Tags);
        Assert.Equal("old-alt", asset.AltText);
        Assert.Equal("old-description", asset.Description);
    }

    [Fact]
    public async Task AnalyzeAll_OneStorageReadTimeout_FailsOneAndContinuesWithOthers()
    {
        var latency = StorageTimeout * 0.6;
        var storage = new SlowStorage(latency, "hung.png");
        var ids = await SeedAssetsAsync(
            ("first.png", null, null, null),
            ("hung.png", "old-tags", "old-alt", "old-description"),
            ("last.png", null, null, null));
        var service = Create(storage, StorageTimeout);

        var result = await service.AnalyzeAllAsync(force: true);

        Assert.Equal(3, result.Total);
        Assert.True(result.Analyzed == 2, $"Analyzed={result.Analyzed}; errors: {string.Join(" | ", result.Errors)}");
        Assert.Equal(1, result.Failed);
        Assert.Single(result.Errors);

        await using var verify = new AppDbContext(_options);
        var assets = await verify.MediaAssets.OrderBy(x => x.FileName).ToListAsync();
        Assert.Equal("old-tags", assets.Single(x => x.Id == ids[1]).Tags);
        Assert.Equal("old-alt", assets.Single(x => x.Id == ids[1]).AltText);
        Assert.Equal("old-description", assets.Single(x => x.Id == ids[1]).Description);
        Assert.All(assets.Where(x => x.Id != ids[1]), asset =>
        {
            Assert.Equal(["khai giảng", "học sinh", "sân trường"], MediaIntelligenceService.ParseKeywords(asset.Tags));
            Assert.Equal("alt", asset.AltText);
            Assert.Equal("description", asset.Description);
        });
    }

    [Fact]
    public async Task AnalyzeAndSave_CallerCancellation_IsNotReportedAsStorageTimeout()
    {
        var storage = new SlowStorage(TimeSpan.FromSeconds(5), "hung.png");
        var id = (await SeedAssetsAsync(("hung.png", "old-tags", "old-alt", "old-description")))[0];
        var service = Create(storage, TimeSpan.FromSeconds(10));
        using var caller = new CancellationTokenSource();

        var task = service.AnalyzeAndSaveAsync(id, caller.Token);
        caller.CancelAfter(TimeSpan.FromMilliseconds(50));

        var exception = await Assert.ThrowsAnyAsync<OperationCanceledException>(() => task);
        Assert.IsNotType<TimeoutException>(exception);
    }

    [Fact]
    public async Task CaptionPath_IsNotLimitedByStorageReadTimeout()
    {
        // Đường caption chỉ bị giới hạn bởi deadline của caption, không phải StorageReadTimeout của đường phân tích.
        var id = (await SeedAssetsAsync(("slow.png", null, null, null)))[0];
        var service = new MediaIntelligenceService(
            new HttpClient(new ImmediateChatHandler(ScriptedChatHandler.Lines("1", "2", "3", "4", "5"))),
            new AppDbContext(_options),
            new SlowStorage(TimeSpan.FromMilliseconds(300), "slow.png"),
            AnalysisOptions.Create(),
            NullLogger<MediaIntelligenceService>.Instance)
        {
            Timeouts = MediaAiTimeouts.Default with { StorageReadTimeout = TimeSpan.FromMilliseconds(50) }
        };

        var saved = await service.GenerateCaptionAsync(id);

        Assert.Equal("1\n2\n3\n4\n5", saved.Caption);
    }

    private MediaIntelligenceService Create(IFileStorageService storage, TimeSpan timeout)
    {
        var handler = new ImmediateChatHandler(AnalysisJson);
        var service = new MediaIntelligenceService(
            new HttpClient(handler) { Timeout = MediaIntelligenceService.HttpClientTimeout },
            new AppDbContext(_options),
            storage,
            AnalysisOptions.Create(),
            NullLogger<MediaIntelligenceService>.Instance)
        {
            Timeouts = MediaAiTimeouts.Default with { StorageReadTimeout = timeout }
        };
        return service;
    }

    private async Task<Guid[]> SeedAssetsAsync(params (string FileName, string? Tags, string? AltText, string? Description)[] assets)
    {
        await using var db = new AppDbContext(_options);
        var models = assets.Select(item => new MediaAssetModel
        {
            Id = Guid.NewGuid(),
            FileName = item.FileName,
            OriginalFileName = item.FileName,
            StoragePath = item.FileName,
            MimeType = "image/png",
            Tags = item.Tags,
            AltText = item.AltText,
            Description = item.Description
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

    /// <summary>Storage chậm nhưng vẫn hoàn tất (mô phỏng độ trễ, không dùng để đồng bộ).</summary>
    private sealed class SlowStorage(TimeSpan latency, string? targetFile = null) : IFileStorageService
    {
        public Task<FileSaveResult> SaveAsync(IFormFile file, string folder, CancellationToken ct = default)
            => throw new NotSupportedException();

        public Task<FileSaveResult> SaveBytesAsync(byte[] data, string folder, string extension, string contentType, CancellationToken ct = default)
            => throw new NotSupportedException();

        public async Task<bool> ExistsAsync(string storageKey, CancellationToken ct = default)
        {
            if (targetFile == null || string.Equals(storageKey, targetFile, StringComparison.Ordinal))
            {
                await Task.Delay(latency, ct);
            }
            return true;
        }

        public async Task<Stream> OpenReadAsync(string storageKey, CancellationToken ct = default)
        {
            if (targetFile == null || string.Equals(storageKey, targetFile, StringComparison.Ordinal))
            {
                await Task.Delay(latency, ct);
            }
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
