using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using NexusCoreDotNet.Services;

namespace NexusCoreDotNet.Pages;

public class OnboardingModel : PageModel
{
    private readonly AuthService _auth;

    public OnboardingModel(AuthService auth)
    {
        _auth = auth;
    }

    [BindProperty] public string OrgName { get; set; } = string.Empty;
    [BindProperty] public string OrgSlug { get; set; } = string.Empty;
    [BindProperty] public string? DisplayName { get; set; }
    public string? ErrorMessage { get; set; }

    public IActionResult OnGet()
    {
        if (User.Identity?.IsAuthenticated == true)
            return Redirect("/Dashboard");
        return Page();
    }

    public async Task<IActionResult> OnPostAsync()
    {
        if (!ModelState.IsValid)
        {
            ErrorMessage = "Please fill in all required fields.";
            return Page();
        }

        var idToken = HttpContext.Session.GetString("pendingIdToken");
        if (string.IsNullOrEmpty(idToken))
        {
            ErrorMessage = "Session expired. Please sign in again.";
            return Page();
        }

        try
        {
            var user = await _auth.RegisterNewOrganizationAsync(
                idToken, OrgName, OrgSlug, DisplayName);

            // Load with org
            user = (await _auth.GetUserByIdAsync(user.Id))!;

            HttpContext.Session.Remove("pendingIdToken");

            var principal = AuthService.BuildClaimsPrincipal(user);
            var orgStatus = user.Organization?.Status.ToString() ?? "PENDING";
            ((System.Security.Claims.ClaimsIdentity)principal.Identity!)
                .AddClaim(new System.Security.Claims.Claim("orgStatus", orgStatus));

            await HttpContext.SignInAsync(
                CookieAuthenticationDefaults.AuthenticationScheme,
                principal,
                new AuthenticationProperties { IsPersistent = true, ExpiresUtc = DateTimeOffset.UtcNow.AddDays(7) });

            return orgStatus == "ACTIVE"
                ? Redirect("/Dashboard")
                : Redirect("/PendingApproval");
        }
        catch (Exception ex)
        {
            ErrorMessage = ex.Message;
            return Page();
        }
    }
}
