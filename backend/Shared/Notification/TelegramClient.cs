using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Options;

namespace Backend.Shared.Notification;

/// <summary>Một nút bấm dưới tin nhắn. <paramref name="Data"/> quay lại trong callback_query.</summary>
public sealed record TelegramButton(string Text, string Data);

/// <summary>Một update đã bóc sẵn — gộp message thường và callback từ nút bấm về một dạng.</summary>
public sealed record TelegramUpdate(
    long UpdateId, long ChatId, string Text, int? MessageId, string? CallbackQueryId);

/// <summary>
/// Lớp mỏng bọc Bot API. Chỉ 4 phương thức mà luồng duyệt tin cần, không dựng cả SDK.
///
/// Mọi lỗi mạng đều nuốt và trả về null/rỗng: bot là kênh phụ, Telegram sập thì luồng cào
/// vẫn phải chạy tiếp và người dùng vẫn duyệt được trên web.
/// </summary>
public class TelegramClient(
    HttpClient http, IOptions<TelegramOptions> options, ILogger<TelegramClient> logger)
{
    private static readonly JsonSerializerOptions Json = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    private TelegramOptions Opt => options.Value;

    public bool IsConfigured => Opt.Enabled && !string.IsNullOrWhiteSpace(Opt.BotToken);

    private string Url(string method) => $"{Opt.BaseUrl.TrimEnd('/')}/bot{Opt.BotToken}/{method}";

    /// <summary>Gửi tin. Trả message_id để sau này sửa/gỡ nút, null nếu hỏng.</summary>
    public Task<int?> SendMessageAsync(
        long chatId, string text, IReadOnlyList<TelegramButton>? buttons = null,
        CancellationToken ct = default)
        => SendKeyboardAsync(chatId, text, buttons is null ? null : [buttons], ct);

    /// <summary>
    /// Bàn phím nhiều hàng — 15 page mà xếp một hàng thì trượt ngang không đọc nổi.
    /// Tên khác hẳn SendMessageAsync là cố ý: hai overload chỉ khác nhau ở độ lồng của
    /// IReadOnlyList thì gọi với null là trình biên dịch bó tay, không phân giải được.
    /// </summary>
    public async Task<int?> SendKeyboardAsync(
        long chatId, string text, IReadOnlyList<IReadOnlyList<TelegramButton>>? rows,
        CancellationToken ct = default)
    {
        if (!IsConfigured) return null;

        var payload = new Dictionary<string, object?>
        {
            ["chat_id"] = chatId,
            ["text"] = text,
            ["parse_mode"] = "HTML",
            // Tin cào luôn kèm link bài gốc. Không tắt preview thì mỗi tin nhắn kéo theo một
            // thẻ ảnh to đùng, cuộn 5 bài là hết màn hình điện thoại.
            ["link_preview_options"] = new { is_disabled = true },
        };
        if (rows is { Count: > 0 }) payload["reply_markup"] = Keyboard(rows);

        return await PostAsync(
            "sendMessage", payload,
            el => el.TryGetProperty("message_id", out var m) ? m.GetInt32() : (int?)null, ct);
    }

    /// <summary>Sửa lại nội dung tin đã gửi — dùng để xoá nút sau khi người dùng bấm.</summary>
    public async Task EditMessageAsync(
        long chatId, int messageId, string text,
        IReadOnlyList<IReadOnlyList<TelegramButton>>? rows = null, CancellationToken ct = default)
    {
        if (!IsConfigured) return;
        var payload = new Dictionary<string, object?>
        {
            ["chat_id"] = chatId,
            ["message_id"] = messageId,
            ["text"] = text,
            ["parse_mode"] = "HTML",
            ["link_preview_options"] = new { is_disabled = true },
        };
        // Không gửi reply_markup thì Telegram GIỮ NGUYÊN bàn phím cũ. Muốn gỡ nút phải gửi
        // mảng rỗng — bỏ qua chỗ này là nút "Đăng" còn nguyên sau khi đã đăng xong.
        payload["reply_markup"] = rows is { Count: > 0 }
            ? Keyboard(rows)
            : new { inline_keyboard = Array.Empty<object[]>() };

        await PostAsync("editMessageText", payload, _ => 0, ct);
    }

    private static object Keyboard(IReadOnlyList<IReadOnlyList<TelegramButton>> rows) => new
    {
        inline_keyboard = rows
            .Select(r => r.Select(b => new { text = b.Text, callback_data = b.Data }).ToArray())
            .ToArray(),
    };

    /// <summary>
    /// Bắt buộc gọi sau mỗi callback_query, kể cả khi không hiện gì. Không gọi thì nút cứ
    /// quay vòng tròn trên máy người dùng cho tới lúc Telegram tự bỏ cuộc.
    /// </summary>
    public async Task AnswerCallbackAsync(string callbackId, string? text, CancellationToken ct = default)
    {
        if (!IsConfigured) return;
        await PostAsync("answerCallbackQuery", new Dictionary<string, object?>
        {
            ["callback_query_id"] = callbackId,
            ["text"] = text,
        }, _ => 0, ct);
    }

    /// <summary>
    /// Long-polling. Telegram giữ kết nối tới khi có update hoặc hết timeout, nên HttpClient
    /// phải có timeout LỚN HƠN PollTimeoutSeconds — bằng nhau là chắc chắn ăn TaskCanceled.
    /// </summary>
    public async Task<List<TelegramUpdate>> GetUpdatesAsync(long offset, CancellationToken ct = default)
    {
        var result = new List<TelegramUpdate>();
        if (!IsConfigured) return result;

        try
        {
            var url = $"{Url("getUpdates")}?offset={offset}&timeout={Opt.PollTimeoutSeconds}"
                      + "&allowed_updates=[\"message\",\"callback_query\"]";
            using var res = await http.GetAsync(url, ct);
            var doc = await res.Content.ReadFromJsonAsync<JsonElement>(cancellationToken: ct);
            if (!doc.TryGetProperty("result", out var arr) || arr.ValueKind != JsonValueKind.Array)
                return result;

            foreach (var u in arr.EnumerateArray())
            {
                var updateId = u.GetProperty("update_id").GetInt64();

                if (u.TryGetProperty("callback_query", out var cb))
                {
                    var msg = cb.TryGetProperty("message", out var m) ? m : default;
                    var chat = msg.ValueKind == JsonValueKind.Object ? msg.GetProperty("chat") : default;
                    result.Add(new TelegramUpdate(
                        updateId,
                        chat.ValueKind == JsonValueKind.Object ? chat.GetProperty("id").GetInt64() : 0,
                        cb.TryGetProperty("data", out var d) ? d.GetString() ?? "" : "",
                        msg.ValueKind == JsonValueKind.Object ? msg.GetProperty("message_id").GetInt32() : null,
                        cb.GetProperty("id").GetString()));
                    continue;
                }

                if (u.TryGetProperty("message", out var message)
                    && message.TryGetProperty("text", out var textEl))
                {
                    result.Add(new TelegramUpdate(
                        updateId,
                        message.GetProperty("chat").GetProperty("id").GetInt64(),
                        textEl.GetString() ?? "",
                        message.GetProperty("message_id").GetInt32(),
                        null));
                    continue;
                }

                // Update loại khác (ảnh, sticker…): vẫn phải ghi nhận update_id, nếu không
                // offset đứng yên và Telegram trả lại đúng update đó mãi mãi.
                result.Add(new TelegramUpdate(updateId, 0, "", null, null));
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogDebug(ex, "getUpdates lỗi, thử lại lượt sau");
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            // HttpClient timeout giữa lúc long-poll — bình thường, không phải sự cố.
        }

        return result;
    }

    private async Task<T?> PostAsync<T>(
        string method, Dictionary<string, object?> payload, Func<JsonElement, T?> read, CancellationToken ct)
    {
        try
        {
            using var res = await http.PostAsJsonAsync(Url(method), payload, Json, ct);
            var doc = await res.Content.ReadFromJsonAsync<JsonElement>(cancellationToken: ct);
            if (!doc.TryGetProperty("ok", out var ok) || !ok.GetBoolean())
            {
                logger.LogWarning("Telegram {Method} thất bại: {Body}", method, doc.ToString());
                return default;
            }
            return doc.TryGetProperty("result", out var r) ? read(r) : default;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogWarning(ex, "Telegram {Method} lỗi mạng", method);
            return default;
        }
    }
}
