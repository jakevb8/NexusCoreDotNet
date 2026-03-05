using System.Globalization;
using CsvHelper;
using CsvHelper.Configuration;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using NexusCoreDotNet.Data.Entities;
using NexusCoreDotNet.Enums;
using NexusCoreDotNet.Filters;
using NexusCoreDotNet.Services;

namespace NexusCoreDotNet.Pages.Assets;

[RequireRole(Role.VIEWER)]
public class IndexModel : PageModel
{
    private readonly AssetService _assets;

    public IndexModel(AssetService assets)
    {
        _assets = assets;
    }

    public List<Asset> Assets { get; private set; } = new();
    public int Total { get; private set; }
    public new int Page { get; private set; } = 1;
    public int TotalPages { get; private set; } = 1;
    public string? Search { get; private set; }
    public bool IsManager { get; private set; }
    public AssetService.BulkImportResult? ImportResult { get; private set; }

    public async Task OnGetAsync(int page = 1, string? search = null)
    {
        Page = Math.Max(1, page);
        Search = search;
        IsManager = AuthService.GetRole(User).HasAtLeast(Role.ORG_MANAGER);
        var orgId = AuthService.GetOrgId(User);
        var (data, total) = await _assets.FindAllAsync(orgId, Page, 20, search);
        Assets = data;
        Total = total;
        TotalPages = Math.Max(1, (int)Math.Ceiling(total / 20.0));
    }

    public async Task<IActionResult> OnPostDeleteAsync(Guid id)
    {
        var orgId = AuthService.GetOrgId(User);
        var actorId = AuthService.GetUserId(User);
        try
        {
            await _assets.DeleteAsync(id, orgId, actorId);
            TempData["Success"] = "Asset deleted successfully.";
        }
        catch (Exception ex)
        {
            TempData["Error"] = ex.Message;
        }
        return RedirectToPage();
    }

    public async Task<IActionResult> OnPostImportAsync(IFormFile csvFile)
    {
        if (csvFile == null || csvFile.Length == 0)
        {
            TempData["Error"] = "Please select a CSV file.";
            return RedirectToPage();
        }

        var records = new List<(string Name, string SKU, string? Description, AssetStatus Status)>();

        try
        {
            using var reader = new StreamReader(csvFile.OpenReadStream());
            var config = new CsvConfiguration(CultureInfo.InvariantCulture) { HeaderValidated = null, MissingFieldFound = null };
            using var csv = new CsvReader(reader, config);
            csv.Read(); csv.ReadHeader();

            while (csv.Read())
            {
                var name = csv.GetField("Name") ?? string.Empty;
                var sku = csv.GetField("SKU") ?? string.Empty;
                if (string.IsNullOrWhiteSpace(name) || string.IsNullOrWhiteSpace(sku)) continue;

                var statusStr = csv.GetField("Status") ?? "AVAILABLE";
                var status = Enum.TryParse<AssetStatus>(statusStr.Replace(" ", "_").ToUpper(), out var s)
                    ? s : AssetStatus.AVAILABLE;
                var description = csv.GetField("Description");
                records.Add((name, sku, description, status));
            }
        }
        catch (Exception ex)
        {
            TempData["Error"] = $"CSV parse error: {ex.Message}";
            return RedirectToPage();
        }

        var orgId = AuthService.GetOrgId(User);
        var actorId = AuthService.GetUserId(User);
        ImportResult = await _assets.BulkImportAsync(records, orgId, actorId);

        await OnGetAsync();
        return Page();
    }

    public IActionResult OnGetSampleCsv()
    {
        var csv = "Name,SKU,Description,Status\nLaptop Pro 15,LP-001,MacBook Pro 15-inch,AVAILABLE\nDesk Chair,DC-001,Ergonomic office chair,IN_USE\nMonitor 27\",MN-001,4K UHD display,AVAILABLE\n";
        return File(System.Text.Encoding.UTF8.GetBytes(csv), "text/csv", "assets-sample.csv");
    }
}
