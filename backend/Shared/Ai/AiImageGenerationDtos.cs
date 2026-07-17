namespace Backend.Shared.Ai;

public class AiImageGenerationRequest
{
    public string Prompt { get; set; } = string.Empty;
    public string? Provider { get; set; }
    public string? Model { get; set; }
}

public class AiImageGenerationResult
{
    public byte[] ImageBytes { get; set; } = [];
    public string MimeType { get; set; } = "image/png";
    public string Provider { get; set; } = string.Empty;
    public string Model { get; set; } = string.Empty;
}
