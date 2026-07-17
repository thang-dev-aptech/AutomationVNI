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
    IOptions<AiProvidersOptions> aiOptions) : ControllerBase
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
}
