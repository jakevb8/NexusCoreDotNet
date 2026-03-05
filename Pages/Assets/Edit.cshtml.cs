using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using NexusCoreDotNet.Enums;
using NexusCoreDotNet.Filters;
using NexusCoreDotNet.Services;

namespace NexusCoreDotNet.Pages.Assets;

[RequireRole(Role.ORG_MANAGER)]
public class EditModel : PageModel
{
    private readonly AssetService _assets;

    public EditModel(AssetService assets)
    {
        _assets = assets;
    }

    [BindProperty] public string Name { get; set; } = string.Empty;
    [BindProperty] public string SKU { get; set; } = string.Empty;
    [BindProperty] public string? Description { get; set; }
    [BindProperty] public AssetStatus Status { get; set; } = AssetStatus.AVAILABLE;
    [BindProperty] public string? AssignedTo { get; set; }
    public string? ErrorMessage { get; set; }
    public Guid AssetId { get; private set; }

    public async Task<IActionResult> OnGetAsync(Guid id)
    {
        AssetId = id;
        var orgId = AuthService.GetOrgId(User);
        var asset = await _assets.FindOneAsync(id, orgId);
        if (asset == null)
        {
            TempData["Error"] = "Asset not found.";
            return RedirectToPage("/Assets/Index");
        }

        Name = asset.Name;
        SKU = asset.SKU;
        Description = asset.Description;
        Status = asset.Status;
        AssignedTo = asset.AssignedTo;
        return Page();
    }

    public async Task<IActionResult> OnPostAsync(Guid id)
    {
        AssetId = id;
        if (string.IsNullOrWhiteSpace(Name) || string.IsNullOrWhiteSpace(SKU))
        {
            ErrorMessage = "Name and SKU are required.";
            return Page();
        }

        try
        {
            var orgId = AuthService.GetOrgId(User);
            var actorId = AuthService.GetUserId(User);
            await _assets.UpdateAsync(id, Name, SKU, Description, Status, AssignedTo, orgId, actorId);
            TempData["Success"] = "Asset updated successfully.";
            return RedirectToPage("/Assets/Index");
        }
        catch (Exception ex)
        {
            ErrorMessage = ex.Message;
            return Page();
        }
    }
}
