using Backend.Modules.SocialChannel.Enums;
using Backend.Shared;

namespace Backend.Modules.Crm.Customers;

public class CrmCustomerFilterRequest : PagedFilterRequest
{
    public string? PhoneE164 { get; set; }
    public bool? HasPhone { get; set; }
}

public class CrmCustomerListItemResponse
{
    public Guid Id { get; set; }
    public string DisplayName { get; set; } = string.Empty;
    public string? PhoneE164 { get; set; }
    public int IdentityCount { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime? UpdatedAt { get; set; }
}

public class CrmCustomerIdentityResponse
{
    public Guid Id { get; set; }
    public Guid CrmCustomerId { get; set; }
    public SocialPlatform Platform { get; set; }
    public Guid SocialChannelId { get; set; }
    public string? ChannelName { get; set; }
    public string ExternalId { get; set; } = string.Empty;
    public CrmIdentitySource Source { get; set; }
    public string? DisplayName { get; set; }
    public string? AvatarUrl { get; set; }
}

public class CrmCustomerPhoneSuggestionResponse
{
    public Guid Id { get; set; }
    public string PhoneE164 { get; set; } = string.Empty;
    public string? RawMatched { get; set; }
    public Guid? SourceMessageId { get; set; }
    public Guid? SourceCommentId { get; set; }
}

public class CrmCustomerDetailResponse
{
    public Guid Id { get; set; }
    public string DisplayName { get; set; } = string.Empty;
    public string? PhoneE164 { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime? UpdatedAt { get; set; }
    public List<CrmCustomerIdentityResponse> Identities { get; set; } = [];
    public List<CrmCustomerPhoneSuggestionResponse> PhoneSuggestions { get; set; } = [];
    public List<Guid> TagIds { get; set; } = [];
    public int NoteCount { get; set; }
    public int ReminderCount { get; set; }
}

public class MergeCustomersRequest
{
    public Guid SourceCustomerId { get; set; }
}

public class ConfirmPhoneRequest
{
    public Guid? SuggestionId { get; set; }
    public string? PhoneE164 { get; set; }
}

public class CrmMergeSuggestionResponse
{
    public Guid CustomerAId { get; set; }
    public string CustomerAName { get; set; } = string.Empty;
    public Guid CustomerBId { get; set; }
    public string CustomerBName { get; set; } = string.Empty;
    public string Reason { get; set; } = string.Empty;
    public string? SharedPhoneE164 { get; set; }
    public string? SharedDisplayName { get; set; }
}

public class CrmCustomerBackfillResult
{
    public int ConversationsScanned { get; set; }
    public int CommentsScanned { get; set; }
    public int CustomersCreated { get; set; }
    public int IdentitiesLinked { get; set; }
    public int AlreadyLinked { get; set; }
}

public class CrmCustomerMergeResult
{
    public Guid MergeRecordId { get; set; }
    public Guid KeptCustomerId { get; set; }
    public Guid MergedCustomerId { get; set; }
}

public class AttachCustomerTagRequest
{
    public Guid TagId { get; set; }
}
