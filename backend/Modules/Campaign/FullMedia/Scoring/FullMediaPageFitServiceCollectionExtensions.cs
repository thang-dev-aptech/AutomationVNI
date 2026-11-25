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
        // VotingPageFitScorer cần MinFitScore từ FullMediaPipeline — đăng ký cả khi gọi PageFit riêng.
        services.Configure<FullMediaPipelineOptions>(
            configuration.GetSection(FullMediaPipelineOptions.SectionName));

        services.AddHttpClient<VotingPageFitScorer>((sp, client) => ApplyTimeout(sp, client));
        services.AddHttpClient<LlmLogprobPageFitScorer>((sp, client) => ApplyTimeout(sp, client));

        services.AddTransient<IPageFitScorer>(sp =>
        {
            var mode = (sp.GetRequiredService<IOptions<PageFitOptions>>().Value.Mode ?? "").Trim();
            if (mode.Length == 0 || string.Equals(mode, "Vote", StringComparison.OrdinalIgnoreCase))
                return sp.GetRequiredService<VotingPageFitScorer>();
            if (string.Equals(mode, "Logprobs", StringComparison.OrdinalIgnoreCase))
                return sp.GetRequiredService<LlmLogprobPageFitScorer>();
            return new InvalidPageFitModeScorer(mode);
        });

        return services;
    }

    private static void ApplyTimeout(IServiceProvider sp, HttpClient client)
    {
        var opts = sp.GetRequiredService<IOptions<PageFitOptions>>().Value;
        var seconds = opts.TimeoutSeconds > 0 ? opts.TimeoutSeconds : 60;
        client.Timeout = TimeSpan.FromSeconds(seconds);
    }
}
