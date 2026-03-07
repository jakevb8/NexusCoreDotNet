using System.Security.Claims;
using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Options;
using NexusCoreDotNet.Services;

namespace NexusCoreDotNet.Api;

/// <summary>
/// Authentication handler that validates a Firebase ID token from the
/// Authorization: Bearer &lt;token&gt; header and populates HttpContext.User
/// with claims derived from the matching database user record.
/// Used exclusively by the /api/v1/* REST endpoints.
/// </summary>
public class FirebaseJwtHandler : AuthenticationHandler<AuthenticationSchemeOptions>
{
    private readonly IFirebaseAuthService _firebase;
    private readonly AuthService _authService;

    public FirebaseJwtHandler(
        IOptionsMonitor<AuthenticationSchemeOptions> options,
        ILoggerFactory logger,
        UrlEncoder encoder,
        IFirebaseAuthService firebase,
        AuthService authService)
        : base(options, logger, encoder)
    {
        _firebase = firebase;
        _authService = authService;
    }

    protected override async Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        var authHeader = Request.Headers.Authorization.FirstOrDefault();
        if (string.IsNullOrWhiteSpace(authHeader) || !authHeader.StartsWith("Bearer "))
            return AuthenticateResult.NoResult();

        var token = authHeader["Bearer ".Length..].Trim();
        if (string.IsNullOrWhiteSpace(token))
            return AuthenticateResult.NoResult();

        try
        {
            var decoded = await _firebase.VerifyIdTokenAsync(token);
            var email = decoded.Claims.TryGetValue("email", out var emailObj)
                ? emailObj?.ToString() ?? string.Empty
                : string.Empty;

            var user = await _authService.GetOrMigrateUserAsync(decoded.Uid, email);
            if (user == null)
                return AuthenticateResult.Fail("User not found");

            var principal = AuthService.BuildClaimsPrincipal(user);
            // Rebuild with our scheme name so the handler matches
            var identity = new ClaimsIdentity(principal.Claims, Scheme.Name);
            var ticket = new AuthenticationTicket(new ClaimsPrincipal(identity), Scheme.Name);
            return AuthenticateResult.Success(ticket);
        }
        catch (Exception ex)
        {
            return AuthenticateResult.Fail(ex.Message);
        }
    }

    protected override Task HandleChallengeAsync(AuthenticationProperties properties)
    {
        Response.StatusCode = 401;
        Response.ContentType = "application/json";
        return Response.WriteAsync("{\"message\":\"Invalid or expired token\",\"statusCode\":401}");
    }

    protected override Task HandleForbiddenAsync(AuthenticationProperties properties)
    {
        Response.StatusCode = 403;
        Response.ContentType = "application/json";
        return Response.WriteAsync("{\"message\":\"Forbidden\",\"statusCode\":403}");
    }
}

public static class FirebaseJwtDefaults
{
    public const string AuthenticationScheme = "FirebaseJwt";
}
