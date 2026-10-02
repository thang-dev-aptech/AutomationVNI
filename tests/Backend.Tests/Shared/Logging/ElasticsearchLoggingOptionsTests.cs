using Backend.Shared.Logging;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Xunit;

namespace Backend.Tests.Shared.Logging;

/// <summary>
/// Quyết định a98f26db: ElasticsearchLoggingOptions phải đọc được từ appsettings.Production.json
/// (mô phỏng bằng IConfiguration in-memory) khi không có biến môi trường thật, và biến môi trường
/// phải luôn override khi cả hai nguồn cùng có giá trị.
/// </summary>
public class ElasticsearchLoggingOptionsTests
{
    private static readonly string[] ManagedEnvKeys =
    [
        "ES_LOGGING_ENABLED", "ES_URL", "ES_USER", "ES_PASSWORD",
        "CF_ACCESS_CLIENT_ID", "CF_ACCESS_CLIENT_SECRET",
        "LOG_SERVICE_NAME", "LOG_ENV", "LOG_LEVEL_TO_ES",
    ];

    private static IConfiguration BuildConfig(Dictionary<string, string?> values) =>
        new ConfigurationBuilder().AddInMemoryCollection(values).Build();

    private static void ClearManagedEnvVars()
    {
        foreach (var key in ManagedEnvKeys)
            System.Environment.SetEnvironmentVariable(key, null);
    }

    [Fact]
    public void FromConfiguration_ReadsFromAppsettingsWhenNoEnvironmentVariableSet()
    {
        ClearManagedEnvVars();
        try
        {
            var config = BuildConfig(new()
            {
                ["ES_LOGGING_ENABLED"] = "true",
                ["ES_URL"] = "https://from-config.example",
                ["ES_USER"] = "config-user",
                ["ES_PASSWORD"] = "config-password",
                ["CF_ACCESS_CLIENT_ID"] = "config-cf-id",
                ["CF_ACCESS_CLIENT_SECRET"] = "config-cf-secret",
                ["LOG_SERVICE_NAME"] = "config-service",
                ["LOG_ENV"] = "config-env",
                ["LOG_LEVEL_TO_ES"] = "WARN",
            });

            var options = ElasticsearchLoggingOptions.FromConfiguration(config);

            Assert.True(options.Enabled);
            Assert.Equal("https://from-config.example", options.Url);
            Assert.Equal("config-user", options.User);
            Assert.Equal("config-password", options.Password);
            Assert.Equal("config-cf-id", options.CfAccessClientId);
            Assert.Equal("config-cf-secret", options.CfAccessClientSecret);
            Assert.Equal("config-service", options.ServiceName);
            Assert.Equal("config-env", options.Environment);
            Assert.Equal(LogLevel.Warning, options.MinimumLevel);
            Assert.True(options.IsReady);
        }
        finally
        {
            ClearManagedEnvVars();
        }
    }

    [Fact]
    public void FromConfiguration_EnvironmentVariableOverridesAppsettingsValue()
    {
        ClearManagedEnvVars();
        try
        {
            System.Environment.SetEnvironmentVariable("ES_URL", "https://from-env.example");
            System.Environment.SetEnvironmentVariable("ES_PASSWORD", "env-password");

            var config = BuildConfig(new()
            {
                ["ES_LOGGING_ENABLED"] = "true",
                ["ES_URL"] = "https://from-config.example",
                ["ES_PASSWORD"] = "config-password",
                ["CF_ACCESS_CLIENT_ID"] = "config-cf-id",
                ["CF_ACCESS_CLIENT_SECRET"] = "config-cf-secret",
            });

            var options = ElasticsearchLoggingOptions.FromConfiguration(config);

            // Biến môi trường override — không phải giá trị trong appsettings.
            Assert.Equal("https://from-env.example", options.Url);
            Assert.Equal("env-password", options.Password);
            // Field không có biến môi trường vẫn fallback đúng về appsettings.
            Assert.Equal("config-cf-id", options.CfAccessClientId);
            Assert.Equal("config-cf-secret", options.CfAccessClientSecret);
        }
        finally
        {
            ClearManagedEnvVars();
        }
    }

    [Fact]
    public void FromConfiguration_MissingBothSources_NotReady_DoesNotThrow()
    {
        ClearManagedEnvVars();
        try
        {
            var config = BuildConfig(new());
            var options = ElasticsearchLoggingOptions.FromConfiguration(config);

            Assert.False(options.Enabled);
            Assert.False(options.IsReady);
            Assert.Equal("elastic", options.User);
            Assert.Equal("automationvni", options.ServiceName);
            Assert.Equal("dev", options.Environment);
        }
        finally
        {
            ClearManagedEnvVars();
        }
    }
}
