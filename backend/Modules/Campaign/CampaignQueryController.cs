using Backend.Shared;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Backend.Modules.Campaign;

/// <summary>
/// Endpoint đọc chiến dịch → page → bài (gom nhóm). Route chung api/Campaign,
/// tách controller để không đụng ownership CRUD của CampaignController.
/// </summary>
[ApiController]
[Route("api/Campaign")]
[Authorize]
public class CampaignQueryController(CampaignQueryService queryService) : ControllerBase
{
    [HttpGet("summaries")]
    public async Task<IActionResult> ListSummaries(CancellationToken ct)
    {
        var items = await queryService.ListSummariesAsync(ct);
        return Ok(ApiResponse.Ok(items));
    }

    [HttpGet("{id:guid}/detail")]
    public async Task<IActionResult> GetDetail(Guid id, CancellationToken ct)
    {
        var detail = await queryService.GetDetailAsync(id, ct);
        if (detail is null)
            return NotFound(ApiResponse.Fail("NOT_FOUND", "Không tìm thấy chiến dịch"));
        return Ok(ApiResponse.Ok(detail));
    }

    [HttpGet("{id:guid}/pages/{channelId:guid}/posts")]
    public async Task<IActionResult> ListPagePosts(
        Guid id,
        Guid channelId,
        [FromQuery] CampaignPagePostsRequest request,
        CancellationToken ct)
    {
        var page = await queryService.ListPagePostsAsync(id, channelId, request, ct);
        if (page is null)
            return NotFound(ApiResponse.Fail("NOT_FOUND", "Không tìm thấy chiến dịch"));
        return Ok(ApiResponse.Ok(page));
    }
}
