using NexusCoreDotNet.Enums;

namespace NexusCoreDotNet.Data.Entities;

public class Invite
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Email { get; set; } = string.Empty;
    public Role Role { get; set; } = Role.VIEWER;
    public Guid OrganizationId { get; set; }
    public string Token { get; set; } = Guid.NewGuid().ToString();
    public DateTime ExpiresAt { get; set; }
    public DateTime? AcceptedAt { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public Organization Organization { get; set; } = null!;
}
