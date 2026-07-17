namespace Backend.Modules.PublishLog.Enums;

public enum PublishStatus
{
    Pending     = 0,
    Success     = 1,
    Failed      = 2,
    RateLimited = 3,
    Cancelled   = 4,
    Processing  = 5,
    DeadLetter  = 6
}
