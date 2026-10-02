using System.Text.Json.Serialization;

namespace Backend.Shared.Logging;

/// <summary>Một bản ghi log sẵn sàng gửi Elasticsearch (ECS-ish flat JSON).</summary>
public sealed class ElasticsearchLogEntry
{
    [JsonPropertyName("@timestamp")]
    public string Timestamp { get; set; } = string.Empty;

    [JsonPropertyName("level")]
    public string Level { get; set; } = string.Empty;

    [JsonPropertyName("service")]
    public string Service { get; set; } = string.Empty;

    [JsonPropertyName("environment")]
    public string Environment { get; set; } = string.Empty;

    [JsonPropertyName("message")]
    public string Message { get; set; } = string.Empty;

    [JsonPropertyName("logger")]
    public string Logger { get; set; } = string.Empty;

    [JsonPropertyName("error")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public ElasticsearchErrorInfo? Error { get; set; }

    /// <summary>Trường ngữ cảnh thêm (request id, user id, …) — merge phẳng vào document.</summary>
    [JsonExtensionData]
    public Dictionary<string, object?>? Extra { get; set; }
}

public sealed class ElasticsearchErrorInfo
{
    [JsonPropertyName("type")]
    public string? Type { get; set; }

    [JsonPropertyName("message")]
    public string? Message { get; set; }

    [JsonPropertyName("stack_trace")]
    public string? StackTrace { get; set; }
}
