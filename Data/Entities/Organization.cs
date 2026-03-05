using NexusCoreDotNet.Enums;

namespace NexusCoreDotNet.Data.Entities;

public class Organization
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Name { get; set; } = string.Empty;
    public string Slug { get; set; } = string.Empty;
    public OrgStatus Status { get; set; } = OrgStatus.PENDING;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

    public ICollection<User> Users { get; set; } = new List<User>();
    public ICollection<Asset> Assets { get; set; } = new List<Asset>();
    public ICollection<Invite> Invites { get; set; } = new List<Invite>();
}
