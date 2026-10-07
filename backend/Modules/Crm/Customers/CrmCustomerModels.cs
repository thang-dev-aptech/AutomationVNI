using Backend.Modules.SocialChannel.Enums;
using Backend.Shared;

namespace Backend.Modules.Crm.Customers;

public enum CrmIdentitySource
{
    Message = 1,
    Comment = 2
}

public class CrmCustomerModel : BaseEntity
{
    public string DisplayName { get; set; } = string.Empty;
    /// <summary>Số đã xác nhận, chuẩn hoá E.164 (+84…). Null nếu chưa xác nhận.</summary>
    public string? PhoneE164 { get; set; }
}

public class CrmCustomerIdentityModel : BaseEntity
{
    public Guid CrmCustomerId { get; set; }
    public SocialPlatform Platform { get; set; }
    public Guid SocialChannelId { get; set; }
    public string ExternalId { get; set; } = string.Empty;
    public CrmIdentitySource Source { get; set; }
    public string? DisplayName { get; set; }
    public string? AvatarUrl { get; set; }
}

/// <summary>Ghi chú hồ sơ — CRUD đầy đủ ở t6; t5 cần model để gộp/tách chuyển được.</summary>
public class CrmCustomerNoteModel : BaseEntity
{
    public Guid CrmCustomerId { get; set; }
    public string Body { get; set; } = string.Empty;
}

/// <summary>Nhắc việc — CRUD đầy đủ ở t6; t5 cần model để gộp/tách chuyển được.</summary>
public class CrmCustomerReminderModel : BaseEntity
{
    public Guid CrmCustomerId { get; set; }
    public string Title { get; set; } = string.Empty;
    public DateTime DueAtUtc { get; set; }
    public Guid? AssigneeUserId { get; set; }
    public bool IsCompleted { get; set; }
}

/// <summary>Tag gắn trực tiếp lên khách (khác tag hội thoại/bình luận).</summary>
public class CrmCustomerTagLinkModel : BaseEntity
{
    public Guid CrmCustomerId { get; set; }
    public Guid CrmTagId { get; set; }
}

public class CrmCustomerPhoneSuggestionModel : BaseEntity
{
    public Guid CrmCustomerId { get; set; }
    public string PhoneE164 { get; set; } = string.Empty;
    public string? RawMatched { get; set; }
    public Guid? SourceMessageId { get; set; }
    public Guid? SourceCommentId { get; set; }
    public bool IsDismissed { get; set; }
}

public class CrmCustomerActionLogModel : BaseEntity
{
    public Guid? CrmCustomerId { get; set; }
    public string ActionType { get; set; } = string.Empty;
    public Guid? ActorUserId { get; set; }
    public string? ActorUserName { get; set; }
    public string? PayloadJson { get; set; }
}

/// <summary>Bản ghi gộp để tách lại đúng trạng thái trước.</summary>
public class CrmCustomerMergeRecordModel : BaseEntity
{
    public Guid KeptCustomerId { get; set; }
    public Guid MergedCustomerId { get; set; }
    public string SnapshotJson { get; set; } = "{}";
    public bool IsUndone { get; set; }
}
