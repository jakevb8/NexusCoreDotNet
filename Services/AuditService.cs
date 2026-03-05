using System.Text.Json;
using NexusCoreDotNet.Data;
using NexusCoreDotNet.Data.Entities;

namespace NexusCoreDotNet.Services;

public class AuditService
{
    private readonly AppDbContext _db;

    public AuditService(AppDbContext db)
    {
        _db = db;
    }

    public async Task LogAsync(
        string action,
        Guid actorId,
        Guid? assetId,
        object? before,
        object? after)
    {
        var changes = JsonDocument.Parse(JsonSerializer.Serialize(new { before, after }));

        var log = new AuditLog
        {
            Action = action,
            ActorId = actorId,
            AssetId = assetId,
            Changes = changes,
            Timestamp = DateTime.UtcNow
        };

        _db.AuditLogs.Add(log);
        await _db.SaveChangesAsync();
    }
}
