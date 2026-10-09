using System.Diagnostics;
using Backend.Data;
using Backend.Modules.Crm.Customers;
using Backend.Modules.Crm.Reminders;
using Backend.Modules.Notification;
using Backend.Shared.Repositories;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;

namespace Backend.Tests.Modules.Crm.Reminders;

/// <summary>AC 3f6105db (d)(f): CrmReminderWorker:Enabled mặc định false; Enabled=true vẫn NotifyDue.</summary>
public sealed class CrmReminderWorkerOptionsTests : IAsyncLifetime
{
    private readonly SqliteConnection _connection = new("DataSource=:memory:");
    private DbContextOptions<AppDbContext> _dbOptions = null!;
    private ServiceProvider _services = null!;

    public async Task InitializeAsync()
    {
        await _connection.OpenAsync();
        _dbOptions = new DbContextOptionsBuilder<AppDbContext>().UseSqlite(_connection).Options;
        await using (var db = new AppDbContext(_dbOptions))
            await db.Database.EnsureCreatedAsync();

        _services = new ServiceCollection()
            .AddLogging()
            .AddSingleton(_dbOptions)
            .AddScoped(_ => new AppDbContext(_dbOptions))
            .AddSingleton<IUserContext, StubUserContext>()
            .AddScoped<CrmCustomerService>()
            .AddScoped<NotificationService>()
            .AddScoped<CrmReminderService>()
            .BuildServiceProvider();
    }

    public async Task DisposeAsync()
    {
        await _services.DisposeAsync();
        await _connection.DisposeAsync();
    }

    [Fact]
    public void Options_Default_EnabledIsFalse_Interval60()
    {
        var opts = new CrmReminderWorkerOptions();
        Assert.False(opts.Enabled);
        Assert.Equal(60, opts.IntervalSeconds);
    }

    [Fact]
    public async Task ExecuteAsync_WhenDisabled_ExitsImmediately_NoNotification_NoPendingModelChanges()
    {
        var reminderId = await SeedDueReminderAsync();
        var scopeCalls = 0;
        var factory = new CountingScopeFactory(_services.GetRequiredService<IServiceScopeFactory>(), () => scopeCalls++);
        var worker = new CrmReminderWorker(
            factory,
            Options.Create(new CrmReminderWorkerOptions { Enabled = false, IntervalSeconds = 60 }),
            NullLogger<CrmReminderWorker>.Instance);

        var sw = Stopwatch.StartNew();
        await worker.StartAsync(CancellationToken.None);
        Assert.NotNull(worker.ExecuteTask);
        await worker.ExecuteTask.WaitAsync(TimeSpan.FromSeconds(2));
        sw.Stop();
        await worker.StopAsync(CancellationToken.None);

        Assert.True(sw.Elapsed < TimeSpan.FromSeconds(2), $"Disabled worker took {sw.Elapsed}");
        Assert.Equal(0, scopeCalls);
        await using var db = new AppDbContext(_dbOptions);
        Assert.Equal(0, await db.Set<AppNotificationModel>()
            .CountAsync(x => x.Kind == NotificationKind.CrmReminderDue));
        Assert.Null((await db.CrmCustomerReminders.SingleAsync(x => x.Id == reminderId)).NotifiedAtUtc);
        Assert.False(db.Database.HasPendingModelChanges());
    }

    [Fact]
    public async Task RunOnce_WhenEnabled_CreatesCrmReminderDueNotification()
    {
        var reminderId = await SeedDueReminderAsync();
        var worker = new CrmReminderWorker(
            _services.GetRequiredService<IServiceScopeFactory>(),
            Options.Create(new CrmReminderWorkerOptions { Enabled = true, IntervalSeconds = 1 }),
            NullLogger<CrmReminderWorker>.Instance);

        Assert.Equal(1, await worker.RunOnceAsync());
        Assert.Equal(0, await worker.RunOnceAsync());

        await using var db = new AppDbContext(_dbOptions);
        Assert.Equal(1, await db.Set<AppNotificationModel>()
            .CountAsync(x => x.Kind == NotificationKind.CrmReminderDue && x.RefId == reminderId));
        Assert.NotNull((await db.CrmCustomerReminders.SingleAsync(x => x.Id == reminderId)).NotifiedAtUtc);
    }

    private async Task<Guid> SeedDueReminderAsync()
    {
        await using var db = new AppDbContext(_dbOptions);
        var customerId = Guid.NewGuid();
        db.CrmCustomers.Add(new CrmCustomerModel
        {
            Id = customerId,
            DisplayName = "Khách due",
            CreatedAt = DateTime.UtcNow,
            CreatedBy = "seed"
        });
        var reminderId = Guid.NewGuid();
        db.CrmCustomerReminders.Add(new CrmCustomerReminderModel
        {
            Id = reminderId,
            CrmCustomerId = customerId,
            Title = "Gọi lại",
            DueAtUtc = DateTime.UtcNow.AddMinutes(-5),
            CreatedAt = DateTime.UtcNow,
            CreatedBy = "seed"
        });
        await db.SaveChangesAsync();
        return reminderId;
    }

    private sealed class StubUserContext : IUserContext
    {
        public Guid? GetCurrentUserId() => Guid.Parse("aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee");
        public string? GetCurrentUserName() => "tester";
        public IReadOnlyList<string> GetCurrentUserRoles() => ["Admin"];
    }

    private sealed class CountingScopeFactory(IServiceScopeFactory inner, Action onCreate) : IServiceScopeFactory
    {
        public IServiceScope CreateScope()
        {
            onCreate();
            return inner.CreateScope();
        }
    }
}
