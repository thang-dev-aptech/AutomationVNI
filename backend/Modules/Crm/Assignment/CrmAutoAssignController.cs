using Backend.Shared;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Backend.Modules.Crm.Assignment;

[ApiController]
[Route("api/[controller]")]
[Authorize]
public class CrmAutoAssignController(CrmAutoAssignService service) : ControllerBase
{
    [HttpGet]
    [Authorize(Roles = "Admin,ContentManager")]
    public async Task<IActionResult> Get(CancellationToken ct)
        => Ok(ApiResponse.Ok(await service.GetSettingsAsync(ct)));

    [HttpPut]
    [Authorize(Roles = "Admin,ContentManager")]
    public async Task<IActionResult> Update(
        [FromBody] UpdateCrmAutoAssignSettingsRequest request,
        CancellationToken ct)
    {
        try
        {
            return Ok(ApiResponse.Ok(await service.UpdateSettingsAsync(request, ct), "Đã lưu cấu hình tự chia"));
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(ApiResponse.Fail("AUTO_ASSIGN_INVALID", ex.Message));
        }
    }
}
