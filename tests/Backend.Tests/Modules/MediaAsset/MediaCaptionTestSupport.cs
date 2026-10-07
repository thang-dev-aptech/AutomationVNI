using System.Net;
using System.Text;
using System.Text.Json;
using Backend.Shared.Ai;
using Backend.Shared.Storage;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Options;

namespace Backend.Tests.Modules.MediaAsset;

/// <summary>HttpMessageHandler giả cho chat completions: trả lần lượt các content theo kịch bản
/// và giữ lại body từng request để test kiểm tra prompt/ngữ cảnh gửi đi.</summary>
internal sealed class ScriptedChatHandler(params string[] contents) : HttpMessageHandler
{
    private readonly Queue<string> _contents = new(contents);
    public List<string> RequestBodies { get; } = [];

    protected override async Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request, CancellationToken cancellationToken)
    {
        RequestBodies.Add(request.Content is null ? "" : await request.Content.ReadAsStringAsync(cancellationToken));
        var content = _contents.Count > 0 ? _contents.Dequeue() : "{\"lines\":[]}";
        var body = JsonSerializer.Serialize(new { choices = new[] { new { message = new { content } } } });
        return new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(body, Encoding.UTF8, "application/json")
        };
    }

}

/// <summary>Trả lời AI giả cho caption sinh từ ảnh (R-029): JSON {caption, hashtags, cta, bannerHeadline} và
/// bài đã ghép tương ứng. Mặc định có CTA + hashtag để kết quả không phụ thuộc fallback của Page.</summary>
internal static class CaptionAi
{
    public const string Cta = "Nhắn tin ngay 💬";
    private static readonly string[] DefaultTags = ["#thu"];

    public static string Json(string caption, string headline = "", string cta = Cta, string[]? hashtags = null)
        => JsonSerializer.Serialize(new { caption, hashtags = hashtags ?? DefaultTags, cta, bannerHeadline = headline });

    public static string Expected(string caption, string headline = "", string cta = Cta, string[]? hashtags = null)
        => Backend.Shared.Ai.FacebookPostComposer.Compose(
            headline, caption, cta, Backend.Shared.Ai.FacebookPostComposer.NormalizeHashtags(hashtags ?? DefaultTags));
}

/// <summary>Handler có cổng: request thứ i chỉ trả lời sau khi test gọi Release(i); Arrived(i) hoàn tất
/// khi request thứ i đã tới. Dùng để dựng race giữa nhiều lời gọi AI đang chạy cùng lúc.</summary>
internal sealed class GatedChatHandler(params string[] contents) : HttpMessageHandler
{
    private readonly object _lock = new();
    private readonly List<TaskCompletionSource> _arrived = [];
    private readonly List<TaskCompletionSource> _gates = [];
    private int _next;

    public int RequestCount => Volatile.Read(ref _next);

    public Task Arrived(int index) => Slot(_arrived, index).Task;
    public void Release(int index) => Slot(_gates, index).TrySetResult();

    private TaskCompletionSource Slot(List<TaskCompletionSource> list, int index)
    {
        lock (_lock)
        {
            while (list.Count <= index)
                list.Add(new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously));
            return list[index];
        }
    }

    protected override async Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var index = Interlocked.Increment(ref _next) - 1;
        Slot(_arrived, index).TrySetResult();
        await Slot(_gates, index).Task.WaitAsync(cancellationToken);
        var content = index < contents.Length ? contents[index] : "{\"lines\":[]}";
        var body = JsonSerializer.Serialize(new { choices = new[] { new { message = new { content } } } });
        return new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(body, Encoding.UTF8, "application/json")
        };
    }
}

/// <summary>Trả lời sau <paramref name="delay"/> (tôn trọng token như HttpClient thật) — để test timeout per-call.</summary>
internal sealed class DelayedChatHandler(TimeSpan delay, string content) : HttpMessageHandler
{
    protected override async Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request, CancellationToken cancellationToken)
    {
        await Task.Delay(delay, cancellationToken);
        var body = JsonSerializer.Serialize(new { choices = new[] { new { message = new { content } } } });
        return new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(body, Encoding.UTF8, "application/json")
        };
    }
}

internal sealed class InMemoryImageStorage : IFileStorageService
{
    public static readonly byte[] ImageBytes = [0x89, 0x50, 0x4E, 0x47, 1, 2, 3];

    public Task<FileSaveResult> SaveAsync(IFormFile file, string folder, CancellationToken ct = default)
        => throw new NotImplementedException();

    public Task<FileSaveResult> SaveBytesAsync(
        byte[] data, string folder, string extension, string contentType, CancellationToken ct = default)
        => throw new NotImplementedException();

    public Task<Stream> OpenReadAsync(string storageKey, CancellationToken ct = default)
        => Task.FromResult<Stream>(new MemoryStream(ImageBytes));

    public Task<bool> ExistsAsync(string storageKey, CancellationToken ct = default)
        => Task.FromResult(true);

    public Task DeleteAsync(string storageKey, CancellationToken ct = default) => Task.CompletedTask;
}

internal static class CaptionAiOptions
{
    public static IOptions<AiProvidersOptions> Create() => Options.Create(new AiProvidersOptions
    {
        DefaultProvider = "test",
        Providers = new(StringComparer.OrdinalIgnoreCase)
        {
            ["test"] = new AiProviderConfig
            {
                BaseUrl = "http://ai.test",
                ApiKey = "key",
                DefaultTextModel = "vision-test"
            }
        }
    });
}
