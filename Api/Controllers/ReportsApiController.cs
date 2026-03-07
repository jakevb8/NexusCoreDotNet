using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using NexusCoreDotNet.Services;

namespace NexusCoreDotNet.Api.Controllers;

[ApiController]
[Route("api/v1/reports")]
[Authorize(AuthenticationSchemes = FirebaseJwtDefaults.AuthenticationScheme)]
public class ReportsApiController : ControllerBase
{
    private readonly ReportsService _reports;

    public ReportsApiController(ReportsService reports)
    {
        _reports = reports;
    }

    // GET /api/v1/reports
    [HttpGet]
    public async Task<IActionResult> GetReports()
    {
        var orgId = AuthService.GetOrgId(User);
        var stats = await _reports.GetOrgStatsAsync(orgId);

        return Ok(new
        {
            totalAssets = stats.TotalAssets,
            available = stats.Available,
            inUse = stats.InUse,
            maintenance = stats.Maintenance,
            retired = stats.Retired,
            utilizationRate = stats.UtilizationRate,
            totalUsers = stats.TotalUsers,
            assetsByStatus = new[]
            {
                new { status = "AVAILABLE", count = stats.Available },
                new { status = "IN_USE",    count = stats.InUse },
                new { status = "MAINTENANCE", count = stats.Maintenance },
                new { status = "RETIRED",   count = stats.Retired }
            }
        });
    }
}
