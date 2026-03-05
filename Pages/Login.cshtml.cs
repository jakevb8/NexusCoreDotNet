using System.Text.Json;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.RateLimiting;
using NexusCoreDotNet.Services;

namespace NexusCoreDotNet.Pages;

public class LoginModel : PageModel
{
    private readonly AuthService _auth;
    private readonly IConfiguration _config;

    public LoginModel(AuthService auth, IConfiguration config)
    {
        _auth = auth;
        _config = config;
    }

    public string FirebaseConfigJson { get; private set; } = "{}";

    public IActionResult OnGet()
    {
        if (User.Identity?.IsAuthenticated == true)
            return Redirect("/Dashboard");

        FirebaseConfigJson = JsonSerializer.Serialize(new
        {
            apiKey = _config["Firebase:ApiKey"] ?? "",
            authDomain = _config["Firebase:AuthDomain"] ?? "",
            projectId = _config["Firebase:ProjectId"] ?? "",
            storageBucket = _config["Firebase:StorageBucket"] ?? "",
            messagingSenderId = _config["Firebase:MessagingSenderId"] ?? "",
            appId = _config["Firebase:AppId"] ?? ""
        });

        return Page();
    }

    public async Task<IActionResult> OnPostVerifyAsync([FromBody] VerifyRequest request)
    {
        if (string.IsNullOrWhiteSpace(request?.IdToken))
            return new JsonResult(new { error = "Missing ID token" }) { StatusCode = 400 };

        try
        {
            var firebaseToken = await new FirebaseAuthService(_config).VerifyIdTokenAsync(request.IdToken);
            var user = await _auth.GetUserByFirebaseUidAsync(firebaseToken.Uid);

            if (user == null)
            {
                // New user — redirect to onboarding; store idToken in session to re-use
                HttpContext.Session.SetString("pendingIdToken", request.IdToken);
                return new JsonResult(new { redirect = "/Onboarding" });
            }

            var principal = AuthService.BuildClaimsPrincipal(user);
            var orgStatus = user.Organization?.Status.ToString() ?? "PENDING";
            ((System.Security.Claims.ClaimsIdentity)principal.Identity!)
                .AddClaim(new System.Security.Claims.Claim("orgStatus", orgStatus));

            await HttpContext.SignInAsync(
                CookieAuthenticationDefaults.AuthenticationScheme,
                principal,
                new AuthenticationProperties { IsPersistent = true, ExpiresUtc = DateTimeOffset.UtcNow.AddDays(7) });

            var redirect = orgStatus == "ACTIVE" ? "/Dashboard" : "/PendingApproval";
            return new JsonResult(new { redirect });
        }
        catch (UnauthorizedAccessException)
        {
            return new JsonResult(new { error = "Invalid or expired Firebase token" }) { StatusCode = 401 };
        }
        catch (Exception ex)
        {
            return new JsonResult(new { error = ex.Message }) { StatusCode = 500 };
        }
    }

    public record VerifyRequest(string IdToken);
}
