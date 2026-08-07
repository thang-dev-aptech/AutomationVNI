namespace Backend.Modules.ContentCrawl;

/// <summary>Một chuyên mục của trang tin. <paramref name="Hint"/> chỉ dùng để dạy AI chọn đúng.</summary>
public sealed record NewsCategory(string Slug, string Name, string Hint);

/// <summary>
/// Danh sách chuyên mục ĐÓNG cho tin cào.
///
/// Vì sao đặt trong CODE chứ không lấy từ bảng Categories:
///   1. Prompt phải liệt kê đúng danh sách này để AI chọn — lấy từ DB thì prompt đổi theo dữ
///      liệu và không ai kiểm soát được model đang thấy gì.
///   2. Slug là MỘT PHẦN CỦA ĐƯỜNG DẪN trang tĩnh (/chuyen-muc/{slug}/). Ai đó sửa slug trên
///      giao diện là mọi trang HTML đã sinh trỏ vào hư không — hỏng câm, không lỗi nào, chỉ có
///      link chết và thứ hạng Google tụt.
///
/// Bảng Categories vẫn được ghi (để Post.CategoryId có khoá thật), nhưng nguồn chân lý là đây.
///
/// Giai đoạn 1 chốt 3 chuyên mục theo phạm vi khách chọn. Thêm mục mới thì thêm vào đây rồi
/// chạy lại seeder — KHÔNG sửa slug của mục đã có.
/// </summary>
public static class NewsTaxonomy
{
    /// <summary>Van xả: tin đúng chủ đề chung nhưng không thuộc mục nào. Không bao giờ bỏ.</summary>
    public const string OtherSlug = "khac";

    public static readonly IReadOnlyList<NewsCategory> All =
    [
        new("giao-duc", "Giáo dục",
            "chính sách giáo dục, tuyển sinh, thi cử, điểm chuẩn, học phí, chương trình học, "
            + "trường lớp, giáo viên, du học"),

        new("cong-nghe-ai", "AI & Công nghệ",
            "trí tuệ nhân tạo, chuyển đổi số, phần mềm, thiết bị, an toàn thông tin, "
            + "công nghệ ứng dụng trong dạy và học"),

        new("ky-nang", "Kỹ năng",
            "kỹ năng nghề, kỹ năng mềm, hướng nghiệp, việc làm, thị trường lao động, "
            + "chứng chỉ nghề, đào tạo ngắn hạn"),

        new(OtherSlug, "Khác",
            "không thuộc ba mục trên"),
    ];

    public static NewsCategory? Find(string? slug)
        => string.IsNullOrWhiteSpace(slug)
            ? null
            : All.FirstOrDefault(c => c.Slug.Equals(slug.Trim(), StringComparison.OrdinalIgnoreCase));

    /// <summary>Slug lạ thì về "khac" — không bao giờ trả null để nơi gọi khỏi phải đoán.</summary>
    public static NewsCategory Resolve(string? slug) => Find(slug) ?? All.Last();

    /// <summary>Khối liệt kê chèn vào prompt. Sinh từ danh sách nên không bao giờ lệch với code.</summary>
    public static string PromptBlock()
        => string.Join("\n", All.Select(c => $"- {c.Slug}: {c.Hint}"));

    /// <summary>Chuyên mục hiện trên thanh điều hướng — bỏ "khac" vì nó là van xả, không phải mục.</summary>
    public static IEnumerable<NewsCategory> Navigable()
        => All.Where(c => c.Slug != OtherSlug);
}
