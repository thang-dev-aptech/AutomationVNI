using Backend.Shared.Storage;
using SixLabors.Fonts;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Drawing.Processing;
using SixLabors.ImageSharp.Formats.Jpeg;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;

namespace Backend.Modules.ContentCrawl;

public sealed record NewsCardResult(string StorageKey, int Width, int Height, long SizeBytes);

/// <summary>
/// Dựng ảnh "thẻ tin": tiêu đề chữ lớn nằm giữa một nền đã làm mờ và tối đi — đúng kiểu các
/// fanpage báo hay dùng.
///
/// KHÔNG gọi AI sinh ảnh: chỉ lấy một ảnh nền có sẵn trong kho rồi phủ chữ, nên gần như miễn
/// phí và chạy trong vài chục mili-giây. Đây là lý do nó khác hẳn luồng sinh banner AI.
/// </summary>
public class NewsCardRenderer(IFileStorageService fileStorage, ILogger<NewsCardRenderer> logger)
{
    private const int CardSize = 1080;
    private const float SidePadding = 90f;

    private static readonly string[] BoldFontCandidates =
    [
        "/System/Library/Fonts/Supplemental/Arial Bold.ttf",
        "/System/Library/Fonts/Supplemental/Arial.ttf",
        "/usr/share/fonts/truetype/dejavu/DejaVuSans-Bold.ttf",
        "/usr/share/fonts/truetype/dejavu/DejaVuSans.ttf",
        "C:\\Windows\\Fonts\\arialbd.ttf",
        "C:\\Windows\\Fonts\\arial.ttf",
    ];

    /// <param name="backgroundStorageKey">
    /// Ảnh nền lấy từ kho media. Không có thì dựng nền chuyển sắc — vẫn ra ảnh dùng được,
    /// không để tính năng chết chỉ vì kho ảnh trống.
    /// </param>
    public async Task<NewsCardResult?> RenderAsync(
        string headline, string? backgroundStorageKey, string outputFolder = "news-cards",
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(headline)) return null;

        if (ResolveBoldFont() is not FontFamily family)
        {
            logger.LogWarning("Không tìm thấy font để dựng thẻ tin — bỏ qua ảnh");
            return null;
        }

        try
        {
            using var canvas = await LoadBackgroundAsync(backgroundStorageKey, ct);

            // Làm mờ + phủ lớp tối. Độ tối tính THEO ĐỘ SÁNG THẬT của nền chứ không cố định:
            // ảnh tài liệu scan nền trắng mà chỉ phủ nhẹ thì chữ trắng chìm nghỉm không đọc nổi,
            // còn ảnh vốn đã tối mà phủ dày thì thành đen kịt.
            canvas.Mutate(ctx => ctx.GaussianBlur(12f));
            var alpha = ResolveOverlayAlpha(canvas);
            canvas.Mutate(ctx => ctx.Fill(Color.FromRgba(0, 0, 0, alpha)));

            DrawHeadline(canvas, headline.Trim(), family);

            using var output = new MemoryStream();
            await canvas.SaveAsync(output, new JpegEncoder { Quality = 88 }, ct);
            var bytes = output.ToArray();

            var saved = await fileStorage.SaveBytesAsync(bytes, outputFolder, ".jpg", "image/jpeg", ct);
            return new NewsCardResult(saved.StorageKey, canvas.Width, canvas.Height, bytes.Length);
        }
        catch (Exception ex)
        {
            // Ảnh chỉ là phần tô điểm — hỏng thì bài vẫn đăng được dạng chữ + link.
            logger.LogWarning(ex, "Dựng thẻ tin thất bại, bài sẽ đăng không kèm ảnh");
            return null;
        }
    }

    /// <summary>
    /// Đo độ sáng trung bình ở DẢI GIỮA ảnh (nơi sẽ đặt chữ) rồi suy ra độ dày lớp phủ đen.
    /// Lấy mẫu thưa cho nhanh — đo từng điểm của ảnh 1080x1080 là thừa.
    /// </summary>
    private static byte ResolveOverlayAlpha(Image<Rgba32> image)
    {
        var top = image.Height / 4;
        var bottom = image.Height * 3 / 4;
        double sum = 0;
        var count = 0;

        image.ProcessPixelRows(accessor =>
        {
            for (var y = top; y < bottom; y += 8)
            {
                var row = accessor.GetRowSpan(y);
                for (var x = 0; x < row.Length; x += 8)
                {
                    var p = row[x];
                    // Trọng số theo cảm nhận sáng của mắt người (Rec. 601).
                    sum += 0.299 * p.R + 0.587 * p.G + 0.114 * p.B;
                    count++;
                }
            }
        });

        if (count == 0) return 140;
        var luminance = sum / count;               // 0 (đen) … 255 (trắng)
        // Nền càng sáng phủ càng dày. Chặn hai đầu để không bao giờ quá nhạt hoặc đen kịt.
        var alpha = 90 + (luminance / 255.0) * 130;
        return (byte)Math.Clamp(alpha, 90, 215);
    }

    private async Task<Image<Rgba32>> LoadBackgroundAsync(string? key, CancellationToken ct)
    {
        if (!string.IsNullOrWhiteSpace(key) && await fileStorage.ExistsAsync(key, ct))
        {
            try
            {
                await using var stream = await fileStorage.OpenReadAsync(key, ct);
                var image = await Image.LoadAsync<Rgba32>(stream, ct);
                // Phủ kín khung vuông, cắt phần thừa thay vì bóp méo ảnh.
                image.Mutate(ctx => ctx.Resize(new ResizeOptions
                {
                    Size = new Size(CardSize, CardSize),
                    Mode = ResizeMode.Crop,
                }));
                return image;
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Không đọc được ảnh nền {Key}, dùng nền chuyển sắc", key);
            }
        }
        return BuildGradient();
    }

    /// <summary>Nền dự phòng: chuyển sắc xanh đậm sang tím, hợp tông thương hiệu và đủ tối để chữ nổi.</summary>
    private static Image<Rgba32> BuildGradient()
    {
        var image = new Image<Rgba32>(CardSize, CardSize);
        image.Mutate(ctx => ctx.Fill(
            new LinearGradientBrush(
                new PointF(0, 0), new PointF(CardSize, CardSize), GradientRepetitionMode.None,
                new ColorStop(0f, Color.FromRgb(22, 42, 82)),
                new ColorStop(1f, Color.FromRgb(78, 26, 92)))));
        return image;
    }

    /// <summary>
    /// Ngắt dòng thủ công theo bề rộng ĐO ĐƯỢC của từng từ, rồi vẽ cả khối vào giữa ảnh.
    /// Tự ngắt bằng cách đếm ký tự sẽ lệch nặng với tiếng Việt có dấu.
    /// </summary>
    private static void DrawHeadline(Image<Rgba32> canvas, string headline, FontFamily family)
    {
        var maxWidth = canvas.Width - SidePadding * 2;

        // Tiêu đề dài thì hạ cỡ chữ để không tràn quá nửa ảnh.
        var fontSize = headline.Length switch
        {
            <= 45 => 74f,
            <= 70 => 64f,
            <= 100 => 54f,
            _ => 46f,
        };
        var font = family.CreateFont(fontSize, FontStyle.Bold);
        var lines = WrapLines(headline, font, maxWidth);

        var lineHeight = fontSize * 1.32f;
        var blockHeight = lines.Count * lineHeight;
        var startY = (canvas.Height - blockHeight) / 2f;

        canvas.Mutate(ctx =>
        {
            for (var i = 0; i < lines.Count; i++)
            {
                var options = new RichTextOptions(font)
                {
                    Origin = new PointF(canvas.Width / 2f, startY + i * lineHeight),
                    HorizontalAlignment = HorizontalAlignment.Center,
                    VerticalAlignment = VerticalAlignment.Top,
                };
                // Vẽ bóng đen lệch 2px trước để chữ trắng vẫn đọc được trên nền sáng.
                var shadow = new RichTextOptions(options)
                {
                    Origin = new PointF(options.Origin.X + 2f, options.Origin.Y + 2f),
                };
                ctx.DrawText(shadow, lines[i], Color.FromRgba(0, 0, 0, 160));
                ctx.DrawText(options, lines[i], Color.White);
            }
        });
    }

    private static List<string> WrapLines(string text, Font font, float maxWidth)
    {
        var words = text.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        var lines = new List<string>();
        var current = string.Empty;

        foreach (var word in words)
        {
            var candidate = current.Length == 0 ? word : $"{current} {word}";
            var width = TextMeasurer.MeasureAdvance(candidate, new TextOptions(font)).Width;
            if (width <= maxWidth || current.Length == 0)
            {
                current = candidate;
            }
            else
            {
                lines.Add(current);
                current = word;
            }
        }
        if (current.Length > 0) lines.Add(current);

        // Quá 6 dòng thì cắt bớt — tiêu đề dài thế thì đằng nào cũng không ai đọc trên ảnh.
        if (lines.Count > 6)
        {
            lines = lines.Take(6).ToList();
            lines[^1] = lines[^1].TrimEnd('.', ',') + "…";
        }
        return lines;
    }

    private static FontFamily? ResolveBoldFont()
    {
        foreach (var path in BoldFontCandidates)
        {
            if (!File.Exists(path)) continue;
            try
            {
                var collection = new FontCollection();
                return collection.Add(path);
            }
            catch { /* thử font kế tiếp */ }
        }
        return SystemFonts.Families.FirstOrDefault() is { } fallback ? fallback : null;
    }
}
