using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using NexusCoreDotNet.Enums;
using NexusCoreDotNet.Filters;
using NexusCoreDotNet.Services;

namespace NexusCoreDotNet.Pages.Settings;

[RequireRole(Role.VIEWER)]
public class IndexModel : PageModel
{
    private readonly AuthService _auth;

    public IndexModel(AuthService auth)
    {
        _auth = auth;
    }

    public string? DisplayName { get; private set; }
    public string Email { get; private set; } = string.Empty;
    public Role Role { get; private set; }
    public string OrgName { get; private set; } = string.Empty;

    public async Task OnGetAsync()
    {
        var userId = AuthService.GetUserId(User);
        var user = await _auth.GetUserByIdAsync(userId);
        if (user == null) return;

        DisplayName = user.DisplayName;
        Email = user.Email;
        Role = user.Role;
        OrgName = user.Organization?.Name ?? string.Empty;
    }

    public async Task<IActionResult> OnPostDeleteAccountAsync()
    {
        var userId = AuthService.GetUserId(User);

        await _auth.DeleteAccountAsync(userId);

        await HttpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);

        return Redirect("/Login");
    }
}
