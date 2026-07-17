namespace Backend.Modules.Post.Enums;

public enum PostStatus
{
    Draft          = 1,
    Queued         = 2,
    Generating     = 3,
    Ready          = 4,
    Scheduled      = 5,
    Publishing     = 6,
    Published      = 7,
    Failed         = 8,
    Cancelled      = 9,
    WaitingReview  = 10,
    Approved       = 11,
    GeneratingMedia = 12,
    NeedMedia       = 13,
    RenderingTemplate = 14,
    NeedFix           = 15
}

public enum GenerationFlow
{
    FullAI = 1,
    RAG    = 2
}
