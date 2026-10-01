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

    public static string Lines(params string[] lines) => JsonSerializer.Serialize(new { lines });
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
