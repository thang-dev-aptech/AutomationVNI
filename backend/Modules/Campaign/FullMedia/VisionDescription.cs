namespace Backend.Modules.Campaign.FullMedia;

/// <summary>Mô tả ảnh tiếng Việt có cấu trúc — đầu vào cho bộ chấm điểm Full Media.</summary>
public sealed record VisionDescription(
    string Topic,
    string Audience,
    string Tone,
    string Setting,
    string TextInImage,
    IReadOnlyList<string> Keywords,
    double Confidence,
    bool Unclear,
    string Model)
{
    /// <summary>Văn bản tiếng Việt cố định định dạng để gửi bộ chấm điểm (không gửi ảnh).</summary>
    public string ToState()
    {
        var keywords = Keywords.Count == 0 ? string.Empty : string.Join(", ", Keywords);
        return string.Join('\n', new[]
        {
            $"Chủ đề: {Topic}",
            $"Đối tượng: {Audience}",
            $"Tông: {Tone}",
            $"Bối cảnh: {Setting}",
            $"Chữ trong ảnh: {TextInImage}",
            $"Từ khoá: {keywords}",
        });
    }
}
