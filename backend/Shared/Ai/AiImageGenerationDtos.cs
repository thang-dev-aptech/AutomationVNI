namespace Backend.Shared.Ai;

/// <summary>
/// Ảnh tham chiếu gửi kèm prompt (logo thương hiệu, ảnh mẫu bố cục...).
/// Model nhìn thấy ảnh thật thay vì đoán từ mô tả chữ.
/// </summary>
public class AiImageReferenceImage
{
    public byte[] Bytes { get; set; } = [];
    public string MimeType { get; set; } = "image/png";

    /// <summary>Nhãn ngắn để log/debug — không gửi lên provider.</summary>
    public string Label { get; set; } = string.Empty;
}

public class AiImageGenerationRequest
{
    public string Prompt { get; set; } = string.Empty;
    public string? Provider { get; set; }
    public string? Model { get; set; }

    /// <summary>Ảnh tham chiếu gửi kèm; rỗng = sinh ảnh thuần từ text.</summary>
    public List<AiImageReferenceImage> ReferenceImages { get; set; } = [];
}

public class AiImageGenerationResult
{
    public byte[] ImageBytes { get; set; } = [];
    public string MimeType { get; set; } = "image/png";
    public string Provider { get; set; } = string.Empty;
    public string Model { get; set; } = string.Empty;
}
