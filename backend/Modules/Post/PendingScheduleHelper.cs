using System.Globalization;
using System.Text.Json;

namespace Backend.Modules.Post;

/// <summary>Đọc / áp dụng pendingSchedule từ Post.ExtraJson (bulk-import CSV).</summary>
public static class PendingScheduleHelper
{
    public sealed record PendingSchedule(string AtLocal, string Timezone);

    public static PendingSchedule? TryRead(string? extraJson)
    {
        if (string.IsNullOrWhiteSpace(extraJson)) return null;
        try
        {
            using var doc = JsonDocument.Parse(extraJson);
            if (!TryGetPropertyIgnoreCase(doc.RootElement, "pendingSchedule", out var ps))
                return null;
            if (ps.ValueKind != JsonValueKind.Object) return null;

            string? atLocal = null;
            string? timezone = null;
            foreach (var prop in ps.EnumerateObject())
            {
                if (prop.Name.Equals("atLocal", StringComparison.OrdinalIgnoreCase))
                    atLocal = prop.Value.GetString();
                else if (prop.Name.Equals("timezone", StringComparison.OrdinalIgnoreCase))
                    timezone = prop.Value.GetString();
            }

            if (string.IsNullOrWhiteSpace(atLocal)) return null;
            return new PendingSchedule(
                atLocal.Trim(),
                string.IsNullOrWhiteSpace(timezone) ? "Asia/Ho_Chi_Minh" : timezone.Trim());
        }
        catch
        {
            return null;
        }
    }

    private static bool TryGetPropertyIgnoreCase(JsonElement root, string name, out JsonElement value)
    {
        if (root.ValueKind == JsonValueKind.Object && root.TryGetProperty(name, out value))
            return true;
        foreach (var prop in root.EnumerateObject())
        {
            if (prop.Name.Equals(name, StringComparison.OrdinalIgnoreCase))
            {
                value = prop.Value;
                return true;
            }
        }
        value = default;
        return false;
    }

    public static bool TryParseLocalToUtc(string atLocal, string timezone, out DateTime utc)
    {
        utc = default;
        var formats = new[]
        {
            "yyyy-MM-dd HH:mm",
            "yyyy-MM-dd HH:mm:ss",
            "yyyy-MM-dd'T'HH:mm",
            "yyyy-MM-dd'T'HH:mm:ss",
            "dd/MM/yyyy HH:mm",
            "d/M/yyyy H:mm",
        };
        if (!DateTime.TryParseExact(
                atLocal.Trim(),
                formats,
                CultureInfo.InvariantCulture,
                DateTimeStyles.None,
                out var local) &&
            !DateTime.TryParse(atLocal.Trim(), CultureInfo.InvariantCulture, DateTimeStyles.None, out local))
        {
            return false;
        }

        local = DateTime.SpecifyKind(local, DateTimeKind.Unspecified);
        var tz = ResolveTimeZone(timezone);
        utc = TimeZoneInfo.ConvertTimeToUtc(local, tz);
        return true;
    }

    public static TimeZoneInfo ResolveTimeZone(string timezone)
    {
        foreach (var id in new[] { timezone, "SE Asia Standard Time", "Asia/Ho_Chi_Minh" })
        {
            try { return TimeZoneInfo.FindSystemTimeZoneById(id); }
            catch { /* next */ }
        }
        return TimeZoneInfo.CreateCustomTimeZone("vn-fallback", TimeSpan.FromHours(7), "UTC+7", "UTC+7");
    }
}
