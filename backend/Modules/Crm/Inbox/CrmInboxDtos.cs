using Backend.Modules.Crm.Customers;
using Backend.Modules.Crm.Tags;
using Backend.Modules.PageMessage;
using Backend.Modules.SocialChannel.Enums;
using Backend.Modules.SocialComment;
using Backend.Shared;

namespace Backend.Modules.Crm.Inbox;

public enum CrmInboxItemKind
{
    Message = 1,
    Comment = 2
}

/// <summary>
/// Bộ lọc hộp thư hợp nhất. AssignedMine / Unassigned dùng id người dùng hiện tại hoặc null.
/// </summary>
public class CrmInboxFilterRequest : PagedFilterRequest
{
    public Guid? SocialChannelId { get; set; }
    public CrmInboxItemKind? Kind { get; set; }
    /// <summary>MessageInboxStatus / CommentInboxStatus (cùng số 1–4 cho New…Ignored).</summary>
    public int? Status { get; set; }
    public Guid? AssignedUserId { get; set; }
    public bool? AssignedMine { get; set; }
    public bool? UnassignedOnly { get; set; }
    public Guid? TagId { get; set; }
    public bool? UnreadOnly { get; set; }
    public DateTime? From { get; set; }
    public DateTime? To { get; set; }
}

public class CrmInboxListItemResponse
{
    public CrmInboxItemKind Kind { get; set; }
    public Guid Id { get; set; }
    public Guid SocialChannelId { get; set; }
    public string? ChannelName { get; set; }
    /// <summary>Nền tảng của kênh — frontend dùng để gắn badge nguồn lên avatar.</summary>
    public SocialPlatform? Platform { get; set; }
    public string? DisplayName { get; set; }
    public string? Snippet { get; set; }
    /// <summary>Thời điểm hoạt động phía khách — dùng để sắp xếp.</summary>
    public DateTime? LastCustomerActivityAt { get; set; }
    public int Status { get; set; }
    public Guid? AssignedUserId { get; set; }
    public string? AssignedTo { get; set; }
    public int UnreadCount { get; set; }
    public bool CanReply { get; set; }
    public DateTime? ReplyWindowClosesAt { get; set; }
    /// <summary>Hội thoại tin nhắn có tin hẹn giờ đang chờ gửi (icon đồng hồ).</summary>
    public bool HasPendingScheduled { get; set; }
    public IReadOnlyList<CrmTagResponse> Tags { get; set; } = [];
}

public class CrmInboxMessageDetailResponse
{
    public CrmInboxItemKind Kind { get; set; } = CrmInboxItemKind.Message;
    public PageConversationResponse Conversation { get; set; } = new();
    public IReadOnlyList<CrmTagResponse> Tags { get; set; } = [];
    /// <summary>Trả lời qua POST /api/PageMessage/{id}/send</summary>
    public string ReplyEndpoint { get; set; } = string.Empty;
}

public class CrmInboxCommentDetailResponse
{
    public CrmInboxItemKind Kind { get; set; } = CrmInboxItemKind.Comment;
    public SocialCommentResponse Thread { get; set; } = new();
    public IReadOnlyList<CrmTagResponse> Tags { get; set; } = [];
    /// <summary>Trả lời qua POST /api/SocialComment/{id}/reply</summary>
    public string ReplyEndpoint { get; set; } = string.Empty;
}

/// <summary>
/// Hồ sơ cột phải SO9 cho hội thoại đang chọn.
/// GET không ghi DB; linked chỉ khi khớp chính xác (Platform, SocialChannelId, ExternalId).
/// </summary>
public class CrmInboxCustomerPanelResponse
{
    public bool Linked { get; set; }
    public CrmInboxParticipantResponse Participant { get; set; } = new();
    public CrmInboxLinkedCustomerResponse? Customer { get; set; }
    public CrmInboxConversationStatsResponse Stats { get; set; } = new();
    /// <summary>
    /// Tối đa 30 ảnh/video trong đúng hội thoại. Comment hiện không có field attachment → [].
    /// </summary>
    public IReadOnlyList<CrmInboxMediaItemResponse> Media { get; set; } = [];
    public IReadOnlyList<CrmTimelineItemResponse> Activities { get; set; } = [];
}

public class CrmInboxParticipantResponse
{
    public string? DisplayName { get; set; }
    public string? ExternalId { get; set; }
    public string? AvatarUrl { get; set; }
    public string? ChannelName { get; set; }
    public SocialPlatform Platform { get; set; }
}

public class CrmInboxLinkedCustomerResponse
{
    public Guid Id { get; set; }
    public string DisplayName { get; set; } = string.Empty;
    public string? PhoneE164 { get; set; }
    /// <summary>Model CRM chưa có email — luôn null.</summary>
    public string? Email { get; set; }
    public IReadOnlyList<Guid> TagIds { get; set; } = [];
    public int NoteCount { get; set; }
    public int ReminderCount { get; set; }
    public IReadOnlyList<CrmInboxCustomerIdentityResponse> Identities { get; set; } = [];
}

public class CrmInboxCustomerIdentityResponse
{
    public SocialPlatform Platform { get; set; }
    public Guid SocialChannelId { get; set; }
    public string? ChannelName { get; set; }
    public string ExternalId { get; set; } = string.Empty;
    public string? DisplayName { get; set; }
    public string? AvatarUrl { get; set; }
}

public class CrmInboxConversationStatsResponse
{
    public int? MessageCount { get; set; }
    public int? CommentCount { get; set; }
    public DateTime? FirstInteractionAt { get; set; }
    public DateTime? LastInteractionAt { get; set; }
    public bool? IsReplyWindowOpen { get; set; }
    public DateTime? ReplyWindowClosesAt { get; set; }
    public Guid? AssignedUserId { get; set; }
    public string? AssignedTo { get; set; }
    public int InboxStatus { get; set; }
}

public class CrmInboxMediaItemResponse
{
    public string Url { get; set; } = string.Empty;
    public string Type { get; set; } = string.Empty;
    public DateTime? SentAt { get; set; }
}
