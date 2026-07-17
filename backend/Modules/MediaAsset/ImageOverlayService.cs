using Backend.Shared.Storage;
using SixLabors.Fonts;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Drawing.Processing;
using SixLabors.ImageSharp.Formats.Png;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;

namespace Backend.Modules.MediaAsset;

public class ImageOverlayService(IFileStorageService fileStorage) : IImageOverlayService
{
    private static readonly string[] FontCandidates =
    [
        "/System/Library/Fonts/Supplemental/Arial.ttf",
        "/System/Library/Fonts/Supplemental/Arial Bold.ttf",
        "/usr/share/fonts/truetype/dejavu/DejaVuSans.ttf",
        "/usr/share/fonts/truetype/dejavu/DejaVuSans-Bold.ttf",
        "C:\\Windows\\Fonts\\arial.ttf",
        "C:\\Windows\\Fonts\\arialbd.ttf"
    ];

    public async Task<ImageOverlayResult> RenderAsync(
        ImageOverlayRequest request, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(request.SourceStorageKey))
            throw new ArgumentException("SourceStorageKey không hợp lệ");

        await using var sourceStream = await fileStorage.OpenReadAsync(request.SourceStorageKey, ct);
        using var memoryStream = new MemoryStream();
        await sourceStream.CopyToAsync(memoryStream, ct);
        var sourceBytes = memoryStream.ToArray();

        try
        {
            return await RenderWithImageSharpAsync(request, sourceBytes, ct);
        }
        catch
        {
            return await FallbackCopyAsync(request, sourceBytes, ct);
        }
    }

    private async Task<ImageOverlayResult> RenderWithImageSharpAsync(
        ImageOverlayRequest request, byte[] sourceBytes, CancellationToken ct)
    {
        using var image = Image.Load<Rgba32>(sourceBytes);

        if (image.Width > 1920 || image.Height > 1920)
        {
            image.Mutate(x => x.Resize(new ResizeOptions
            {
                Mode = ResizeMode.Max,
                Size = new Size(1920, 1920)
            }));
        }

        var overlayHeight = Math.Max(120, image.Height / 5);
        var overlayRect = new RectangleF(0, image.Height - overlayHeight, image.Width, overlayHeight);

        image.Mutate(ctx => ctx.Fill(Color.FromRgba(0, 0, 0, 160), overlayRect));

        var textRendered = TryDrawOverlayText(image, request.Headline, request.CtaText, overlayHeight);
        await TryDrawLogoAsync(image, request.LogoStorageKey, ct);

        await using var outputStream = new MemoryStream();
        await image.SaveAsync(outputStream, new PngEncoder(), ct);
        var outputBytes = outputStream.ToArray();

        var saveResult = await fileStorage.SaveBytesAsync(
            outputBytes,
            request.OutputFolder,
            ".png",
            "image/png",
            ct);

        return new ImageOverlayResult
        {
            StorageKey = saveResult.StorageKey,
            ContentType = saveResult.ContentType,
            SizeBytes = saveResult.SizeBytes,
            Width = image.Width,
            Height = image.Height,
            TextRendered = textRendered,
            UsedFallbackCopy = false
        };
    }

    private async Task<ImageOverlayResult> FallbackCopyAsync(
        ImageOverlayRequest request, byte[] sourceBytes, CancellationToken ct)
    {
        var saveResult = await fileStorage.SaveBytesAsync(
            sourceBytes,
            request.OutputFolder,
            ".png",
            "image/png",
            ct);

        var width = 0;
        var height = 0;
        try
        {
            var info = Image.Identify(sourceBytes);
            if (info is not null)
            {
                width = info.Width;
                height = info.Height;
            }
        }
        catch
        {
            // ignore metadata errors in fallback
        }

        return new ImageOverlayResult
        {
            StorageKey = saveResult.StorageKey,
            ContentType = saveResult.ContentType,
            SizeBytes = saveResult.SizeBytes,
            Width = width,
            Height = height,
            TextRendered = false,
            UsedFallbackCopy = true
        };
    }

    private static bool TryDrawOverlayText(Image image, string headline, string ctaText, int overlayHeight)
    {
        if (TryResolveFontFamily() is not FontFamily fontFamily) return false;

        try
        {
            var titleFont = fontFamily.CreateFont(Math.Max(22f, overlayHeight * 0.22f), FontStyle.Bold);
            var ctaFont = fontFamily.CreateFont(Math.Max(16f, overlayHeight * 0.16f), FontStyle.Regular);

            var title = Truncate(headline, 60);
            var cta = Truncate(ctaText, 40);
            var padding = 24f;
            var titleY = image.Height - overlayHeight + padding;
            var ctaY = titleY + titleFont.Size + 8f;

            image.Mutate(ctx =>
            {
                ctx.DrawText(title, titleFont, Color.White, new PointF(padding, titleY));
                ctx.DrawText(cta, ctaFont, Color.FromRgba(255, 220, 100, 255), new PointF(padding, ctaY));
            });

            return true;
        }
        catch
        {
            return false;
        }
    }

    private async Task TryDrawLogoAsync(Image image, string? logoStorageKey, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(logoStorageKey)) return;
        if (!await fileStorage.ExistsAsync(logoStorageKey, ct)) return;

        try
        {
            await using var logoStream = await fileStorage.OpenReadAsync(logoStorageKey, ct);
            using var logo = await Image.LoadAsync<Rgba32>(logoStream, ct);
            var logoWidth = Math.Min(120, image.Width / 6);
            var ratio = (float)logoWidth / logo.Width;
            var logoHeight = (int)(logo.Height * ratio);

            logo.Mutate(x => x.Resize(logoWidth, logoHeight));

            var xPos = image.Width - logoWidth - 20;
            var yPos = 20;
            image.Mutate(ctx => ctx.DrawImage(logo, new Point(xPos, yPos), 1f));
        }
        catch
        {
            // optional logo — ignore errors
        }
    }

    private static FontFamily? TryResolveFontFamily()
    {
        foreach (var path in FontCandidates)
        {
            if (!File.Exists(path)) continue;
            try
            {
                var collection = new FontCollection();
                var family = collection.Add(path);
                return family;
            }
            catch
            {
                // try next font
            }
        }

        return null;
    }

    private static string Truncate(string value, int maxLength)
    {
        var trimmed = value.Trim();
        if (trimmed.Length <= maxLength) return trimmed;
        return trimmed[..(maxLength - 1)] + "…";
    }
}
