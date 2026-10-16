namespace Backend.Modules.Campaign.FullMedia;

public enum CampaignMediaEvaluationStatus
{
    Pending = 1,
    Scoring = 2,
    Verifying = 3,
    Accepted = 4,
    Rejected = 5,
    Unusable = 6,
    Failed = 7,
}
