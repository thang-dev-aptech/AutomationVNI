using Backend.Shared;

namespace Backend.Modules.Crm.Opportunities;

public enum CrmOpportunityStageKind
{
    Open = 1,
    Won = 2,
    Lost = 3
}

public enum CrmOpportunityStatus
{
    Open = 1,
    Won = 2,
    Lost = 3
}

public enum CrmOpportunitySource
{
    Manual = 1,
    Message = 2,
    Comment = 3
}

/// <summary>Id giai đoạn seed cố định (migration HasData).</summary>
public static class CrmOpportunityStageIds
{
    public static readonly Guid Moi = Guid.Parse("11111111-1111-1111-1111-111111111101");
    public static readonly Guid DuDieuKien = Guid.Parse("11111111-1111-1111-1111-111111111102");
    public static readonly Guid BamDuoi = Guid.Parse("11111111-1111-1111-1111-111111111103");
    public static readonly Guid DamPhanChot = Guid.Parse("11111111-1111-1111-1111-111111111104");
    public static readonly Guid DaMua = Guid.Parse("11111111-1111-1111-1111-111111111105");
    public static readonly Guid ThatBai = Guid.Parse("11111111-1111-1111-1111-111111111106");
}

public class CrmOpportunityStageModel : BaseEntity
{
    public string Name { get; set; } = string.Empty;
    public string Color { get; set; } = "#6B7280";
    public int SortOrder { get; set; }
    public CrmOpportunityStageKind Kind { get; set; } = CrmOpportunityStageKind.Open;
}

public class CrmOpportunityModel : BaseEntity
{
    public Guid CrmCustomerId { get; set; }
    public string Title { get; set; } = string.Empty;
    public Guid StageId { get; set; }
    public CrmOpportunityStatus Status { get; set; } = CrmOpportunityStatus.Open;
    public bool IsArchived { get; set; }
    public Guid? AssigneeUserId { get; set; }
    public string? AssignedTo { get; set; }
    public CrmOpportunitySource Source { get; set; } = CrmOpportunitySource.Manual;
    public Guid? SocialChannelId { get; set; }
    public Guid? PageConversationId { get; set; }
    public Guid? SocialCommentId { get; set; }
    public decimal ExpectedValue { get; set; }
    public string? LostReason { get; set; }
    public DateTime? ClosedAtUtc { get; set; }
    public DateTime? LastActivityAtUtc { get; set; }
}

public class CrmOpportunityWatcherModel : BaseEntity
{
    public Guid OpportunityId { get; set; }
    public Guid UserId { get; set; }
}
