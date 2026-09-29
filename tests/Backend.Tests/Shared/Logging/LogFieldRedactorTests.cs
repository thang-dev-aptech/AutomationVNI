using Backend.Shared.Logging;
using Xunit;

namespace Backend.Tests.Shared.Logging;

public class LogFieldRedactorTests
{
    [Theory]
    [InlineData("password")]
    [InlineData("Token")]
    [InlineData("authorization")]
    [InlineData("api_key")]
    [InlineData("COOKIE")]
    [InlineData("client_secret")]
    public void IsSensitiveKey_MatchesCommonSecretNames(string key)
        => Assert.True(LogFieldRedactor.IsSensitiveKey(key));

    [Fact]
    public void RedactExtra_ReplacesSensitiveValues()
    {
        var redacted = LogFieldRedactor.RedactExtra(
        [
            new("userId", "u-1"),
            new("password", "super-secret"),
            new("Authorization", "Bearer abc"),
        ]);

        Assert.Equal("u-1", redacted["userId"]);
        Assert.Equal("[REDACTED]", redacted["password"]);
        Assert.Equal("[REDACTED]", redacted["Authorization"]);
    }

    [Fact]
    public void RedactMessage_MasksInlineAssignments()
    {
        var raw = "login failed password=secret123 token=xyz";
        var cleaned = LogFieldRedactor.RedactMessage(raw);
        Assert.DoesNotContain("secret123", cleaned);
        Assert.DoesNotContain("xyz", cleaned);
        Assert.Contains("[REDACTED]", cleaned);
    }

    /// <summary>
    /// AC / violation b7d37bc1: mọi tên trong SensitiveKeys (kể cả id_token, secret_key,
    /// connectionstring, cf-access-client-secret) phải bị che khi dạng key=value trong message.
    /// </summary>
    [Fact]
    public void RedactMessage_MasksEverySensitiveKeyAssignment()
    {
        foreach (var key in LogFieldRedactor.SensitiveKeyNames)
        {
            var secretValue = $"raw-secret-for-{key.Replace('-', '_')}";
            var raw = $"ctx {key}={secretValue} trailing";
            var cleaned = LogFieldRedactor.RedactMessage(raw);

            Assert.DoesNotContain(secretValue, cleaned);
            Assert.Contains("[REDACTED]", cleaned);

            // Dạng key: value cũng phải che.
            var colonRaw = $"{key}: {secretValue}";
            var colonCleaned = LogFieldRedactor.RedactMessage(colonRaw);
            Assert.DoesNotContain(secretValue, colonCleaned);
            Assert.Contains("[REDACTED]", colonCleaned);
        }
    }

    [Theory]
    [InlineData("id_token", "jwt-abc")]
    [InlineData("secret_key", "sk-live")]
    [InlineData("connectionstring", "DataSource=/secret.db")]
    [InlineData("cf-access-client-secret", "cf-secret-xyz")]
    public void RedactMessage_MasksPreviouslyMissingKeys(string key, string value)
    {
        var cleaned = LogFieldRedactor.RedactMessage($"probe {key}={value} ok");
        Assert.DoesNotContain(value, cleaned);
        Assert.Contains($"{key}=[REDACTED]", cleaned, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Queue_DropsWhenFull_IncrementsDroppedCount_DoesNotThrow()
    {
        const int capacity = 2;
        var queue = new ElasticsearchLogQueue(capacity);
        Assert.Equal(capacity, queue.Capacity);

        Assert.True(queue.TryEnqueue(NewEntry("a")));
        Assert.True(queue.TryEnqueue(NewEntry("b")));
        Assert.Equal(0, queue.DroppedCount);

        // Ep đầy thật: 3 lần vượt capacity phải fail nhanh, không throw, DroppedCount = 3.
        Assert.False(queue.TryEnqueue(NewEntry("c")));
        Assert.False(queue.TryEnqueue(NewEntry("d")));
        Assert.False(queue.TryEnqueue(NewEntry("e")));
        Assert.Equal(3, queue.DroppedCount);

        // Trong queue chỉ còn đúng capacity phần tử đã nhận trước khi đầy.
        var drained = 0;
        while (queue.TryRead(out _))
            drained++;
        Assert.Equal(capacity, drained);
        Assert.Equal(3, queue.DroppedCount);
    }

    private static ElasticsearchLogEntry NewEntry(string message) => new()
    {
        Timestamp = DateTime.UtcNow.ToString("o"),
        Level = "INFO",
        Service = "test",
        Environment = "test",
        Message = message,
        Logger = "test",
    };
}
