using Backend.Data;
using Backend.Modules.ContentCrawl;
using Backend.Modules.GoogleDrive;
using Backend.Modules.MediaAsset;
using Backend.Modules.MediaFolder;
using Backend.Modules.PageContext;
using Backend.Modules.SocialChannel;
using Backend.Modules.SocialChannel.Enums;
using Backend.Modules.SocialConnection;
using Backend.Shared.Notification;
using Backend.Tests.Modules.MediaFolder;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;

namespace Backend.Tests.Modules.SocialChannel;

/// <summary>
/// R-028: mọi endpoint liệt kê Page trả nhóm "vni" (không phân biệt hoa/thường) trước, mỗi nhóm A→Z rồi Id,
/// áp trước Skip/Take. Dữ liệu cố ý để CreatedAt xếp NHÓM KHÁC lên đầu để thứ tự cũ (CreatedAt giảm dần)
/// không thể vô tình đúng.
/// </summary>
public sealed class PageOrderingTests : IDisposable
{
    private const string Owner = "owner";

    // Cùng bộ tên + kỳ vọng với ClientApp/src/shared/utils/channelSort.test.js
    // ("matches the backend pinned Vietnamese order for the shared name set").
    private static readonly string[] Expected =
    [
        "Beta VNI", "VNi Bắc Ninh", "VNi Đông Anh", "VNi Hà Nội", "vni sài gòn",
        "Alpha", "Ân Thi", "Ba Vì", "Đà Nẵng", "Zeta",
    ];

    // CreatedAt giảm dần cố ý khác VNi-first + collation VI (Đ/Â đứng sau z theo mã ký tự).
    private static readonly (string Name, int Minutes)[] Seed =
    [
        ("Zeta", 90), ("Đà Nẵng", 80), ("Alpha", 70), ("Ân Thi", 60), ("vni sài gòn", 50),
        ("Ba Vì", 40), ("Beta VNI", 30), ("VNi Đông Anh", 20), ("VNi Hà Nội", 10), ("VNi Bắc Ninh", 5),
    ];

    private readonly SqliteConnection _connection;
    private readonly AppDbContext _db;
    private readonly TestUserContext _admin = new();
    private readonly Dictionary<string, SocialChannelModel> _channels = [];

    public PageOrderingTests()
    {
        _connection = new SqliteConnection("DataSource=:memory:");
        _connection.Open();
        _db = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>().UseSqlite(_connection).Options);
        _db.Database.EnsureCreated();

        var baseTime = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        foreach (var (name, minutes) in Seed)
        {
            var channel = new SocialChannelModel
            {
                Id = Guid.NewGuid(),
                Platform = SocialPlatform.Facebook,
                ChannelType = SocialChannelType.Page,
                PageName = name,
                ExternalPageId = $"fb-{name}",
                AccessToken = "token",
                IsActive = true,
                CreatedBy = Owner,
                CreatedAt = baseTime.AddMinutes(minutes),
            };
            _channels[name] = channel;
            _db.SocialChannels.Add(channel);
        }
        _db.SaveChanges();
    }

    public void Dispose()
    {
        _db.Dispose();
        _connection.Dispose();
    }

    private SocialChannelRepository ChannelRepo(TestUserContext? user = null) => new(_db, user ?? _admin);
    private MediaFolderRepository FolderRepo(TestUserContext? user = null) => new(_db, user ?? _admin);

    private MediaFolderModel AddRoot(string pageName, string? folderName = null)
    {
        var folder = new MediaFolderModel
        {
            Id = Guid.NewGuid(), Name = folderName ?? pageName, SocialChannelId = _channels[pageName].Id,
        };
        _db.MediaFolders.Add(folder);
        _db.SaveChanges();
        return folder;
    }

    // ── SocialChannel ───────────────────────────────────────────────────────

    [Fact]
    public async Task GetAll_ListsVniFirstThenAlphabetical()
    {
        var all = await ChannelRepo().GetAllAsync();

        Assert.Equal(Expected, all.Select(x => x.PageName));
    }

    [Fact]
    public async Task Filter_AppliesOrderBeforePaging_AcrossPages()
    {
        var repo = ChannelRepo();

        var page1 = await repo.FilterAsync(new SocialChannelFilterRequest { Index = 1, Size = 2 });
        var page2 = await repo.FilterAsync(new SocialChannelFilterRequest { Index = 2, Size = 2 });
        var page4 = await repo.FilterAsync(new SocialChannelFilterRequest { Index = 4, Size = 2 });
        var page5 = await repo.FilterAsync(new SocialChannelFilterRequest { Index = 5, Size = 2 });

        Assert.Equal(["Beta VNI", "VNi Bắc Ninh"], page1.Items.Select(x => x.PageName));
        Assert.Equal(["VNi Đông Anh", "VNi Hà Nội"], page2.Items.Select(x => x.PageName));
        Assert.Equal(["Ân Thi", "Ba Vì"], page4.Items.Select(x => x.PageName));
        Assert.Equal(["Đà Nẵng", "Zeta"], page5.Items.Select(x => x.PageName));
        Assert.All(new[] { page1, page2, page4, page5 }, p => Assert.Equal(10, p.Total));
    }

    [Fact]
    public async Task Filter_KeepsKeywordFilterAndOrder()
    {
        var result = await ChannelRepo().FilterAsync(new SocialChannelFilterRequest { Keyword = "e", Index = 1, Size = 10 });

        // "e" thường chỉ có ở "Beta VNI" và "Zeta" (Contains phân biệt hoa/thường như trước).
        Assert.Equal(["Beta VNI", "Zeta"], result.Items.Select(x => x.PageName));
        Assert.Equal(2, result.Total);
    }

    [Fact]
    public async Task OtherRepositoriesUsingPaginateAsync_KeepCreatedAtDescending()
    {
        var paged = await ChannelRepo().PaginatePublicAsync(null, 1, 10);

        Assert.Equal(
            ["Zeta", "Đà Nẵng", "Alpha", "Ân Thi", "vni sài gòn", "Ba Vì", "Beta VNI", "VNi Đông Anh", "VNi Hà Nội", "VNi Bắc Ninh"],
            paged.Items.Select(x => x.PageName));
    }

    // ── MediaFolder ─────────────────────────────────────────────────────────

    [Fact]
    public async Task PageRoots_KeepDriveRowFirstThenVniFirst_AcrossPages()
    {
        foreach (var (name, _) in Seed) AddRoot(name);
        var drive = new MediaFolderModel { Id = Guid.NewGuid(), Name = "Google Drive" };
        _db.MediaFolders.Add(drive);
        (await _db.GoogleDriveSyncStates.SingleAsync(x => x.Id == GoogleDriveSyncStateModel.SingletonId))
            .DedicatedFolderId = drive.Id;
        await _db.SaveChangesAsync();
        var repo = FolderRepo();

        var page1 = await repo.GetPageRootsAsync(new GetMediaFolderPageRootsRequest { Index = 1, Size = 2 });
        var page2 = await repo.GetPageRootsAsync(new GetMediaFolderPageRootsRequest { Index = 2, Size = 2 });
        var page4 = await repo.GetPageRootsAsync(new GetMediaFolderPageRootsRequest { Index = 4, Size = 2 });
        var page5 = await repo.GetPageRootsAsync(new GetMediaFolderPageRootsRequest { Index = 5, Size = 2 });

        Assert.Equal(["Google Drive", "Beta VNI", "VNi Bắc Ninh"], page1.Items.Select(x => x.Name));
        Assert.Equal(["VNi Đông Anh", "VNi Hà Nội"], page2.Items.Select(x => x.Name));
        Assert.Equal(["Ân Thi", "Ba Vì"], page4.Items.Select(x => x.Name));
        Assert.Equal(["Đà Nẵng", "Zeta"], page5.Items.Select(x => x.Name));
        Assert.Equal(11, page1.Total);
    }

    [Fact]
    public async Task WritablePages_AdminSeesAllVniFirst_AndWithoutRootKeepsFilterAndOrder()
    {
        AddRoot("Zeta");
        AddRoot("vni sài gòn");
        var repo = FolderRepo();

        var all = await repo.GetWritablePagesAsync();
        var withoutRoot = await repo.GetWritablePagesAsync(withoutRoot: true);

        Assert.Equal(Expected, all.Select(x => x.PageName));
        Assert.Equal(
            ["Beta VNI", "VNi Bắc Ninh", "VNi Đông Anh", "VNi Hà Nội", "Alpha", "Ân Thi", "Ba Vì", "Đà Nẵng"],
            withoutRoot.Select(x => x.PageName));
    }

    [Fact]
    public async Task WritablePages_NonAdminKeepsOwnershipFilterAndOrder()
    {
        _channels["Alpha"].CreatedBy = "someone-else";
        _channels["vni sài gòn"].CreatedBy = "someone-else";
        await _db.SaveChangesAsync();
        var user = new TestUserContext { UserName = Owner, Roles = ["ContentManager"] };

        var pages = await FolderRepo(user).GetWritablePagesAsync();

        Assert.Equal(
            ["Beta VNI", "VNi Bắc Ninh", "VNi Đông Anh", "VNi Hà Nội", "Ân Thi", "Ba Vì", "Đà Nẵng", "Zeta"],
            pages.Select(x => x.PageName));
    }

    [Fact]
    public async Task ChungChiEligiblePages_VniFirstAndOnlyEligible()
    {
        foreach (var name in new[] { "Zeta", "vni sài gòn", "Alpha" }) AddEligibleChungChi(name);
        AddRoot("Beta VNI"); // có root nhưng không có chung_chi ⇒ không đủ điều kiện

        var pages = await FolderRepo().GetChungChiEligiblePagesAsync();

        Assert.Equal(["vni sài gòn", "Alpha", "Zeta"], pages.Select(x => x.PageName));
    }

    private void AddEligibleChungChi(string pageName)
    {
        var root = AddRoot(pageName);
        var folder = new MediaFolderModel
        {
            Id = Guid.NewGuid(), Name = "chung_chi", SocialChannelId = _channels[pageName].Id, ParentFolderId = root.Id,
        };
        _db.MediaFolders.Add(folder);
        _db.MediaAssets.Add(new MediaAssetModel
        {
            Id = Guid.NewGuid(), FolderId = folder.Id, FileName = "c.png", StoragePath = "c.png", MimeType = "image/png",
        });
        _db.SaveChanges();
    }

    // ── SocialConnection ────────────────────────────────────────────────────

    [Fact]
    public async Task ConnectionChannels_VniFirstRegardlessOfChannelType()
    {
        var connection = new SocialConnectionModel
        {
            Id = Guid.NewGuid(), DisplayName = "Owner", ExternalUserId = "u1", ConnectedAt = DateTime.UtcNow, IsActive = true,
        };
        _db.SocialConnections.Add(connection);
        foreach (var channel in _channels.Values) channel.SocialConnectionId = connection.Id;
        _channels["Beta VNI"].ChannelType = SocialChannelType.Instagram;
        _channels["Alpha"].ChannelType = SocialChannelType.Instagram;
        await _db.SaveChangesAsync();
        var repo = new SocialConnectionRepository(_db, _admin, ChannelRepo());

        var result = await repo.GetWithChannelsAsync();

        Assert.Equal(Expected, result.Single().Channels.Select(x => x.PageName));
    }

    // ── PageContext ─────────────────────────────────────────────────────────

    [Fact]
    public async Task PageContextFilter_OrdersByChannelNameNotBrandName_AcrossPages()
    {
        foreach (var name in Expected)
            _db.PageContexts.Add(new PageContextModel
            {
                Id = Guid.NewGuid(), SocialChannelId = _channels[name].Id, BrandName = $"Brand {name}",
                CreatedAt = _channels[name].CreatedAt, // thứ tự cũ (CreatedAt giảm dần) khác thứ tự mong đợi
            });
        await _db.SaveChangesAsync();
        var repo = new PageContextRepository(_db, _admin);

        var page1 = await repo.FilterAsync(new PageContextFilterRequest { Index = 1, Size = 2 });
        var page2 = await repo.FilterAsync(new PageContextFilterRequest { Index = 2, Size = 2 });
        var page4 = await repo.FilterAsync(new PageContextFilterRequest { Index = 4, Size = 2 });
        var page5 = await repo.FilterAsync(new PageContextFilterRequest { Index = 5, Size = 2 });

        Assert.Equal(["Brand Beta VNI", "Brand VNi Bắc Ninh"], page1.Items.Select(x => x.BrandName));
        Assert.Equal(["Brand VNi Đông Anh", "Brand VNi Hà Nội"], page2.Items.Select(x => x.BrandName));
        Assert.Equal(["Brand Ân Thi", "Brand Ba Vì"], page4.Items.Select(x => x.BrandName));
        Assert.Equal(["Brand Đà Nẵng", "Brand Zeta"], page5.Items.Select(x => x.BrandName));
        Assert.Equal(10, page1.Total);
    }

    [Fact]
    public async Task PageContextFilter_KeepsKeywordFilter_AndFallsBackToBrandNameWithoutChannel()
    {
        var t = _channels["Zeta"].CreatedAt; // 50 phút; CreatedAt giảm dần cũ ⇒ Zeta, Gamma, Alpha
        _db.PageContexts.AddRange(
            new PageContextModel { Id = Guid.NewGuid(), SocialChannelId = _channels["Zeta"].Id, BrandName = "Brand Zeta", CreatedAt = t },
            new PageContextModel { Id = Guid.NewGuid(), SocialChannelId = _channels["Alpha"].Id, BrandName = "Brand Alpha", CreatedAt = t.AddMinutes(-10) },
            new PageContextModel { Id = Guid.NewGuid(), SocialChannelId = Guid.NewGuid(), BrandName = "Brand Gamma", CreatedAt = t.AddMinutes(-5) },
            new PageContextModel { Id = Guid.NewGuid(), SocialChannelId = _channels["VNi Hà Nội"].Id, BrandName = "Other", CreatedAt = t });
        await _db.SaveChangesAsync();
        var repo = new PageContextRepository(_db, _admin);

        var branded = await repo.FilterAsync(new PageContextFilterRequest { Keyword = "Brand", Index = 1, Size = 10 });
        var single = await repo.FilterAsync(new PageContextFilterRequest { SocialChannelId = _channels["Alpha"].Id, Index = 1, Size = 10 });

        // Không có kênh ⇒ xếp theo BrandName ("brand gamma") trong nhóm không-VNi: Alpha, (Brand Gamma), Zeta.
        Assert.Equal(["Brand Alpha", "Brand Gamma", "Brand Zeta"], branded.Items.Select(x => x.BrandName));
        Assert.Equal(["Brand Alpha"], single.Items.Select(x => x.BrandName));
    }

    // ── Telegram ────────────────────────────────────────────────────────────

    [Fact]
    public async Task TelegramPageList_VniFirstAndSkipsInactive()
    {
        _channels["Alpha"].IsActive = false;
        await _db.SaveChangesAsync();
        var service = new CrawlTelegramService(
            _db, null!, null!, null!, null!, null!, null!, null!,
            Options.Create(new TelegramOptions()), Options.Create(new ContentCrawlOptions()),
            NullLogger<CrawlTelegramService>.Instance);

        var reply = await service.HandleCommandAsync(1, "/page");

        // Bot HTML-encode tên (Esc) để gửi Telegram; giải mã lại trước khi so sánh.
        var names = reply.Text.Split('\n').Where(l => l.StartsWith("· "))
            .Select(l => System.Net.WebUtility.HtmlDecode(l[2..])).ToList();
        Assert.Equal(Expected.Where(n => n != "Alpha"), names);
    }
}
