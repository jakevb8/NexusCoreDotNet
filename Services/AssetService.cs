using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using NexusCoreDotNet.Data;
using NexusCoreDotNet.Data.Entities;
using NexusCoreDotNet.Enums;

namespace NexusCoreDotNet.Services;

public class AssetService
{
    private readonly AppDbContext _db;
    private readonly AuditService _audit;
    private const int TrialAssetLimit = 100;

    public AssetService(AppDbContext db, AuditService audit)
    {
        _db = db;
        _audit = audit;
    }

    public async Task<(List<Asset> Data, int Total)> FindAllAsync(
        Guid organizationId, int page = 1, int perPage = 20, string? search = null)
    {
        var query = _db.Assets.Where(a => a.OrganizationId == organizationId);

        if (!string.IsNullOrWhiteSpace(search))
        {
            var lower = search.ToLower();
            query = query.Where(a =>
                a.Name.ToLower().Contains(lower) ||
                a.SKU.ToLower().Contains(lower));
        }

        var total = await query.CountAsync();
        var data = await query
            .OrderByDescending(a => a.CreatedAt)
            .Skip((page - 1) * perPage)
            .Take(perPage)
            .ToListAsync();

        return (data, total);
    }

    public async Task<Asset?> FindOneAsync(Guid id, Guid organizationId)
        => await _db.Assets.FirstOrDefaultAsync(a => a.Id == id && a.OrganizationId == organizationId);

    public async Task<Asset> CreateAsync(
        string name, string sku, string? description, AssetStatus status,
        string? assignedTo, Guid organizationId, Guid actorId)
    {
        if (await _db.Assets.AnyAsync(a => a.SKU == sku))
            throw new InvalidOperationException($"SKU \"{sku}\" already exists");

        var count = await _db.Assets.CountAsync(a => a.OrganizationId == organizationId);
        if (count >= TrialAssetLimit)
            throw new InvalidOperationException(
                $"Trial limit reached: organizations are limited to {TrialAssetLimit} assets.");

        var asset = new Asset
        {
            Name = name,
            SKU = sku,
            Description = description,
            Status = status,
            AssignedTo = assignedTo,
            OrganizationId = organizationId
        };
        _db.Assets.Add(asset);
        await _db.SaveChangesAsync();

        await _audit.LogAsync("ASSET_CREATED", actorId, asset.Id,
            before: null, after: AssetSnapshot(asset));

        return asset;
    }

    public async Task<Asset> UpdateAsync(
        Guid id, string name, string sku, string? description, AssetStatus status,
        string? assignedTo, Guid organizationId, Guid actorId)
    {
        var asset = await FindOneAsync(id, organizationId)
            ?? throw new KeyNotFoundException($"Asset {id} not found");

        var before = AssetSnapshot(asset);

        // If SKU changed, verify uniqueness
        if (asset.SKU != sku && await _db.Assets.AnyAsync(a => a.SKU == sku && a.Id != id))
            throw new InvalidOperationException($"SKU \"{sku}\" already exists");

        asset.Name = name;
        asset.SKU = sku;
        asset.Description = description;
        asset.Status = status;
        asset.AssignedTo = assignedTo;
        asset.UpdatedAt = DateTime.UtcNow;

        await _db.SaveChangesAsync();

        await _audit.LogAsync("ASSET_UPDATED", actorId, asset.Id,
            before: before, after: AssetSnapshot(asset));

        return asset;
    }

    public async Task DeleteAsync(Guid id, Guid organizationId, Guid actorId)
    {
        var asset = await FindOneAsync(id, organizationId)
            ?? throw new KeyNotFoundException($"Asset {id} not found");

        await _audit.LogAsync("ASSET_DELETED", actorId, asset.Id,
            before: AssetSnapshot(asset), after: null);

        _db.Assets.Remove(asset);
        await _db.SaveChangesAsync();
    }

    public record BulkImportResult(int Created, int Skipped, bool LimitReached, List<string> Errors);

    public async Task<BulkImportResult> BulkImportAsync(
        List<(string Name, string SKU, string? Description, AssetStatus Status)> records,
        Guid organizationId, Guid actorId)
    {
        int created = 0, skipped = 0;
        bool limitReached = false;
        var errors = new List<string>();

        foreach (var (name, sku, description, status) in records)
        {
            if (limitReached) { skipped++; continue; }

            try
            {
                await CreateAsync(name, sku, description, status, null, organizationId, actorId);
                created++;
            }
            catch (InvalidOperationException ex) when (ex.Message.Contains("Trial limit"))
            {
                limitReached = true;
                skipped++;
            }
            catch (InvalidOperationException)
            {
                skipped++;
            }
            catch (Exception ex)
            {
                errors.Add($"SKU {sku}: {ex.Message}");
            }
        }

        return new BulkImportResult(created, skipped, limitReached, errors);
    }

    private static object AssetSnapshot(Asset a) => new
    {
        a.Id, a.Name, a.SKU, a.Description,
        Status = a.Status.ToString(),
        a.AssignedTo, a.OrganizationId,
        a.CreatedAt, a.UpdatedAt
    };
}
