using System.Text.Json;

namespace NexusCoreDotNet.Data.Entities;

public class AuditLog
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Action { get; set; } = string.Empty;
    public Guid? ActorId { get; set; }
    public Guid? AssetId { get; set; }
    public JsonDocument Changes { get; set; } = JsonDocument.Parse("{}");
    /// <summary>
    /// Plain text (no FK) so org-level events (e.g. ORG_DELETED) survive after
    /// the organization row is removed.
    /// </summary>
    public string? OrganizationId { get; set; }
    public DateTime Timestamp { get; set; } = DateTime.UtcNow;

    public User? Actor { get; set; }
    public Asset? Asset { get; set; }
}
