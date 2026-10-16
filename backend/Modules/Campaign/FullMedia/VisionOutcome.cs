namespace Backend.Modules.Campaign.FullMedia;

/// <summary>
/// Kết luận khả năng dùng mô tả Vision.
/// <see cref="Usable"/> = dùng được để chấm điểm;
/// <see cref="Unusable"/> = không bao giờ đăng (unclear / lệch tag / ảnh quá lớn…);
/// <see cref="Failed"/> = lỗi tạm hoặc cấu hình (có thể thử lại sau).
/// </summary>
public enum VisionUsability
{
    Usable = 1,
    Unusable = 2,
    Failed = 3,
}

/// <summary>
/// Kết quả <see cref="MediaVisionService.GetUsableAsync"/>.
/// <see cref="Status"/> là <see cref="VisionUsability"/> { Usable, Unusable, Failed };
/// <see cref="Description"/> có thể null (Failed / một số Unusable);
/// <see cref="ReasonCode"/> là mã lý do ngắn (vd. unclear_or_low_confidence, image_too_large).
/// </summary>
public sealed record VisionOutcome(
    VisionUsability Status,
    VisionDescription? Description,
    string? ReasonCode = null);
