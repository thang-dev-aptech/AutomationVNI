namespace Backend.Shared;

/// <summary>
/// Cấu hình worker sinh nội dung nền (bulk). MaxConcurrency thấp để tránh 429 từ AI provider.
/// </summary>
public class GenerationWorkerOptions
{
    public bool Enabled { get; set; } = true;
    public int IntervalSeconds { get; set; } = 10;
    public int BatchSize { get; set; } = 20;
    public int MaxConcurrency { get; set; } = 2;
}
