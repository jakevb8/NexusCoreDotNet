using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using NexusCoreDotNet.Data;
using NexusCoreDotNet.Data.Entities;
using NexusCoreDotNet.Enums;
using NexusCoreDotNet.Services;

namespace NexusCoreDotNet.Tests;

public class ReportsServiceTests : IDisposable
{
    private readonly AppDbContext _db;
    private readonly IMemoryCache _cache;
    private readonly ReportsService _sut;
    private readonly Guid _orgId = Guid.NewGuid();

    public ReportsServiceTests()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        _db = new AppDbContext(options);
        _cache = new MemoryCache(new MemoryCacheOptions());
        _sut = new ReportsService(_db, _cache);
    }

    public void Dispose()
    {
        _db.Dispose();
        _cache.Dispose();
    }

    private void SeedAssets(params AssetStatus[] statuses)
    {
        var org = new Organization { Id = _orgId, Name = "Test Org", Slug = "test-org" };
        _db.Organizations.Add(org);
        foreach (var s in statuses)
        {
            _db.Assets.Add(new Asset
            {
                Name = "Asset",
                SKU = Guid.NewGuid().ToString(),
                Status = s,
                OrganizationId = _orgId
            });
        }
        _db.SaveChanges();
    }

    private void SeedUsers(int count)
    {
        for (int i = 0; i < count; i++)
        {
            _db.Users.Add(new User
            {
                FirebaseUid = Guid.NewGuid().ToString(),
                Email = $"user{i}@test.com",
                Role = Role.VIEWER,
                OrganizationId = _orgId
            });
        }
        _db.SaveChanges();
    }

    [Fact]
    public async Task GetOrgStats_ReturnsCorrectTotals()
    {
        SeedAssets(AssetStatus.AVAILABLE, AssetStatus.IN_USE, AssetStatus.IN_USE, AssetStatus.MAINTENANCE);
        SeedUsers(3);

        var stats = await _sut.GetOrgStatsAsync(_orgId);

        Assert.Equal(4, stats.TotalAssets);
        Assert.Equal(1, stats.Available);
        Assert.Equal(2, stats.InUse);
        Assert.Equal(1, stats.Maintenance);
        Assert.Equal(0, stats.Retired);
        Assert.Equal(3, stats.TotalUsers);
    }

    [Fact]
    public async Task GetOrgStats_CalculatesUtilizationRate()
    {
        SeedAssets(AssetStatus.IN_USE, AssetStatus.IN_USE, AssetStatus.AVAILABLE, AssetStatus.AVAILABLE);

        var stats = await _sut.GetOrgStatsAsync(_orgId);

        // 2 in use out of 4 total = 50%
        Assert.Equal(50.0, stats.UtilizationRate);
    }

    [Fact]
    public async Task GetOrgStats_ZeroUtilizationWhenNoAssets()
    {
        var org = new Organization { Id = _orgId, Name = "Test Org", Slug = "test-org" };
        _db.Organizations.Add(org);
        _db.SaveChanges();

        var stats = await _sut.GetOrgStatsAsync(_orgId);

        Assert.Equal(0, stats.TotalAssets);
        Assert.Equal(0.0, stats.UtilizationRate);
    }

    [Fact]
    public async Task GetOrgStats_ReturnsCachedResultOnSecondCall()
    {
        SeedAssets(AssetStatus.AVAILABLE);

        var first = await _sut.GetOrgStatsAsync(_orgId);

        // Add another asset — should NOT be reflected due to cache
        _db.Assets.Add(new Asset
        {
            Name = "New Asset",
            SKU = Guid.NewGuid().ToString(),
            Status = AssetStatus.AVAILABLE,
            OrganizationId = _orgId
        });
        _db.SaveChanges();

        var second = await _sut.GetOrgStatsAsync(_orgId);

        Assert.Equal(first.TotalAssets, second.TotalAssets);
        Assert.Equal(1, second.TotalAssets); // cached value
    }

    [Fact]
    public async Task GetOrgStats_UsesSeparateCacheKeysPerOrg()
    {
        var orgId2 = Guid.NewGuid();
        var org2 = new Organization { Id = orgId2, Name = "Org2", Slug = "org2" };
        _db.Organizations.Add(org2);
        SeedAssets(AssetStatus.AVAILABLE, AssetStatus.AVAILABLE);
        _db.Assets.Add(new Asset
        {
            Name = "Org2 Asset",
            SKU = Guid.NewGuid().ToString(),
            Status = AssetStatus.IN_USE,
            OrganizationId = orgId2
        });
        _db.SaveChanges();

        var stats1 = await _sut.GetOrgStatsAsync(_orgId);
        var stats2 = await _sut.GetOrgStatsAsync(orgId2);

        Assert.Equal(2, stats1.TotalAssets);
        Assert.Equal(1, stats2.TotalAssets);
    }

    [Fact]
    public async Task GetOrgStats_ScopedToOrg_IgnoresOtherOrgAssets()
    {
        var otherOrgId = Guid.NewGuid();
        var otherOrg = new Organization { Id = otherOrgId, Name = "Other", Slug = "other" };
        _db.Organizations.Add(otherOrg);
        SeedAssets(AssetStatus.AVAILABLE);
        _db.Assets.Add(new Asset
        {
            Name = "Other Asset",
            SKU = Guid.NewGuid().ToString(),
            Status = AssetStatus.IN_USE,
            OrganizationId = otherOrgId
        });
        _db.SaveChanges();

        var stats = await _sut.GetOrgStatsAsync(_orgId);

        Assert.Equal(1, stats.TotalAssets);
        Assert.Equal(1, stats.Available);
        Assert.Equal(0, stats.InUse);
    }
}
