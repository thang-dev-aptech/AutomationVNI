using System.Collections.Concurrent;

namespace Backend.Shared.Logging;

internal sealed class ElasticsearchLogger(
    string categoryName,
    ElasticsearchLogQueue queue,
    ElasticsearchLoggingOptions options) : ILogger
{
    private static readonly AsyncLocal<ConcurrentStack<IReadOnlyList<KeyValuePair<string, object?>>>?> ScopeStack = new();

    public IDisposable? BeginScope<TState>(TState state) where TState : notnull
    {
        if (state is not IEnumerable<KeyValuePair<string, object?>> pairs)
            return null;

        var list = pairs as IReadOnlyList<KeyValuePair<string, object?>>
                   ?? pairs.ToList();
        var stack = ScopeStack.Value ??= new ConcurrentStack<IReadOnlyList<KeyValuePair<string, object?>>>();
        stack.Push(list);
        return new ScopePopper(stack);
    }

    public bool IsEnabled(LogLevel logLevel)
        => options.IsReady
           && logLevel != LogLevel.None
           && logLevel >= options.MinimumLevel;

    public void Log<TState>(
        LogLevel logLevel,
        EventId eventId,
        TState state,
        Exception? exception,
        Func<TState, Exception?, string> formatter)
    {
        if (!IsEnabled(logLevel)) return;

        try
        {
            var message = LogFieldRedactor.RedactMessage(formatter(state, exception));
            var extra = CollectExtra(state);

            var entry = new ElasticsearchLogEntry
            {
                Timestamp = DateTime.UtcNow.ToString("o"),
                Level = ToLevelName(logLevel),
                Service = options.ServiceName,
                Environment = options.Environment,
                Message = message,
                Logger = categoryName,
                Extra = extra.Count > 0 ? extra : null,
            };

            if (exception is not null)
            {
                entry.Error = new ElasticsearchErrorInfo
                {
                    Type = exception.GetType().FullName,
                    Message = LogFieldRedactor.RedactMessage(exception.Message),
                    StackTrace = exception.ToString(),
                };
            }

            queue.TryEnqueue(entry);
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"[es-logging] log capture failed: {ex.GetType().Name}: {ex.Message}");
        }
    }

    private static Dictionary<string, object?> CollectExtra<TState>(TState state)
    {
        var bag = new List<KeyValuePair<string, object?>>();

        if (state is IEnumerable<KeyValuePair<string, object?>> statePairs)
        {
            foreach (var pair in statePairs)
            {
                // Bỏ key placeholder formatter "{OriginalFormat}"
                if (string.Equals(pair.Key, "{OriginalFormat}", StringComparison.Ordinal))
                    continue;
                bag.Add(pair);
            }
        }

        var stack = ScopeStack.Value;
        if (stack is not null)
        {
            foreach (var scope in stack.Reverse())
            {
                foreach (var pair in scope)
                    bag.Add(pair);
            }
        }

        return LogFieldRedactor.RedactExtra(bag);
    }

    private static string ToLevelName(LogLevel level) => level switch
    {
        LogLevel.Trace => "TRACE",
        LogLevel.Debug => "DEBUG",
        LogLevel.Information => "INFO",
        LogLevel.Warning => "WARN",
        LogLevel.Error => "ERROR",
        LogLevel.Critical => "CRITICAL",
        _ => level.ToString().ToUpperInvariant(),
    };

    private sealed class ScopePopper(ConcurrentStack<IReadOnlyList<KeyValuePair<string, object?>>> stack) : IDisposable
    {
        public void Dispose()
        {
            stack.TryPop(out _);
        }
    }
}
