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

        var imageInfo = new SKImageInfo(targetWidth, targetHeight);
        using var surface = SKSurface.Create(imageInfo);
        var canvas = surface.Canvas;

        canvas.DrawBitmap(bitmap, new SKRect(0, 0, targetWidth, targetHeight));

        var layout = request.LayoutStyle ?? "TopBottomSplit";

        if (layout == "FreeText" && request.SafeTextRegionWidth.HasValue)
        {
            DrawFreeTextLayout(canvas, targetWidth, targetHeight, request, logo, palette);
        }
        else
        {
            DrawTopBottomSplitLayout(canvas, targetWidth, targetHeight, request, logo, palette);
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

    private static void DrawLogo(SKCanvas canvas, int width, SKBitmap? logo)
    {
        if (logo == null) return;

        var maxHeight = width * 0.11f;
        var ratio = maxHeight / logo.Height;
        var logoWidth = logo.Width * ratio;
        var padding = width * 0.045f;

        var dest = new SKRect(padding, padding, padding + logoWidth, padding + maxHeight);
        canvas.DrawBitmap(logo, dest);
    }

    /// <summary>Layout TopBottomSplit — bố cục đối xứng, headline/subheadline canh giữa (khớp ảnh mẫu kiểu banner cân đối).</summary>
    private void DrawTopBottomSplitLayout(SKCanvas canvas, int width, int height, ImageOverlayRequest request, SKBitmap? logo, BrandPalette palette)
    {
        var topHeight = height * 0.30f;
        DrawVerticalScrim(canvas, new SKRect(0, 0, width, topHeight), fromTop: true, palette.Primary);

        DrawLogo(canvas, width, logo);

        var headlineTop = width * 0.05f + (logo != null ? width * 0.11f + width * 0.03f : 0f);

        // TextBlock đã tự canh giữa mỗi dòng bên trong MaxWidth khi Alignment = Center — Paint phải nhận
        // điểm gốc trái của khung MaxWidth, KHÔNG bù thêm theo MeasuredWidth kẻo bị canh giữa 2 lần (tràn lệch phải).
        var headlineMaxWidth = width * 0.9f;
        var headlineBlock = BuildFittedHeadlineBlock(request.Headline, headlineMaxWidth, width * 0.055f, 700, TextAlignment.Center, palette.TextPrimary);
        var headlineX = (width - headlineMaxWidth) / 2f;
        headlineBlock.Paint(canvas, new SKPoint(headlineX, headlineTop));

        var subheadlineTop = headlineTop + headlineBlock.MeasuredHeight + width * 0.015f;
        if (!string.IsNullOrWhiteSpace(request.Subheadline))
        {
            var subMaxWidth = width * 0.85f;
            var subBlock = new TextBlock { Alignment = TextAlignment.Center, MaxWidth = subMaxWidth };
            AddRichText(subBlock, request.Subheadline,
                new Style { FontFamily = MainFamily, FontSize = width * 0.032f, TextColor = palette.TextSecondary },
                new Style { FontFamily = EmojiFamily, FontSize = width * 0.032f, TextColor = palette.TextSecondary });
            subBlock.Layout();
            subBlock.Paint(canvas, new SKPoint((width - subMaxWidth) / 2f, subheadlineTop));
        }

        var botHeight = height * 0.42f;
        DrawVerticalScrim(canvas, new SKRect(0, height - botHeight, width, height), fromTop: false, palette.Primary);

        var bulletBottom = DrawBulletBox(canvas, width, height - width * 0.11f, width * 0.05f, width * 0.95f,
            request.BulletPoints, TextAlignment.Left, palette);

        DrawFooterBar(canvas, width, height, request, palette);
    }

    /// <summary>Layout FreeText — khối chữ canh trái, đặt trong vùng an toàn AI đã quét (khớp ảnh mẫu có vùng trống lệch).</summary>
    private void DrawFreeTextLayout(SKCanvas canvas, int width, int height, ImageOverlayRequest request, SKBitmap? logo, BrandPalette palette)
    {
        DrawVerticalScrim(canvas, new SKRect(0, 0, width, height * 0.16f), fromTop: true, palette.Primary);
        DrawLogo(canvas, width, logo);

        var safeX = width * request.SafeTextRegionX!.Value / 100f;
        var safeY = height * request.SafeTextRegionY!.Value / 100f;
        var safeW = width * request.SafeTextRegionWidth!.Value / 100f;

        var headlineBlock = BuildFittedHeadlineBlock(request.Headline, safeW, width * 0.06f, 800, TextAlignment.Left, palette.TextPrimary);

        var subBlock = new TextBlock { Alignment = TextAlignment.Left, MaxWidth = safeW };
        if (!string.IsNullOrWhiteSpace(request.Subheadline))
        {
            AddRichText(subBlock, request.Subheadline,
                new Style { FontFamily = MainFamily, FontSize = width * 0.032f, TextColor = palette.TextSecondary },
                new Style { FontFamily = EmojiFamily, FontSize = width * 0.032f, TextColor = palette.TextSecondary });
            subBlock.Layout();
        }

        var bulletBlock = BuildBulletBlock(width, safeW, request.BulletPoints, palette.TextPrimary);
        bulletBlock.Layout();

        var gap = width * 0.02f;
        var totalHeight = headlineBlock.MeasuredHeight
            + (subBlock.MeasuredLength > 0 ? gap + subBlock.MeasuredHeight : 0)
            + (request.BulletPoints.Count > 0 ? gap * 1.5f + bulletBlock.MeasuredHeight + gap : 0);

        // Khung nền bán trong suốt bám màu thương hiệu sau toàn bộ khối chữ — đảm bảo đọc được dù ảnh nền sáng/tối.
        var boxPadding = width * 0.03f;
        var boxRect = new SKRect(
            safeX - boxPadding, safeY - boxPadding,
            safeX + safeW + boxPadding, safeY + totalHeight + boxPadding);
        using (var paint = new SKPaint { Color = palette.Primary.WithAlpha(150), IsAntialias = true })
            canvas.DrawRoundRect(boxRect, 18, 18, paint);
        DrawAccentStroke(canvas, boxRect, 18, palette.Accent);

        var y = safeY;
        headlineBlock.Paint(canvas, new SKPoint(safeX, y));
        y += headlineBlock.MeasuredHeight;

        if (subBlock.MeasuredLength > 0)
        {
            y += gap;
            subBlock.Paint(canvas, new SKPoint(safeX, y));
            y += subBlock.MeasuredHeight;
        }

        if (request.BulletPoints.Count > 0)
        {
            y += gap * 1.5f;
            bulletBlock.Paint(canvas, new SKPoint(safeX, y));
        }

        DrawFooterBar(canvas, width, height, request, palette);
    }

    /// <summary>Viền mảnh nhấn màu thương hiệu thứ 3 (nếu page có cấu hình) — bỏ qua hoàn toàn nếu không có.</summary>
    private static void DrawAccentStroke(SKCanvas canvas, SKRect rect, float cornerRadius, SKColor? accent)
    {
        if (!accent.HasValue) return;
        using var paint = new SKPaint
        {
            Style = SKPaintStyle.Stroke, StrokeWidth = 2, IsAntialias = true, Color = accent.Value.WithAlpha(200)
        };
        canvas.DrawRoundRect(rect, cornerRadius, cornerRadius, paint);
    }

    private static void DrawVerticalScrim(SKCanvas canvas, SKRect rect, bool fromTop, SKColor baseColor)
    {
        var start = fromTop ? new SKPoint(0, rect.Top) : new SKPoint(0, rect.Top);
        var end = fromTop ? new SKPoint(0, rect.Bottom) : new SKPoint(0, rect.Bottom);
        var colors = fromTop
            ? new[] { baseColor.WithAlpha(210), baseColor.WithAlpha(0) }
            : new[] { baseColor.WithAlpha(0), baseColor.WithAlpha(230) };

        using var paint = new SKPaint
        {
            Shader = SKShader.CreateLinearGradient(start, end, colors, null, SKShaderTileMode.Clamp)
        };
        canvas.DrawRect(rect, paint);
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

    private static TextBlock BuildBulletBlock(int width, float maxWidth, List<string> bulletPoints, SKColor textColor)
    {
        var block = new TextBlock { Alignment = TextAlignment.Left, MaxWidth = maxWidth };
        var textStyle = new Style { FontFamily = MainFamily, FontSize = width * 0.033f, FontWeight = 600, TextColor = textColor, LineHeight = 1.35f };
        var emojiStyle = new Style { FontFamily = EmojiFamily, FontSize = width * 0.033f, TextColor = textColor, LineHeight = 1.35f };
        foreach (var bp in bulletPoints)
            AddRichText(block, bp + "\n", textStyle, emojiStyle);
        return block;
    }

    /// <summary>Vẽ khung bo góc nền bán trong suốt (màu phụ của bảng màu) chứa bullet, căn theo chiều rộng [left, right]. Trả về Y đáy khung.</summary>
    private static float DrawBulletBox(
        SKCanvas canvas, int width, float bottomY, float left, float right,
        List<string> bulletPoints, TextAlignment alignment, BrandPalette palette)
    {
        if (bulletPoints.Count == 0) return bottomY;

        var block = BuildBulletBlock(width, right - left - width * 0.06f, bulletPoints, palette.TextPrimary);
        block.Alignment = alignment;
        block.Layout();

        var padding = width * 0.03f;
        var boxTop = bottomY - block.MeasuredHeight - padding * 2;
        var boxRect = new SKRect(left, boxTop, right, bottomY);
        using (var paint = new SKPaint { Color = palette.Secondary.WithAlpha(150), IsAntialias = true })
            canvas.DrawRoundRect(boxRect, 18, 18, paint);
        DrawAccentStroke(canvas, boxRect, 18, palette.Accent);

        block.Paint(canvas, new SKPoint(left + width * 0.03f, boxTop + padding));
        return boxTop;
    }

    private static void DrawFooterBar(SKCanvas canvas, int width, int height, ImageOverlayRequest request, BrandPalette palette)
    {
        var orgLine = request.OrgLine?.Trim();
        var contactLine = string.IsNullOrWhiteSpace(request.ContactLine) ? request.CtaText?.Trim() : request.ContactLine.Trim();

        if (string.IsNullOrWhiteSpace(orgLine) && string.IsNullOrWhiteSpace(contactLine)) return;

        var maxWidth = width * 0.92f;
        TextBlock? orgBlock = null;
        if (!string.IsNullOrWhiteSpace(orgLine))
        {
            orgBlock = new TextBlock { Alignment = TextAlignment.Center, MaxWidth = maxWidth };
            AddRichText(orgBlock, orgLine,
                new Style { FontFamily = MainFamily, FontSize = width * 0.032f, FontWeight = 700, TextColor = palette.TextPrimary },
                new Style { FontFamily = EmojiFamily, FontSize = width * 0.032f, TextColor = palette.TextPrimary });
            orgBlock.Layout();
        }

        TextBlock? contactBlock = null;
        if (!string.IsNullOrWhiteSpace(contactLine))
        {
            contactBlock = new TextBlock { Alignment = TextAlignment.Center, MaxWidth = maxWidth };
            AddRichText(contactBlock, contactLine,
                new Style { FontFamily = MainFamily, FontSize = width * 0.026f, TextColor = palette.TextSecondary },
                new Style { FontFamily = EmojiFamily, FontSize = width * 0.026f, TextColor = palette.TextSecondary });
            contactBlock.Layout();
        }

        var vGap = orgBlock != null && contactBlock != null ? width * 0.012f : 0f;
        var padding = width * 0.025f;
        var contentHeight = (orgBlock?.MeasuredHeight ?? 0) + vGap + (contactBlock?.MeasuredHeight ?? 0);
        var barHeight = contentHeight + padding * 2;

        var barRect = new SKRect(width * 0.04f, height - barHeight - width * 0.03f, width * 0.96f, height - width * 0.03f);
        using (var paint = new SKPaint { Color = palette.Primary, IsAntialias = true })
            canvas.DrawRoundRect(barRect, 16, 16, paint);
        DrawAccentStroke(canvas, barRect, 16, palette.Accent);

        // TextBlock đã tự canh giữa mỗi dòng bên trong MaxWidth khi Alignment = Center — Paint phải nhận
        // điểm gốc trái của khung MaxWidth (barRect.Left), KHÔNG bù thêm theo MeasuredWidth kẻo bị canh giữa 2 lần.
        var y = barRect.Top + padding;
        if (orgBlock != null)
        {
            orgBlock.Paint(canvas, new SKPoint(barRect.Left, y));
            y += orgBlock.MeasuredHeight + vGap;
        }
        contactBlock?.Paint(canvas, new SKPoint(barRect.Left, y));
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
