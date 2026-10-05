using System.Text.Json;
using Backend.Data;
using Backend.Modules.GoogleDrive;
using Backend.Modules.MediaAsset;
using Backend.Modules.MediaFolder;
using Backend.Shared.Repositories;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Backend.Tests.Modules.MediaAsset;

/// <summary>MEDIA-CAPTION-01 (R-023): GenerateCaptionAsync + update Caption.</summary>
public class MediaIntelligenceCaptionTests : IDisposable
{
    private const string SeedTags =
        "{\"keywords\":[\"khai giảng\",\"học sinh\",\"sân trường\"],\"safeTextRegion\":{\"x\":10,\"y\":20,\"width\":80,\"height\":30},\"layoutStyle\":\"FreeText\"}";
    private const string SeedAlt = "Học sinh xếp hàng trên sân trường";
    private const string SeedDescription = "Lễ khai giảng, nhiều học sinh mặc đồng phục trắng.";

    private readonly SqliteConnection _connection;
    private readonly DbContextOptions<AppDbContext> _options;

    public MediaIntelligenceCaptionTests()
    {
        _connection = new SqliteConnection("DataSource=:memory:");
        _connection.Open();
        _options = new DbContextOptionsBuilder<AppDbContext>().UseSqlite(_connection).Options;
        using var db = new AppDbContext(_options);
        db.Database.EnsureCreated();
    }

    public void Dispose() => _connection.Dispose();

    private (MediaIntelligenceService Service, AppDbContext Db) CreateService(ScriptedChatHandler handler)
    {
        var db = new AppDbContext(_options);
        var service = new MediaIntelligenceService(
            new HttpClient(handler), db, new InMemoryImageStorage(), CaptionAiOptions.Create(),
            NullLogger<MediaIntelligenceService>.Instance);
        return (service, db);
    }

    private async Task<Guid> SeedAssetAsync(
        Guid? folderId = null, string mimeType = "image/png", string? caption = null)
    {
        await using var db = new AppDbContext(_options);
        var asset = new MediaAssetModel
        {
            Id = Guid.NewGuid(),
            FileName = "a.png",
            StoragePath = "media/a.png",
            MimeType = mimeType,
            FolderId = folderId,
            Tags = SeedTags,
            AltText = SeedAlt,
            Description = SeedDescription,
            Caption = caption
        };
        db.MediaAssets.Add(asset);
        await db.SaveChangesAsync();
        return asset.Id;
    }

    private async Task<Guid> SeedFolderAsync(string name, Guid? parentId = null, bool deleted = false)
    {
        await using var db = new AppDbContext(_options);
        var folder = new MediaFolderModel { Id = Guid.NewGuid(), Name = name, ParentFolderId = parentId, IsDeleted = deleted };
        db.MediaFolders.Add(folder);
        await db.SaveChangesAsync();
        return folder.Id;
    }

    private async Task<MediaAssetModel> ReloadAsync(Guid id)
    {
        await using var db = new AppDbContext(_options);
        return await db.MediaAssets.AsNoTracking().SingleAsync(x => x.Id == id);
    }

    private static string UserText(string requestBody)
    {
        using var doc = JsonDocument.Parse(requestBody);
        var user = doc.RootElement.GetProperty("messages")[1].GetProperty("content");
        return user[0].GetProperty("text").GetString()!;
    }

    private static string ImageUrl(string requestBody)
    {
        using var doc = JsonDocument.Parse(requestBody);
        var user = doc.RootElement.GetProperty("messages")[1].GetProperty("content");
        return user[1].GetProperty("image_url").GetProperty("url").GetString()!;
    }

    // ---- AC 73433ad7 caption-retry-or-unchanged-test + R-029 AC 1d27c6a5 image-caption-format-test ----

    [Fact]
    public async Task ValidJson_IsComposedByTheFullAiHelper()
    {
        var id = await SeedAssetAsync();
        var handler = new ScriptedChatHandler(CaptionAi.Json(
            "Hook 🌞\n• ý 1\n• ý 2", headline: "Khai giảng.", cta: "Inbox ngay 💬",
            hashtags: ["khaigiang", "#hocsinh", "khaigiang"]));
        var (service, db) = CreateService(handler);
        await using var _ = db;

        await service.GenerateCaptionAsync(id);

        Assert.Equal(
            "KHAI GIẢNG\n\nHook 🌞\n• ý 1\n• ý 2\n\nInbox ngay 💬\n\n#khaigiang #hocsinh",
            (await ReloadAsync(id)).Caption);
        Assert.Single(handler.RequestBodies);
    }

    [Fact]
    public async Task MissingCtaAndHashtags_FallBackToFullAiDefaults()
    {
        var id = await SeedAssetAsync();
        var handler = new ScriptedChatHandler("{\"caption\":\"Thân bài\"}");
        var (service, db) = CreateService(handler);
        await using var _ = db;

        await service.GenerateCaptionAsync(id);

        // Không PageContext: CTA mặc định và hashtag mặc định của Full AI.
        Assert.Equal(
            "Thân bài\n\nInbox ngay để được tư vấn chi tiết nhé 💬\n\n#Chung #Facebook #Marketing #BanHang",
            (await ReloadAsync(id)).Caption);
    }

    [Fact]
    public async Task FencedJson_StringHashtags_AndCaseInsensitiveKeys_AreAccepted()
    {
        var id = await SeedAssetAsync();
        var handler = new ScriptedChatHandler(
            "```json\n{\"Caption\":\"Nội dung\",\"Hashtags\":\"#a, b #c\",\"CTA\":\"Gọi ngay\",\"BannerHeadline\":\"Tiêu đề\"}\n```");
        var (service, db) = CreateService(handler);
        await using var _ = db;

        await service.GenerateCaptionAsync(id);

        Assert.Equal("TIÊU ĐỀ\n\nNội dung\n\nGọi ngay\n\n#a #b #c", (await ReloadAsync(id)).Caption);
    }

    [Fact]
    public async Task MoreThanEightHashtags_AreCapped()
    {
        var id = await SeedAssetAsync();
        var tags = Enumerable.Range(1, 12).Select(i => $"t{i}").ToArray();
        var (service, db) = CreateService(new ScriptedChatHandler(CaptionAi.Json("Nội dung", hashtags: tags)));
        await using var _ = db;

        await service.GenerateCaptionAsync(id);

        Assert.EndsWith("#t1 #t2 #t3 #t4 #t5 #t6 #t7 #t8", (await ReloadAsync(id)).Caption);
    }

    [Theory]
    [InlineData("{\"caption\":\"   \"}")]
    [InlineData("{\"hashtags\":[\"#a\"]}")]
    [InlineData("không phải JSON")]
    [InlineData("[\"mảng\"]")]
    [InlineData("{\"lines\":[\"định\",\"dạng\",\"cũ\",\"5\",\"dòng\"]}")]
    public async Task InvalidThenValid_RetriesOnceAndSucceeds(string invalidFirstReply)
    {
        var id = await SeedAssetAsync();
        var handler = new ScriptedChatHandler(invalidFirstReply, CaptionAi.Json("Bài hợp lệ"));
        var (service, db) = CreateService(handler);
        await using var _ = db;

        await service.GenerateCaptionAsync(id);

        Assert.Equal(2, handler.RequestBodies.Count);
        Assert.Equal(CaptionAi.Expected("Bài hợp lệ"), (await ReloadAsync(id)).Caption);
    }

    [Fact]
    public async Task InvalidTwice_ThrowsAndKeepsOldCaption()
    {
        var id = await SeedAssetAsync(caption: "caption cũ");
        var handler = new ScriptedChatHandler(
            "{\"caption\":\"\"}", "không phải JSON", CaptionAi.Json("lần thứ ba không bao giờ được gọi"));
        var (service, db) = CreateService(handler);
        await using var _ = db;

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => service.GenerateCaptionAsync(id));

        Assert.Equal("AI không trả caption hợp lệ", ex.Message);
        Assert.Equal(2, handler.RequestBodies.Count);
        Assert.Equal("caption cũ", (await ReloadAsync(id)).Caption);
    }

    // ---- AC aba9f481 caption-no-metadata-change-test ----

    [Fact]
    public async Task Success_DoesNotTouchTagsAltTextDescription()
    {
        var id = await SeedAssetAsync();
        var handler = new ScriptedChatHandler(CaptionAi.Json("Bài mới"));
        var (service, db) = CreateService(handler);
        await using var _ = db;

        await service.GenerateCaptionAsync(id);

        var after = await ReloadAsync(id);
        Assert.Equal(SeedTags, after.Tags);
        Assert.Equal(SeedAlt, after.AltText);
        Assert.Equal(SeedDescription, after.Description);
        Assert.NotNull(after.Caption);
    }

    // ---- AC ea6c986e caption-folder-context-test ----

    [Fact]
    public async Task FolderName_IsSentAsContext_WithImageDataUrl()
    {
        var folderId = await SeedFolderAsync("2026-10 Khai giảng");
        var id = await SeedAssetAsync(folderId);
        var handler = new ScriptedChatHandler(CaptionAi.Json("Bài mới"));
        var (service, db) = CreateService(handler);
        await using var _ = db;

        await service.GenerateCaptionAsync(id);

        var body = handler.RequestBodies.Single();
        Assert.Equal("Tên thư mục: 2026-10 Khai giảng", UserText(body).Split('\n')[0]);
        Assert.Equal(
            "data:image/png;base64," + Convert.ToBase64String(InMemoryImageStorage.ImageBytes),
            ImageUrl(body));
        using var doc = JsonDocument.Parse(body);
        Assert.False(doc.RootElement.TryGetProperty("response_format", out JsonElement _));
    }

    [Fact]
    public async Task NoFolder_SendsNoFolderName()
    {
        var id = await SeedAssetAsync();
        var handler = new ScriptedChatHandler(CaptionAi.Json("Bài mới"));
        var (service, db) = CreateService(handler);
        await using var _ = db;

        await service.GenerateCaptionAsync(id);

        var body = handler.RequestBodies.Single();
        Assert.Equal("Tên thư mục: không có", UserText(body).Split('\n')[0]);
        Assert.StartsWith("data:image/png;base64,", ImageUrl(body));
    }

    [Fact]
    public async Task DedicatedGoogleDriveRoot_SendsNoFolderName()
    {
        var folderId = await SeedFolderAsync(GoogleDriveRepository.DedicatedFolderName);
        var id = await SeedAssetAsync(folderId);
        var handler = new ScriptedChatHandler(CaptionAi.Json("Bài mới"));
        var (service, db) = CreateService(handler);
        await using var _ = db;

        await service.GenerateCaptionAsync(id);

        var body = handler.RequestBodies.Single();
        Assert.Equal("Tên thư mục: không có", UserText(body).Split('\n')[0]);
        Assert.DoesNotContain(GoogleDriveRepository.DedicatedFolderName, body);
        Assert.StartsWith("data:image/png;base64,", ImageUrl(body));
    }

    [Fact]
    public async Task DeletedFolder_SendsNoFolderName()
    {
        var folderId = await SeedFolderAsync("Thư mục đã xoá", deleted: true);
        var id = await SeedAssetAsync(folderId);
        var handler = new ScriptedChatHandler(CaptionAi.Json("Bài mới"));
        var (service, db) = CreateService(handler);
        await using var _ = db;

        await service.GenerateCaptionAsync(id);

        Assert.DoesNotContain("Thư mục đã xoá", handler.RequestBodies.Single());
    }

    [Fact]
    public async Task SystemPrompt_CarriesTheFullAiRulesAndTheImageGuardrails()
    {
        var id = await SeedAssetAsync();
        var handler = new ScriptedChatHandler(CaptionAi.Json("Bài mới"));
        var (service, db) = CreateService(handler);
        await using var _ = db;

        await service.GenerateCaptionAsync(id);

        using var doc = JsonDocument.Parse(handler.RequestBodies.Single());
        var system = doc.RootElement.GetProperty("messages")[0].GetProperty("content").GetString()!;
        // Cùng khối cấu trúc với Full AI: hook + emoji, 2–4 ý bullet, CTA riêng, hashtag không nhồi giữa bài.
        Assert.Contains(Backend.Shared.Ai.FacebookCaptionRules.Structure, system);
        Assert.Contains("Hook 1 câu", system);
        Assert.Contains("bullet", system);
        Assert.Contains("emoji", system);
        Assert.Contains("80–160 từ", system);
        // Ràng buộc riêng của bài viết từ ảnh.
        Assert.Contains("KHÔNG bịa", system);
        Assert.Contains("Bức ảnh", system);
        Assert.Contains("Hình ảnh cho thấy", system);
        foreach (var key in new[] { "\"caption\"", "\"hashtags\"", "\"cta\"", "\"bannerHeadline\"" })
            Assert.Contains(key, system);
        Assert.DoesNotContain("{\"lines\"", system);
    }

    [Fact]
    public async Task NonImage_ThrowsArgumentException_WithoutCallingAi()
    {
        var id = await SeedAssetAsync(mimeType: "video/mp4");
        var handler = new ScriptedChatHandler(CaptionAi.Json("Bài mới"));
        var (service, db) = CreateService(handler);
        await using var _ = db;

        await Assert.ThrowsAsync<ArgumentException>(() => service.GenerateCaptionAsync(id));
        Assert.Empty(handler.RequestBodies);
    }

    // ---- AC 1d94cc25 caption-persist-ui-test (phần backend) ----

    [Fact]
    public async Task Update_WithCaption_ChangesOnlyCaption()
    {
        var id = await SeedAssetAsync(caption: "cũ");
        await using var db = new AppDbContext(_options);
        var repo = new MediaAssetRepository(db, new FixedUserContext(), new InMemoryImageStorage());

        await repo.UpdateAsync(id, new UpdateMediaAssetRequest { Caption = "Dòng 1\nDòng 2" });

        var after = await ReloadAsync(id);
        Assert.Equal("Dòng 1\nDòng 2", after.Caption);
        Assert.Equal(SeedTags, after.Tags);
        Assert.Equal(SeedAlt, after.AltText);
        Assert.Equal(SeedDescription, after.Description);
        Assert.Equal("Dòng 1\nDòng 2", MediaAssetRepository.ToResponse(after).Caption);
    }

    [Fact]
    public async Task Update_WithoutCaption_KeepsCaption()
    {
        var id = await SeedAssetAsync(caption: "giữ nguyên");
        await using var db = new AppDbContext(_options);
        var repo = new MediaAssetRepository(db, new FixedUserContext(), new InMemoryImageStorage());

        await repo.UpdateAsync(id, new UpdateMediaAssetRequest { AltText = "alt mới" });

        var after = await ReloadAsync(id);
        Assert.Equal("giữ nguyên", after.Caption);
        Assert.Equal("alt mới", after.AltText);
    }

    [Fact]
    public void Migration_AddMediaAssetCaption_HasCodeAndDesignerFiles()
    {
        var dir = FindMigrationsDir();
        var files = Directory.GetFiles(dir, "*_AddMediaAssetCaption*.cs").Select(Path.GetFileName).ToList();
        Assert.Contains(files, f => f!.EndsWith("_AddMediaAssetCaption.cs"));
        Assert.Contains(files, f => f!.EndsWith("_AddMediaAssetCaption.Designer.cs"));
        Assert.Contains("\"Caption\"", File.ReadAllText(Path.Combine(dir, "AppDbContextModelSnapshot.cs")));
    }

    private static string FindMigrationsDir()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !Directory.Exists(Path.Combine(dir.FullName, "backend", "Migrations")))
            dir = dir.Parent;
        Assert.NotNull(dir);
        return Path.Combine(dir!.FullName, "backend", "Migrations");
    }
}

file sealed class FixedUserContext : IUserContext
{
    public Guid? GetCurrentUserId() => null;
    public string? GetCurrentUserName() => "tester";
    public IReadOnlyList<string> GetCurrentUserRoles() => ["Admin"];
}
