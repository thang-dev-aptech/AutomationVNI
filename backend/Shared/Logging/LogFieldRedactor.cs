using System.Collections;
using System.Text.RegularExpressions;

namespace Backend.Shared.Logging;

/// <summary>Che secret trước khi ship log — không để password/token lọt sang Elasticsearch.</summary>
public static class LogFieldRedactor
{
    /// <summary>
    /// Danh sách khoá nhạy cảm duy nhất — vừa dùng cho RedactExtra, vừa dựng regex RedactMessage
    /// (tránh hai danh sách trôi lệch như violation b7d37bc1).
    /// </summary>
    private static readonly HashSet<string> SensitiveKeys = new(StringComparer.OrdinalIgnoreCase)
    {
        "password", "passwd", "pwd",
        "token", "access_token", "refresh_token", "id_token",
        "authorization", "auth",
        "api_key", "apikey", "api-key",
        "cookie", "set-cookie",
        "secret", "client_secret", "secret_key", "secretkey",
        "cf-access-client-secret", "cf_access_client_secret",
        "connectionstring", "connection_string",
    };

    /// <summary>Snapshot khoá nhạy cảm — dùng cho regression test RedactMessage phủ đủ tập.</summary>
    public static IReadOnlyCollection<string> SensitiveKeyNames { get; } =
        SensitiveKeys.OrderBy(k => k, StringComparer.OrdinalIgnoreCase).ToArray();

    private static readonly Regex SecretAssignmentRegex = BuildSecretAssignmentRegex();

    public static bool IsSensitiveKey(string? key)
    {
        if (string.IsNullOrWhiteSpace(key)) return false;
        var normalized = key.Trim().Replace('-', '_').Replace(' ', '_');
        if (SensitiveKeys.Contains(normalized)) return true;
        foreach (var sensitive in SensitiveKeys)
        {
            if (normalized.Contains(sensitive, StringComparison.OrdinalIgnoreCase))
                return true;
        }
        return false;
    }

    public static string RedactMessage(string? message)
    {
        if (string.IsNullOrEmpty(message)) return message ?? string.Empty;
        return SecretAssignmentRegex.Replace(message, "$1=[REDACTED]");
    }

    public static Dictionary<string, object?> RedactExtra(IEnumerable<KeyValuePair<string, object?>>? fields)
    {
        var result = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
        if (fields is null) return result;

        foreach (var (key, value) in fields)
        {
            if (string.IsNullOrWhiteSpace(key)) continue;
            if (IsSensitiveKey(key))
            {
                result[key] = "[REDACTED]";
                continue;
            }

            result[key] = FlattenValue(value);
        }

        return result;
    }

    private static object? FlattenValue(object? value)
    {
        if (value is null) return null;
        if (value is string s) return RedactMessage(s);
        if (value is bool or byte or sbyte or short or ushort or int or uint or long or ulong
            or float or double or decimal or Guid or DateTime or DateTimeOffset)
            return value;

        if (value is IEnumerable enumerable and not string)
        {
            var list = new List<object?>();
            foreach (var item in enumerable)
                list.Add(FlattenValue(item));
            return list;
        }

        return RedactMessage(Convert.ToString(value));
    }

    /// <summary>
    /// Dựng một lần từ SensitiveKeys (dài trước ngắn) để client_secret/id_token khớp
    /// trước secret/token; mọi tên trong set đều redact dạng key=value / key: value.
    /// </summary>
    private static Regex BuildSecretAssignmentRegex()
    {
        var alternation = string.Join("|",
            SensitiveKeys
                .OrderByDescending(k => k.Length)
                .ThenBy(k => k, StringComparer.Ordinal)
                .Select(Regex.Escape));

        // Giữ ngữ nghĩa cũ: bắt key rồi [=:] rồi value tới khoảng trắng/,/; 
        var pattern = $@"(?i)\b({alternation})\b\s*[=:]\s*([^\s,;]+)";
        return new Regex(pattern, RegexOptions.Compiled | RegexOptions.CultureInvariant);
    }
}
