using System.Globalization;
using System.Text;
using Backend.Shared.Storage;
using SkiaSharp;
using Topten.RichTextKit;

namespace Backend.Modules.MediaAsset;

public class RichTemplateRenderService(IFileStorageService fileStorage) : IImageOverlayService
{
    private const string MainFamily = "Inter";
    private const string EmojiFamily = "AppEmoji";

    private static readonly SKColor DefaultNavy = new(10, 40, 70);

    /// <summary>Bảng màu khung chữ cho 1 lần render — bám theo PageContext.BrandColors, chữ tự đổi đen/trắng theo độ tương phản.</summary>
    private readonly record struct BrandPalette(
        SKColor Primary, SKColor Secondary, SKColor? Accent,
        SKColor TextPrimary, SKColor TextSecondary);

    private static BrandPalette ResolvePalette(string? brandColors)
    {
        var colors = ParseHexColors(brandColors);
        var primary = colors.Count > 0 ? colors[0] : DefaultNavy;
        var secondary = colors.Count > 1 ? colors[1] : primary;
        var accent = colors.Count > 2 ? colors[2] : (SKColor?)null;
        var (textPrimary, textSecondary) = ContrastTextColors(primary);
        return new BrandPalette(primary, secondary, accent, textPrimary, textSecondary);
    }

    /// <summary>
    /// "Secondary"/"Accent" luôn an toàn kể cả trang chỉ cấu hình 1-2 màu — <see cref="ResolvePalette"/>
    /// đã tự fallback Secondary/Accent về Primary khi thiếu, và <c>?? palette.Primary</c> ở đây bắt buộc
    /// vì Accent là <see cref="SKColor"/>? (nullable), truy cập .Value trực tiếp sẽ ném exception.
    /// </summary>
    private static SKColor ResolveColorSlot(BrandPalette palette, string? slot) => slot switch
    {
        "Secondary" => palette.Secondary,
        "Accent" => palette.Accent ?? palette.Primary,
        _ => palette.Primary,
    };

    private static List<SKColor> ParseHexColors(string? raw)
    {
        var result = new List<SKColor>();
        if (string.IsNullOrWhiteSpace(raw)) return result;
        foreach (var token in raw.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries))
            if (SKColor.TryParse(token, out var color)) result.Add(color);
        return result;
    }

    /// <summary>Luminance kiểu ITU-R BT.601 — nền sáng dùng chữ đen, nền tối dùng chữ trắng, luôn đọc được bất kể brand color gì.</summary>
    private static (SKColor primary, SKColor secondary) ContrastTextColors(SKColor bg)
    {
        var luminance = 0.299f * bg.Red + 0.587f * bg.Green + 0.114f * bg.Blue;
        return luminance > 150
            ? (new SKColor(20, 20, 20), new SKColor(20, 20, 20, 200))
            : (SKColors.White, new SKColor(255, 255, 255, 225));
    }

    /// <summary>
    /// Bản TỐI hơn của màu, dành riêng cho scrim/quầng sáng ĐÈ LÊN ẢNH (không đụng tới footer/panel —
    /// những chỗ đó là nền ĐẶC, ContrastTextColors đã tự chọn đúng chữ đen/trắng nên màu sáng vẫn đọc
    /// tốt). Vấn đề chỉ nằm ở scrim bán trong suốt: hạ opacity (xem DrawVerticalScrim) làm ảnh gốc lộ rõ
    /// hơn — đẹp hơn với màu tối (brand color chính thường tối), nhưng với màu SÁNG (cam/vàng/xanh lá
    /// nhạt) thì chữ đen chọn theo luminance màu gốc lại chìm vào vùng ảnh sáng bên dưới. Trộn cố định
    /// 55% về đen — luminance kết quả luôn ≤ 255*0.45=114.75, chắc chắn dưới ngưỡng 150 của
    /// ContrastTextColors nên chữ sẽ luôn tự đổi sang TRẮNG khi hàm này thực sự đổi màu. Màu vốn đã tối
    /// (luminance ≤ 150) trả về nguyên vẹn — không đổi gì so với trước.
    /// </summary>
    private static SKColor DarkenIfLight(SKColor color)
    {
        var luminance = 0.299f * color.Red + 0.587f * color.Green + 0.114f * color.Blue;
        if (luminance <= 150) return color;
        const float mix = 0.55f;
        return new SKColor(
            (byte)(color.Red * (1 - mix)),
            (byte)(color.Green * (1 - mix)),
            (byte)(color.Blue * (1 - mix)),
            color.Alpha);
    }

    private static readonly object FontLock = new();
    private static bool _fontsInitialized;
    private static SKTypeface? _mainTypeface;
    private static SKTypeface? _mainBoldTypeface;
    private static SKTypeface? _emojiTypeface;

    private static void InitializeFonts()
    {
        if (_fontsInitialized) return;
        lock (FontLock)
        {
            if (_fontsInitialized) return;

            var resourcesDir = Path.Combine(Directory.GetCurrentDirectory(), "Resources");
            var mainFontPath = Path.Combine(resourcesDir, "Inter.ttf");
            var mainBoldFontPath = Path.Combine(resourcesDir, "Inter-Bold.ttf");
            var emojiFontPath = Path.Combine(resourcesDir, "NotoColorEmoji.ttf");

            if (File.Exists(mainFontPath))
                _mainTypeface = SKTypeface.FromFile(mainFontPath);
            if (File.Exists(mainBoldFontPath))
                _mainBoldTypeface = SKTypeface.FromFile(mainBoldFontPath);
            if (File.Exists(emojiFontPath))
                _emojiTypeface = SKTypeface.FromFile(emojiFontPath);

            // Đăng ký thật vào RichTextKit — trước đây chỉ reset về FontMapper gốc nên
            // "Inter"/emoji không bao giờ được nạp, luôn fallback font hệ thống.
            FontMapper.Default = new AppFontMapper(_mainTypeface, _mainBoldTypeface, _emojiTypeface);
            _fontsInitialized = true;
        }
    }

    private sealed class AppFontMapper(SKTypeface? main, SKTypeface? mainBold, SKTypeface? emoji) : FontMapper
    {
        public override SKTypeface TypefaceFromStyle(IStyle style, bool ignoreFontVariants)
        {
            if (emoji != null && string.Equals(style.FontFamily, EmojiFamily, StringComparison.Ordinal))
                return emoji;
            if (string.Equals(style.FontFamily, MainFamily, StringComparison.Ordinal))
            {
                // Inter.ttf/Inter-Bold.ttf là 2 file weight tĩnh riêng — chọn bản đậm khi Style yêu cầu
                // weight >= 600 (khớp bullet/headline/org line), còn lại dùng bản Regular.
                if (style.FontWeight >= 600 && mainBold != null) return mainBold;
                if (main != null) return main;
            }
            return base.TypefaceFromStyle(style, ignoreFontVariants);
        }
    }

    public async Task<ImageOverlayResult> RenderAsync(ImageOverlayRequest request, CancellationToken ct = default)
    {
        InitializeFonts();

        if (string.IsNullOrWhiteSpace(request.SourceStorageKey))
            throw new ArgumentException("SourceStorageKey không hợp lệ");

        await using var sourceStream = await fileStorage.OpenReadAsync(request.SourceStorageKey, ct);
        using var memoryStream = new MemoryStream();
        await sourceStream.CopyToAsync(memoryStream, ct);
        var sourceBytes = memoryStream.ToArray();

        using var bitmap = SKBitmap.Decode(sourceBytes);
        if (bitmap == null) throw new InvalidOperationException("Không thể decode ảnh nguồn");

        var targetWidth = bitmap.Width;
        var targetHeight = bitmap.Height;
        if (targetWidth > 1920 || targetHeight > 1920)
        {
            float scale = 1920f / Math.Max(targetWidth, targetHeight);
            targetWidth = (int)(targetWidth * scale);
            targetHeight = (int)(targetHeight * scale);
        }

        using var logo = await LoadLogoAsync(request.LogoStorageKey, ct);
        var palette = ResolvePalette(request.BrandColors);

        // Phân giải màu theo colorSlot MỘT LẦN DUY NHẤT ở đây rồi truyền BrandPalette đã thay màu xuống
        // cả 3 hàm vẽ — nhờ đó mọi chỗ trong file vẫn đọc palette.Primary/TextPrimary/TextSecondary như
        // cũ, không cần sửa từng nơi dùng rải rác. Tính lại ContrastTextColors theo slotColor (không
        // phải theo Primary gốc) — nếu giữ nguyên màu chữ đã chọn theo độ tương phản của màu CŨ mà đổi
        // nền sang màu khác, chữ có thể hoá khó đọc trên nền mới.
        var slotColor = ResolveColorSlot(palette, request.ColorSlot);
        var (slotTextPrimary, slotTextSecondary) = ContrastTextColors(slotColor);
        var effectivePalette = palette with
        {
            Primary = slotColor, TextPrimary = slotTextPrimary, TextSecondary = slotTextSecondary
        };

        // Palette RIÊNG cho vùng scrim/quầng sáng đè lên ảnh (headline/subtitle/bullet) — footer vẫn
        // dùng effectivePalette nguyên vẹn (xem DarkenIfLight). Khi slotColor đã tối, 2 palette này giống
        // hệt nhau nên không đổi gì so với trước.
        var scrimColor = DarkenIfLight(slotColor);
        var (scrimTextPrimary, scrimTextSecondary) = ContrastTextColors(scrimColor);
        var scrimPalette = effectivePalette with
        {
            Primary = scrimColor, TextPrimary = scrimTextPrimary, TextSecondary = scrimTextSecondary
        };

        var imageInfo = new SKImageInfo(targetWidth, targetHeight);
        using var surface = SKSurface.Create(imageInfo);
        var canvas = surface.Canvas;

        var layout = request.LayoutStyle ?? "TopBottomSplit";

        if (layout == "SolidPanelSplit")
        {
            // Layout này tự vẽ nền panel + ảnh (nửa ảnh cắt "cover") riêng — KHÔNG vẽ ảnh gốc phủ kín cả
            // khung như 2 layout kia, vì nửa panel không phải là ảnh. Toàn panel là nền ĐẶC (không có
            // ảnh chen vào) nên không cần bản tối riêng — dùng effectivePalette nguyên gốc.
            DrawSolidPanelSplitLayout(canvas, targetWidth, targetHeight, request, bitmap, logo, effectivePalette);
        }
        else
        {
            canvas.DrawBitmap(bitmap, new SKRect(0, 0, targetWidth, targetHeight));
            if (layout == "FreeText" && request.SafeTextRegionWidth.HasValue)
            {
                DrawFreeTextLayout(canvas, targetWidth, targetHeight, request, logo, scrimPalette, effectivePalette);
            }
            else
            {
                DrawTopBottomSplitLayout(canvas, targetWidth, targetHeight, request, logo, scrimPalette, effectivePalette);
            }
        }

        using var snapshot = surface.Snapshot();
        using var data = snapshot.Encode(SKEncodedImageFormat.Png, 100);
        await using var outputStream = new MemoryStream();
        data.SaveTo(outputStream);
        var outputBytes = outputStream.ToArray();

        var saveResult = await fileStorage.SaveBytesAsync(
            outputBytes, request.OutputFolder, ".png", "image/png", ct);

        return new ImageOverlayResult
        {
            StorageKey = saveResult.StorageKey,
            ContentType = saveResult.ContentType,
            SizeBytes = saveResult.SizeBytes,
            Width = targetWidth,
            Height = targetHeight,
            TextRendered = true,
            UsedFallbackCopy = false
        };
    }

    private async Task<SKBitmap?> LoadLogoAsync(string? logoStorageKey, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(logoStorageKey)) return null;
        try
        {
            if (!await fileStorage.ExistsAsync(logoStorageKey, ct)) return null;
            await using var stream = await fileStorage.OpenReadAsync(logoStorageKey, ct);
            using var memoryStream = new MemoryStream();
            await stream.CopyToAsync(memoryStream, ct);
            return SKBitmap.Decode(memoryStream.ToArray());
        }
        catch
        {
            // logo là optional — lỗi đọc file không được chặn render banner.
            return null;
        }
    }

    /// <summary>
    /// <paramref name="origin"/> mặc định (0,0) và <paramref name="maxWidth"/> mặc định null (không giới
    /// hạn ngang) giữ nguyên 100% vị trí/kích thước cũ cho 2 chỗ gọi hiện có (góc trên-trái ảnh) — chỉ
    /// <see cref="DrawSolidPanelSplitLayout"/> truyền khác để neo logo theo góc panel và giới hạn theo bề
    /// ngang panel (hẹp hơn cả ảnh) thay vì bề ngang toàn ảnh.
    /// </summary>
    private static void DrawLogo(
        SKCanvas canvas, int width, SKBitmap? logo, string? shape = null, SKPoint origin = default, float? maxWidth = null)
    {
        if (logo == null) return;

        var maxHeight = width * 0.11f;
        var ratio = maxHeight / logo.Height;
        var logoWidth = logo.Width * ratio;
        var padding = width * 0.045f;
        var left = origin.X + padding;
        var top = origin.Y + padding;

        if (shape == "Circle")
        {
            // "Contain" (thu nhỏ vừa khít, giữ nguyên tỉ lệ, KHÔNG cắt) chứ không phải "cover" (crop) —
            // logo thật (icon + chữ "VNI EDUCATION" xếp dọc) là khối cao, nếu crop giữa kiểu cover sẽ cắt
            // ngang qua đúng dòng chữ, ra chữ lô nhô nửa dòng (bug đã gặp: "DUCATIO", "EDUC" rời rạc, đè
            // lên tiêu đề bên dưới). Contain chấp nhận có khoảng trống trong hình tròn quanh logo, đổi
            // lấy việc KHÔNG BAO GIỜ cắt mất nội dung — đúng cách các logo tròn thật vẫn làm.
            var size = maxWidth.HasValue ? Math.Min(maxHeight, maxWidth.Value) : maxHeight;
            var dest = new SKRect(left, top, left + size, top + size);
            var containRatio = Math.Min(size / logo.Width, size / logo.Height);
            var logoW = logo.Width * containRatio;
            var logoH = logo.Height * containRatio;
            var logoRect = new SKRect(
                dest.MidX - logoW / 2f, dest.MidY - logoH / 2f,
                dest.MidX + logoW / 2f, dest.MidY + logoH / 2f);
            canvas.Save();
            using var clipPath = new SKPath();
            clipPath.AddCircle(dest.MidX, dest.MidY, size / 2f);
            canvas.ClipPath(clipPath, antialias: true);
            using (var bgPaint = new SKPaint { Color = SKColors.White })
                canvas.DrawRect(dest, bgPaint);
            canvas.DrawBitmap(logo, logoRect);
            canvas.Restore();
            return;
        }

        if (maxWidth.HasValue && logoWidth > maxWidth.Value)
        {
            logoWidth = maxWidth.Value;
            maxHeight = logo.Height * (logoWidth / logo.Width);
        }
        canvas.DrawBitmap(logo, new SKRect(left, top, left + logoWidth, top + maxHeight));
    }

    /// <summary>
    /// Vẽ ảnh lấp đầy <paramref name="dest"/> kiểu "cover" (crop giữa ảnh theo tỉ lệ khung đích, không
    /// méo hình) — khác với cách file này vẫn dùng cho ảnh nền (overload 2 tham số ở <see cref="RenderAsync"/>,
    /// luôn kéo giãn méo ảnh cho vừa khung). Dùng cho nửa ảnh của <see cref="DrawSolidPanelSplitLayout"/>
    /// và logo hình tròn (crop vào khung vuông trước khi clip tròn).
    /// </summary>
    private static void DrawBitmapCover(SKCanvas canvas, SKBitmap bitmap, SKRect dest)
    {
        var bitmapAspect = bitmap.Width / (float)bitmap.Height;
        var destAspect = dest.Width / dest.Height;
        SKRect src;
        if (bitmapAspect > destAspect)
        {
            var cropWidth = bitmap.Height * destAspect;
            var offsetX = (bitmap.Width - cropWidth) / 2f;
            src = new SKRect(offsetX, 0, offsetX + cropWidth, bitmap.Height);
        }
        else
        {
            var cropHeight = bitmap.Width / destAspect;
            var offsetY = (bitmap.Height - cropHeight) / 2f;
            src = new SKRect(0, offsetY, bitmap.Width, offsetY + cropHeight);
        }
        canvas.DrawBitmap(bitmap, src, dest);
    }

    /// <summary>Layout TopBottomSplit — headline/subheadline/bullet canh trái theo 1 trục duy nhất (phong cách biên tập phẳng, khớp ảnh mẫu).</summary>
    private void DrawTopBottomSplitLayout(
        SKCanvas canvas, int width, int height, ImageOverlayRequest request, SKBitmap? logo,
        BrandPalette palette, BrandPalette footerPalette)
    {
        var leftMargin = width * 0.05f;
        // topHeight bám theo Vùng An Toàn AI đã quét (nút "Quét Vùng An Toàn") khi ảnh có dữ liệu —
        // trước đây CỐ ĐỊNH 30% bất kể dải nền tối thật của ảnh cao/thấp hơn. Kẹp [0.20f, 0.55f] đề
        // phòng AI trả về vùng bất thường làm layout vỡ. Ảnh chưa quét (không có SafeTextRegion) thì
        // giữ nguyên fallback 30% như cũ — đây chỉ là ĐIỂM KHỞI ĐẦU, có thể bị nới thêm bên dưới nếu
        // khối chữ thực tế cần nhiều chỗ hơn AI gợi ý.
        var aiTopHeight = request.SafeTextRegionY.HasValue && request.SafeTextRegionHeight.HasValue
            ? Math.Clamp(
                height * (request.SafeTextRegionY.Value + request.SafeTextRegionHeight.Value) / 100f,
                height * 0.20f, height * 0.55f)
            : height * 0.30f;

        var headlineTop = width * 0.05f + (logo != null ? width * 0.11f + width * 0.05f : 0f);
        var headlineMaxWidth = width * 0.9f;
        var subMaxWidth = width * 0.85f;
        var headerGap = width * 0.015f;
        var headerBottomPad = width * 0.03f;

        // Headline + subtitle co CÙNG NHAU theo khoảng trống thật còn lại trong aiTopHeight (đo TRƯỚC
        // khi vẽ scrim) — trước đây chỉ headline tự co theo số dòng, subtitle giữ font cố định nên vẫn
        // tràn khi khối dài. Sàn `width*0.04f` chỉ để tránh chia cho khoảng âm/0 khi logo đã chiếm gần
        // hết aiTopHeight, KHÔNG phải "khoảng trống tối thiểu coi như đủ" — nếu sàn đó lớn hơn khoảng
        // trống thật, vòng lặp co chữ sẽ tưởng còn chỗ mà không co, đúng bug đã gặp khi có logo.
        //
        // DrawVerticalScrim giữ ĐẶC tới 55% khung rồi mới mờ dần (xem doc-comment ở đó) — dùng đúng mốc
        // 0.55f này làm "vùng chữ nên nằm trong", không phải toàn bộ aiTopHeight, để chữ không rơi vào
        // phần đã nhạt sát mép dưới.
        var solidZoneRatio = 0.55f;
        var headerAvailableHeight = Math.Max(width * 0.04f, aiTopHeight * solidZoneRatio - headlineTop - headerBottomPad);
        var (headlineBlock, subBlock, headerTotalHeight) = BuildFittedHeaderStack(
            request.Headline, request.Subheadline, headlineMaxWidth, subMaxWidth,
            width * 0.1f, width * 0.038f, TextAlignment.Left,
            palette.TextPrimary, palette.TextSecondary, headerAvailableHeight, headerGap);

        // Co hết mức (4 lần, hệ số 0.88f) mà vẫn không vừa vùng đặc thì NỚI topHeight — vì mốc mờ dần
        // (solidZoneRatio) tỉ lệ THEO topHeight, nới topHeight cũng tự kéo dài vùng đặc theo, đủ chứa
        // đúng phần nội dung vừa đo được (chia ngược lại cho solidZoneRatio để đáy nội dung rơi đúng
        // ngay mốc bắt đầu mờ, không phải giữa vùng đã nhạt). Cùng triết lý "nudge" DrawFreeTextLayout
        // đã áp dụng cho safeY — ưu tiên chữ luôn đọc được, hơn là co tới mức khó đọc hoặc tràn ra ngoài.
        // Trần cứng 50% chiều cao — không có trần, nội dung dài (subtitle nhiều cụm nối "•", 4+ bullet)
        // từng đẩy topHeight lên tới ~85% ảnh, gần chạm luôn botHeight tính riêng bên dưới, khiến 2 dải
        // scrim chồng lấn nhau và gần như phủ mờ TOÀN BỘ ảnh (mất hẳn phần ảnh rõ ở giữa, đúng lỗi đã
        // gặp). Nội dung vượt quá 50% dù đã co hết mức (4 lần) thì chấp nhận tràn nhẹ ra vùng ảnh chưa
        // tối thay vì nuốt trọn khung hình.
        var requiredTopHeight = (headlineTop + headerTotalHeight + headerBottomPad) / solidZoneRatio;
        var topHeight = Math.Min(Math.Max(aiTopHeight, requiredTopHeight), height * 0.5f);
        DrawVerticalScrim(canvas, new SKRect(0, 0, width, topHeight), fromTop: true, palette.Primary);
        DrawLogo(canvas, width, logo, request.LogoShape);

        PaintWithSoftShadow(canvas, headlineBlock, new SKPoint(leftMargin, headlineTop), width);
        if (subBlock != null)
        {
            var subheadlineTop = headlineTop + headlineBlock.MeasuredHeight + headerGap;
            PaintWithSoftShadow(canvas, subBlock, new SKPoint(leftMargin, subheadlineTop), width);
        }

        // Đo footer (org/hotline/website) TRƯỚC khi đặt khung bullet — trước đây bullet box luôn đặt
        // đáy ở "height - width*0.11f" (ước lượng cố định), không liên quan gì tới chiều cao thật của
        // footer bên dưới. Với ảnh dọc (width < height) hoặc footer 2 dòng, ước lượng đó nhỏ hơn thực
        // tế, khiến bullet box tràn xuống bị footer (vẽ sau) đè lên/che mất chữ.
        var (footerOrgBlock, footerContactBlock, footerBarHeight) = MeasureFooterBar(width, request, footerPalette);
        var footerMargin = width * 0.03f;
        var bulletBottomLimit = footerBarHeight > 0
            ? height - footerBarHeight - footerMargin * 2
            : height - width * 0.11f;

        // Bullet co chữ theo khoảng trống THẬT giữa đáy khung trên và bulletBottomLimit — trước đây chỉ
        // CẮT theo bulletBottomLimit mà không co font, bullet dài đẩy khung tràn lên đè khung trên.
        var bulletRight = width * 0.95f;
        TextBlock? bulletBlock = null;
        var bulletTop = bulletBottomLimit;
        if (request.BulletPoints.Count > 0)
        {
            var bulletAvailableHeight = Math.Max(width * 0.15f, bulletBottomLimit - topHeight - width * 0.03f);
            (bulletBlock, bulletTop) = MeasureBulletBox(
                width, bulletBottomLimit, leftMargin, bulletRight, request.BulletPoints, palette.TextPrimary, bulletAvailableHeight);
        }

        // botHeight bám theo đáy khối bullet THỰC TẾ thay vì cố định 42% — bullet dài thì che đủ,
        // bullet ngắn/không có thì không ăn lố diện tích ảnh sáng phía trên khung tối không cần thiết.
        // Trần 45% cùng lý do với topHeight ở trên — tránh 2 dải scrim chồng lấn nuốt hết ảnh.
        var botHeight = Math.Min(Math.Max(height * 0.25f, height - bulletTop + footerMargin), height * 0.45f);
        DrawVerticalScrim(canvas, new SKRect(0, height - botHeight, width, height), fromTop: false, palette.Primary);

        if (bulletBlock != null)
            PaintWithSoftShadow(canvas, bulletBlock, new SKPoint(leftMargin, bulletTop), width);

        PaintFooterBar(canvas, width, height, footerOrgBlock, footerContactBlock, footerBarHeight, footerPalette);
    }

    /// <summary>Layout FreeText — khối chữ canh trái, đặt trong vùng an toàn AI đã quét (khớp ảnh mẫu có vùng trống lệch).</summary>
    private void DrawFreeTextLayout(
        SKCanvas canvas, int width, int height, ImageOverlayRequest request, SKBitmap? logo,
        BrandPalette palette, BrandPalette footerPalette)
    {
        DrawVerticalScrim(canvas, new SKRect(0, 0, width, height * 0.16f), fromTop: true, palette.Primary);
        DrawLogo(canvas, width, logo, request.LogoShape);

        // Kẹp lại lần 2 ở render time (phòng thủ kép — MediaIntelligenceService đã kẹp lúc ghi Tags,
        // nhưng GenerationJobPipelineService đọc thẳng Tags JSON bằng try/catch riêng, không chạy lại
        // validate đó, nên 1 dòng dữ liệu cũ/hỏng trong DB vẫn có thể lọt xuống đây). Trước đây 3 dòng
        // này dùng thẳng giá trị AI trả về KHÔNG hề Math.Clamp — cùng họ bug với topHeight/botHeight đã
        // vá ở DrawTopBottomSplitLayout, chỉ là chưa có nội dung nào kích hoạt được nó.
        var safeX = Math.Min(width * Math.Clamp(request.SafeTextRegionX!.Value, 0, 100) / 100f, width * 0.8f);
        var safeY = height * Math.Clamp(request.SafeTextRegionY!.Value, 0, 100) / 100f;
        var safeW = Math.Max(
            width * 0.15f,
            Math.Min(width * Math.Clamp(request.SafeTextRegionWidth!.Value, 0, 100) / 100f, width * 0.97f - safeX));

        // Đo footer TRƯỚC để biết khoảng trống thật còn lại phía dưới, và dùng SafeTextRegionHeight
        // (AI trả về nhưng trước đây bị bỏ qua hoàn toàn) làm giới hạn chiều cao thứ 2 — trước đây
        // khối chữ (headline+subheadline+bullet) được đặt hoàn toàn độc lập với 2 giới hạn này, nên
        // khi nội dung dài (nhiều bullet) sẽ tràn xuống ĐÈ LÊN footer hoặc tràn QUA MÉP DƯỚI ảnh (bug
        // quan sát được: bullet cuối bị cắt mất/bị banner che một phần).
        var (footerOrgBlock, footerContactBlock, footerBarHeight) = MeasureFooterBar(width, request, footerPalette);
        var footerMargin = width * 0.03f;
        var footerTop = footerBarHeight > 0 ? height - footerBarHeight - footerMargin : height;
        var bottomLimit = footerTop - footerMargin;

        var safeH = request.SafeTextRegionHeight.HasValue
            ? height * request.SafeTextRegionHeight.Value / 100f
            : (float?)null;
        var maxBottom = safeH.HasValue ? Math.Min(safeY + safeH.Value, bottomLimit) : bottomLimit;

        var boxPadding = width * 0.03f;
        var minSafeTop = height * 0.17f;
        var availableHeight = Math.Max(width * 0.15f, maxBottom - safeY - boxPadding * 2);

        // Thu nhỏ dần font (giống cơ chế BuildFittedHeadlineBlock) tới khi toàn bộ khối chữ vừa trong
        // availableHeight — đảm bảo KHÔNG BAO GIỜ tràn qua footer/mép ảnh dù bullet dài cỡ nào.
        var scale = 1f;
        TextBlock headlineBlock;
        TextBlock subBlock;
        TextBlock bulletBlock;
        var gap = width * 0.02f;
        float totalHeight;
        for (var attempt = 0; ; attempt++)
        {
            headlineBlock = BuildFittedHeadlineBlock(request.Headline, safeW, width * 0.1f * scale, 800, TextAlignment.Left, palette.TextPrimary);

            subBlock = new TextBlock { Alignment = TextAlignment.Left, MaxWidth = safeW };
            if (!string.IsNullOrWhiteSpace(request.Subheadline))
            {
                AddRichText(subBlock, request.Subheadline,
                    new Style { FontFamily = MainFamily, FontSize = width * 0.038f * scale, TextColor = palette.TextSecondary },
                    new Style { FontFamily = EmojiFamily, FontSize = width * 0.038f * scale, TextColor = palette.TextSecondary });
                subBlock.Layout();
            }

            bulletBlock = BuildBulletBlock(width, safeW, request.BulletPoints, palette.TextPrimary, scale);
            bulletBlock.Layout();

            totalHeight = headlineBlock.MeasuredHeight
                + (subBlock.MeasuredLength > 0 ? gap + subBlock.MeasuredHeight : 0)
                + (request.BulletPoints.Count > 0 ? gap * 1.5f + bulletBlock.MeasuredHeight + gap : 0);

            if (totalHeight <= availableHeight || attempt >= 4) break;
            scale *= 0.85f;
        }

        // Nếu vẫn tràn dù đã thu nhỏ hết mức (vd quá nhiều bullet) — kéo safeY lên để đáy khối chữ
        // không bao giờ vượt quá bottomLimit, tránh chữ bị footer đè hoặc bị cắt mép ảnh.
        if (safeY + totalHeight + boxPadding * 2 > bottomLimit)
            safeY = Math.Max(minSafeTop, bottomLimit - totalHeight - boxPadding * 2);

        // Trước đây vẽ 1 khung bo góc nền phẳng bán trong suốt sau khối chữ — nhìn như "thẻ nổi" (viền
        // cứng, cạnh thẳng) rất lộ, không giống cách đối thủ đặt chữ (luôn hoà mềm vào ảnh, không có
        // hình khối rõ cạnh). Đổi sang quầng sáng RADIAL GRADIENT (đặc ở tâm khối chữ, mờ dần ra ngoài,
        // không cạnh/không góc) — vẫn là lưới an toàn đọc chữ duy nhất cho layout này (không có scrim cố
        // định bao trùm cả dải như TopBottomSplit, vùng an toàn AI chọn có thể nằm bất kỳ đâu trên ảnh).
        //
        // HÌNH ELIP theo đúng tỉ lệ khối chữ (radiusX/radiusY riêng), KHÔNG dùng 1 bán kính tròn tính từ
        // Math.Max(safeW, totalHeight) như lần trước — headline rộng gần hết bề ngang ảnh khiến bán kính
        // đó phình to che gần hết ảnh (bug đã gặp: quầng sáng lấn cả xuống phần ảnh phía dưới không liên
        // quan). Chỉ 1 màu thương hiệu (không pha thêm Secondary) — pha 2 màu bán trong suốt đè lên ảnh
        // dễ tạo cảm giác "ám màu" trên ảnh nền sáng, cùng lý do đã bỏ ở DrawVerticalScrim.
        var glowCenterX = safeX + safeW / 2f;
        var glowCenterY = safeY + totalHeight / 2f;
        var radiusX = safeW / 2f + boxPadding * 1.6f;
        var radiusY = totalHeight / 2f + boxPadding * 2.2f;
        // Alpha hạ theo tỉ lệ giống DrawVerticalScrim (220/165 → 165/120, ~25%) — cùng lý do: nhường đất
        // cho ảnh gốc lộ rõ hơn, dựa vào PaintWithSoftShadow làm lưới an toàn đọc chữ chính.
        using var glowBase = SKShader.CreateRadialGradient(
            new SKPoint(0, 0), 1f,
            new[] { palette.Primary.WithAlpha(165), palette.Primary.WithAlpha(120), palette.Primary.WithAlpha(0) },
            new[] { 0f, 0.6f, 1f },
            SKShaderTileMode.Clamp);
        var glowMatrix = new SKMatrix
        {
            ScaleX = radiusX, ScaleY = radiusY, TransX = glowCenterX, TransY = glowCenterY, Persp2 = 1
        };
        using var glowShader = glowBase.WithLocalMatrix(glowMatrix);
        using var paint = new SKPaint { Shader = glowShader };
        canvas.DrawRect(new SKRect(
            glowCenterX - radiusX, glowCenterY - radiusY,
            glowCenterX + radiusX, glowCenterY + radiusY), paint);

        var y = safeY;
        PaintWithSoftShadow(canvas, headlineBlock, new SKPoint(safeX, y), width);
        y += headlineBlock.MeasuredHeight;

        if (subBlock.MeasuredLength > 0)
        {
            y += gap;
            PaintWithSoftShadow(canvas, subBlock, new SKPoint(safeX, y), width);
            y += subBlock.MeasuredHeight;
        }

        if (request.BulletPoints.Count > 0)
        {
            y += gap * 1.5f;
            PaintWithSoftShadow(canvas, bulletBlock, new SKPoint(safeX, y), width);
        }

        PaintFooterBar(canvas, width, height, footerOrgBlock, footerContactBlock, footerBarHeight, footerPalette);
    }

    /// <summary>
    /// Layout SolidPanelSplit — nửa PANEL bên trái (màu thương hiệu ĐẶC, chứa toàn bộ logo/chữ/footer)
    /// + nửa ẢNH bên phải (crop "cover", không có chữ đè lên) — không cần bất kỳ scrim/khung mờ nào vì
    /// chữ không bao giờ nằm trên ảnh, khác hẳn 2 layout kia luôn cần 1 cơ chế đảm bảo đọc được. Bề rộng
    /// panel CỐ ĐỊNH theo tỉ lệ (không co giãn theo nội dung) — chỉ nội dung co theo chiều DỌC bằng đúng
    /// các helper co-chữ đã dùng cho 2 layout kia, tránh phải giải bài toán fit 2 chiều (vừa co ngang vừa
    /// co dọc) — nếu co hết mức (4 lần, quy ước sẵn có) vẫn tràn thì chấp nhận tràn nhẹ, cùng triết lý đã
    /// áp dụng cho topHeight/botHeight ở TopBottomSplit thay vì thêm 1 cơ chế mới.
    /// </summary>
    private void DrawSolidPanelSplitLayout(
        SKCanvas canvas, int width, int height, ImageOverlayRequest request, SKBitmap sourceBitmap,
        SKBitmap? logo, BrandPalette palette)
    {
        var panelWidth = width * 0.44f;
        // Gradient chéo Primary→Secondary thay vì 1 màu phẳng — ảnh mẫu đối thủ hầu như không có mảng
        // màu đơn sắc thuần túy nào, luôn có chuyển sắc dù nhẹ. Dùng đúng 2 màu thương hiệu đã cấu hình
        // (palette.Secondary tự fallback về Primary nếu trang chỉ khai 1 màu — ResolvePalette đã lo).
        using (var shader = SKShader.CreateLinearGradient(
                   new SKPoint(0, 0), new SKPoint(panelWidth, height),
                   new[] { palette.Primary, palette.Secondary }, null, SKShaderTileMode.Clamp))
        using (var paint = new SKPaint { Shader = shader })
            canvas.DrawRect(new SKRect(0, 0, panelWidth, height), paint);
        DrawBitmapCover(canvas, sourceBitmap, new SKRect(panelWidth, 0, width, height));

        var leftMargin = panelWidth * 0.08f;
        var contentWidth = panelWidth - leftMargin * 2;
        var gap = panelWidth * 0.05f;

        // width truyền cho DrawLogo/BuildBulletBlock/MeasureFooterBar là panelWidth (không phải width
        // toàn ảnh) — mọi cỡ chữ trong file này đều tính theo tỉ lệ % của tham số "width" nhận vào, nên
        // truyền panelWidth khiến chữ tự co theo bề rộng cột panel thay vì theo cả khung ảnh (quá to).
        DrawLogo(canvas, (int)panelWidth, logo, request.LogoShape, maxWidth: contentWidth);
        var contentTop = leftMargin + (logo != null ? panelWidth * 0.11f + gap : 0f);

        var (footerOrgBlock, footerContactBlock, footerBarHeight) = MeasureFooterBar((int)panelWidth, request, palette);
        var footerBottomPad = panelWidth * 0.08f;
        var footerTop = footerBarHeight > 0 ? height - footerBarHeight - footerBottomPad : height - footerBottomPad;

        var headerAvailableHeight = Math.Max(panelWidth * 0.15f, (footerTop - contentTop) * 0.5f);
        var (headlineBlock, subBlock, headerTotalHeight) = BuildFittedHeaderStack(
            request.Headline, request.Subheadline, contentWidth, contentWidth,
            panelWidth * 0.19f, panelWidth * 0.065f, TextAlignment.Left,
            palette.TextPrimary, palette.TextSecondary, headerAvailableHeight, gap * 0.3f);
        headlineBlock.Paint(canvas, new SKPoint(leftMargin, contentTop));
        if (subBlock != null)
            subBlock.Paint(canvas, new SKPoint(leftMargin, contentTop + headlineBlock.MeasuredHeight + gap * 0.3f));

        if (request.BulletPoints.Count > 0)
        {
            // bottomY = footerTop - gap (không phải footerTop thẳng) — MeasureBulletBox neo ĐÁY khối
            // bullet đúng vào bottomY, thiếu trừ hao thì bullet dính sát chữ footer ngay bên dưới.
            var bulletBottomY = footerTop - gap;
            var bulletAvailableHeight = Math.Max(panelWidth * 0.15f, bulletBottomY - (contentTop + headerTotalHeight + gap));
            var (bulletBlock, bulletTop) = MeasureBulletBox(
                (int)panelWidth, bulletBottomY, leftMargin, leftMargin + contentWidth, request.BulletPoints, palette.TextPrimary, bulletAvailableHeight);
            bulletBlock.Paint(canvas, new SKPoint(leftMargin, bulletTop));
        }

        // Footer nằm TRONG panel (xếp dọc cùng cột, không gọi PaintFooterBar hiện có vì hàm đó vẽ full
        // canvas width, sẽ tràn cả sang nửa ảnh) — nhờ vậy nửa ảnh bên phải không cần scrim/khung gì cả.
        if (footerBarHeight > 0)
        {
            var footerVGap = footerOrgBlock != null && footerContactBlock != null ? panelWidth * 0.02f : 0f;
            var y = footerTop;
            if (footerOrgBlock != null)
            {
                footerOrgBlock.Paint(canvas, new SKPoint(leftMargin, y));
                y += footerOrgBlock.MeasuredHeight + footerVGap;
            }
            footerContactBlock?.Paint(canvas, new SKPoint(leftMargin, y));
        }
    }

    /// <summary>
    /// Trước đây mờ TUYẾN TÍNH suốt cả khung (alpha cao ở mép ngoài → 0 ở mép trong) — chữ đặt gần mép
    /// trong (nơi khung nối vào phần ảnh không che) vẫn "vừa khung" về kích thước nhưng rơi vào vùng đã
    /// nhạt, khó đọc (đúng vùng subtitle bị mờ khi khung trên phải nới rộng để chứa logo+headline+sub).
    /// Đổi sang 3 điểm dừng: giữ ĐẶC (alpha vừa dưới đỉnh) hết ~55% khung, chỉ mờ dần ở 45% còn lại sát
    /// mép trong — cho 1 vùng nền đủ đậm ổn định để đặt chữ, khớp cách các banner mẫu thường dùng.
    /// Đã thử pha 2 màu thương hiệu (Primary→Secondary) cho phong phú hơn, nhưng test thực tế lộ ra vấn
    /// đề: scrim này bán trong suốt ĐÈ LÊN ẢNH (khác panel đặc của SolidPanelSplit), nên trên ảnh nền
    /// SÁNG (trắng/kem), pha thêm màu thứ 2 tạo cảm giác "ám màu" ở vùng nửa mờ — rõ nhất khi Secondary
    /// là màu nóng (cam/vàng) khác hẳn tông Primary. Quay lại 1 màu duy nhất (chỉ đổi alpha) — an toàn
    /// trên MỌI ảnh nền, đổi màu phong phú hơn dồn hết cho panel đặc của <see cref="DrawSolidPanelSplitLayout"/>
    /// (nơi không có ảnh bên dưới nên phối 2 màu không rủi ro ám màu).
    /// Cũng đã thử <c>SKBlendMode.SoftLight</c> thay cho SrcOver mặc định (theo gợi ý "chuyển Blend Mode
    /// sang Soft Light" để trông hoà tự nhiên hơn thay vì "dán đè") — test thực tế cho kết quả TỆ HƠN:
    /// soft-light tính theo độ sáng từng pixel nền bên dưới nên màu thương hiệu bị biến dạng thành xanh
    /// rêu/xám đục tuỳ ảnh, không còn nhận ra đúng brand color nữa. SrcOver + alpha vẫn là lựa chọn đúng
    /// duy nhất ở đây vì nó giữ được màu thương hiệu CHÍNH XÁC trên mọi ảnh nền.
    /// Đỉnh alpha hạ từ 215/230 xuống 160/175 (~25%) theo yêu cầu người dùng — test với brand color SÁNG
    /// (cam #F59E0B) cho thấy alpha cũ quá đậm vẫn không đủ tối để chữ ĐEN (tự đổi theo ContrastTextColors
    /// khi nền sáng) nổi bật, hạ alpha để ảnh gốc lộ rõ hơn, đổi lại dựa nhiều hơn vào bóng đổ chữ
    /// (<see cref="PaintWithSoftShadow"/>) làm lưới an toàn đọc chữ chính thay vì chỉ dựa vào scrim đặc.
    /// </summary>
    private static void DrawVerticalScrim(SKCanvas canvas, SKRect rect, bool fromTop, SKColor baseColor)
    {
        var start = new SKPoint(0, rect.Top);
        var end = new SKPoint(0, rect.Bottom);
        var (outerAlpha, innerAlpha) = fromTop ? (160, 0) : (0, 175);
        var colors = fromTop
            ? new[] { baseColor.WithAlpha((byte)outerAlpha), baseColor.WithAlpha((byte)outerAlpha), baseColor.WithAlpha((byte)innerAlpha) }
            : new[] { baseColor.WithAlpha((byte)outerAlpha), baseColor.WithAlpha((byte)innerAlpha), baseColor.WithAlpha((byte)innerAlpha) };
        var stops = fromTop ? new[] { 0f, 0.55f, 1f } : new[] { 0f, 0.45f, 1f };

        using var paint = new SKPaint
        {
            Shader = SKShader.CreateLinearGradient(start, end, colors, stops, SKShaderTileMode.Clamp)
        };
        canvas.DrawRect(rect, paint);
    }

    /// <summary>
    /// Vẽ TextBlock kèm đổ bóng mềm phía sau (offset nhẹ + blur, 1 lớp duy nhất qua SaveLayer) — chữ
    /// trắng/nhạt nằm trên ẢNH CHỤP THẬT (không phải nền phẳng) nên dù đã có scrim, vùng ảnh sáng/nhiều
    /// chi tiết ngay dưới chữ vẫn có thể kéo tương phản xuống thấp tuỳ ảnh. Bóng đổ là lưới an toàn đọc
    /// chữ THỨ 2, bổ sung cho scrim chứ không thay thế — chỉ đen mờ (không pha màu), an toàn trên mọi nền.
    /// </summary>
    private static void PaintWithSoftShadow(SKCanvas canvas, TextBlock block, SKPoint origin, int canvasWidth)
    {
        using var shadowFilter = SKImageFilter.CreateDropShadow(
            canvasWidth * 0.0015f, canvasWidth * 0.003f, canvasWidth * 0.006f, canvasWidth * 0.006f,
            new SKColor(0, 0, 0, 110));
        using var layerPaint = new SKPaint { ImageFilter = shadowFilter };
        canvas.SaveLayer(layerPaint);
        block.Paint(canvas, origin);
        canvas.Restore();
    }

    /// <summary>Build TextBlock headline, tự giảm FontSize nếu wrap quá maxLines (tránh headline dài tràn 4-5 dòng như ảnh mẫu không có).</summary>
    private static TextBlock BuildFittedHeadlineBlock(
        string headline, float maxWidth, float baseFontSize, int fontWeight,
        TextAlignment alignment, SKColor textColor, int maxLines = 3)
    {
        var fontSize = baseFontSize;
        TextBlock block;
        for (var attempt = 0; ; attempt++)
        {
            block = new TextBlock { Alignment = alignment, MaxWidth = maxWidth };
            AddRichText(block, headline.ToUpperInvariant(),
                new Style { FontFamily = MainFamily, FontSize = fontSize, FontWeight = fontWeight, TextColor = textColor },
                new Style { FontFamily = EmojiFamily, FontSize = fontSize, TextColor = textColor });
            block.Layout();

            if (block.LineCount <= maxLines || attempt >= 4) return block;
            fontSize *= 0.88f;
        }
    }

    /// <summary>
    /// Dựng headline + subtitle CÙNG co theo 1 tỉ lệ chung tới khi vừa <paramref name="availableHeight"/> —
    /// trước đây chỉ headline tự co theo SỐ DÒNG (<see cref="BuildFittedHeadlineBlock"/> gọi trực tiếp),
    /// subtitle giữ font cố định nên khi cả khối dài hơn khung an toàn thực tế vẫn tràn ra ngoài dải nền
    /// tối. Không dùng chung vòng lặp với <see cref="DrawFreeTextLayout"/> vì khối đó gộp cả bullet vào
    /// cùng 1 lần co (bố cục 1 khung duy nhất) — TopBottomSplit có 2 dải nền tối tách biệt (trên/dưới)
    /// nên headline+subtitle co riêng với khối bullet, cùng chung quy ước hệ số 0.88f / tối đa 4 lần thử.
    /// </summary>
    private static (TextBlock Headline, TextBlock? Sub, float TotalHeight) BuildFittedHeaderStack(
        string headline, string? subheadline, float maxWidthHeadline, float maxWidthSub,
        float baseHeadlineFontSize, float baseSubFontSize, TextAlignment alignment,
        SKColor headlineColor, SKColor subColor, float availableHeight, float gap)
    {
        var scale = 1f;
        TextBlock headlineBlock;
        TextBlock? subBlock;
        float totalHeight;
        for (var attempt = 0; ; attempt++)
        {
            headlineBlock = BuildFittedHeadlineBlock(
                headline, maxWidthHeadline, baseHeadlineFontSize * scale, 700, alignment, headlineColor);

            subBlock = null;
            if (!string.IsNullOrWhiteSpace(subheadline))
            {
                subBlock = new TextBlock { Alignment = alignment, MaxWidth = maxWidthSub };
                AddRichText(subBlock, subheadline,
                    new Style { FontFamily = MainFamily, FontSize = baseSubFontSize * scale, TextColor = subColor },
                    new Style { FontFamily = EmojiFamily, FontSize = baseSubFontSize * scale, TextColor = subColor });
                subBlock.Layout();
            }

            totalHeight = headlineBlock.MeasuredHeight + (subBlock != null ? gap + subBlock.MeasuredHeight : 0f);
            if (totalHeight <= availableHeight || attempt >= 4) break;
            scale *= 0.88f;
        }
        return (headlineBlock, subBlock, totalHeight);
    }

    private static TextBlock BuildBulletBlock(int width, float maxWidth, List<string> bulletPoints, SKColor textColor, float scale = 1f)
    {
        var block = new TextBlock { Alignment = TextAlignment.Left, MaxWidth = maxWidth };
        var fontSize = width * 0.034f * scale;
        var textStyle = new Style { FontFamily = MainFamily, FontSize = fontSize, FontWeight = 600, TextColor = textColor, LineHeight = 1.35f };
        var emojiStyle = new Style { FontFamily = EmojiFamily, FontSize = fontSize, TextColor = textColor, LineHeight = 1.35f };
        foreach (var bp in bulletPoints)
            AddRichText(block, "• " + StripLeadingEmoji(bp) + "\n", textStyle, emojiStyle);
        return block;
    }

    /// <summary>
    /// AI vẫn tự thêm emoji đầu dòng cho bullet (✅, 🎓...) — bỏ đi để đổi hẳn sang bullet "•",
    /// không hiện cả hai cùng lúc. Chỉ cắt phần emoji+khoảng trắng ở ĐẦU chuỗi, giữ nguyên
    /// emoji nằm giữa câu (nếu AI lỡ chèn) vì đó là nội dung, không phải icon trang trí.
    /// </summary>
    private static string StripLeadingEmoji(string text)
    {
        var enumerator = StringInfo.GetTextElementEnumerator(text);
        var index = 0;
        while (enumerator.MoveNext())
        {
            var element = (string)enumerator.Current;
            var firstRune = element.EnumerateRunes().FirstOrDefault();
            if (!IsEmojiCodepoint(firstRune.Value) && !char.IsWhiteSpace(element[0])) break;
            index += element.Length;
        }
        return text[index..];
    }

    /// <summary>
    /// Đo khối bullet KHÔNG vẽ — co dần <c>scale</c> (giống <see cref="BuildFittedHeaderStack"/>) tới
    /// khi vừa <paramref name="maxHeight"/>, thay vì trước đây dựng ở font cố định rồi chỉ CẮT theo đáy
    /// giới hạn — cách cũ khiến bullet dài đẩy khối cao hơn khoảng trống hợp lý, tràn lên đè khối
    /// headline/subtitle phía trên. Không còn khung/nền riêng — bullet nằm thẳng trên scrim đã tối sẵn
    /// phía sau (xem <see cref="DrawVerticalScrim"/>), giống phong cách biên tập phẳng của ảnh mẫu, thay
    /// vì "hộp nổi trên hộp" (nested card) như trước.
    /// </summary>
    private static (TextBlock Block, float Top) MeasureBulletBox(
        int width, float bottomY, float left, float right,
        List<string> bulletPoints, SKColor textColor, float maxHeight)
    {
        var maxWidth = right - left;
        var scale = 1f;
        TextBlock block;
        float blockHeight;
        for (var attempt = 0; ; attempt++)
        {
            block = BuildBulletBlock(width, maxWidth, bulletPoints, textColor, scale);
            block.Layout();
            blockHeight = block.MeasuredHeight;
            if (blockHeight <= maxHeight || attempt >= 4) break;
            scale *= 0.88f;
        }
        return (block, bottomY - blockHeight);
    }

    /// <summary>
    /// Đo trước nội dung footer (org/hotline/website) mà KHÔNG vẽ — layout gọi hàm này trước khi đặt
    /// vị trí các khối chữ khác, để biết chính xác footer chiếm bao nhiêu chỗ ở đáy ảnh (tránh chữ
    /// phía trên tràn đè lên footer, xem <see cref="DrawFreeTextLayout"/>/<see cref="DrawTopBottomSplitLayout"/>).
    /// </summary>
    private static (TextBlock? Org, TextBlock? Contact, float BarHeight) MeasureFooterBar(
        int width, ImageOverlayRequest request, BrandPalette palette)
    {
        var orgLine = request.OrgLine?.Trim();
        var contactLine = string.IsNullOrWhiteSpace(request.ContactLine) ? request.CtaText?.Trim() : request.ContactLine.Trim();

        if (string.IsNullOrWhiteSpace(orgLine) && string.IsNullOrWhiteSpace(contactLine))
            return (null, null, 0f);

        var maxWidth = width * 0.9f;
        TextBlock? orgBlock = null;
        if (!string.IsNullOrWhiteSpace(orgLine))
        {
            orgBlock = new TextBlock { Alignment = TextAlignment.Left, MaxWidth = maxWidth };
            AddRichText(orgBlock, orgLine,
                new Style { FontFamily = MainFamily, FontSize = width * 0.032f, FontWeight = 700, TextColor = palette.TextPrimary },
                new Style { FontFamily = EmojiFamily, FontSize = width * 0.032f, TextColor = palette.TextPrimary });
            orgBlock.Layout();
        }

        TextBlock? contactBlock = null;
        if (!string.IsNullOrWhiteSpace(contactLine))
        {
            contactBlock = new TextBlock { Alignment = TextAlignment.Left, MaxWidth = maxWidth };
            AddRichText(contactBlock, contactLine,
                new Style { FontFamily = MainFamily, FontSize = width * 0.026f, TextColor = palette.TextSecondary },
                new Style { FontFamily = EmojiFamily, FontSize = width * 0.026f, TextColor = palette.TextSecondary });
            contactBlock.Layout();
        }

        var vGap = orgBlock != null && contactBlock != null ? width * 0.012f : 0f;
        var padding = width * 0.025f;
        var contentHeight = (orgBlock?.MeasuredHeight ?? 0) + vGap + (contactBlock?.MeasuredHeight ?? 0);
        return (orgBlock, contactBlock, contentHeight + padding * 2);
    }

    /// <summary>
    /// Footer là 1 dải thông tin liền mạch sát mép ảnh (full-width, không margin/viền) — trước đây vẽ
    /// như 1 khung nổi thụt vào 2 bên + bo tròn 4 góc + viền accent, tạo cảm giác "pill lơ lửng" thay vì
    /// dải thông tin gắn liền cạnh đáy như ảnh mẫu. Chỉ bo 2 góc TRÊN vì cạnh dưới/2 bên đã trùng mép
    /// ảnh — bo cả 4 góc sẽ lộ góc vuông ảnh gốc ở dưới góc bo.
    /// </summary>
    private static void PaintFooterBar(
        SKCanvas canvas, int width, int height, TextBlock? orgBlock, TextBlock? contactBlock, float barHeight, BrandPalette palette)
    {
        if (barHeight <= 0) return;

        var vGap = orgBlock != null && contactBlock != null ? width * 0.012f : 0f;
        var padding = width * 0.025f;
        var leftMargin = width * 0.05f;

        var barRect = new SKRect(0, height - barHeight, width, height);

        // Dải mờ dần NGAY TRÊN mép bar (alpha 0 → 255, cùng màu palette.Primary) — trước đây bar đặc vẽ
        // thẳng cạnh vuông, tạo đường cắt cứng đè lên ảnh (rõ nhất khi ảnh có người/vật ngay sát đáy).
        // Chỉ 1 màu + chỉ đổi alpha (không pha 2 màu) — giữ đúng nguyên tắc an toàn ám màu của
        // <see cref="DrawVerticalScrim"/>, seam nối liền botHeight scrim phía trên nó thành 1 dải mượt.
        var fadeHeight = Math.Min(width * 0.05f, barRect.Top);
        if (fadeHeight > 0)
        {
            var fadeRect = new SKRect(0, barRect.Top - fadeHeight, width, barRect.Top);
            using var fadePaint = new SKPaint
            {
                Shader = SKShader.CreateLinearGradient(
                    new SKPoint(0, fadeRect.Top), new SKPoint(0, fadeRect.Bottom),
                    new[] { palette.Primary.WithAlpha(0), palette.Primary },
                    null, SKShaderTileMode.Clamp)
            };
            canvas.DrawRect(fadeRect, fadePaint);
        }

        using (var roundRect = new SKRoundRect())
        {
            roundRect.SetRectRadii(barRect, [new SKPoint(16, 16), new SKPoint(16, 16), SKPoint.Empty, SKPoint.Empty]);
            using var paint = new SKPaint { Color = palette.Primary, IsAntialias = true };
            canvas.DrawRoundRect(roundRect, paint);
        }

        var y = barRect.Top + padding;
        if (orgBlock != null)
        {
            orgBlock.Paint(canvas, new SKPoint(leftMargin, y));
            y += orgBlock.MeasuredHeight + vGap;
        }
        contactBlock?.Paint(canvas, new SKPoint(leftMargin, y));
    }

    /// <summary>
    /// Thêm text vào TextBlock, tách riêng ký tự emoji sang style/font khác (NotoColorEmoji) —
    /// FontMapper của RichTextKit không tự fallback font theo glyph, phải tự tách run.
    /// </summary>
    private static void AddRichText(TextBlock block, string text, Style textStyle, Style emojiStyle)
    {
        if (string.IsNullOrEmpty(text)) return;

        var buffer = new StringBuilder();
        bool? currentIsEmoji = null;

        void Flush()
        {
            if (buffer.Length == 0) return;
            block.AddText(buffer.ToString(), currentIsEmoji == true ? emojiStyle : textStyle);
            buffer.Clear();
        }

        var enumerator = StringInfo.GetTextElementEnumerator(text);
        while (enumerator.MoveNext())
        {
            var element = (string)enumerator.Current;
            var firstRune = element.EnumerateRunes().FirstOrDefault();
            var isEmoji = IsEmojiCodepoint(firstRune.Value);

            if (currentIsEmoji.HasValue && currentIsEmoji.Value != isEmoji)
                Flush();
            currentIsEmoji = isEmoji;
            buffer.Append(element);
        }
        Flush();
    }

    private static bool IsEmojiCodepoint(int cp) =>
        (cp >= 0x1F300 && cp <= 0x1FAFF)
        || (cp >= 0x2600 && cp <= 0x27BF)
        || (cp >= 0x2B00 && cp <= 0x2BFF)
        || (cp >= 0x1F1E6 && cp <= 0x1F1FF)
        || cp is 0x2764 or 0x2705 or 0x2714 or 0x274C or 0x2757 or 0x2753 or 0x203C or 0x2049;
}
