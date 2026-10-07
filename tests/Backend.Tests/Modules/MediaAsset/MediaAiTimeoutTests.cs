using Backend.Data;
using Backend.Modules.MediaAsset;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Backend.Tests.Modules.MediaAsset;

/// <summary>Mỗi đường AI có timeout per-call riêng: đổi timeout caption không đổi đường analyze và ngược lại.
/// Timeout per-call giữ đúng dạng lỗi cũ của HttpClient.Timeout; caller tự huỷ thì không bị đổi thành timeout.</summary>
public sealed class MediaAiTimeoutTests : IDisposable
{
    private static readonly TimeSpan Short = TimeSpan.FromMilliseconds(150);
    private static readonly TimeSpan Long = TimeSpan.FromSeconds(10);
    private static readonly TimeSpan AiDelay = TimeSpan.FromMilliseconds(600);
    private const string AnalysisJson = "{\"keywords\":[\"khai giảng\",\"học sinh\",\"sân trường\"],\"altText\":\"a\",\"description\":\"d\"}";
    private static readonly string CaptionJson = CaptionAi.Json("Bài thử");

    private readonly SqliteConnection _connection = new("DataSource=:memory:");
    private readonly DbContextOptions<AppDbContext> _options;

    public MediaAiTimeoutTests()
    {
        _connection.Open();
        _options = new DbContextOptionsBuilder<AppDbContext>().UseSqlite(_connection).Options;
        using var db = new AppDbContext(_options);
        db.Database.EnsureCreated();
    }

    public void Dispose() => _connection.Dispose();

    [Fact]
    public void DefaultsKeepEveryPathAt120SecondsAndHttpClientHasNoSharedTimeout()
    {
        Assert.Equal(TimeSpan.FromSeconds(120), MediaIntelligenceService.CaptionAiRequestTimeout);
        Assert.Equal(TimeSpan.FromSeconds(120), MediaIntelligenceService.AnalysisAiRequestTimeout);
        Assert.Equal(TimeSpan.FromSeconds(120), MediaAiTimeouts.Default.CaptionRequest);
        Assert.Equal(TimeSpan.FromSeconds(120), MediaAiTimeouts.Default.LayoutAnalysisRequest);
        Assert.Equal(TimeSpan.FromSeconds(120), MediaAiTimeouts.Default.ImageAnalysisRequest);
        Assert.Equal(TimeSpan.FromSeconds(15), MediaAiTimeouts.Default.MediaPickRequest);
        Assert.Equal(TimeSpan.FromSeconds(12), MediaAiTimeouts.Default.QueryKeywordRequest);
        Assert.Equal(Timeout.InfiniteTimeSpan, MediaIntelligenceService.HttpClientTimeout);
    }

    [Fact]
    public async Task ShortCaptionTimeoutDoesNotShortenAnalysis()
    {
        var timeouts = MediaAiTimeouts.Default with { CaptionRequest = Short, ImageAnalysisRequest = Long };

        var analysis = await Create(AnalysisJson, timeouts).Service.AnalyzeImageAsync([1, 2, 3], "image/png");
        Assert.Equal(3, analysis.Keywords.Count);

        var (service, db) = Create(CaptionJson, timeouts);
        await using var _ = db;
        var id = await SeedAssetAsync();
        var ex = await Assert.ThrowsAsync<TaskCanceledException>(() => service.GenerateCaptionAsync(id));
        Assert.IsType<TimeoutException>(ex.InnerException);
        await using var verify = new AppDbContext(_options);
        Assert.Equal("cũ", (await verify.MediaAssets.SingleAsync(x => x.Id == id)).Caption);
    }

    [Fact]
    public async Task ShortAnalysisTimeoutDoesNotShortenCaption()
    {
        var timeouts = MediaAiTimeouts.Default with { CaptionRequest = Long, ImageAnalysisRequest = Short };

        var ex = await Assert.ThrowsAsync<TaskCanceledException>(
            () => Create(AnalysisJson, timeouts).Service.AnalyzeImageAsync([1, 2, 3], "image/png"));
        Assert.IsType<TimeoutException>(ex.InnerException);

        var (service, db) = Create(CaptionJson, timeouts);
        await using var _ = db;
        var id = await SeedAssetAsync();
        var saved = await service.GenerateCaptionAsync(id);
        Assert.Equal(CaptionAi.Expected("Bài thử"), saved.Caption);
    }

    [Fact]
    public async Task CallerCancellationIsNotReportedAsTimeout()
    {
        var timeouts = MediaAiTimeouts.Default with { ImageAnalysisRequest = Long };
        using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(100));

        var ex = await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => Create(AnalysisJson, timeouts).Service.AnalyzeImageAsync([1, 2, 3], "image/png", cts.Token));

        Assert.IsNotType<TimeoutException>(ex.InnerException);
        Assert.True(cts.IsCancellationRequested);
    }

    private (MediaIntelligenceService Service, AppDbContext Db) Create(string content, MediaAiTimeouts timeouts)
    {
        var db = new AppDbContext(_options);
        var service = new MediaIntelligenceService(
            new HttpClient(new DelayedChatHandler(AiDelay, content)) { Timeout = MediaIntelligenceService.HttpClientTimeout },
            db, new InMemoryImageStorage(), CaptionAiOptions.Create(), NullLogger<MediaIntelligenceService>.Instance)
        {
            Timeouts = timeouts
        };
        return (service, db);
    }

    private async Task<Guid> SeedAssetAsync()
    {
        await using var db = new AppDbContext(_options);
        var asset = new MediaAssetModel
        {
            Id = Guid.NewGuid(), FileName = "a.png", StoragePath = "a.png", MimeType = "image/png", Caption = "cũ"
        };
        db.MediaAssets.Add(asset);
        await db.SaveChangesAsync();
        return asset.Id;
    }
}
