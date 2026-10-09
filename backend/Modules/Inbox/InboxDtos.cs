using System.Text.Json;
using System.Text.Json.Serialization;

namespace Backend.Modules.Inbox;

/// <summary>
/// Loại hội thoại cho suggest-reply. Serialized as "message" | "comment".
/// </summary>
[JsonConverter(typeof(CamelCaseInboxKindConverter))]
public enum InboxItemKind
{
    Message,
    Comment
}

/// <summary>Serialize/deserialize InboxItemKind as lowercase message|comment.</summary>
public sealed class CamelCaseInboxKindConverter : JsonConverter<InboxItemKind>
{
    public override InboxItemKind Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        var s = reader.GetString();
        if (string.Equals(s, "message", StringComparison.OrdinalIgnoreCase))
            return InboxItemKind.Message;
        if (string.Equals(s, "comment", StringComparison.OrdinalIgnoreCase))
            return InboxItemKind.Comment;
        throw new JsonException($"Unknown inbox kind '{s}'");
    }

    public override void Write(Utf8JsonWriter writer, InboxItemKind value, JsonSerializerOptions options)
        => writer.WriteStringValue(value switch
        {
            InboxItemKind.Message => "message",
            InboxItemKind.Comment => "comment",
            _ => value.ToString().ToLowerInvariant()
        });
}
