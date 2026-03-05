using System.Text.Json;

namespace NexusCoreDotNet.Data.Entities;

public class AuditLog
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Action { get; set; } = string.Empty;
    public Guid ActorId { get; set; }
    public Guid? AssetId { get; set; }
    public JsonDocument Changes { get; set; } = JsonDocument.Parse("{}");
    public DateTime Timestamp { get; set; } = DateTime.UtcNow;

    public User Actor { get; set; } = null!;
    public Asset? Asset { get; set; }
}
