using Backend.Data;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace Backend.Tests.Shared;

/// <summary>
/// SQLite in-memory cho test, đã có sẵn schema của <see cref="AppDbContext"/>.
/// Schema được tạo một lần mỗi tiến trình bằng <c>EnsureCreated</c> trên một DB mẫu, rồi mỗi lần gọi
/// sao chép sang một kết nối :memory: MỚI bằng SQLite backup API (&lt;1ms thay vì ~110ms tạo lại
/// toàn bộ bảng). Mỗi test vẫn có DB riêng, không chia sẻ dữ liệu; lời gọi <c>EnsureCreated</c>
/// sẵn có trong test vẫn chạy (trả false vì bảng đã có).
/// </summary>
internal static class SqliteTestDatabase
{
    private static readonly object Gate = new();
    private static SqliteConnection? _template;

    /// <summary>Kết nối :memory: đã mở, schema giống hệt EnsureCreated (kể cả dữ liệu HasData).</summary>
    public static SqliteConnection OpenInMemory()
    {
        var connection = new SqliteConnection("DataSource=:memory:");
        connection.Open();
        lock (Gate)
        {
            _template ??= CreateTemplate();
            _template.BackupDatabase(connection);
        }

        return connection;
    }

    private static SqliteConnection CreateTemplate()
    {
        var template = new SqliteConnection("DataSource=:memory:");
        template.Open();
        var options = new DbContextOptionsBuilder<AppDbContext>().UseSqlite(template).Options;
        using var db = new AppDbContext(options);
        db.Database.EnsureCreated();
        return template;
    }
}
