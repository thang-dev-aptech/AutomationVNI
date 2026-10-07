using Backend.Modules.Crm.Tags;
using Backend.Modules.PageMessage;
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
