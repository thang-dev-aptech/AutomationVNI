using Microsoft.Extensions.Configuration;

namespace Backend.Shared.Logging;

/// <summary>
/// Cấu hình ship log sang Elasticsearch — đọc từ biến môi trường HOẶC appsettings.Production.json
/// (đã gitignore), không bao giờ từ appsettings.json gốc/appsettings.Development.json (quyết định
/// a98f26db, giống GoogleDriveOptions.CredentialsPath).
/// </summary>
public sealed class ElasticsearchLoggingOptions
{
    public bool Enabled { get; set; }
    public string Url { get; set; } = string.Empty;
    public string User { get; set; } = "elastic";
    public string Password { get; set; } = string.Empty;
    public string CfAccessClientId { get; set; } = string.Empty;
    public string CfAccessClientSecret { get; set; } = string.Empty;
    public string ServiceName { get; set; } = "automationvni";
    public string Environment { get; set; } = "dev";
    public LogLevel MinimumLevel { get; set; } = LogLevel.Information;

    public int BatchSize { get; set; } = 50;
    public int FlushIntervalSeconds { get; set; } = 5;
    public int RequestTimeoutSeconds { get; set; } = 5;
    public int MaxQueueSize { get; set; } = 2000;
    public int MaxRetries { get; set; } = 3;

    /// <summary>
    /// Đọc từng biến theo thứ tự: biến môi trường thật trước (luôn override), rồi fallback về
    /// <paramref name="configuration"/> — nạp cùng TÊN PHẲNG hiện có (vd. "ES_URL" làm key gốc,
    /// không lồng section) nên chỉ cần ghi đúng các key đó ở appsettings.Production.json là dùng
    /// được, không cần đổi .env.example/scripts/README đang tham chiếu tên phẳng này.
    /// </summary>
    public static ElasticsearchLoggingOptions FromConfiguration(IConfiguration configuration)
    {
        string Env(string key) =>
            (System.Environment.GetEnvironmentVariable(key) ?? configuration[key])?.Trim() ?? string.Empty;

        bool EnvBool(string key, bool fallback)
        {
            var raw = Env(key);
            if (string.IsNullOrEmpty(raw)) return fallback;
            return raw is "1" or "true" or "TRUE" or "yes" or "YES";
        }

        static LogLevel ParseLevel(string raw)
        {
            if (string.IsNullOrWhiteSpace(raw)) return LogLevel.Information;
            return raw.Trim().ToUpperInvariant() switch
            {
                "TRACE" or "DEBUG" => LogLevel.Debug,
                "INFO" or "INFORMATION" => LogLevel.Information,
                "WARN" or "WARNING" => LogLevel.Warning,
                "ERROR" => LogLevel.Error,
                "CRITICAL" or "FATAL" => LogLevel.Critical,
                _ => Enum.TryParse<LogLevel>(raw, ignoreCase: true, out var level)
                    ? level
                    : LogLevel.Information,
            };
        }

        return new ElasticsearchLoggingOptions
        {
            Enabled = EnvBool("ES_LOGGING_ENABLED", false),
            Url = Env("ES_URL").TrimEnd('/'),
            User = string.IsNullOrEmpty(Env("ES_USER")) ? "elastic" : Env("ES_USER"),
            Password = Env("ES_PASSWORD"),
            CfAccessClientId = Env("CF_ACCESS_CLIENT_ID"),
            CfAccessClientSecret = Env("CF_ACCESS_CLIENT_SECRET"),
            ServiceName = string.IsNullOrEmpty(Env("LOG_SERVICE_NAME"))
                ? "automationvni"
                : Env("LOG_SERVICE_NAME"),
            Environment = string.IsNullOrEmpty(Env("LOG_ENV")) ? "dev" : Env("LOG_ENV"),
            MinimumLevel = ParseLevel(Env("LOG_LEVEL_TO_ES")),
        };
    }

    public bool IsReady =>
        Enabled
        && !string.IsNullOrWhiteSpace(Url)
        && !string.IsNullOrWhiteSpace(Password)
        && !string.IsNullOrWhiteSpace(CfAccessClientId)
        && !string.IsNullOrWhiteSpace(CfAccessClientSecret);
}
