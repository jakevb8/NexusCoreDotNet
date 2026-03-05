using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using NexusCoreDotNet.Enums;
using NexusCoreDotNet.Filters;
using NexusCoreDotNet.Services;

namespace NexusCoreDotNet.Pages.Assets;

[RequireRole(Role.ORG_MANAGER)]
public class CreateModel : PageModel
{
    private readonly AssetService _assets;

    public CreateModel(AssetService assets)
    {
        _assets = assets;
    }

    [BindProperty] public string Name { get; set; } = string.Empty;
    [BindProperty] public string SKU { get; set; } = string.Empty;
    [BindProperty] public string? Description { get; set; }
    [BindProperty] public AssetStatus Status { get; set; } = AssetStatus.AVAILABLE;
    [BindProperty] public string? AssignedTo { get; set; }
    public string? ErrorMessage { get; set; }

    public IActionResult OnGet() => Page();

    public async Task<IActionResult> OnPostAsync()
    {
        if (string.IsNullOrWhiteSpace(Name) || string.IsNullOrWhiteSpace(SKU))
        {
            ErrorMessage = "Name and SKU are required.";
            return Page();
        }

        try
        {
            var orgId = AuthService.GetOrgId(User);
            var actorId = AuthService.GetUserId(User);
            await _assets.CreateAsync(Name, SKU, Description, Status, AssignedTo, orgId, actorId);
            TempData["Success"] = $"Asset \"{Name}\" created successfully.";
            return RedirectToPage("/Assets/Index");
        }
        catch (Exception ex)
        {
            ErrorMessage = ex.Message;
            return Page();
        }
    }
}
