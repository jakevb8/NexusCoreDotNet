using Microsoft.AspNetCore.Mvc.RazorPages;
using NexusCoreDotNet.Enums;
using NexusCoreDotNet.Filters;
using NexusCoreDotNet.Services;

namespace NexusCoreDotNet.Pages.Reports;

[RequireRole(Role.VIEWER)]
public class IndexModel : PageModel
{
    private readonly ReportsService _reports;

    public IndexModel(ReportsService reports)
    {
        _reports = reports;
    }

    public ReportsService.OrgStats Stats { get; private set; } = new(0, 0, 0, 0, 0, 0, 0);

    public async Task OnGetAsync()
    {
        var orgId = AuthService.GetOrgId(User);
        Stats = await _reports.GetOrgStatsAsync(orgId);
    }
}
