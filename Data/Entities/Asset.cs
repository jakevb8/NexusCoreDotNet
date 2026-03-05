using NexusCoreDotNet.Enums;

namespace NexusCoreDotNet.Data.Entities;

public class Asset
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Name { get; set; } = string.Empty;
    public string SKU { get; set; } = string.Empty;
    public string? Description { get; set; }
    public AssetStatus Status { get; set; } = AssetStatus.AVAILABLE;
    public string? AssignedTo { get; set; }
    public Guid OrganizationId { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

    public Organization Organization { get; set; } = null!;
    public ICollection<AuditLog> AuditLogs { get; set; } = new List<AuditLog>();
}
