using System.Net.Http.Headers;
using System.Text;

namespace Backend.Shared.Logging;

public static class ElasticsearchLoggingExtensions
{
    /// <summary>
    /// Gắn ILoggerProvider + background shipper. Console/file logging mặc định của ASP.NET vẫn giữ.
    /// </summary>
    public static WebApplicationBuilder AddElasticsearchLogging(this WebApplicationBuilder builder)
    {
        var options = ElasticsearchLoggingOptions.FromConfiguration(builder.Configuration);
        builder.Services.AddSingleton(options);

        if (!options.Enabled)
            return builder;

        if (!options.IsReady)
        {
            Console.Error.WriteLine(
                "[es-logging] ES_LOGGING_ENABLED=true nhưng thiếu ES_URL / ES_PASSWORD / CF_ACCESS_* — bỏ qua ship ES.");
            return builder;
        }

        var queue = new ElasticsearchLogQueue(options.MaxQueueSize);
        builder.Services.AddSingleton(queue);

        builder.Services.AddHttpClient("ElasticsearchLogging", client =>
        {
            client.Timeout = TimeSpan.FromSeconds(Math.Max(1, options.RequestTimeoutSeconds));
            client.DefaultRequestHeaders.Accept.Add(
                new MediaTypeWithQualityHeaderValue("application/json"));
            var token = Convert.ToBase64String(
                Encoding.UTF8.GetBytes($"{options.User}:{options.Password}"));
            client.DefaultRequestHeaders.Authorization =
                new AuthenticationHeaderValue("Basic", token);
        });

        // Provider gắn vào logging pipeline (cùng queue singleton dùng cho shipper + unhandled hooks).
        builder.Logging.AddProvider(new ElasticsearchLoggerProvider(queue, options));
        builder.Services.AddHostedService<ElasticsearchLogShipper>();

        AppDomain.CurrentDomain.UnhandledException += (_, args) =>
        {
            try
            {
                if (args.ExceptionObject is not Exception ex) return;
                queue.TryEnqueue(new ElasticsearchLogEntry
                {
                    Timestamp = DateTime.UtcNow.ToString("o"),
                    Level = "ERROR",
                    Service = options.ServiceName,
                    Environment = options.Environment,
                    Message = LogFieldRedactor.RedactMessage(
                        $"Unhandled exception: {ex.Message}"),
                    Logger = "AppDomain.UnhandledException",
                    Error = new ElasticsearchErrorInfo
                    {
                        Type = ex.GetType().FullName,
                        Message = LogFieldRedactor.RedactMessage(ex.Message),
                        StackTrace = ex.ToString(),
                    },
                });
            }
            catch (Exception logEx)
            {
                Console.Error.WriteLine(
                    $"[es-logging] UnhandledException hook failed: {logEx.GetType().Name}: {logEx.Message}");
            }
        };

        TaskScheduler.UnobservedTaskException += (_, args) =>
        {
            try
            {
                var ex = args.Exception.GetBaseException();
                queue.TryEnqueue(new ElasticsearchLogEntry
                {
                    Timestamp = DateTime.UtcNow.ToString("o"),
                    Level = "ERROR",
                    Service = options.ServiceName,
                    Environment = options.Environment,
                    Message = LogFieldRedactor.RedactMessage(
                        $"Unobserved task exception: {ex.Message}"),
                    Logger = "TaskScheduler.UnobservedTaskException",
                    Error = new ElasticsearchErrorInfo
                    {
                        Type = ex.GetType().FullName,
                        Message = LogFieldRedactor.RedactMessage(ex.Message),
                        StackTrace = args.Exception.ToString(),
                    },
                });
                args.SetObserved();
            }
            catch (Exception logEx)
            {
                Console.Error.WriteLine(
                    $"[es-logging] UnobservedTaskException hook failed: {logEx.GetType().Name}: {logEx.Message}");
            }
        };

        return builder;
    }
}
