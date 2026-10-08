using System.Text;
using Backend.Data;
using Backend.Modules.PageContext;
using Backend.Modules.PageMessage;
using Backend.Modules.SocialComment;
using Backend.Shared.Ai;
using Microsoft.EntityFrameworkCore;

namespace Backend.Modules.Inbox;

public class InboxSuggestReplyResponse
{
    public string Draft { get; set; } = string.Empty;
}

/// <summary>
/// AI gợi ý trả lời — CHỈ bản nháp. Không ghi DB, không gửi tin/trả lời.
/// Phạm vi hội thoại khớp InboxQueryService / PageMessage + SocialComment filter.
/// </summary>
public class InboxSuggestService(
    AppDbContext db,
    IAiTextGenerationService aiText,
    ILogger<InboxSuggestService> logger)
{
    public const int MaxContextMessages = 20;

    /// <summary>
    /// Trả bản nháp hoặc null nếu hội thoại ngoài phạm vi (caller → 404).
    /// Ném <see cref="InvalidOperationException"/> khi AI lỗi/timeout (caller → 400).
    /// </summary>
    public async Task<InboxSuggestReplyResponse?> SuggestReplyAsync(
        InboxItemKind kind,
        Guid id,
        CancellationToken ct = default)
    {
        var loaded = kind switch
        {
            InboxItemKind.Message => await LoadMessageContextAsync(id, ct),
            InboxItemKind.Comment => await LoadCommentContextAsync(id, ct),
            _ => null
        };
        if (loaded is null) return null;

        var pageCtx = await PromptContextDefaults.ResolveAsync(
            db, loaded.SocialChannelId, PromptContextDefaults.FallbackCategory, ct);

        var promptOverride = BuildReplyPrompt(loaded, pageCtx);

        // Chỉ đưa ngữ cảnh page qua các field brand/tone/cta — không đưa AccessToken hay hội thoại khác.
        var request = new AiTextGenerationRequest
        {
            Title = $"Gợi ý trả lời {loaded.KindLabel}",
            Objective = "Soạn bản nháp trả lời khách trên Messenger/bình luận Facebook",
            Category = PromptContextDefaults.FallbackCategory,
            Audience = "khách hàng đang nhắn hoặc bình luận",
            BrandContext = pageCtx.Brand,
            Tone = pageCtx.Tone,
            CtaText = pageCtx.Cta,
            Hashtags = pageCtx.Hashtags,
            PromptOverride = promptOverride
        };

        AiTextGenerationResult result;
        try
        {
            if (!aiText.IsAvailable())
                throw new InvalidOperationException(
                    "AI chưa được cấu hình. Không thể gợi ý trả lời lúc này.");

            result = await aiText.GenerateAsync(request, ct);
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            logger.LogWarning("Inbox suggest-reply timeout for {Kind} {Id}", kind, id);
            throw new InvalidOperationException(
                "AI quá hạn phản hồi. Vui lòng thử lại sau.");
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (AiProviderUnavailableException ex)
        {
            logger.LogWarning(ex, "Inbox suggest-reply provider unavailable");
            throw new InvalidOperationException(
                "AI chưa sẵn sàng. Không thể gợi ý trả lời lúc này.");
        }
        catch (AiProviderConfigException ex)
        {
            logger.LogWarning(ex, "Inbox suggest-reply provider config error");
            throw new InvalidOperationException(
                "Cấu hình AI không hợp lệ. Không thể gợi ý trả lời lúc này.");
        }
        catch (AiTextGenerationException ex)
        {
            logger.LogWarning(ex, "Inbox suggest-reply AI failed");
            throw new InvalidOperationException(
                string.IsNullOrWhiteSpace(ex.Message)
                    ? "AI không tạo được bản nháp. Vui lòng thử lại."
                    : $"AI không tạo được bản nháp: {ex.Message}");
        }
        catch (InvalidOperationException)
        {
            throw;
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Inbox suggest-reply unexpected AI error");
            throw new InvalidOperationException(
                "AI gặp lỗi khi gợi ý trả lời. Vui lòng thử lại.");
        }

        var draft = (result.Caption ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(draft))
            throw new InvalidOperationException("AI trả về bản nháp rỗng. Vui lòng thử lại.");

        // Tuyệt đối không SaveChanges / send / reply — chỉ trả draft.
        return new InboxSuggestReplyResponse { Draft = draft };
    }

    private async Task<ConversationContext?> LoadMessageContextAsync(Guid id, CancellationToken ct)
    {
        // Scope = PageMessage filter: !IsDeleted
        var conversation = await db.PageConversations.AsNoTracking()
            .FirstOrDefaultAsync(x => x.Id == id && !x.IsDeleted, ct);
        if (conversation is null) return null;

        var messages = await db.PageMessages.AsNoTracking()
            .Where(x => x.PageConversationId == id && !x.IsDeleted)
            .OrderByDescending(x => x.SentAt ?? x.CreatedAt)
            .Take(MaxContextMessages)
            .ToListAsync(ct);

        messages.Reverse(); // chronological for prompt

        var lines = messages.Select(m =>
        {
            var who = m.IsFromPage ? "Page" : "Khách";
            var text = string.IsNullOrWhiteSpace(m.Text) ? "[đính kèm/media]" : m.Text.Trim();
            return $"{who}: {text}";
        }).ToList();

        return new ConversationContext(
            InboxItemKind.Message,
            "tin nhắn",
            conversation.SocialChannelId,
            conversation.ParticipantName ?? conversation.ParticipantExternalId,
            lines);
    }

    private async Task<ConversationContext?> LoadCommentContextAsync(Guid id, CancellationToken ct)
    {
        // Scope = SocialComment filter inbox: top-level customer comment
        var root = await db.SocialComments.AsNoTracking()
            .FirstOrDefaultAsync(x =>
                x.Id == id
                && !x.IsDeleted
                && !x.IsDeletedOnPlatform
                && !x.IsFromPage
                && x.ParentCommentId == null, ct);
        if (root is null) return null;

        var thread = await db.SocialComments.AsNoTracking()
            .Where(x =>
                !x.IsDeleted
                && (x.Id == root.Id || x.ParentCommentId == root.Id))
            .OrderByDescending(x => x.CommentedAt ?? x.CreatedAt)
            .Take(MaxContextMessages)
            .ToListAsync(ct);

        thread.Reverse();

        var lines = thread.Select(c =>
        {
            var who = c.IsFromPage ? "Page" : "Khách";
            var text = string.IsNullOrWhiteSpace(c.Message) ? "[trống]" : c.Message.Trim();
            return $"{who}: {text}";
        }).ToList();

        return new ConversationContext(
            InboxItemKind.Comment,
            "bình luận",
            root.SocialChannelId,
            root.AuthorName ?? root.AuthorUsername ?? root.AuthorExternalId ?? "khách",
            lines);
    }

    private static string BuildReplyPrompt(ConversationContext ctx, PromptContextDefaults pageCtx)
    {
        var sb = new StringBuilder();
        sb.AppendLine("## Nhiệm vụ");
        sb.AppendLine(
            "Soạn MỘT bản nháp trả lời ngắn, lịch sự bằng tiếng Việt cho hội thoại bên dưới.");
        sb.AppendLine(
            "Đúng giọng page; không bịa giá, khuyến mãi hay chính sách không có trong ngữ cảnh.");
        sb.AppendLine(
            "Chỉ viết nội dung trả lời khách — không tiêu đề, không hashtag, không giải thích.");
        sb.AppendLine(
            "Đặt toàn bộ bản nháp vào field \"caption\" của JSON schema (các field khác có thể để trống/ngắn).");
        sb.AppendLine();
        sb.AppendLine("## Ngữ cảnh page");
        sb.AppendLine($"Thương hiệu: {pageCtx.Brand}");
        sb.AppendLine($"Giọng văn: {pageCtx.Tone}");
        if (!string.IsNullOrWhiteSpace(pageCtx.Hotline))
            sb.AppendLine($"Hotline: {pageCtx.Hotline}");
        if (!string.IsNullOrWhiteSpace(pageCtx.Website))
            sb.AppendLine($"Website: {pageCtx.Website}");
        if (pageCtx.PageContext?.PromptTemplateText is { Length: > 0 } tpl)
        {
            // Chỉ lấy đoạn ngắn — không đưa secret.
            var clip = tpl.Length > 500 ? tpl[..500] : tpl;
            sb.AppendLine($"Gợi ý soạn thảo page: {clip.Trim()}");
        }

        sb.AppendLine();
        sb.AppendLine($"## Hội thoại ({ctx.KindLabel}) với {ctx.ParticipantLabel}");
        sb.AppendLine($"(Tối đa {MaxContextMessages} tin/bình luận gần nhất của ĐÚNG hội thoại này)");
        if (ctx.Lines.Count == 0)
            sb.AppendLine("(Chưa có nội dung văn bản — hãy hỏi lịch sự khách cần hỗ trợ gì.)");
        else
        {
            foreach (var line in ctx.Lines)
                sb.AppendLine(line);
        }

        return sb.ToString().Trim();
    }

    private sealed record ConversationContext(
        InboxItemKind Kind,
        string KindLabel,
        Guid SocialChannelId,
        string ParticipantLabel,
        List<string> Lines);
}
