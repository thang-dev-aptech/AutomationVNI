using Backend.Modules.ContentCrawl;
using SixLabors.Fonts;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Drawing.Processing;
using SixLabors.ImageSharp.Formats.Jpeg;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;

namespace Backend.Modules.NewsSite;

/// <summary>
/// Dựng ảnh og:image 1200×630 cho từng bài: tít chữ trắng trên nền chuyển sắc theo chuyên mục.
///
/// Vì sao phải có: thiếu og:image thì bài share lên fanpage ra một ô xám không ảnh, và đó
/// chính là thứ quyết định có ai bấm vào hay không.
///
/// Vì sao KHÔNG dùng ảnh của toà soạn: ảnh gốc thuộc bản quyền báo — đăng lại lên trang mình
/// là rủi ro rõ ràng hơn cả phần chữ. Đây cũng là lý do ContentCrawl.ImportThumbnails mặc
/// định false.
///
/// Vì sao KHÔNG dùng NewsCardRenderer sẵn có: nó cố định 1080×1080 (khung vuông cho bài
/// Facebook) và gắn với IFileStorageService, trong khi ở đây cần 1200×630 ghi thẳng ra thư
/// mục nginx. Sửa nó thành hai chế độ thì rối hơn viết riêng.
/// </summary>
public class NewsOgImageService(ILogger<NewsOgImageService> logger)
{
    private const int W = 1200;
    private const int H = 630;
    private const float Pad = 72f;

    /// <summary>Trùng bảng màu .t-* trong VNINews/assets/styles.css để ảnh và web cùng tông.</summary>
    private static (Rgba32 From, Rgba32 To) Palette(string? slug) => slug switch
    {
        "giao-duc" => (Hex(0x2D6CB6), Hex(0x1E4E82)),
        "phap-luat-chinh-sach" => (Hex(0x8A1F2B), Hex(0x4A0E15)),
        "cong-nghe-ai" => (Hex(0x1E4E82), Hex(0x5B21B6)),
        "ky-nang" => (Hex(0xF08A22), Hex(0xC2610A)),
        _ => (Hex(0x556274), Hex(0x2B3441)),
    };

    private static readonly string[] FontCandidates =
    [
        "/System/Library/Fonts/Supplemental/Arial Bold.ttf",
        "/System/Library/Fonts/Supplemental/Arial.ttf",
        "/usr/share/fonts/truetype/dejavu/DejaVuSans-Bold.ttf",
        "/usr/share/fonts/truetype/dejavu/DejaVuSans.ttf",
        @"C:\Windows\Fonts\arialbd.ttf",
        @"C:\Windows\Fonts\arial.ttf",
    ];

    /// <summary>
    /// Trả false khi không dựng được. Nơi gọi cứ đi tiếp — thẻ share không ảnh còn hơn thẻ
    /// share đầy ô vuông vì font thiếu glyph tiếng Việt.
    /// </summary>
    public bool TryRender(string headline, string? categorySlug, string outputPath)
    {
        if (string.IsNullOrWhiteSpace(headline)) return false;

        if (ResolveFont() is not FontFamily family)
        {
            // Rơi vào đây nghĩa là máy không có font nào trong danh sách. KHÔNG lấy bừa font
            // hệ thống đầu tiên: font đó có thể thiếu glyph tiếng Việt và ảnh ra toàn ô vuông
            // mà không có lỗi nào — hỏng câm đúng nghĩa.
            logger.LogError("Không tìm thấy font để dựng og:image — bỏ ảnh cho bài này");
            return false;
        }

        try
        {
            var (from, to) = Palette(NewsTaxonomy.Resolve(categorySlug).Slug);

            using var img = new Image<Rgba32>(W, H);
            img.Mutate(ctx => ctx.Fill(
                new LinearGradientBrush(
                    new PointF(0, 0), new PointF(W, H), GradientRepetitionMode.None,
                    new ColorStop(0f, from), new ColorStop(1f, to))));

            var font = FitFont(family, headline);
            var opts = new RichTextOptions(font)
            {
                Origin = new PointF(Pad, H / 2f),
                WrappingLength = W - Pad * 2,
                HorizontalAlignment = HorizontalAlignment.Left,
                VerticalAlignment = VerticalAlignment.Center,
                LineSpacing = 1.22f,
            };
            img.Mutate(ctx => ctx.DrawText(opts, headline, Color.White));

            // Dải ba màu thương hiệu ở chân ảnh — chữ ký nhận diện, giống dải trên đầu trang web.
            var band = H - 10;
            img.Mutate(ctx => ctx
                .Fill(Hex(0x2D6CB6), new RectangleF(0, band, W / 3f, 10))
                .Fill(Hex(0xF08A22), new RectangleF(W / 3f, band, W / 3f, 10))
                .Fill(Hex(0x33A457), new RectangleF(W * 2f / 3f, band, W / 3f, 10)));

            Directory.CreateDirectory(Path.GetDirectoryName(outputPath)!);

            // Ghi tạm cùng thư mục rồi đổi tên, cùng lý do với file HTML: Facebook cache ảnh
            // rất lâu, một file JPEG cụt sẽ dính lại nhiều ngày.
            var tmp = outputPath + ".tmp";
            using (var fs = File.Create(tmp)) img.Save(fs, new JpegEncoder { Quality = 82 });
            File.Move(tmp, outputPath, overwrite: true);
            return true;
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Dựng og:image thất bại cho \"{Headline}\"", headline);
            return false;
        }
    }

    /// <summary>
    /// Tít dài thì hạ cỡ chữ. Không hạ thì câu dài tràn khỏi khung và bị cắt mất đuôi — trên
    /// thẻ share thì đó là mất luôn phần quan trọng nhất của tiêu đề.
    /// </summary>
    private static Font FitFont(FontFamily family, string headline) => headline.Length switch
    {
        <= 45 => family.CreateFont(62f, FontStyle.Bold),
        <= 70 => family.CreateFont(52f, FontStyle.Bold),
        <= 95 => family.CreateFont(44f, FontStyle.Bold),
        _ => family.CreateFont(38f, FontStyle.Bold),
    };

    private static FontFamily? ResolveFont()
    {
        foreach (var path in FontCandidates)
        {
            if (!File.Exists(path)) continue;
            try { return new FontCollection().Add(path); }
            catch (FontException) { }
        }
        return null;
    }

    private static Rgba32 Hex(uint rgb)
        => new((byte)(rgb >> 16), (byte)(rgb >> 8), (byte)rgb);
}
