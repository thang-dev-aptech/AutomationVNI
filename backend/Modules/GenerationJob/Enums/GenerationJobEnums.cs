namespace Backend.Modules.GenerationJob.Enums;

public enum JobType
{
    TextGeneration  = 1,
    ImageGeneration = 2,
    ImageOverlay    = 3,
    MediaMatch      = 4,
    Publish         = 5
}

public enum JobStatus
{
    Pending    = 1,
    Processing = 2,
    Completed  = 3,
    Failed     = 4,
    Retry      = 5,
    Cancelled  = 6,
    DeadLetter = 7
}

public enum JobFlowType
{
    FullAI   = 1,
    RAG      = 2,
    Fallback = 3
}
