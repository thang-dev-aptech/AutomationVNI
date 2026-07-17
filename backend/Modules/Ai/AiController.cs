using Backend.Modules.GenerationJob;
using Backend.Shared;
using Backend.Shared.Ai;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;

namespace Backend.Modules.Ai;

[ApiController]
[Route("api/[controller]")]
[Authorize(Roles = "Admin,ContentManager")]
public class AiController(
    IAiTextGenerationService aiTextService,
    IOptions<AiProvidersOptions> aiOptions,
    IAiImageGenerationService aiImageService,
    IOptions<AiImageProvidersOptions> imageOptions) : ControllerBase
{
    [HttpPost("test-text-generation")]
    public async Task<IActionResult> TestTextGeneration(
        [FromBody] AiTextGenerationRequest request,
        CancellationToken ct)
    {
        var provider = request.Provider ?? aiOptions.Value.DefaultProvider;

        if (!aiTextService.IsAvailable(provider))
        {
            var mock = MockTextGenerator.GenerateFromRequest(request);
            return Ok(ApiResponse.Ok(new AiTestTextGenerationResponse
            {
                ProviderAvailable = false,
                Source = "mock",
                Provider = provider,
                Message = $"Provider '{provider}' has no ApiKey. Set via user-secrets: " +
                          $"AiProviders:Providers:{provider}:ApiKey. Returning mock preview.",
                Result = mock
            }, "AI provider unavailable — mock preview returned"));
        }

        try
        {
            var result = await aiTextService.GenerateAsync(request, ct);
            return Ok(ApiResponse.Ok(new AiTestTextGenerationResponse
            {
                ProviderAvailable = true,
                Source = "ai",
                Provider = provider,
                Model = request.Model ?? aiOptions.Value.Providers.GetValueOrDefault(provider)?.DefaultTextModel,
                Result = result
            }, "AI text generation succeeded"));
        }
        catch (AiProviderUnavailableException ex)
        {
            var mock = MockTextGenerator.GenerateFromRequest(request);
            return Ok(ApiResponse.Ok(new AiTestTextGenerationResponse
            {
                ProviderAvailable = false,
                Source = "mock",
                Provider = provider,
                Message = ex.Message,
                Result = mock
            }, "AI provider unavailable — mock preview returned"));
        }
        catch (AiProviderConfigException ex)
        {
            return BadRequest(ApiResponse.Fail("AI_CONFIG_ERROR", ex.Message));
        }
        catch (AiTextGenerationException ex)
        {
            return BadRequest(ApiResponse.Fail("AI_GENERATION_ERROR", ex.Message));
        }
    }

    [HttpPost("test-image-generation")]
    public async Task<IActionResult> TestImageGeneration(
        [FromBody] AiImageGenerationRequest request,
        CancellationToken ct)
    {
        var provider = request.Provider ?? imageOptions.Value.DefaultProvider;

        if (!aiImageService.IsAvailable(provider))
        {
            return Ok(ApiResponse.Ok(new
            {
                providerAvailable = false,
                source = "mock",
                provider,
                message = $"Image provider '{provider}' has no ApiKey. " +
                          $"Set via user-secrets: AiImageProviders:Providers:{provider}:ApiKey."
            }, "Image provider unavailable — set ApiKey to test real generation"));
        }

        try
        {
            var result = await aiImageService.GenerateAsync(request, ct);
            return Ok(ApiResponse.Ok(new
            {
                providerAvailable = true,
                source = "ai",
                provider = result.Provider,
                model = result.Model,
                mimeType = result.MimeType,
                sizeBytes = result.ImageBytes.Length
            }, "AI image generation succeeded"));
        }
        catch (AiProviderUnavailableException ex)
        {
            return Ok(ApiResponse.Ok(new
            {
                providerAvailable = false,
                source = "mock",
                provider,
                message = ex.Message
            }, "Image provider unavailable — mock fallback in pipeline"));
        }
        catch (AiProviderConfigException ex)
        {
            return BadRequest(ApiResponse.Fail("AI_CONFIG_ERROR", ex.Message));
        }
        catch (AiImageGenerationException ex)
        {
            return BadRequest(ApiResponse.Fail("AI_IMAGE_ERROR", ex.Message));
        }
    }
}
