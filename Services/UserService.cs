using Microsoft.EntityFrameworkCore;
using NexusCoreDotNet.Data;
using NexusCoreDotNet.Data.Entities;
using NexusCoreDotNet.Enums;

namespace NexusCoreDotNet.Services;

public class UserService
{
    private readonly AppDbContext _db;
    private readonly EmailService _email;
    private readonly AuditService _audit;
    private readonly IConfiguration _config;

    public UserService(AppDbContext db, EmailService email, AuditService audit, IConfiguration config)
    {
        _db = db;
        _email = email;
        _audit = audit;
        _config = config;
    }

    public async Task<List<User>> FindAllAsync(Guid organizationId)
        => await _db.Users
            .Where(u => u.OrganizationId == organizationId)
            .OrderByDescending(u => u.CreatedAt)
            .ToListAsync();

    public async Task<Invite> CreateInviteAsync(
        string email, Role role, Guid organizationId, Guid inviterId)
    {
        if (await _db.Users.AnyAsync(u => u.Email == email))
            throw new InvalidOperationException("User with this email already exists");

        var pending = await _db.Invites.AnyAsync(i =>
            i.Email == email &&
            i.OrganizationId == organizationId &&
            i.AcceptedAt == null &&
            i.ExpiresAt > DateTime.UtcNow);
        if (pending)
            throw new InvalidOperationException("Active invite already exists for this email");

        var org = await _db.Organizations.FindAsync(organizationId);
        var inviter = await _db.Users.FindAsync(inviterId);

        var invite = new Invite
        {
            Email = email,
            Role = role,
            OrganizationId = organizationId,
            Token = Guid.NewGuid().ToString(),
            ExpiresAt = DateTime.UtcNow.AddDays(7)
        };
        _db.Invites.Add(invite);
        await _db.SaveChangesAsync();

        var baseUrl = _config["App:FrontendUrl"] ?? "http://localhost:5000";
        _ = _email.SendInviteEmailAsync(
            toEmail: email,
            inviteToken: invite.Token,
            organizationName: org?.Name ?? "your organization",
            inviterName: inviter?.DisplayName ?? inviter?.Email ?? "A team member",
            baseUrl: baseUrl);

        return invite;
    }

    public async Task<List<Invite>> ListInvitesAsync(Guid organizationId)
        => await _db.Invites
            .Where(i => i.OrganizationId == organizationId)
            .OrderByDescending(i => i.CreatedAt)
            .ToListAsync();

    public async Task DeleteInviteAsync(Guid inviteId, Guid organizationId)
    {
        var invite = await _db.Invites.FirstOrDefaultAsync(i =>
            i.Id == inviteId && i.OrganizationId == organizationId)
            ?? throw new KeyNotFoundException("Invite not found");

        if (invite.AcceptedAt.HasValue)
            throw new InvalidOperationException("Cannot delete an already accepted invite");

        _db.Invites.Remove(invite);
        await _db.SaveChangesAsync();
    }

    public async Task<User> UpdateRoleAsync(Guid userId, Role newRole, Guid organizationId, Guid actorId)
    {
        if (newRole == Role.SUPERADMIN)
            throw new InvalidOperationException("Cannot assign SUPERADMIN via this action");

        var user = await _db.Users.FirstOrDefaultAsync(u =>
            u.Id == userId && u.OrganizationId == organizationId)
            ?? throw new KeyNotFoundException("User not found in this organization");

        var before = new { user.Id, user.Email, Role = user.Role.ToString() };
        user.Role = newRole;
        user.UpdatedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync();

        await _audit.LogAsync("MEMBER_ROLE_CHANGED", actorId, null,
            before: before, after: new { user.Id, user.Email, Role = user.Role.ToString() });

        return user;
    }

    public async Task RemoveMemberAsync(Guid userId, Guid actorId, Guid organizationId)
    {
        if (userId == actorId)
            throw new InvalidOperationException("You cannot remove yourself");

        var user = await _db.Users.FirstOrDefaultAsync(u =>
            u.Id == userId && u.OrganizationId == organizationId)
            ?? throw new KeyNotFoundException("User not found in this organization");

        if (user.Role == Role.SUPERADMIN)
            throw new InvalidOperationException("Cannot remove a SUPERADMIN");

        await _audit.LogAsync("MEMBER_REMOVED", actorId, null,
            before: new { user.Id, user.Email, Role = user.Role.ToString() }, after: null);

        _db.Users.Remove(user);
        await _db.SaveChangesAsync();
    }
}
