using Backend.Modules.SocialChannel.Enums;
using Backend.Shared;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Backend.Modules.Inbox;

/// <summary>
/// Loại hội thoại gộp. Serialized as "message" | "comment".
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

public class InboxFilterRequest : PagedFilterRequest
{
    public List<InboxItemKind>? Kinds { get; set; }
    /// <summary>MessageInboxStatus / CommentInboxStatus (1–4 New…Ignored).</summary>
    public List<int>? Statuses { get; set; }
    public List<Guid>? SocialChannelIds { get; set; }
    public List<Guid>? ChannelGroupIds { get; set; }
    public bool? UnreadOnly { get; set; }
    /// <summary>Hoạt động cuối ≥ FromUtc (nửa khoảng mở với ToUtc).</summary>
    public DateTime? FromUtc { get; set; }
    /// <summary>Hoạt động cuối &lt; ToUtc.</summary>
    public DateTime? ToUtc { get; set; }
    public List<Guid>? AssignedUserIds { get; set; }
    /// <summary>Tin cuối do page gửi (đang chờ khách trả lời).</summary>
    public bool? CustomerUnansweredOnly { get; set; }
    /// <summary>Chỉ áp cho message — cửa sổ 24h còn mở. Khi bật, loại bỏ comment.</summary>
    public bool? OpenWindowOnly { get; set; }
}

public class InboxListItemResponse
{
    public InboxItemKind Kind { get; set; }
    public Guid Id { get; set; }
    public Guid SocialChannelId { get; set; }
    public string? ChannelName { get; set; }
    public SocialPlatform Platform { get; set; }
    public string? ParticipantName { get; set; }
    public string? ParticipantAvatarUrl { get; set; }
    public string? Snippet { get; set; }
    public DateTime LastActivityAt { get; set; }
    public int UnreadCount { get; set; }
    public int InboxStatus { get; set; }
    public Guid? AssignedUserId { get; set; }
    public string? AssignedTo { get; set; }
    public bool? IsReplyWindowOpen { get; set; }
    public Guid? SocialPostId { get; set; }
    public string? PermalinkUrl { get; set; }
}

public class InboxSummaryResponse
{
    public int Unread { get; set; }
    public int NewCount { get; set; }
    public int InProgress { get; set; }
}

public class InboxProfileResponse
{
    public InboxItemKind Kind { get; set; }
    public Guid Id { get; set; }
    public string? ParticipantName { get; set; }
    public string? ParticipantAvatarUrl { get; set; }
    public string? ParticipantExternalId { get; set; }
    public Guid SocialChannelId { get; set; }
    public string? ChannelName { get; set; }
    public SocialPlatform Platform { get; set; }
    public Guid? AssignedUserId { get; set; }
    public string? AssignedTo { get; set; }
    public string? InternalNote { get; set; }
    public int InboxStatus { get; set; }
    public InboxInteractionStats Stats { get; set; } = new();
    public List<InboxMediaItem> Media { get; set; } = [];
    public List<InboxActivityItem> Activities { get; set; } = [];
    public List<InboxRelatedConversationItem> OtherConversations { get; set; } = [];
    public Guid? SocialPostId { get; set; }
    public string? PermalinkUrl { get; set; }
    public bool? IsReplyWindowOpen { get; set; }
    public DateTime? ReplyWindowClosesAt { get; set; }
}

public class InboxInteractionStats
{
    public int CustomerCount { get; set; }
    public int PageCount { get; set; }
    public DateTime? FirstAt { get; set; }
    public DateTime? LastAt { get; set; }
}

public class InboxMediaItem
{
    public string? Type { get; set; }
    public string? Url { get; set; }
    public DateTime? SentAt { get; set; }
}

public class InboxActivityItem
{
    public string Source { get; set; } = string.Empty;
    public string ActionType { get; set; } = string.Empty;
    public string? ActorUserName { get; set; }
    public bool Success { get; set; }
    public string? Detail { get; set; }
    public DateTime CreatedAt { get; set; }
}

public class InboxRelatedConversationItem
{
    public InboxItemKind Kind { get; set; }
    public Guid Id { get; set; }
    public string? ParticipantName { get; set; }
    public string? Snippet { get; set; }
    public DateTime LastActivityAt { get; set; }
    public int InboxStatus { get; set; }
}
