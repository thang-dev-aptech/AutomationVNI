using System.Text.Json;

namespace Backend.Modules.Post;

/// <summary>Đọc cờ reelsRequested từ Post.ExtraJson (bulk-create/bulk-import) — gắn ngay lúc tạo lô,
/// PostGenerationWorker tự gọi ConvertToReelsAsync sau khi generation xong.</summary>
public static class PendingReelsHelper
{
    public static bool TryRead(string? extraJson)
    {
        if (string.IsNullOrWhiteSpace(extraJson)) return false;
        try
        {
            using var doc = JsonDocument.Parse(extraJson);
            if (doc.RootElement.ValueKind != JsonValueKind.Object) return false;
            foreach (var prop in doc.RootElement.EnumerateObject())
            {
                if (prop.Name.Equals("reelsRequested", StringComparison.OrdinalIgnoreCase))
                    return prop.Value.ValueKind == JsonValueKind.True;
            }
            return false;
        }
        catch
        {
            return false;
        }
    }
}
