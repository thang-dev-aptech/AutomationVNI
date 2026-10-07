using System.Text.Json;
using Backend.Data;
using Backend.Modules.MediaAsset;
using Backend.Modules.MediaFolder;
using Backend.Modules.PageContext;
using Backend.Modules.SocialChannel;
using Backend.Modules.SocialChannel.Enums;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Backend.Tests.Modules.MediaAsset;

/// <summary>
/// R-029 AC 42a00e9e (phần service): Page của thư mục cho đường tay (GenerateCaptionAsync) và đường worker
/// (GenerateCaptionIfEmptyAsync, vẫn compare-and-swap); Page null tường minh = không PageContext.
/// </summary>
public sealed class MediaCaptionPageContextServiceTests : IDisposable
{
    private readonly SqliteConnection _connection = new("DataSource=:memory:");
    private readonly DbContextOptions<AppDbContext> _options;
    private readonly Guid _page = Guid.NewGuid();
    private Guid _assetInPage;

    public MediaCaptionPageContextServiceTests()
    {
        _connection.Open();
        _options = new DbContextOptionsBuilder<AppDbContext>().UseSqlite(_connection).Options;
        using var db = new AppDbContext(_options);
        db.Database.EnsureCreated();

        db.SocialChannels.Add(new SocialChannelModel
        {
            Id = _page, Platform = SocialPlatform.Facebook, ChannelType = SocialChannelType.Page,
            PageName = "Page Gamma", ExternalPageId = "fb-gamma", AccessToken = "t", IsActive = true,
        });
        db.PageContexts.Add(new PageContextModel
        {
            Id = Guid.NewGuid(), SocialChannelId = _page, BrandName = "Thương hiệu Gamma",
            ToneOfVoice = "giọng Gamma", CtaText = "CTA Gamma", DefaultHashtags = "#gamma1 #gamma2",
        });
        var folder = new MediaFolderModel { Id = Guid.NewGuid(), Name = "Album sự kiện", SocialChannelId = _page };
        db.MediaFolders.Add(folder);
        var asset = new MediaAssetModel
        {
            Id = Guid.NewGuid(), FileName = "g.png", StoragePath = "g.png", MimeType = "image/png", FolderId = folder.Id,
        };
        db.MediaAssets.Add(asset);
        db.SaveChanges();
        _assetInPage = asset.Id;
    }

    public void Dispose() => _connection.Dispose();

    private MediaIntelligenceService Create(HttpMessageHandler handler, AppDbContext db)
        => new(new HttpClient(handler), db, new InMemoryImageStorage(), CaptionAiOptions.Create(),
            NullLogger<MediaIntelligenceService>.Instance);

    private static string UserText(string body)
    {
        using var doc = JsonDocument.Parse(body);
        return doc.RootElement.GetProperty("messages")[1].GetProperty("content")[0].GetProperty("text").GetString()!;
    }

    [Fact]
    public async Task ManualPath_DefaultsToTheFolderPagesContext()
    {
        var handler = new ScriptedChatHandler(CaptionAi.Json("Bài"));
        await using var db = new AppDbContext(_options);

        await Create(handler, db).GenerateCaptionAsync(_assetInPage);

        var text = UserText(handler.RequestBodies.Single());
        Assert.Contains("Thương hiệu / Page: Thương hiệu Gamma", text);
        Assert.Contains("Giọng điệu: giọng Gamma", text);
    }

    [Fact]
    public async Task ExplicitNullPage_UsesNoPageContextEvenInsideAPagesFolder()
    {
        var handler = new ScriptedChatHandler(CaptionAi.Json("Bài"));
        await using var db = new AppDbContext(_options);

        await Create(handler, db).GenerateCaptionForPageAsync(_assetInPage, pageId: null);

        var text = UserText(handler.RequestBodies.Single());
        Assert.DoesNotContain("Gamma", text);
        Assert.DoesNotContain("Thương hiệu / Page:", text);
        Assert.StartsWith("Tên thư mục: Album sự kiện", text);
    }

    [Fact]
    public async Task WorkerPath_UsesTheFolderPagesContext()
    {
        var handler = new ScriptedChatHandler(CaptionAi.Json("Bài của job"));
        await using var db = new AppDbContext(_options);

        var written = await Create(handler, db).GenerateCaptionIfEmptyAsync(_assetInPage);

        Assert.True(written);
        Assert.Contains("CTA gợi ý: CTA Gamma", UserText(handler.RequestBodies.Single()));
        Assert.Equal(CaptionAi.Expected("Bài của job"), await CaptionAsync());
    }

    [Fact]
    public async Task WorkerPath_StillCompareAndSwaps_WhenTheUserWritesDuringAi()
    {
        var handler = new GatedChatHandler(CaptionAi.Json("Bài của job"));
        await using var db = new AppDbContext(_options);
        var task = Create(handler, db).GenerateCaptionIfEmptyAsync(_assetInPage);
        await handler.Arrived(0).WaitAsync(TimeSpan.FromSeconds(10));

        await using (var other = new AppDbContext(_options))
        {
            (await other.MediaAssets.SingleAsync(x => x.Id == _assetInPage)).Caption = "tự viết";
            await other.SaveChangesAsync();
        }
        handler.Release(0);

        Assert.False(await task.WaitAsync(TimeSpan.FromSeconds(10)));
        Assert.Equal("tự viết", await CaptionAsync());
    }

    [Fact]
    public async Task FolderPageId_IsNullForImagesWithoutAPage()
    {
        await using var db = new AppDbContext(_options);
        var drive = new MediaFolderModel { Id = Guid.NewGuid(), Name = "Google Drive" };
        var loose = new MediaAssetModel
        {
            Id = Guid.NewGuid(), FileName = "d.png", StoragePath = "d.png", MimeType = "image/png", FolderId = drive.Id,
        };
        db.AddRange(drive, loose);
        await db.SaveChangesAsync();
        var service = Create(new ScriptedChatHandler(), db);

        Assert.Equal(_page, await service.GetFolderPageIdAsync(_assetInPage));
        Assert.Null(await service.GetFolderPageIdAsync(loose.Id));
        Assert.Null(await service.GetFolderPageIdAsync(Guid.NewGuid()));
    }

    private async Task<string?> CaptionAsync()
    {
        await using var db = new AppDbContext(_options);
        return (await db.MediaAssets.AsNoTracking().SingleAsync(x => x.Id == _assetInPage)).Caption;
    }
}
