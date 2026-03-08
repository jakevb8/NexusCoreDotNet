using System.Text.Json;
using FirebaseAdmin.Auth;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using NexusCoreDotNet.Data;
using NexusCoreDotNet.Services;

namespace NexusCoreDotNet.Pages;

public class AcceptInviteModel : PageModel
{
    private readonly AuthService _auth;
    private readonly AppDbContext _db;
    private readonly IConfiguration _config;
    private readonly ILogger<AcceptInviteModel> _logger;

    public AcceptInviteModel(AuthService auth, AppDbContext db, IConfiguration config, ILogger<AcceptInviteModel> logger)
    {
        _auth = auth;
        _db = db;
        _config = config;
        _logger = logger;
    }

    public bool InviteValid { get; private set; }
    public bool AlreadyAccepted { get; private set; }
    public bool Expired { get; private set; }
    public string? OrgName { get; private set; }
    public string? InviteRole { get; private set; }
    public string Token { get; private set; } = string.Empty;
    public string FirebaseConfigJson { get; private set; } = "{}";
    public string? ErrorMessage { get; set; }

    public async Task<IActionResult> OnGetAsync(string token)
    {
        Token = token ?? string.Empty;

        FirebaseConfigJson = JsonSerializer.Serialize(new
        {
            apiKey = _config["Firebase:ApiKey"] ?? "",
            authDomain = _config["Firebase:AuthDomain"] ?? "",
            projectId = _config["Firebase:ProjectId"] ?? "",
            storageBucket = _config["Firebase:StorageBucket"] ?? "",
            messagingSenderId = _config["Firebase:MessagingSenderId"] ?? "",
            appId = _config["Firebase:AppId"] ?? ""
        });

        var invite = await _db.Invites
            .Include(i => i.Organization)
            .FirstOrDefaultAsync(i => i.Token == token);

        if (invite == null) return Page();

        if (invite.AcceptedAt.HasValue) { AlreadyAccepted = true; return Page(); }
        if (invite.ExpiresAt < DateTime.UtcNow) { Expired = true; return Page(); }

        InviteValid = true;
        OrgName = invite.Organization?.Name ?? "your organization";
        InviteRole = invite.Role.ToString().Replace("_", " ");
        return Page();
    }

    public async Task<IActionResult> OnPostAcceptAsync([FromBody] AcceptRequest request)
    {
        try
        {
            // Verify the token against the nexus-core-dotnet Firebase project (default app).
            // FirebaseAuthService (injected into AuthService) uses the "rms" app for mobile
            // clients — we must NOT use it here.
            FirebaseToken decoded;
            try
            {
                decoded = await FirebaseAuth.DefaultInstance.VerifyIdTokenAsync(request.IdToken);
            }
            catch
            {
                return new JsonResult(new { error = "Invalid or expired Firebase token" }) { StatusCode = 401 };
            }

            var firebaseUid = decoded.Uid;
            var email = decoded.Claims.TryGetValue("email", out var emailObj)
                ? emailObj?.ToString() ?? string.Empty : string.Empty;

            var user = await _auth.AcceptInviteAsync(firebaseUid, email, request.DisplayName, request.Token);
            var fullUser = (await _auth.GetUserByIdAsync(user.Id))!;

            var principal = AuthService.BuildClaimsPrincipal(fullUser);
            var orgStatus = fullUser.Organization?.Status.ToString() ?? "PENDING";
            ((System.Security.Claims.ClaimsIdentity)principal.Identity!)
                .AddClaim(new System.Security.Claims.Claim("orgStatus", orgStatus));

            await HttpContext.SignInAsync(
                CookieAuthenticationDefaults.AuthenticationScheme,
                principal,
                new AuthenticationProperties { IsPersistent = true, ExpiresUtc = DateTimeOffset.UtcNow.AddDays(7) });

            return new JsonResult(new { redirect = "/Dashboard" });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[AcceptInvite] Accept failed for token={Token}", request.Token);
            return new JsonResult(new { error = ex.Message }) { StatusCode = 400 };
        }
    }

    public record AcceptRequest(string IdToken, string Token, string? DisplayName);
}
