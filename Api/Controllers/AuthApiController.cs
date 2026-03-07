using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using NexusCoreDotNet.Enums;
using NexusCoreDotNet.Services;

namespace NexusCoreDotNet.Api.Controllers;

[ApiController]
[Route("api/v1/auth")]
public class AuthApiController : ControllerBase
{
    private readonly AuthService _auth;
    private readonly IFirebaseAuthService _firebase;

    public AuthApiController(AuthService auth, IFirebaseAuthService firebase)
    {
        _auth = auth;
        _firebase = firebase;
    }

    // GET /api/v1/auth/me — returns the authenticated user's profile
    [HttpGet("me")]
    [Authorize(AuthenticationSchemes = FirebaseJwtDefaults.AuthenticationScheme)]
    public IActionResult GetMe()
    {
        var userId = AuthService.GetUserId(User);
        var orgId = AuthService.GetOrgId(User);
        var role = AuthService.GetRole(User);
        var email = User.FindFirst("email")?.Value ?? string.Empty;
        var name = User.FindFirst("name")?.Value ?? email;
        var orgStatus = User.FindFirst("orgStatus")?.Value;

        return Ok(new
        {
            id = userId,
            email,
            displayName = name,
            role = role.ToString(),
            organizationId = orgId,
            organization = new
            {
                id = orgId,
                status = orgStatus
            }
        });
    }

    public record RegisterRequest(string FirebaseToken, string OrgName, string Name, string Email);

    // POST /api/v1/auth/register — create a new org + user from a Firebase ID token
    [HttpPost("register")]
    [AllowAnonymous]
    public async Task<IActionResult> Register([FromBody] RegisterRequest body)
    {
        if (string.IsNullOrWhiteSpace(body.FirebaseToken))
            return BadRequest(new { message = "firebaseToken is required" });
        if (string.IsNullOrWhiteSpace(body.OrgName))
            return BadRequest(new { message = "orgName is required" });
        if (string.IsNullOrWhiteSpace(body.Name))
            return BadRequest(new { message = "name is required" });

        // Derive a slug from the org name (lowercase, alphanumeric + hyphens)
        var orgSlug = System.Text.RegularExpressions.Regex
            .Replace(body.OrgName.ToLower().Trim(), @"[^a-z0-9]+", "-")
            .Trim('-');

        try
        {
            var user = await _auth.RegisterNewOrganizationAsync(
                body.FirebaseToken, body.OrgName, orgSlug, body.Name);

            return Ok(MapUser(user));
        }
        catch (InvalidOperationException ex) when (ex.Message.Contains("already registered"))
        {
            return Conflict(new { message = ex.Message });
        }
        catch (InvalidOperationException ex) when (ex.Message.Contains("slug already taken"))
        {
            return Conflict(new { message = ex.Message });
        }
        catch (Exception ex)
        {
            return StatusCode(500, new { message = ex.Message });
        }
    }

    public record AcceptInviteRequest(string FirebaseToken, string Token, string? DisplayName);

    // POST /api/v1/auth/accept-invite — join an org via an invite token
    [HttpPost("accept-invite")]
    [AllowAnonymous]
    public async Task<IActionResult> AcceptInvite([FromBody] AcceptInviteRequest body)
    {
        if (string.IsNullOrWhiteSpace(body.FirebaseToken))
            return BadRequest(new { message = "firebaseToken is required" });
        if (string.IsNullOrWhiteSpace(body.Token))
            return BadRequest(new { message = "token is required" });

        try
        {
            var user = await _auth.AcceptInviteAsync(body.FirebaseToken, body.Token, body.DisplayName);
            return Ok(MapUser(user));
        }
        catch (KeyNotFoundException ex)
        {
            return NotFound(new { message = ex.Message });
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { message = ex.Message });
        }
        catch (Exception ex)
        {
            return StatusCode(500, new { message = ex.Message });
        }
    }

    private static object MapUser(NexusCoreDotNet.Data.Entities.User user) => new
    {
        id = user.Id,
        email = user.Email,
        displayName = user.DisplayName,
        role = user.Role.ToString(),
        organizationId = user.OrganizationId,
        organization = new
        {
            id = user.OrganizationId,
            name = user.Organization?.Name,
            slug = user.Organization?.Slug,
            status = user.Organization?.Status.ToString()
        }
    };
}
