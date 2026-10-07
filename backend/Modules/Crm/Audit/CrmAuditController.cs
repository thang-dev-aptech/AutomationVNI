using Backend.Shared;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Backend.Modules.Crm.Audit;

[ApiController]
[Route("api/[controller]")]
[Authorize]
public class CrmAuditController(CrmAuditService service) : ControllerBase
{
    [HttpGet]
    [Authorize(Roles = "Admin,ContentManager,Reviewer")]
    public async Task<IActionResult> List(
        [FromQuery] Guid? customerId,
        [FromQuery] int take = 100,
        CancellationToken ct = default)
        => Ok(ApiResponse.Ok(await service.ListAsync(customerId, take, ct)));
}
