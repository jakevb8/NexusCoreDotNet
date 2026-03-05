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
    private readonly ILogger<LoginModel> _logger;

    public LoginModel(AuthService auth, IConfiguration config, ILogger<LoginModel> logger)
    {
        _auth = auth;
        _config = config;
        _logger = logger;
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
        _logger.LogInformation("[login] OnPostVerifyAsync called, idToken present: {present}", !string.IsNullOrWhiteSpace(request?.IdToken));
        if (string.IsNullOrWhiteSpace(request?.IdToken))
            return new JsonResult(new { error = "Missing ID token" }) { StatusCode = 400 };

        try
        {
            var firebaseToken = await new FirebaseAuthService(_config).VerifyIdTokenAsync(request.IdToken);
            _logger.LogInformation("[login] Firebase token verified, uid: {uid}", firebaseToken.Uid);
            var user = await _auth.GetUserByFirebaseUidAsync(firebaseToken.Uid);
            _logger.LogInformation("[login] DB user found: {found}", user != null);

            if (user == null)
            {
                // New user — redirect to onboarding; store idToken in session to re-use
                HttpContext.Session.SetString("pendingIdToken", request.IdToken);
                return new JsonResult(new { redirect = "/Onboarding" });
            }

            var principal = AuthService.BuildClaimsPrincipal(user);
            var orgStatus = user.Organization?.Status.ToString() ?? "PENDING";
            _logger.LogInformation("[login] orgStatus: {status}", orgStatus);
            ((System.Security.Claims.ClaimsIdentity)principal.Identity!)
                .AddClaim(new System.Security.Claims.Claim("orgStatus", orgStatus));

            await HttpContext.SignInAsync(
                CookieAuthenticationDefaults.AuthenticationScheme,
                principal,
                new AuthenticationProperties { IsPersistent = true, ExpiresUtc = DateTimeOffset.UtcNow.AddDays(7) });

            var redirect = orgStatus == "ACTIVE" ? "/Dashboard" : "/PendingApproval";
            _logger.LogInformation("[login] SignInAsync complete, returning redirect: {redirect}", redirect);
            return new JsonResult(new { redirect });
        }
        catch (UnauthorizedAccessException ex)
        {
            _logger.LogWarning("[login] UnauthorizedAccessException: {msg}", ex.Message);
            return new JsonResult(new { error = "Invalid or expired Firebase token" }) { StatusCode = 401 };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[login] Unexpected exception");
            return new JsonResult(new { error = ex.Message }) { StatusCode = 500 };
        }
    }

    public record VerifyRequest(string IdToken);
}
