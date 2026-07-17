using System.Text.Json;
using Backend.Modules.MediaAsset.Enums;
using Backend.Modules.Post;

namespace Backend.Modules.GenerationJob;

public static class MockImageGenerator
{
    // 1x1 PNG placeholder
    private static readonly byte[] PlaceholderPng = Convert.FromBase64String(
        "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR42mP8z8BQDwAEhQGAhKmMIQAAAABJRU5ErkJggg==");

    public static byte[] GetPlaceholderPngBytes() => PlaceholderPng;

    public static MockImageGenerationResult Generate(PostModel post, string? imagePrompt = null)
    {
        var prompt = string.IsNullOrWhiteSpace(imagePrompt)
            ? $"Professional social media visual for '{post.Title.Trim()}', modern clean style"
            : imagePrompt.Trim();

        var fileId = Guid.NewGuid().ToString("N")[..12];
        var fileName = $"mock-{fileId}.png";

        return new MockImageGenerationResult
        {
            Prompt = prompt,
            FileName = fileName,
            OriginalFileName = $"ai-generated-{SanitizeFileName(post.Title)}.png",
            MimeType = "image/png",
            FileSize = PlaceholderPng.Length,
            Source = MediaSource.AIGenerated,
            Width = 1080,
            Height = 1080,
            AltText = $"AI generated image for {post.Title.Trim()}",
            Description = prompt
        };
    }

    public static string ToJson(MockImageGenerationResult result, Guid mediaAssetId, Guid postMediaId, string previewUrl)
    {
        var payload = new
        {
            mediaAssetId,
            postMediaId,
            previewUrl,
            result.Prompt,
            result.FileName,
            result.MimeType,
            result.Width,
            result.Height
        };
        return JsonSerializer.Serialize(payload, JsonOptions);
    }

    public static string? TryExtractImagePrompt(string? textJobOutput)
    {
        if (string.IsNullOrWhiteSpace(textJobOutput)) return null;
        try
        {
            var output = JsonSerializer.Deserialize<TextGenerationJobOutput>(textJobOutput, JsonOptions);
            if (!string.IsNullOrWhiteSpace(output?.ImagePrompt))
                return output.ImagePrompt;

            var legacy = JsonSerializer.Deserialize<MockTextGenerationResult>(textJobOutput, JsonOptions);
            return string.IsNullOrWhiteSpace(legacy?.ImagePrompt) ? null : legacy.ImagePrompt;
        }
        catch
        {
            return null;
        }
    }

    private static string SanitizeFileName(string input)
    {
        var name = new string(input.Where(c => !Path.GetInvalidFileNameChars().Contains(c)).ToArray()).Trim();
        return string.IsNullOrEmpty(name) ? "image" : name[..Math.Min(name.Length, 80)];
    }

    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true };
}
