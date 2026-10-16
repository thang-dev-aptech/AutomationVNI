using Backend.Data;
using Backend.Modules.PageContext;
using Backend.Modules.SocialChannel;
using Backend.Modules.SocialChannel.Enums;
using Backend.Shared.Repositories;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Backend.Tests.Modules.PageContext;

/// <summary>FULLMEDIA t1: Topics/Exclusions trên create/update/response; client cũ không gửi vẫn chạy.</summary>
public sealed class PageContextTopicsExclusionsTests : IDisposable
{
    private readonly SqliteConnection _connection;
    private readonly DbContextOptions<AppDbContext> _options;
    private readonly Guid _channelId = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");

    public PageContextTopicsExclusionsTests()
    {
        _connection = new SqliteConnection("DataSource=:memory:");
        _connection.Open();
        _options = new DbContextOptionsBuilder<AppDbContext>().UseSqlite(_connection).Options;
        using var db = new AppDbContext(_options);
        db.Database.EnsureCreated();
        db.SocialChannels.Add(new SocialChannelModel
        {
            Id = _channelId,
            PageName = "Page Alpha",
            ExternalPageId = "ext-1",
            AccessToken = "tok",
            Platform = SocialPlatform.Facebook,
        });
        db.SaveChanges();
    }

    public void Dispose() => _connection.Dispose();

    private PageContextRepository Repo(AppDbContext db) => new(db, new StubUser());

    [Fact]
    public async Task CreateUpdateResponse_CarryTopicsExclusions()
    {
        await using var db = new AppDbContext(_options);
        var repo = Repo(db);

        var created = await repo.CreateAsync(new CreatePageContextRequest
        {
            SocialChannelId = _channelId,
            BrandName = "VNi",
            Topics = "  chứng chỉ  ",
            Exclusions = "  chính trị  ",
        });

        var response = PageContextRepository.ToResponse(created);
        Assert.Equal("chứng chỉ", response.Topics);
        Assert.Equal("chính trị", response.Exclusions);

        var updated = await repo.UpdateAsync(created.Id, new UpdatePageContextRequest
        {
            Topics = " tuyển sinh ",
            Exclusions = " bạo lực ",
        });
        Assert.NotNull(updated);
        var updatedResponse = PageContextRepository.ToResponse(updated!);
        Assert.Equal("tuyển sinh", updatedResponse.Topics);
        Assert.Equal("bạo lực", updatedResponse.Exclusions);

        await using var reload = new AppDbContext(_options);
        var stored = await reload.PageContexts.AsNoTracking().SingleAsync(x => x.Id == created.Id);
        Assert.Equal("tuyển sinh", stored.Topics);
        Assert.Equal("bạo lực", stored.Exclusions);
    }

    [Fact]
    public async Task OldClientWithoutNewFields_StillWorks()
    {
        await using var db = new AppDbContext(_options);
        var repo = Repo(db);

        // Client cũ: create không gửi Topics/Exclusions → cả hai null (không chứng minh "giữ nguyên" khi đã có giá trị).
        var created = await repo.CreateAsync(new CreatePageContextRequest
        {
            SocialChannelId = _channelId,
            BrandName = "Legacy Brand",
            ToneOfVoice = "gần gũi",
            // Topics / Exclusions omitted — client cũ
        });

        Assert.Null(created.Topics);
        Assert.Null(created.Exclusions);

        var response = PageContextRepository.ToResponse(created);
        Assert.Null(response.Topics);
        Assert.Null(response.Exclusions);

        var updated = await repo.UpdateAsync(created.Id, new UpdatePageContextRequest
        {
            BrandName = "Legacy Brand 2",
            // Topics / Exclusions omitted — vẫn null
        });
        Assert.NotNull(updated);
        Assert.Equal("Legacy Brand 2", updated!.BrandName);
        Assert.Null(updated.Topics);
        Assert.Null(updated.Exclusions);
    }

    [Fact]
    public async Task UpdateWithoutNewFields_KeepsExistingTopicsAndExclusions()
    {
        await using var db = new AppDbContext(_options);
        var repo = Repo(db);

        var created = await repo.CreateAsync(new CreatePageContextRequest
        {
            SocialChannelId = _channelId,
            BrandName = "VNi",
            Topics = "chứng chỉ",
            Exclusions = "chính trị",
        });

        var updated = await repo.UpdateAsync(created.Id, new UpdatePageContextRequest
        {
            BrandName = "VNi Updated",
            // Topics / Exclusions null — client cũ không gửi → phải giữ giá trị đã lưu
        });
        Assert.NotNull(updated);
        Assert.Equal("VNi Updated", updated!.BrandName);
        Assert.Equal("chứng chỉ", updated.Topics);
        Assert.Equal("chính trị", updated.Exclusions);

        await using var reload = new AppDbContext(_options);
        var stored = await reload.PageContexts.AsNoTracking().SingleAsync(x => x.Id == created.Id);
        Assert.Equal("chứng chỉ", stored.Topics);
        Assert.Equal("chính trị", stored.Exclusions);
    }

    private sealed class StubUser : IUserContext
    {
        public Guid? GetCurrentUserId() => Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb");
        public string? GetCurrentUserName() => "pagecontext-tester";
        public IReadOnlyList<string> GetCurrentUserRoles() => ["Admin"];
    }
}
