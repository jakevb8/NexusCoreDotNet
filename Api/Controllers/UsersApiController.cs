using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using NexusCoreDotNet.Enums;
using NexusCoreDotNet.Services;

namespace NexusCoreDotNet.Api.Controllers;

[ApiController]
[Route("api/v1/users")]
[Authorize(AuthenticationSchemes = FirebaseJwtDefaults.AuthenticationScheme)]
public class UsersApiController : ControllerBase
{
    private readonly UserService _users;

    public UsersApiController(UserService users)
    {
        _users = users;
    }

    // GET /api/v1/users — list all members in the org
    [HttpGet]
    public async Task<IActionResult> GetAll()
    {
        var orgId = AuthService.GetOrgId(User);
        var members = await _users.FindAllAsync(orgId);
        return Ok(members.Select(MapUser));
    }

    public record InviteRequest(string Email, string? Role);

    // POST /api/v1/users/invite
    [HttpPost("invite")]
    public async Task<IActionResult> Invite([FromBody] InviteRequest body)
    {
        if (string.IsNullOrWhiteSpace(body.Email))
            return BadRequest(new { message = "email is required" });

        var orgId = AuthService.GetOrgId(User);
        var userId = AuthService.GetUserId(User);
        var callerRole = AuthService.GetRole(User);

        if (!callerRole.HasAtLeast(Role.ORG_MANAGER))
            return Forbid();

        var role = Enum.TryParse<Role>(body.Role ?? "VIEWER", true, out var r) ? r : Role.VIEWER;
        if (role == Role.SUPERADMIN)
            return BadRequest(new { message = "Cannot invite with SUPERADMIN role" });

        try
        {
            var invite = await _users.CreateInviteAsync(body.Email, role, orgId, userId);

            // Build invite link pointing at the .NET frontend URL
            var baseUrl = HttpContext.RequestServices
                .GetRequiredService<IConfiguration>()["App:FrontendUrl"] ?? string.Empty;
            var inviteLink = string.IsNullOrWhiteSpace(baseUrl)
                ? null
                : $"{baseUrl}/AcceptInvite?token={invite.Token}";

            return Ok(new
            {
                message = "Invite sent",
                inviteLink,
                token = invite.Token
            });
        }
        catch (InvalidOperationException ex)
        {
            return Conflict(new { message = ex.Message });
        }
    }

    // DELETE /api/v1/users/{id} — remove a member
    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> Remove(Guid id)
    {
        var orgId = AuthService.GetOrgId(User);
        var userId = AuthService.GetUserId(User);
        var callerRole = AuthService.GetRole(User);

        if (!callerRole.HasAtLeast(Role.ORG_MANAGER))
            return Forbid();

        try
        {
            await _users.RemoveMemberAsync(id, userId, orgId);
            return NoContent();
        }
        catch (KeyNotFoundException ex)
        {
            return NotFound(new { message = ex.Message });
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { message = ex.Message });
        }
    }

    public record UpdateRoleRequest(string Role);

    // PATCH /api/v1/users/{id}/role
    [HttpPatch("{id:guid}/role")]
    public async Task<IActionResult> UpdateRole(Guid id, [FromBody] UpdateRoleRequest body)
    {
        if (string.IsNullOrWhiteSpace(body.Role))
            return BadRequest(new { message = "role is required" });

        var orgId = AuthService.GetOrgId(User);
        var userId = AuthService.GetUserId(User);
        var callerRole = AuthService.GetRole(User);

        if (!callerRole.HasAtLeast(Role.ORG_MANAGER))
            return Forbid();

        if (!Enum.TryParse<Role>(body.Role, true, out var newRole))
            return BadRequest(new { message = $"Invalid role: {body.Role}" });

        try
        {
            var updated = await _users.UpdateRoleAsync(id, newRole, orgId, userId);
            return Ok(MapUser(updated));
        }
        catch (KeyNotFoundException ex)
        {
            return NotFound(new { message = ex.Message });
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { message = ex.Message });
        }
    }

    private static object MapUser(NexusCoreDotNet.Data.Entities.User u) => new
    {
        id = u.Id,
        email = u.Email,
        displayName = u.DisplayName,
        role = u.Role.ToString(),
        organizationId = u.OrganizationId,
        createdAt = u.CreatedAt
    };
}
