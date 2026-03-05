using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using NexusCoreDotNet.Data;
using NexusCoreDotNet.Enums;

namespace NexusCoreDotNet.Services;

public class ReportsService
{
    private readonly AppDbContext _db;
    private readonly IMemoryCache _cache;
    private static readonly TimeSpan CacheTtl = TimeSpan.FromMinutes(5);

    public ReportsService(AppDbContext db, IMemoryCache cache)
    {
        _db = db;
        _cache = cache;
    }

    public record OrgStats(
        int TotalAssets,
        int Available,
        int InUse,
        int Maintenance,
        int Retired,
        double UtilizationRate,
        int TotalUsers);

    public async Task<OrgStats> GetOrgStatsAsync(Guid organizationId)
    {
        var cacheKey = $"org-stats:{organizationId}";

        if (_cache.TryGetValue(cacheKey, out OrgStats? cached) && cached is not null)
            return cached;

        var assetCounts = await _db.Assets
            .Where(a => a.OrganizationId == organizationId)
            .GroupBy(a => a.Status)
            .Select(g => new { Status = g.Key, Count = g.Count() })
            .ToListAsync();

        var totalUsers = await _db.Users.CountAsync(u => u.OrganizationId == organizationId);

        int available = 0, inUse = 0, maintenance = 0, retired = 0;
        foreach (var row in assetCounts)
        {
            switch (row.Status)
            {
                case AssetStatus.AVAILABLE: available = row.Count; break;
                case AssetStatus.IN_USE: inUse = row.Count; break;
                case AssetStatus.MAINTENANCE: maintenance = row.Count; break;
                case AssetStatus.RETIRED: retired = row.Count; break;
            }
        }

        var total = available + inUse + maintenance + retired;
        var utilization = total > 0 ? Math.Round((double)inUse / total * 100, 2) : 0;

        var stats = new OrgStats(total, available, inUse, maintenance, retired, utilization, totalUsers);
        _cache.Set(cacheKey, stats, CacheTtl);
        return stats;
    }
}
