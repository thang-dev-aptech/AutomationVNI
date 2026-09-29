namespace Backend.Shared.Logging;

public sealed class ElasticsearchLoggerProvider(
    ElasticsearchLogQueue queue,
    ElasticsearchLoggingOptions options) : ILoggerProvider
{
    public ILogger CreateLogger(string categoryName)
        => new ElasticsearchLogger(categoryName, queue, options);

    public void Dispose()
    {
        // Queue / shipper sống theo host lifetime — không dispose ở đây.
    }
}
