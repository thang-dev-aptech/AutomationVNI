using System.Text.Json;
using Backend.Modules.PageMessage;
using Backend.Shared.SocialPublish;
using Microsoft.Extensions.Options;

namespace Backend.Shared.PageMessage;

public class ProviderConversationDto
{
    public string ExternalConversationId { get; set; } = string.Empty;
    public string ParticipantExternalId { get; set; } = string.Empty;
    public string? ParticipantName { get; set; }
    public string? ParticipantAvatarUrl { get; set; }
    public string? Snippet { get; set; }
    public DateTime? UpdatedAt { get; set; }
    public int UnreadCount { get; set; }
    public int MessageCount { get; set; }
    public List<ProviderPageMessageDto> Messages { get; set; } = [];
}

public class ProviderPageMessageDto
{
    public string ExternalMessageId { get; set; } = string.Empty;
    public string? SenderExternalId { get; set; }
    public string? SenderName { get; set; }
    public string? RecipientExternalId { get; set; }
    public string? Text { get; set; }
    public string? AttachmentsJson { get; set; }
    public DateTime? SentAt { get; set; }
    public bool IsEcho { get; set; }
}

public class FacebookPageMessagingProvider(
    HttpClient httpClient,
    IOptions<SocialPublishOptions> options,
    ILogger<FacebookPageMessagingProvider> logger)
{
    public async Task<(List<ProviderConversationDto> Items, string? NextCursor)> ListConversationsAsync(
        string pageId,
        string pageAccessToken,
        string? cursor,
        int limit,
        CancellationToken ct = default)
    {
        var fb = options.Value.Facebook;
        var messageFields = "id,message,from,to,created_time,attachments";
        var fields = $"id,updated_time,message_count,unread_count,participants,snippet,messages.limit(50){{{messageFields}}}";
        var url = $"{fb.GraphBaseUrl.TrimEnd('/')}/{fb.GraphVersion}/{Uri.EscapeDataString(pageId)}/conversations"
                  + $"?platform=MESSENGER&fields={Uri.EscapeDataString(fields)}"
                  + $"&limit={Math.Clamp(limit, 1, 100)}"
                  + $"&access_token={Uri.EscapeDataString(pageAccessToken)}";
        if (!string.IsNullOrWhiteSpace(cursor))
            url += $"&after={Uri.EscapeDataString(cursor)}";

        using var doc = await GetJsonAsync(url, ct);
        var result = new List<ProviderConversationDto>();
        if (doc?.RootElement.TryGetProperty("data", out var data) == true
            && data.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in data.EnumerateArray())
            {
                var id = GetString(item, "id");
                if (string.IsNullOrWhiteSpace(id)) continue;

                var dto = new ProviderConversationDto
                {
                    ExternalConversationId = id,
                    Snippet = GetString(item, "snippet"),
                    UpdatedAt = GetDateTime(item, "updated_time"),
                    UnreadCount = GetInt(item, "unread_count"),
                    MessageCount = GetInt(item, "message_count")
                };

                if (item.TryGetProperty("participants", out var participants)
                    && participants.TryGetProperty("data", out var participantData)
                    && participantData.ValueKind == JsonValueKind.Array)
                {
                    foreach (var participant in participantData.EnumerateArray())
                    {
                        var participantId = GetString(participant, "id");
                        if (string.IsNullOrWhiteSpace(participantId) || participantId == pageId) continue;
                        dto.ParticipantExternalId = participantId;
                        dto.ParticipantName = GetString(participant, "name");
                        dto.ParticipantAvatarUrl = GetNestedString(participant, "picture", "data", "url");
                        break;
                    }
                }

                if (item.TryGetProperty("messages", out var messages)
                    && messages.TryGetProperty("data", out var messageData)
                    && messageData.ValueKind == JsonValueKind.Array)
                {
                    foreach (var message in messageData.EnumerateArray())
                    {
                        var mapped = MapMessage(message);
                        if (mapped is not null) dto.Messages.Add(mapped);
                    }
                }

                result.Add(dto);
            }
        }

        return (result, GetPagingCursor(doc?.RootElement));
    }

    public async Task<SendPageMessageResult> SendTextAsync(
        string pageId,
        string pageAccessToken,
        string recipientPsid,
        string text,
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(text))
            return new SendPageMessageResult { ErrorMessage = "Nội dung tin nhắn trống" };

        var fb = options.Value.Facebook;
        var url = $"{fb.GraphBaseUrl.TrimEnd('/')}/{fb.GraphVersion}/{Uri.EscapeDataString(pageId)}/messages";
        var recipient = JsonSerializer.Serialize(new { id = recipientPsid });
        var message = JsonSerializer.Serialize(new { text = text.Trim() });
        using var content = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["recipient"] = recipient,
            ["messaging_type"] = "RESPONSE",
            ["message"] = message,
            ["access_token"] = pageAccessToken
        });

        using var response = await httpClient.PostAsync(url, content, ct);
        var body = await response.Content.ReadAsStringAsync(ct);
        if (!response.IsSuccessStatusCode)
        {
            logger.LogWarning(
                "Messenger send failed for page {PageId}: {Status} {Body}",
                pageId,
                response.StatusCode,
                Truncate(body));
            return new SendPageMessageResult
            {
                ErrorMessage = ReadGraphError(body) ?? $"Meta HTTP {(int)response.StatusCode}"
            };
        }

        try
        {
            using var doc = JsonDocument.Parse(body);
            return new SendPageMessageResult
            {
                Success = true,
                MessageId = GetString(doc.RootElement, "message_id"),
                RecipientId = GetString(doc.RootElement, "recipient_id")
            };
        }
        catch
        {
            return new SendPageMessageResult { Success = true, RecipientId = recipientPsid };
        }
    }

    private async Task<JsonDocument?> GetJsonAsync(string url, CancellationToken ct)
    {
        using var response = await httpClient.GetAsync(url, ct);
        var body = await response.Content.ReadAsStringAsync(ct);
        if (!response.IsSuccessStatusCode)
        {
            logger.LogWarning(
                "Messenger Graph GET failed: {Status} {Body}",
                response.StatusCode,
                Truncate(body));
            throw new InvalidOperationException(ReadGraphError(body) ?? $"Meta HTTP {(int)response.StatusCode}");
        }
        return JsonDocument.Parse(body);
    }

    private static ProviderPageMessageDto? MapMessage(JsonElement item)
    {
        var id = GetString(item, "id");
        if (string.IsNullOrWhiteSpace(id)) return null;

        string? senderId = null, senderName = null, recipientId = null;
        if (item.TryGetProperty("from", out var from))
        {
            senderId = GetString(from, "id");
            senderName = GetString(from, "name");
        }
        if (item.TryGetProperty("to", out var to)
            && to.TryGetProperty("data", out var toData)
            && toData.ValueKind == JsonValueKind.Array)
        {
            recipientId = toData.EnumerateArray()
                .Select(x => GetString(x, "id"))
                .FirstOrDefault(x => !string.IsNullOrWhiteSpace(x));
        }

        return new ProviderPageMessageDto
        {
            ExternalMessageId = id,
            SenderExternalId = senderId,
            SenderName = senderName,
            RecipientExternalId = recipientId,
            Text = GetString(item, "message"),
            AttachmentsJson = item.TryGetProperty("attachments", out var attachments)
                ? attachments.GetRawText()
                : null,
            SentAt = GetDateTime(item, "created_time")
        };
    }

    private static string? GetPagingCursor(JsonElement? root)
    {
        if (root is null
            || !root.Value.TryGetProperty("paging", out var paging)
            || !paging.TryGetProperty("cursors", out var cursors))
            return null;
        return GetString(cursors, "after");
    }

    private static string? GetNestedString(JsonElement root, params string[] path)
    {
        var current = root;
        foreach (var part in path)
        {
            if (!current.TryGetProperty(part, out current)) return null;
        }
        return current.ValueKind == JsonValueKind.String ? current.GetString() : null;
    }

    private static string? GetString(JsonElement element, string name)
        => element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;

    private static int GetInt(JsonElement element, string name)
        => element.TryGetProperty(name, out var value) && value.TryGetInt32(out var parsed)
            ? parsed
            : 0;

    private static DateTime? GetDateTime(JsonElement element, string name)
    {
        var raw = GetString(element, name);
        return DateTime.TryParse(raw, out var value) ? value.ToUniversalTime() : null;
    }

    private static string? ReadGraphError(string body)
    {
        try
        {
            using var doc = JsonDocument.Parse(body);
            if (doc.RootElement.TryGetProperty("error", out var error))
                return GetString(error, "message");
        }
        catch { }
        return null;
    }

    private static string Truncate(string? value, int max = 500)
        => string.IsNullOrEmpty(value) ? string.Empty : value.Length <= max ? value : value[..max];
}
