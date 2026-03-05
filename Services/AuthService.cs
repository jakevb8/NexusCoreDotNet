using System.Security.Claims;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.EntityFrameworkCore;
using NexusCoreDotNet.Data;
using NexusCoreDotNet.Data.Entities;
using NexusCoreDotNet.Enums;

namespace NexusCoreDotNet.Services;

public class AuthService
{
    private readonly AppDbContext _db;
    private readonly IFirebaseAuthService _firebase;
    private const int AutoApproveDailyLimit = 5;
    private const int AutoApproveTotalLimit = 50;

    public AuthService(AppDbContext db, IFirebaseAuthService firebase)
    {
        _db = db;
        _firebase = firebase;
    }

    public async Task<bool> ShouldAutoApproveAsync()
    {
        var todayUtc = DateTime.UtcNow.Date;

        var totalActive = await _db.Organizations.CountAsync(o => o.Status == OrgStatus.ACTIVE);
        if (totalActive >= AutoApproveTotalLimit) return false;

        var approvedToday = await _db.Organizations.CountAsync(
            o => o.Status == OrgStatus.ACTIVE && o.UpdatedAt >= todayUtc);
        if (approvedToday >= AutoApproveDailyLimit) return false;

        return true;
    }

    public async Task<User> RegisterNewOrganizationAsync(
        string idToken,
        string orgName,
        string orgSlug,
        string? displayName)
    {
        var decoded = await _firebase.VerifyIdTokenAsync(idToken);
        var firebaseUid = decoded.Uid;
        var email = decoded.Claims.TryGetValue("email", out var emailObj)
            ? emailObj?.ToString() ?? string.Empty
            : string.Empty;

        if (string.IsNullOrEmpty(email))
            throw new InvalidOperationException("Firebase token has no email claim");

        if (await _db.Users.AnyAsync(u => u.FirebaseUid == firebaseUid))
            throw new InvalidOperationException("User already registered");

        if (await _db.Organizations.AnyAsync(o => o.Slug == orgSlug))
            throw new InvalidOperationException("Organization slug already taken");

        var autoApprove = await ShouldAutoApproveAsync();

        var org = new Organization
        {
            Name = orgName,
            Slug = orgSlug,
            Status = autoApprove ? OrgStatus.ACTIVE : OrgStatus.PENDING,
            UpdatedAt = DateTime.UtcNow
        };
        _db.Organizations.Add(org);

        var user = new User
        {
            FirebaseUid = firebaseUid,
            Email = email,
            DisplayName = displayName,
            Role = Role.ORG_MANAGER,
            OrganizationId = org.Id
        };
        _db.Users.Add(user);

        await _db.SaveChangesAsync();
        return user;
    }

    public async Task<User> AcceptInviteAsync(string idToken, string token, string? displayName)
    {
        var decoded = await _firebase.VerifyIdTokenAsync(idToken);
        var firebaseUid = decoded.Uid;
        var email = decoded.Claims.TryGetValue("email", out var emailObj)
            ? emailObj?.ToString() ?? string.Empty
            : string.Empty;

        if (string.IsNullOrEmpty(email))
            throw new InvalidOperationException("Firebase token has no email claim");

        var invite = await _db.Invites.FirstOrDefaultAsync(i => i.Token == token);
        if (invite == null) throw new KeyNotFoundException("Invite not found");
        if (invite.AcceptedAt.HasValue) throw new InvalidOperationException("Invite already used");
        if (invite.ExpiresAt < DateTime.UtcNow) throw new InvalidOperationException("Invite has expired");
        if (!string.Equals(invite.Email, email, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Invite email mismatch");

        if (await _db.Users.AnyAsync(u => u.FirebaseUid == firebaseUid))
            throw new InvalidOperationException("User already registered");

        var user = new User
        {
            FirebaseUid = firebaseUid,
            Email = email,
            DisplayName = displayName,
            Role = invite.Role,
            OrganizationId = invite.OrganizationId
        };
        _db.Users.Add(user);

        invite.AcceptedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync();

        return user;
    }

    public async Task<User?> GetUserByFirebaseUidAsync(string firebaseUid)
        => await _db.Users
            .Include(u => u.Organization)
            .FirstOrDefaultAsync(u => u.FirebaseUid == firebaseUid);

    public async Task<User?> GetUserByIdAsync(Guid userId)
        => await _db.Users
            .Include(u => u.Organization)
            .FirstOrDefaultAsync(u => u.Id == userId);

    public static ClaimsPrincipal BuildClaimsPrincipal(User user)
    {
        var claims = new List<Claim>
        {
            new("sub", user.Id.ToString()),
            new("org", user.OrganizationId.ToString()),
            new("role", user.Role.ToString()),
            new(ClaimTypes.Email, user.Email),
            new("email", user.Email),
            new("name", user.DisplayName ?? user.Email),
        };
        var identity = new ClaimsIdentity(claims, CookieAuthenticationDefaults.AuthenticationScheme);
        return new ClaimsPrincipal(identity);
    }

    public static Guid GetUserId(ClaimsPrincipal principal)
        => Guid.Parse(principal.FindFirstValue("sub")!);

    public static Guid GetOrgId(ClaimsPrincipal principal)
        => Guid.Parse(principal.FindFirstValue("org")!);

    public static Role GetRole(ClaimsPrincipal principal)
        => Enum.Parse<Role>(principal.FindFirstValue("role")!);
}
