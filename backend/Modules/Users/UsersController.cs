using Backend.Shared;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Backend.Modules.Users;

[ApiController]
[Route("api/[controller]")]
[Authorize]
public class UsersController(UsersService usersService) : ControllerBase
{
    /// <summary>
    /// Danh sách người dùng đang hoạt động cho gán phụ trách CRM.
    /// Chỉ Admin/ContentManager/Reviewer — Viewer → 403.
    /// </summary>
    [HttpGet]
    [Authorize(Roles = "Admin,ContentManager,Reviewer")]
    public async Task<IActionResult> List(CancellationToken ct)
        => Ok(ApiResponse.Ok(await usersService.ListActiveAsync(ct)));
}
