using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using NexusCoreDotNet.Data.Entities;
using NexusCoreDotNet.Enums;
using NexusCoreDotNet.Filters;
using NexusCoreDotNet.Services;

namespace NexusCoreDotNet.Pages.Team;

[RequireRole(Role.ORG_MANAGER)]
public class IndexModel : PageModel
{
    private readonly UserService _users;

    public IndexModel(UserService users)
    {
        _users = users;
    }

    public List<User> Members { get; private set; } = new();
    public List<Invite> Invites { get; private set; } = new();
    public Guid CurrentUserId { get; private set; }
    public string? InviteError { get; set; }

    public async Task OnGetAsync()
    {
        var orgId = AuthService.GetOrgId(User);
        CurrentUserId = AuthService.GetUserId(User);
        Members = await _users.FindAllAsync(orgId);
        Invites = await _users.ListInvitesAsync(orgId);
    }

    public async Task<IActionResult> OnPostInviteAsync(string inviteEmail, string inviteRole)
    {
        if (!Enum.TryParse<Role>(inviteRole, out var role))
        {
            InviteError = "Invalid role selected.";
            await OnGetAsync();
            return Page();
        }

        try
        {
            var orgId = AuthService.GetOrgId(User);
            var actorId = AuthService.GetUserId(User);
            var invite = await _users.CreateInviteAsync(inviteEmail, role, orgId, actorId);
            TempData["Success"] = $"Invitation sent to {inviteEmail}.";
        }
        catch (Exception ex)
        {
            TempData["Error"] = ex.Message;
        }

        return RedirectToPage();
    }

    public async Task<IActionResult> OnPostUpdateRoleAsync(Guid targetUserId, string newRole)
    {
        if (!Enum.TryParse<Role>(newRole, out var role))
        {
            TempData["Error"] = "Invalid role.";
            return RedirectToPage();
        }

        try
        {
            var orgId = AuthService.GetOrgId(User);
            var actorId = AuthService.GetUserId(User);
            await _users.UpdateRoleAsync(targetUserId, role, orgId, actorId);
            TempData["Success"] = "Role updated successfully.";
        }
        catch (Exception ex)
        {
            TempData["Error"] = ex.Message;
        }

        return RedirectToPage();
    }

    public async Task<IActionResult> OnPostRemoveAsync(Guid targetUserId)
    {
        try
        {
            var orgId = AuthService.GetOrgId(User);
            var actorId = AuthService.GetUserId(User);
            await _users.RemoveMemberAsync(targetUserId, actorId, orgId);
            TempData["Success"] = "Member removed.";
        }
        catch (Exception ex)
        {
            TempData["Error"] = ex.Message;
        }

        return RedirectToPage();
    }

    public async Task<IActionResult> OnPostDeleteInviteAsync(Guid inviteId)
    {
        try
        {
            var orgId = AuthService.GetOrgId(User);
            await _users.DeleteInviteAsync(inviteId, orgId);
            TempData["Success"] = "Invitation deleted.";
        }
        catch (Exception ex)
        {
            TempData["Error"] = ex.Message;
        }

        return RedirectToPage();
    }
}
