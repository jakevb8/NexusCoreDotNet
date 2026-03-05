using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using NexusCoreDotNet.Services;

namespace NexusCoreDotNet.Middleware;

public class SessionAuthMiddleware
{
    private readonly RequestDelegate _next;

    public SessionAuthMiddleware(RequestDelegate next)
    {
        _next = next;
    }

    public async Task InvokeAsync(HttpContext context, AuthService authService)
    {
        // Already authenticated via cookie scheme — refresh org status claim
        if (context.User?.Identity?.IsAuthenticated == true)
        {
            var subClaim = context.User.FindFirst("sub")?.Value;
            if (Guid.TryParse(subClaim, out var userId))
            {
                var user = await authService.GetUserByIdAsync(userId);
                if (user != null)
                {
                    // Re-build principal with up-to-date org status
                    var principal = AuthService.BuildClaimsPrincipal(user);
                    // Attach orgStatus for RequireRole filter
                    var identity = (System.Security.Claims.ClaimsIdentity)principal.Identity!;
                    identity.AddClaim(new System.Security.Claims.Claim("orgStatus",
                        user.Organization?.Status.ToString() ?? "PENDING"));

                    context.User = principal;
                }
            }
        }

        await _next(context);
    }
}
