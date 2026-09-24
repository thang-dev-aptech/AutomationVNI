using Backend.Shared;

namespace Backend.Modules.ContentCrawl;

/// <summary>
/// Trạng thái dừng/chạy dùng chung cho toàn bộ pipeline cào tin.
/// Bảng này luôn chỉ có một dòng với <see cref="SingletonId"/>.
/// </summary>
public class ContentCrawlPipelineStateModel : BaseEntity
{
    public static readonly Guid SingletonId = Guid.Parse("8dff99ee-b1a0-4ff6-b7a5-ea9f32b13e63");

    public bool IsEnabled { get; set; } = true;
    public string? UpdatedByUserName { get; set; }
}
