namespace Backend.Modules.Crm.Customers;

public class CreateCrmCustomerRequest
{
    public string DisplayName { get; set; } = string.Empty;
    public string? PhoneE164 { get; set; }
}

public class UpdateCrmCustomerRequest
{
    public string DisplayName { get; set; } = string.Empty;
    public string? PhoneE164 { get; set; }
}

public class CreateCrmCustomerNoteRequest
{
    public string Body { get; set; } = string.Empty;
}

public class UpdateCrmCustomerNoteRequest
{
    public string Body { get; set; } = string.Empty;
}

public class CrmCustomerNoteResponse
{
    public Guid Id { get; set; }
    public Guid CrmCustomerId { get; set; }
    public string Body { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; }
    public string? CreatedBy { get; set; }
    public DateTime? UpdatedAt { get; set; }
}

public class CrmTimelineItemResponse
{
    public string Kind { get; set; } = string.Empty;
    public DateTime At { get; set; }
    public string Title { get; set; } = string.Empty;
    public string? Body { get; set; }
    public Guid? RefId { get; set; }
    public string? Actor { get; set; }
    public Guid? SocialChannelId { get; set; }
    public string? ChannelName { get; set; }
}

public class CrmCsvPreviewRow
{
    public int LineNumber { get; set; }
    public string DisplayName { get; set; } = string.Empty;
    public string? PhoneRaw { get; set; }
    public string? PhoneE164 { get; set; }
    public string? Note { get; set; }
    public string Status { get; set; } = "create";
    public Guid? ExistingCustomerId { get; set; }
    public string? Message { get; set; }
}

public class CrmCsvPreviewResponse
{
    public int TotalRows { get; set; }
    public int CreateCount { get; set; }
    public int DuplicatePhoneCount { get; set; }
    public int InvalidCount { get; set; }
    public List<CrmCsvPreviewRow> Rows { get; set; } = [];
}

public class CrmCsvCommitResponse
{
    public int Created { get; set; }
    public int SkippedDuplicatePhone { get; set; }
    public int Invalid { get; set; }
    public List<Guid> CreatedCustomerIds { get; set; } = [];
}
