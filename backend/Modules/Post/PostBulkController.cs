using Backend.Shared;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Backend.Modules.Post;

/// <summary>
/// Endpoint hành động hàng loạt lịch đăng (Huỷ lịch / Đăng ngay / Xoá).
/// Route chung prefix api/Post; DTO/service tách file để không đụng ownership task t2.
/// </summary>
[ApiController]
[Route("api/Post")]
[Authorize]
public class PostBulkController(PostBulkActionService bulkActionService) : ControllerBase
{
    [HttpPost("bulk-action")]
    public async Task<IActionResult> BulkAction(
        [FromBody] PostBulkActionRequest? request, CancellationToken ct)
    {
        if (request is null)
            return BadRequest(ApiResponse.Fail("VALIDATION_ERROR", "Dữ liệu không hợp lệ"));

        if (string.IsNullOrWhiteSpace(request.Action)
            || !PostBulkActions.All.Contains(request.Action.Trim()))
        {
            return BadRequest(ApiResponse.Fail(
                "VALIDATION_ERROR",
                "action phải là cancelSchedule, publishNow hoặc delete"));
        }

        var postIds = request.PostIds ?? [];
        if (postIds.Count == 0)
            return BadRequest(ApiResponse.Fail("VALIDATION_ERROR", "Phải chọn ít nhất một bài viết"));

        if (postIds.Count > PostBulkActionService.MaxPostIds)
        {
            return BadRequest(ApiResponse.Fail(
                "VALIDATION_ERROR",
                $"Tối đa {PostBulkActionService.MaxPostIds} bài mỗi lượt"));
        }

        request.Action = request.Action.Trim();
        request.PostIds = postIds;

        var result = await bulkActionService.ExecuteAsync(request, ct);
        return Ok(ApiResponse.Ok(result));
    }
}
