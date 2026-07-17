using System.Text.Json;
using Backend.Modules.MediaAsset;

namespace Backend.Modules.GenerationJob;

public static class RenderOutputHelper
{
    public static string ToJson(
        Guid mediaAssetId,
        Guid postMediaId,
        string previewUrl,
        ImageOverlayResult renderResult)
    {
        var payload = new
        {
            mediaAssetId,
            postMediaId,
            previewUrl,
            publicUrl = previewUrl,
            renderResult.StorageKey,
            renderResult.Width,
            renderResult.Height,
            renderResult.TextRendered,
            renderResult.UsedFallbackCopy
        };
        return JsonSerializer.Serialize(payload, JsonOptions);
    }

    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true };
}
