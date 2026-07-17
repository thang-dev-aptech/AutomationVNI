using System.Text.Json;
using System.Text.RegularExpressions;

namespace Backend.Shared;

public static partial class PayloadSanitizer
{
    private static readonly string[] SensitiveKeys =
    [
        "password", "passwordhash", "token", "refreshtoken", "accesstoken",
        "bearertoken", "authorization", "apikey", "secret", "secretkey",
        "encryptionkey", "clientsecret", "privatekey", "credential"
    ];

    private const int MaxPayloadLength = 4000;

    public static string? Sanitize(string? payload)
    {
        if (string.IsNullOrWhiteSpace(payload)) return payload;

        var trimmed = payload.Length > MaxPayloadLength
            ? payload[..MaxPayloadLength] + "...[truncated]"
            : payload;

        if (LooksLikeBase64(trimmed))
            return "[binary/base64 omitted]";

        try
        {
            using var doc = JsonDocument.Parse(trimmed);
            using var stream = new MemoryStream();
            using (var writer = new Utf8JsonWriter(stream))
                WriteSanitized(doc.RootElement, writer);
            return System.Text.Encoding.UTF8.GetString(stream.ToArray());
        }
        catch (JsonException)
        {
            return RedactSensitiveText(trimmed);
        }
    }

    private static void WriteSanitized(JsonElement element, Utf8JsonWriter writer)
    {
        switch (element.ValueKind)
        {
            case JsonValueKind.Object:
                writer.WriteStartObject();
                foreach (var prop in element.EnumerateObject())
                {
                    writer.WritePropertyName(prop.Name);
                    if (IsSensitiveKey(prop.Name))
                        writer.WriteStringValue("[redacted]");
                    else
                        WriteSanitized(prop.Value, writer);
                }
                writer.WriteEndObject();
                break;
            case JsonValueKind.Array:
                writer.WriteStartArray();
                foreach (var item in element.EnumerateArray())
                    WriteSanitized(item, writer);
                writer.WriteEndArray();
                break;
            default:
                element.WriteTo(writer);
                break;
        }
    }

    private static bool IsSensitiveKey(string key)
        => SensitiveKeys.Contains(key.Replace("_", "").Replace("-", "").ToLowerInvariant());

    private static string RedactSensitiveText(string text)
        => SensitiveJsonPattern().Replace(text, match =>
        {
            var key = match.Groups[1].Value;
            return IsSensitiveKey(key) ? $"\"{key}\":\"[redacted]\"" : match.Value;
        });

    private static bool LooksLikeBase64(string text)
        => text.Length >= 200 && Base64Pattern().IsMatch(text);

    [GeneratedRegex(@"(?i)""([a-zA-Z0-9_-]+)""\s*:\s*""[^""]*""")]
    private static partial Regex SensitiveJsonPattern();

    [GeneratedRegex(@"^[A-Za-z0-9+/=\r\n]{200,}$")]
    private static partial Regex Base64Pattern();
}
