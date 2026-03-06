using Microsoft.EntityFrameworkCore;
using NexusCoreDotNet.Data;
using NexusCoreDotNet.Data.Entities;
using NexusCoreDotNet.Enums;
using NexusCoreDotNet.Services;

namespace NexusCoreDotNet.Tests;

public class AssetServiceTests : IDisposable
{
    private readonly AppDbContext _db;
    private readonly AssetService _sut;
    private readonly Guid _orgId = Guid.NewGuid();
    private readonly Guid _actorId = Guid.NewGuid();

    public AssetServiceTests()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        _db = new AppDbContext(options);
        var audit = new AuditService(_db);
        _sut = new AssetService(_db, audit);

        // Seed org + actor user required by audit FK
        var org = new Organization { Id = _orgId, Name = "Test Org", Slug = "test-org" };
        _db.Organizations.Add(org);
        _db.Users.Add(new User
        {
            Id = _actorId,
            FirebaseUid = "uid-actor",
            Email = "actor@test.com",
            OrganizationId = _orgId
        });
        _db.SaveChanges();
    }

    public void Dispose() => _db.Dispose();

    // ── FindAllAsync ──────────────────────────────────────────────────────────

    [Fact]
    public async Task FindAll_ReturnsPaginatedResultsScopedToOrg()
    {
        for (int i = 0; i < 5; i++)
            _db.Assets.Add(new Asset { Name = $"Asset{i}", SKU = $"SKU-{i}", OrganizationId = _orgId });
        _db.SaveChanges();

        var (data, total) = await _sut.FindAllAsync(_orgId, page: 1, perPage: 3);

        Assert.Equal(5, total);
        Assert.Equal(3, data.Count);
    }

    [Fact]
    public async Task FindAll_FiltersOnSearchTerm()
    {
        _db.Assets.Add(new Asset { Name = "Laptop Pro", SKU = "LP-001", OrganizationId = _orgId });
        _db.Assets.Add(new Asset { Name = "Desk Chair", SKU = "DC-001", OrganizationId = _orgId });
        _db.SaveChanges();

        var (data, total) = await _sut.FindAllAsync(_orgId, search: "laptop");

        Assert.Equal(1, total);
        Assert.Equal("Laptop Pro", data[0].Name);
    }

    // ── FindOneAsync ──────────────────────────────────────────────────────────

    [Fact]
    public async Task FindOne_ReturnsAsset()
    {
        var asset = new Asset { Name = "A", SKU = "A-001", OrganizationId = _orgId };
        _db.Assets.Add(asset);
        _db.SaveChanges();

        var result = await _sut.FindOneAsync(asset.Id, _orgId);

        Assert.NotNull(result);
        Assert.Equal("A-001", result!.SKU);
    }

    [Fact]
    public async Task FindOne_ReturnsNullWhenNotInOrg()
    {
        var asset = new Asset { Name = "A", SKU = "A-002", OrganizationId = Guid.NewGuid() };
        _db.Assets.Add(asset);
        _db.SaveChanges();

        var result = await _sut.FindOneAsync(asset.Id, _orgId);

        Assert.Null(result);
    }

    // ── CreateAsync ───────────────────────────────────────────────────────────

    [Fact]
    public async Task Create_CreatesAssetAndAuditLog()
    {
        var asset = await _sut.CreateAsync("Laptop", "LT-001", null, AssetStatus.AVAILABLE, null, _orgId, _actorId);

        Assert.NotEqual(Guid.Empty, asset.Id);
        Assert.Equal("Laptop", asset.Name);

        var log = await _db.AuditLogs.FirstOrDefaultAsync(l => l.AssetId == asset.Id);
        Assert.NotNull(log);
        Assert.Equal("ASSET_CREATED", log!.Action);
    }

    [Fact]
    public async Task Create_ThrowsOnDuplicateSKU()
    {
        _db.Assets.Add(new Asset { Name = "X", SKU = "DUP-001", OrganizationId = _orgId });
        _db.SaveChanges();

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => _sut.CreateAsync("Y", "DUP-001", null, AssetStatus.AVAILABLE, null, _orgId, _actorId));
    }

    [Fact]
    public async Task Create_ThrowsAtTrialLimit()
    {
        for (int i = 0; i < 100; i++)
            _db.Assets.Add(new Asset { Name = $"A{i}", SKU = $"S-{i:D4}", OrganizationId = _orgId });
        _db.SaveChanges();

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(
            () => _sut.CreateAsync("Over", "OVER-001", null, AssetStatus.AVAILABLE, null, _orgId, _actorId));

        Assert.Contains("Trial limit", ex.Message);
    }

    [Fact]
    public async Task Create_SucceedsAtOneBeforeLimit()
    {
        for (int i = 0; i < 99; i++)
            _db.Assets.Add(new Asset { Name = $"A{i}", SKU = $"S-{i:D4}", OrganizationId = _orgId });
        _db.SaveChanges();

        var asset = await _sut.CreateAsync("Last", "LAST-001", null, AssetStatus.AVAILABLE, null, _orgId, _actorId);

        Assert.NotNull(asset);
    }

    // ── UpdateAsync ───────────────────────────────────────────────────────────

    [Fact]
    public async Task Update_UpdatesAssetAndLogsChanges()
    {
        var asset = new Asset { Name = "Old", SKU = "U-001", OrganizationId = _orgId };
        _db.Assets.Add(asset);
        _db.SaveChanges();

        var updated = await _sut.UpdateAsync(
            asset.Id, "New", "U-001", "desc", AssetStatus.IN_USE, "John", _orgId, _actorId);

        Assert.Equal("New", updated.Name);
        Assert.Equal(AssetStatus.IN_USE, updated.Status);
        Assert.Equal("John", updated.AssignedTo);

        var log = await _db.AuditLogs.FirstOrDefaultAsync(l => l.AssetId == asset.Id);
        Assert.NotNull(log);
        Assert.Equal("ASSET_UPDATED", log!.Action);
    }

    [Fact]
    public async Task Update_ThrowsWhenNotFound()
    {
        await Assert.ThrowsAsync<KeyNotFoundException>(
            () => _sut.UpdateAsync(Guid.NewGuid(), "X", "X-001", null, AssetStatus.AVAILABLE, null, _orgId, _actorId));
    }

    [Fact]
    public async Task Update_ThrowsOnDuplicateSKUOfAnotherAsset()
    {
        var a1 = new Asset { Name = "A1", SKU = "A-001", OrganizationId = _orgId };
        var a2 = new Asset { Name = "A2", SKU = "A-002", OrganizationId = _orgId };
        _db.Assets.AddRange(a1, a2);
        _db.SaveChanges();

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => _sut.UpdateAsync(a1.Id, "A1", "A-002", null, AssetStatus.AVAILABLE, null, _orgId, _actorId));
    }

    // ── DeleteAsync ───────────────────────────────────────────────────────────

    [Fact]
    public async Task Delete_RemovesAssetAndLogsAudit()
    {
        var asset = new Asset { Name = "Del", SKU = "DEL-001", OrganizationId = _orgId };
        _db.Assets.Add(asset);
        _db.SaveChanges();

        await _sut.DeleteAsync(asset.Id, _orgId, _actorId);

        Assert.Null(await _db.Assets.FindAsync(asset.Id));
        var log = await _db.AuditLogs.FirstOrDefaultAsync(l => l.Action == "ASSET_DELETED");
        Assert.NotNull(log);
    }

    [Fact]
    public async Task Delete_ThrowsWhenNotFound()
    {
        await Assert.ThrowsAsync<KeyNotFoundException>(
            () => _sut.DeleteAsync(Guid.NewGuid(), _orgId, _actorId));
    }

    // ── ParseCsvStream ────────────────────────────────────────────────────────

    [Fact]
    public void ParseCsvStream_ParsesWellFormedCsv()
    {
        var csv = "Name,SKU,Description,Status\nLaptop Pro 15,LP-001,MacBook Pro,AVAILABLE\nDesk Chair,DC-001,Ergonomic,IN_USE\n";
        using var stream = new System.IO.MemoryStream(System.Text.Encoding.UTF8.GetBytes(csv));

        var records = AssetService.ParseCsvStream(stream);

        Assert.Equal(2, records.Count);
        Assert.Equal("Laptop Pro 15", records[0].Name);
        Assert.Equal("LP-001", records[0].SKU);
        Assert.Equal(AssetStatus.AVAILABLE, records[0].Status);
        Assert.Equal(AssetStatus.IN_USE, records[1].Status);
    }

    [Fact]
    public void ParseCsvStream_SkipsRowsWithMissingNameOrSku()
    {
        var csv = "Name,SKU,Description,Status\n,EMPTY-001,,AVAILABLE\nGoodName,,desc,AVAILABLE\nValid,V-001,,AVAILABLE\n";
        using var stream = new System.IO.MemoryStream(System.Text.Encoding.UTF8.GetBytes(csv));

        var records = AssetService.ParseCsvStream(stream);

        Assert.Single(records);
        Assert.Equal("Valid", records[0].Name);
    }

    [Fact]
    public void ParseCsvStream_DefaultsStatusToAvailableOnUnknownValue()
    {
        var csv = "Name,SKU,Status\nAsset,A-001,BOGUS_STATUS\n";
        using var stream = new System.IO.MemoryStream(System.Text.Encoding.UTF8.GetBytes(csv));

        var records = AssetService.ParseCsvStream(stream);

        Assert.Single(records);
        Assert.Equal(AssetStatus.AVAILABLE, records[0].Status);
    }

    [Fact]
    public void ParseCsvStream_HandlesUnquotedSpecialCharsViaBadDataFoundNull()
    {
        // This is the sample CSV that was previously broken: Monitor 27" contains an unescaped quote
        var csv = "Name,SKU,Description,Status\nLaptop Pro 15,LP-001,MacBook Pro 15-inch,AVAILABLE\nDesk Chair,DC-001,Ergonomic office chair,IN_USE\nMonitor 27\",MN-001,4K UHD display,AVAILABLE\n";
        using var stream = new System.IO.MemoryStream(System.Text.Encoding.UTF8.GetBytes(csv));

        // Should not throw; bad row is skipped, valid rows are returned
        var records = AssetService.ParseCsvStream(stream);

        Assert.True(records.Count >= 2, $"Expected at least 2 records but got {records.Count}");
        Assert.Contains(records, r => r.SKU == "LP-001");
        Assert.Contains(records, r => r.SKU == "DC-001");
    }



    [Fact]
    public async Task BulkImport_ReturnsCreatedAndSkippedCounts()
    {
        _db.Assets.Add(new Asset { Name = "Existing", SKU = "SKU-DUP", OrganizationId = _orgId });
        _db.SaveChanges();

        var records = new List<(string, string, string?, AssetStatus)>
        {
            ("Asset1", "NEW-001", null, AssetStatus.AVAILABLE),
            ("Dupe", "SKU-DUP", null, AssetStatus.AVAILABLE), // duplicate SKU → skipped
            ("Asset2", "NEW-002", null, AssetStatus.AVAILABLE),
        };

        var result = await _sut.BulkImportAsync(records, _orgId, _actorId);

        Assert.Equal(2, result.Created);
        Assert.Equal(1, result.Skipped);
        Assert.False(result.LimitReached);
    }

    [Fact]
    public async Task BulkImport_SetsLimitReachedAndSkipsRemainder()
    {
        for (int i = 0; i < 99; i++)
            _db.Assets.Add(new Asset { Name = $"A{i}", SKU = $"S-{i:D4}", OrganizationId = _orgId });
        _db.SaveChanges();

        var records = new List<(string, string, string?, AssetStatus)>
        {
            ("Last", "LAST-001", null, AssetStatus.AVAILABLE),   // fills limit (100th)
            ("Over1", "OVER-001", null, AssetStatus.AVAILABLE),  // skipped
            ("Over2", "OVER-002", null, AssetStatus.AVAILABLE),  // skipped
        };

        var result = await _sut.BulkImportAsync(records, _orgId, _actorId);

        Assert.Equal(1, result.Created);
        Assert.Equal(2, result.Skipped);
        Assert.True(result.LimitReached);
    }
}
