namespace Backend.Modules.MediaAsset;

public static class MediaAssetUrls
{
    public static string Preview(Guid id) => $"/api/mediaasset/{id}/preview";
    public static string Download(Guid id) => $"/api/mediaasset/{id}/download";
}
