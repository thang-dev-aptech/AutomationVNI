using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Backend.Modules.Campaign.FullMedia.Scoring;

public static class FullMediaPageFitServiceCollectionExtensions
{
    public static IServiceCollection AddFullMediaPageFit(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        services.Configure<PageFitOptions>(configuration.GetSection(PageFitOptions.SectionName));
        services.AddHttpClient<IPageFitScorer, LlmLogprobPageFitScorer>((sp, client) =>
        {
            var opts = sp.GetRequiredService<IOptions<PageFitOptions>>().Value;
            var seconds = opts.TimeoutSeconds > 0 ? opts.TimeoutSeconds : 60;
            client.Timeout = TimeSpan.FromSeconds(seconds);
        });
        return services;
    }
}