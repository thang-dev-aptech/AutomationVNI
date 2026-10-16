using System.Text.Json;
using Backend.Data;
using Backend.Modules.Campaign.FullMedia;
using Backend.Modules.MediaAsset;
using Backend.Shared.Storage;
using Backend.Tests.Modules.MediaAsset;
using Microsoft.AspNetCore.Http;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;

namespace Backend.Tests.Modules.Campaign.FullMedia;

public sealed class MediaVisionServiceTests : IDisposable
{
    private readonly SqliteConnection _connection;
    private readonly DbContextOptions<AppDbContext> _options;
    private readonly Guid _mediaId = Guid.Parse("cccccccc-cccc-cccc-cccc-cccccccccccc");

    public MediaVisionServiceTests()
    {
        _connection = new SqliteConnection("DataSource=:memory:");
        _connection.Open();
        _options = new DbContextOptionsBuilder<AppDbContext>().UseSqlite(_connection).Options;
        using var db = new AppDbContext(_options);
        db.Database.EnsureCreated();
        db.MediaAssets.Add(new MediaAssetModel
        {
            Id = _mediaId,
            FileName = "a.png",
            OriginalFileName = "a.png",
            StoragePath = "mem/a.png",
            MimeType = "image/png",
            FileSize = InMemoryImageStorage.ImageBytes.Length,
            Tags = JsonSerializer.Serialize(new { keywords = new[] { "mèo", "thú cưng" } }),
            Description = "Ảnh mèo đáng yêu",
        });
        db.SaveChanges();
    }

    public void Dispose() => _connection.Dispose();

    [Fact]
    public async Task DescribeTwice_SameFileHash_CallsVisionOnce()
    {
        var handler = new ScriptedChatHandler(VisionJson(unclear: false, keywords: ["mèo", "thú cưng"]));
        var svc = CreateService(handler);

        var a = await svc.DescribeAsync(_mediaId, VisionPass.A);
        var b = await svc.DescribeAsync(_mediaId, VisionPass.A);

        Assert.Equal(a.ToState(), b.ToState());
        Assert.Single(handler.RequestBodies);
    }

    [Fact]
    public async Task FileHashChanged_RefreshesCache()
    {
        var storage = new MutableImageStorage(InMemoryImageStorage.ImageBytes);
        var handler = new ScriptedChatHandler(
            VisionJson(unclear: false, keywords: ["mèo"], topic: "mèo"),
            VisionJson(unclear: false, keywords: ["chó"], topic: "chó"));
        var svc = CreateService(handler, storage);

        var first = await svc.DescribeAsync(_mediaId, VisionPass.A);
        Assert.Equal("mèo", first.Topic);
        Assert.Single(handler.RequestBodies);

        storage.Bytes = [0x89, 0x50, 0x4E, 0x47, 9, 9, 9, 9];
        var second = await svc.DescribeAsync(_mediaId, VisionPass.A);
        Assert.Equal("chó", second.Topic);
        Assert.Equal(2, handler.RequestBodies.Count);
    }

    [Fact]
    public async Task UnclearDescription_RetriesFallbackThenUnusable()
    {
        var handler = new ScriptedChatHandler(
            VisionJson(unclear: true, confidence: 0.9, keywords: ["mèo", "thú cưng"]),
            VisionJson(unclear: true, confidence: 0.9, keywords: ["mèo", "thú cưng"]));
        var svc = CreateService(handler, opts: PipelineOpts(fallback: "fallback-model"));

        var outcome = await svc.GetUsableAsync(_mediaId, VisionPass.A);

        Assert.Equal(VisionUsability.Unusable, outcome.Status);
        Assert.Equal(2, handler.RequestBodies.Count);
        Assert.NotEqual(VisionUsability.Usable, outcome.Status);
    }

    [Fact]
    public async Task TagsContradict_FallbackThenUnusable()
    {
        var handler = new ScriptedChatHandler(
            VisionJson(unclear: false, confidence: 0.95, keywords: ["xe hơi", "đường phố"], topic: "ô tô"),
            VisionJson(unclear: false, confidence: 0.95, keywords: ["máy bay", "sân bay"], topic: "hàng không"));
        var svc = CreateService(handler, opts: PipelineOpts(fallback: "fallback-model"));

        var outcome = await svc.GetUsableAsync(_mediaId, VisionPass.A);

        Assert.Equal(VisionUsability.Unusable, outcome.Status);
        Assert.Equal(2, handler.RequestBodies.Count);
    }

    [Fact]
    public async Task OversizedImage_Rejected_NoAiCall()
    {
        var huge = new byte[100];
        Array.Fill(huge, (byte)1);
        var storage = new MutableImageStorage(huge);
        var handler = new ScriptedChatHandler(VisionJson(unclear: false, keywords: ["mèo"]));
        var svc = CreateService(handler, storage, PipelineOpts(maxBytes: 16));

        await Assert.ThrowsAsync<ArgumentException>(() => svc.DescribeAsync(_mediaId, VisionPass.A));
        Assert.Empty(handler.RequestBodies);

        var outcome = await svc.GetUsableAsync(_mediaId, VisionPass.A);
        Assert.Equal(VisionUsability.Failed, outcome.Status);
        Assert.Empty(handler.RequestBodies);
    }

    [Fact]
    public async Task UnusableNeverReturnedAsUsable()
    {
        var handler = new ScriptedChatHandler(
            VisionJson(unclear: true, confidence: 0.2, keywords: ["mèo"]),
            VisionJson(unclear: true, confidence: 0.2, keywords: ["mèo"]));
        var svc = CreateService(handler, opts: PipelineOpts(fallback: "fallback-model"));

        var outcome = await svc.GetUsableAsync(_mediaId, VisionPass.A);

        Assert.Equal(VisionUsability.Unusable, outcome.Status);
        Assert.False(outcome.Status == VisionUsability.Usable);
    }

    private MediaVisionService CreateService(
        ScriptedChatHandler handler,
        IFileStorageService? storage = null,
        FullMediaPipelineOptions? opts = null)
    {
        var db = new AppDbContext(_options);
        storage ??= new InMemoryImageStorage();
        var intelligence = new MediaIntelligenceService(
            new HttpClient(handler),
            db,
            storage,
            CaptionAiOptions.Create(),
            NullLogger<MediaIntelligenceService>.Instance);
        return new MediaVisionService(
            db,
            storage,
            intelligence,
            Options.Create(opts ?? PipelineOpts(fallback: "fallback-model")),
            NullLogger<MediaVisionService>.Instance);
    }

    private static FullMediaPipelineOptions PipelineOpts(
        string? fallback = "fallback-model",
        long maxBytes = 5_242_880)
        => new()
        {
            Enabled = false,
            VisionModelA = "vision-a",
            VisionModelB = "vision-b",
            VisionFallbackModel = fallback,
            MaxImageBytes = maxBytes,
            MinVisionConfidence = 0.5,
            MaxVisionFallbackAttempts = 1,
        };

    private static string VisionJson(
        bool unclear,
        double confidence = 0.95,
        string[]? keywords = null,
        string topic = "mèo")
        => JsonSerializer.Serialize(new
        {
            topic,
            audience = "gia đình",
            tone = "ấm áp",
            setting = "nhà",
            textInImage = "",
            keywords = keywords ?? ["mèo"],
            confidence,
            unclear,
        });

    private sealed class MutableImageStorage(byte[] bytes) : IFileStorageService
    {
        public byte[] Bytes { get; set; } = bytes;

        public Task<FileSaveResult> SaveAsync(IFormFile file, string folder, CancellationToken ct = default)
            => throw new NotImplementedException();

        public Task<FileSaveResult> SaveBytesAsync(
            byte[] data, string folder, string extension, string contentType, CancellationToken ct = default)
            => throw new NotImplementedException();

        public Task<Stream> OpenReadAsync(string storageKey, CancellationToken ct = default)
            => Task.FromResult<Stream>(new MemoryStream(Bytes));

        public Task<bool> ExistsAsync(string storageKey, CancellationToken ct = default)
            => Task.FromResult(true);

        public Task DeleteAsync(string storageKey, CancellationToken ct = default) => Task.CompletedTask;
    }
}
