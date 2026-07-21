using System.Text;
using System.Text.Json;
using Backend.Modules.SocialChannel.Enums;
using Backend.Modules.SocialComment;
using Backend.Modules.PageMessage;
using Backend.Shared;
using Backend.Shared.Meta;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;

namespace Backend.Modules.SocialComment;

[ApiController]
[Route("api/[controller]")]
[Authorize]
public class SocialCommentController(SocialCommentService service) : ControllerBase
{
    [HttpPost("filter")]
    public async Task<IActionResult> Filter([FromBody] SocialCommentFilterRequest request, CancellationToken ct)
    {
        var data = await service.FilterInboxAsync(request, ct);
        return Ok(ApiResponse.Ok(data));
    }

    [HttpGet("summary")]
    public async Task<IActionResult> Summary(CancellationToken ct)
    {
        var data = await service.GetSummaryAsync(ct);
        return Ok(ApiResponse.Ok(data));
    }

    [HttpGet("{id:guid}")]
    public async Task<IActionResult> GetThread(Guid id, CancellationToken ct)
    {
        var data = await service.GetThreadAsync(id, ct);
        if (data is null) return NotFound(ApiResponse.Fail("NOT_FOUND", "Comment không tồn tại"));
        return Ok(ApiResponse.Ok(data));
    }

    [HttpGet("{id:guid}/actions")]
    public async Task<IActionResult> Actions(Guid id, CancellationToken ct)
    {
        var data = await service.GetActionLogsAsync(id, ct);
        return Ok(ApiResponse.Ok(data));
    }

    [HttpPost("sync")]
    [Authorize(Roles = "Admin,ContentManager")]
    public async Task<IActionResult> Sync([FromBody] SyncCommentsRequest request, CancellationToken ct)
    {
        var data = await service.SyncAsync(request, ct);
        return Ok(ApiResponse.Ok(data, "Đồng bộ xong"));
    }

    [HttpPost("subscribe-facebook")]
    [Authorize(Roles = "Admin")]
    public async Task<IActionResult> SubscribeFacebook(CancellationToken ct)
    {
        await service.SubscribeFacebookPagesAsync(ct);
        return Ok(ApiResponse.Ok("Đã gửi subscribe feed webhook cho các Facebook Page"));
    }

    [HttpPost("{id:guid}/reply")]
    [Authorize(Roles = "Admin,ContentManager,Reviewer")]
    public async Task<IActionResult> Reply(Guid id, [FromBody] ReplyCommentRequest request, CancellationToken ct)
    {
        try
        {
            var data = await service.ReplyAsync(id, request, ct);
            return Ok(ApiResponse.Ok(data, "Đã trả lời"));
        }
        catch (Exception ex)
        {
            return BadRequest(ApiResponse.Fail("REPLY_FAILED", ex.Message));
        }
    }

    [HttpPost("{id:guid}/hide")]
    [Authorize(Roles = "Admin,ContentManager,Reviewer")]
    public async Task<IActionResult> Hide(Guid id, CancellationToken ct)
    {
        try
        {
            var data = await service.HideAsync(id, true, ct);
            return Ok(ApiResponse.Ok(data, "Đã ẩn comment"));
        }
        catch (Exception ex)
        {
            return BadRequest(ApiResponse.Fail("HIDE_FAILED", ex.Message));
        }
    }

    [HttpPost("{id:guid}/unhide")]
    [Authorize(Roles = "Admin,ContentManager,Reviewer")]
    public async Task<IActionResult> Unhide(Guid id, CancellationToken ct)
    {
        try
        {
            var data = await service.HideAsync(id, false, ct);
            return Ok(ApiResponse.Ok(data, "Đã hiện comment"));
        }
        catch (Exception ex)
        {
            return BadRequest(ApiResponse.Fail("UNHIDE_FAILED", ex.Message));
        }
    }

    [HttpDelete("{id:guid}")]
    [Authorize(Roles = "Admin,ContentManager,Reviewer")]
    public async Task<IActionResult> Delete(Guid id, CancellationToken ct)
    {
        try
        {
            var data = await service.DeleteAsync(id, ct);
            return Ok(ApiResponse.Ok(data, "Đã xóa comment"));
        }
        catch (Exception ex)
        {
            return BadRequest(ApiResponse.Fail("DELETE_FAILED", ex.Message));
        }
    }

    [HttpPost("{id:guid}/pending")]
    [Authorize(Roles = "Admin,ContentManager,Reviewer")]
    public async Task<IActionResult> Pending(Guid id, [FromQuery] bool approve = true, CancellationToken ct = default)
    {
        try
        {
            var data = await service.ManagePendingAsync(id, approve, ct);
            return Ok(ApiResponse.Ok(data, approve ? "Đã duyệt" : "Đã bỏ qua"));
        }
        catch (Exception ex)
        {
            return BadRequest(ApiResponse.Fail("PENDING_FAILED", ex.Message));
        }
    }

    [HttpPost("{id:guid}/status")]
    [Authorize(Roles = "Admin,ContentManager,Reviewer")]
    public async Task<IActionResult> SetStatus(Guid id, [FromBody] SetCommentStatusRequest request, CancellationToken ct)
    {
        var data = await service.SetStatusAsync(id, request.Status, ct);
        return Ok(ApiResponse.Ok(data));
    }

    [HttpPost("{id:guid}/assign")]
    [Authorize(Roles = "Admin,ContentManager,Reviewer")]
    public async Task<IActionResult> Assign(Guid id, [FromBody] AssignCommentRequest request, CancellationToken ct)
    {
        var data = await service.AssignAsync(id, request.AssignedTo, ct);
        return Ok(ApiResponse.Ok(data));
    }

    [HttpPost("{id:guid}/note")]
    [Authorize(Roles = "Admin,ContentManager,Reviewer")]
    public async Task<IActionResult> Note(Guid id, [FromBody] CommentNoteRequest request, CancellationToken ct)
    {
        var data = await service.AddNoteAsync(id, request.Note, ct);
        return Ok(ApiResponse.Ok(data));
    }
}

/// <summary>
/// Webhook endpoints — anonymous verify + signed POST.
/// Facebook: GET/POST /api/webhooks/meta
/// Threads: GET/POST /api/webhooks/threads
/// </summary>
[ApiController]
[Route("api/webhooks")]
[AllowAnonymous]
public class SocialWebhookController(
    SocialCommentService service,
    PageMessageService pageMessageService,
    IOptions<MetaOAuthOptions> metaOptions,
    IConfiguration configuration,
    ILogger<SocialWebhookController> logger) : ControllerBase
{
    [HttpGet("meta")]
    public IActionResult VerifyMeta(
        [FromQuery(Name = "hub.mode")] string? mode,
        [FromQuery(Name = "hub.verify_token")] string? verifyToken,
        [FromQuery(Name = "hub.challenge")] string? challenge)
    {
        var expected = configuration["MetaWebhooks:VerifyToken"]
                       ?? configuration["MetaOAuth:WebhookVerifyToken"]
                       ?? "vni-meta-verify";
        if (mode == "subscribe" && verifyToken == expected && !string.IsNullOrWhiteSpace(challenge))
            return Content(challenge, "text/plain");
        return Unauthorized();
    }

    [HttpPost("meta")]
    public async Task<IActionResult> ReceiveMeta(CancellationToken ct)
    {
        using var reader = new StreamReader(Request.Body, Encoding.UTF8);
        var raw = await reader.ReadToEndAsync(ct);
        var signature = Request.Headers["X-Hub-Signature-256"].FirstOrDefault();

        // In production require signature; allow empty secret only in Development for local tunnel tests.
        var requireSig = !string.IsNullOrWhiteSpace(metaOptions.Value.AppSecret);
        if (requireSig && !service.VerifyMetaSignature(raw, signature))
        {
            logger.LogWarning("Meta webhook signature invalid");
            return Unauthorized();
        }

        try
        {
            using var doc = JsonDocument.Parse(string.IsNullOrWhiteSpace(raw) ? "{}" : raw);
            // Messenger events are under entry[].messaging and are persisted directly.
            await pageMessageService.IngestMetaWebhookAsync(doc.RootElement, ct);

            if (doc.RootElement.TryGetProperty("entry", out var entries)
                && entries.ValueKind == JsonValueKind.Array)
            {
                foreach (var entry in entries.EnumerateArray())
                {
                    var entryId = entry.TryGetProperty("id", out var idEl) ? idEl.GetString() : null;
                    var time = entry.TryGetProperty("time", out var timeEl) && timeEl.TryGetInt64(out var t)
                        ? t.ToString()
                        : DateTimeOffset.UtcNow.ToUnixTimeSeconds().ToString();

                    if (!entry.TryGetProperty("changes", out var changes)) continue;
                    foreach (var change in changes.EnumerateArray())
                    {
                        var field = change.TryGetProperty("field", out var f) ? f.GetString() : null;
                        string? verb = null, item = null, commentId = null;
                        if (change.TryGetProperty("value", out var value))
                        {
                            verb = value.TryGetProperty("verb", out var v) ? v.GetString() : null;
                            item = value.TryGetProperty("item", out var i) ? i.GetString() : null;
                            commentId = value.TryGetProperty("comment_id", out var c) ? c.GetString() : null;
                        }

                        if (!string.Equals(item, "comment", StringComparison.OrdinalIgnoreCase)
                            && !string.Equals(field, "feed", StringComparison.OrdinalIgnoreCase))
                            continue;

                        var eventKey = $"fb:{entryId}:{commentId}:{verb}:{time}";
                        await service.EnqueueWebhookAsync(
                            SocialPlatform.Facebook,
                            eventKey,
                            commentId,
                            verb,
                            item ?? field,
                            change.GetRawText(),
                            ct);
                    }
                }
            }
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Meta webhook parse error — still returning 200");
        }

        return Ok();
    }

    [HttpGet("threads")]
    public IActionResult VerifyThreads(
        [FromQuery(Name = "hub.mode")] string? mode,
        [FromQuery(Name = "hub.verify_token")] string? verifyToken,
        [FromQuery(Name = "hub.challenge")] string? challenge)
    {
        var expected = configuration["ThreadsWebhooks:VerifyToken"]
                       ?? configuration["ThreadsOAuth:WebhookVerifyToken"]
                       ?? "vni-threads-verify";
        if (mode == "subscribe" && verifyToken == expected && !string.IsNullOrWhiteSpace(challenge))
            return Content(challenge, "text/plain");
        return Unauthorized();
    }

    [HttpPost("threads")]
    public async Task<IActionResult> ReceiveThreads(CancellationToken ct)
    {
        using var reader = new StreamReader(Request.Body, Encoding.UTF8);
        var raw = await reader.ReadToEndAsync(ct);
        var signature = Request.Headers["X-Hub-Signature-256"].FirstOrDefault();
        var appSecret = configuration["ThreadsOAuth:AppSecret"];
        if (!string.IsNullOrWhiteSpace(appSecret) && !service.VerifyThreadsSignature(raw, signature))
        {
            logger.LogWarning("Threads webhook signature invalid");
            return Unauthorized();
        }

        try
        {
            using var doc = JsonDocument.Parse(string.IsNullOrWhiteSpace(raw) ? "{}" : raw);
            var root = doc.RootElement;
            // Threads can send flat or nested shapes
            var replyId = root.TryGetProperty("id", out var idEl) ? idEl.GetString() : null;
            if (root.TryGetProperty("value", out var value) && value.TryGetProperty("id", out var vid))
                replyId ??= vid.GetString();

            var field = root.TryGetProperty("field", out var f) ? f.GetString()
                : (root.TryGetProperty("values", out var values)
                   && values.TryGetProperty("field", out var vf) ? vf.GetString() : "replies");

            var time = DateTimeOffset.UtcNow.ToUnixTimeSeconds().ToString();
            var eventKey = $"th:{replyId}:{field}:{time}:{raw.GetHashCode()}";
            await service.EnqueueWebhookAsync(
                SocialPlatform.Threads,
                eventKey,
                replyId,
                "add",
                field,
                raw,
                ct);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Threads webhook parse error — still returning 200");
        }

        return Ok();
    }
}
