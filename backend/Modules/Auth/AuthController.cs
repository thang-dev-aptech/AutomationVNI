using System.Security.Claims;
using Backend.Data;
using Backend.Shared;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;

namespace Backend.Modules.Auth;

[ApiController]
[Route("api/[controller]")]
public class AuthController(
    UserManager<ApplicationUser> userManager,
    SignInManager<ApplicationUser> signInManager,
    JwtTokenService jwtTokenService) : ControllerBase
{
    [HttpPost("login")]
    [AllowAnonymous]
    public async Task<IActionResult> Login([FromBody] LoginRequest request, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(request.Email) || string.IsNullOrWhiteSpace(request.Password))
            return BadRequest(ApiResponse.Fail("VALIDATION_ERROR", "Email và mật khẩu không được để trống"));

        var user = await userManager.FindByEmailAsync(request.Email.Trim());
        if (user is null)
            return Unauthorized(ApiResponse.Fail("UNAUTHORIZED", "Email hoặc mật khẩu không đúng"));

        var result = await signInManager.CheckPasswordSignInAsync(user, request.Password, lockoutOnFailure: false);
        if (!result.Succeeded)
            return Unauthorized(ApiResponse.Fail("UNAUTHORIZED", "Email hoặc mật khẩu không đúng"));

        var roles = await userManager.GetRolesAsync(user);
        var (token, expiresAt) = jwtTokenService.GenerateToken(user, roles);

        var response = new LoginResponse
        {
            AccessToken = token,
            ExpiresAt = expiresAt,
            User = new AuthUserResponse
            {
                Id = user.Id,
                Email = user.Email ?? string.Empty,
                UserName = user.UserName,
                Roles = roles.ToList()
            }
        };

        return Ok(ApiResponse.Ok(response, "Đăng nhập thành công"));
    }

    [HttpGet("me")]
    [Authorize]
    public async Task<IActionResult> Me(CancellationToken ct)
    {
        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (!Guid.TryParse(userId, out var id))
            return Unauthorized(ApiResponse.Fail("UNAUTHORIZED", "Phiên đăng nhập không hợp lệ"));

        var user = await userManager.FindByIdAsync(id.ToString());
        if (user is null)
            return Unauthorized(ApiResponse.Fail("UNAUTHORIZED", "Phiên đăng nhập không hợp lệ"));

        var roles = await userManager.GetRolesAsync(user);
        var response = new AuthUserResponse
        {
            Id = user.Id,
            Email = user.Email ?? string.Empty,
            UserName = user.UserName,
            Roles = roles.ToList()
        };

        return Ok(ApiResponse.Ok(response));
    }

    [HttpPost("logout")]
    [Authorize]
    public IActionResult Logout()
    {
        // JWT stateless — client xóa token. Endpoint xác nhận phiên hợp lệ trước khi logout.
        return Ok(ApiResponse.Ok("Đăng xuất thành công"));
    }
}
