using Backend.Modules.SocialChannel.Enums;
using Backend.Shared;

namespace Backend.Modules.Crm.Opportunities;

public class CrmOpportunityFilterRequest : PagedFilterRequest
{
    public Guid? StageId { get; set; }
    public CrmOpportunityStatus? Status { get; set; }
    public bool? IsArchived { get; set; }
    /// <summary>mine | unassigned | user Guid string.</summary>
    public string? AssigneeFilter { get; set; }
    public Guid? AssigneeUserId { get; set; }
    public bool? AssignedMine { get; set; }
    public bool? UnassignedOnly { get; set; }
    public Guid? SocialChannelId { get; set; }
    public CrmOpportunitySource? Source { get; set; }
    public DateTime? CreatedFrom { get; set; }
    public DateTime? CreatedTo { get; set; }
}

public class CrmOpportunityListItemResponse
{
    public Guid Id { get; set; }
    public string Title { get; set; } = string.Empty;
    public Guid CrmCustomerId { get; set; }
    public string? CustomerName { get; set; }
    public string? CustomerPhoneE164 { get; set; }
    public Guid StageId { get; set; }
    public string? StageName { get; set; }
    public string? StageColor { get; set; }
    public CrmOpportunityStageKind StageKind { get; set; }
    public CrmOpportunityStatus Status { get; set; }
    public bool IsArchived { get; set; }
    public Guid? AssigneeUserId { get; set; }
    public string? AssignedTo { get; set; }
    public IReadOnlyList<Guid> WatcherUserIds { get; set; } = [];
    public decimal ExpectedValue { get; set; }
    public CrmOpportunitySource Source { get; set; }
    public Guid? SocialChannelId { get; set; }
    public string? ChannelName { get; set; }
    public SocialPlatform? ChannelPlatform { get; set; }
    public Guid? PageConversationId { get; set; }
    public Guid? SocialCommentId { get; set; }
    public DateTime? LastActivityAtUtc { get; set; }
    public string? SourceSnippet { get; set; }
    public DateTime CreatedAt { get; set; }
}

public class CrmOpportunityDetailResponse : CrmOpportunityListItemResponse
{
    public string? LostReason { get; set; }
    public DateTime? ClosedAtUtc { get; set; }
    public DateTime? UpdatedAt { get; set; }
}

public class CreateCrmOpportunityRequest
{
    public Guid CrmCustomerId { get; set; }
    public string Title { get; set; } = string.Empty;
    public Guid? StageId { get; set; }
    public Guid? AssigneeUserId { get; set; }
    public decimal ExpectedValue { get; set; }
    public CrmOpportunitySource Source { get; set; } = CrmOpportunitySource.Manual;
    public Guid? SocialChannelId { get; set; }
    public Guid? PageConversationId { get; set; }
    public Guid? SocialCommentId { get; set; }
    public IReadOnlyList<Guid>? WatcherUserIds { get; set; }
}

public class UpdateCrmOpportunityRequest
{
    public string Title { get; set; } = string.Empty;
    public decimal ExpectedValue { get; set; }
    public Guid? AssigneeUserId { get; set; }
    public string? LostReason { get; set; }
}

public class MoveCrmOpportunityStageRequest
{
    public Guid StageId { get; set; }
    public string? LostReason { get; set; }
}

public class AssignCrmOpportunityRequest
{
    public Guid? AssigneeUserId { get; set; }
    public string? AssignedTo { get; set; }
}

public class CrmOpportunityFromConversationRequest
{
    /// <summary>message | comment</summary>
    public string Kind { get; set; } = "message";
    public Guid Id { get; set; }
    public string? Title { get; set; }
}

public class CrmOpportunityStatsResponse
{
    public int Total { get; set; }
    public int Open { get; set; }
    public int Won { get; set; }
    public int Lost { get; set; }
    public int Activity { get; set; }
    public decimal Rev { get; set; }
}

public class CrmOpportunityPipelineRequest
{
    public CrmOpportunityFilterRequest? Filters { get; set; }
    public int PerStage { get; set; } = 20;
}

public class CrmOpportunityPipelineStageColumn
{
    public Guid StageId { get; set; }
    public string StageName { get; set; } = string.Empty;
    public string StageColor { get; set; } = string.Empty;
    public CrmOpportunityStageKind Kind { get; set; }
    public int SortOrder { get; set; }
    public int Total { get; set; }
    public IReadOnlyList<CrmOpportunityListItemResponse> Items { get; set; } = [];
}

public class CrmOpportunityPipelineResponse
{
    public IReadOnlyList<CrmOpportunityPipelineStageColumn> Columns { get; set; } = [];
}

public class CrmOpportunityPipelinePageRequest : PagedFilterRequest
{
    public CrmOpportunityFilterRequest? Filters { get; set; }
}

public class CrmOpportunityStageResponse
{
    public Guid Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Color { get; set; } = string.Empty;
    public int SortOrder { get; set; }
    public CrmOpportunityStageKind Kind { get; set; }
}

public class CreateCrmOpportunityStageRequest
{
    public string Name { get; set; } = string.Empty;
    public string Color { get; set; } = "#6B7280";
    public CrmOpportunityStageKind Kind { get; set; } = CrmOpportunityStageKind.Open;
    public int? SortOrder { get; set; }
}

public class UpdateCrmOpportunityStageRequest
{
    public string Name { get; set; } = string.Empty;
    public string Color { get; set; } = "#6B7280";
    public CrmOpportunityStageKind Kind { get; set; } = CrmOpportunityStageKind.Open;
}

public class ReorderCrmOpportunityStagesRequest
{
    public IReadOnlyList<Guid> StageIds { get; set; } = [];
}
