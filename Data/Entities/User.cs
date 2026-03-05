using NexusCoreDotNet.Enums;

namespace NexusCoreDotNet.Data.Entities;

public class User
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string FirebaseUid { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public string? DisplayName { get; set; }
    public Role Role { get; set; } = Role.VIEWER;
    public Guid OrganizationId { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

    public Organization Organization { get; set; } = null!;
    public ICollection<AuditLog> AuditLogs { get; set; } = new List<AuditLog>();
}
