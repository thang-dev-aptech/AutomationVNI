namespace Backend.Modules.MediaAsset;

public interface IImageOverlayService
{
    Task<ImageOverlayResult> RenderAsync(ImageOverlayRequest request, CancellationToken ct = default);
}
