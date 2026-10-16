using Backend.Modules.Campaign.FullMedia.Scoring;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Backend.Modules.Campaign.FullMedia;

/// <summary>
/// Đăng ký toàn bộ Full Media (Vision + PageFit + pipeline + worker) tại một chỗ.
/// </summary>
public static class FullMediaPipelineServiceCollectionExtensions
{
    public static IServiceCollection AddFullMediaPipeline(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        services.Configure<FullMediaPipelineOptions>(
            configuration.GetSection(FullMediaPipelineOptions.SectionName));
        services.AddSingleton<FullMediaCircuitBreaker>();
        services.AddScoped<MediaVisionService>();
        services.AddScoped<IMediaVisionDescriber>(sp => sp.GetRequiredService<MediaVisionService>());
        services.AddFullMediaPageFit(configuration);
        services.AddScoped<FullMediaPipelineService>();
        services.AddHostedService<CampaignFullMediaWorker>();
        return services;
    }
}
