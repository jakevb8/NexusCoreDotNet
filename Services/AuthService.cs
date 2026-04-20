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
    private readonly AuditService _audit;
    private const int AutoApproveDailyLimit = 5;
    private const int AutoApproveTotalLimit = 50;

    public AuthService(AppDbContext db, IFirebaseAuthService firebase, AuditService audit)
    {
        _db = db;
        _firebase = firebase;
        _audit = audit;
    }

    /// <summary>
    /// Verify a raw Firebase ID token using the injected IFirebaseAuthService.
    /// Used by the REST API (mobile clients) which authenticate against nexus-core-rms.
    /// Razor Pages must verify tokens themselves using FirebaseAuth.DefaultInstance.
    /// </summary>
    public async Task<DecodedToken> VerifyTokenAsync(string idToken)
        => await _firebase.VerifyIdTokenAsync(idToken);

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
        string firebaseUid,
        string email,
        string orgName,
        string orgSlug,
        string? displayName)
    {
        if (string.IsNullOrEmpty(email))
            throw new InvalidOperationException("Firebase token has no email claim");

        // Block if this email already has a user record (regardless of which Firebase
        // project issued the UID — email is the cross-client identity).
        if (await _db.Users.AnyAsync(u => u.Email == email))
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

    public async Task<User> AcceptInviteAsync(string firebaseUid, string email, string? displayName, string token)
    {
        if (string.IsNullOrEmpty(email))
            throw new InvalidOperationException("Firebase token has no email claim");

        var invite = await _db.Invites.FirstOrDefaultAsync(i => i.Token == token);
        if (invite == null) throw new KeyNotFoundException("Invite not found");
        if (invite.ExpiresAt < DateTime.UtcNow) throw new InvalidOperationException("Invite has expired");
        if (!string.Equals(invite.Email, email, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Invite email mismatch");

        // Check for an existing user BEFORE checking AcceptedAt — an already-accepted
        // invite where the user exists in the right org is a recoverable state (e.g.
        // the session cookie was lost after a successful accept). Return the existing
        // user so the caller can re-issue a session.
        var existingUser = await _db.Users.FirstOrDefaultAsync(u => u.Email == email);
        if (existingUser != null)
        {
            if (existingUser.OrganizationId != invite.OrganizationId)
                throw new InvalidOperationException(
                    "An account with this email already exists in a different organization.");

            // Recovery path: user exists in the right org — ensure invite is marked accepted
            // and UID is up to date, then return the existing user.
            existingUser.FirebaseUid = firebaseUid;
            if (displayName != null) existingUser.DisplayName = displayName;
            if (!invite.AcceptedAt.HasValue) invite.AcceptedAt = DateTime.UtcNow;
            await _db.SaveChangesAsync();
            return existingUser;
        }

        // No existing user — enforce the invite-already-used guard only now.
        if (invite.AcceptedAt.HasValue) throw new InvalidOperationException("Invite already used");

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
    {
        // Primary lookup by UID — fast path for established sessions.
        var user = await _db.Users
            .Include(u => u.Organization)
            .FirstOrDefaultAsync(u => u.FirebaseUid == firebaseUid);

        if (user != null) return user;

        // UID not found — this Firebase project may be different from the one that
        // originally created the user (e.g. JS app vs .NET app vs mobile).
        // We cannot do the email fallback here because we don't have the email —
        // this method only receives the UID. Callers that have the decoded token
        // should use GetOrMigrateUserAsync instead.
        return null;
    }

    /// <summary>
    /// Looks up a user by Firebase UID. If not found, falls back to email lookup
    /// and migrates the UID so subsequent lookups hit the fast path.
    /// This is the correct method to use when you have both UID and email
    /// (i.e. after verifying a Firebase ID token).
    /// </summary>
    public async Task<User?> GetOrMigrateUserAsync(string firebaseUid, string email)
    {
        // Fast path: UID already matches.
        var user = await _db.Users
            .Include(u => u.Organization)
            .FirstOrDefaultAsync(u => u.FirebaseUid == firebaseUid);

        if (user != null) return user;

        // Fallback: find by email (user registered via a different Firebase project / client).
        var byEmail = await _db.Users
            .Include(u => u.Organization)
            .FirstOrDefaultAsync(u => u.Email == email);

        if (byEmail == null) return null;

        // Migrate the stored UID to the current Firebase UID so future lookups
        // hit the fast path without needing the email fallback.
        byEmail.FirebaseUid = firebaseUid;
        await _db.SaveChangesAsync();

        return byEmail;
    }

    public async Task<User?> GetUserByIdAsync(Guid userId)
        => await _db.Users
            .Include(u => u.Organization)
            .FirstOrDefaultAsync(u => u.Id == userId);

    /// <summary>
    /// Delete the calling user's account:
    ///   1. Delete the user row (EF cascade nulls AuditLog.ActorId via DB constraint).
    ///   2. If they were the last member of their org, write an ORG_DELETED audit log
    ///      entry (before deletion so ActorId is still valid), then delete the org
    ///      (cascades assets, invites).
    ///   3. Delete the Firebase Auth record (best-effort).
    /// </summary>
    public async Task DeleteAccountAsync(Guid userId)
    {
        var user = await _db.Users.FirstOrDefaultAsync(u => u.Id == userId);
        if (user == null)
        {
            // User is already gone from the DB (e.g. deleted via another client).
            // Nothing to delete — return cleanly so the caller can sign out the cookie.
            return;
        }

        var remainingMembers = await _db.Users.CountAsync(u => u.OrganizationId == user.OrganizationId);

        if (remainingMembers == 1)
        {
            // Fetch org details before deletion for the audit log.
            var org = await _db.Organizations.FindAsync(user.OrganizationId);

            // Write the audit log before removing rows so ActorId FK is still valid.
            await _audit.LogAsync(
                action: "ORG_DELETED",
                actorId: userId,
                assetId: null,
                before: new
                {
                    organizationId = user.OrganizationId.ToString(),
                    organizationName = org?.Name,
                    actorEmail = user.Email,
                },
                after: (object?)null,
                organizationId: user.OrganizationId.ToString());

            _db.Users.Remove(user);

            if (org != null) _db.Organizations.Remove(org);
        }
        else
        {
            _db.Users.Remove(user);
        }

        await _db.SaveChangesAsync();

        // Remove Firebase Auth record — best-effort; do not let a Firebase failure
        // surface as a 500 after the DB deletion has already committed.
        try
        {
            await _firebase.DeleteUserAsync(user.FirebaseUid);
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"[DeleteAccount] Failed to delete Firebase user {user.FirebaseUid}: {ex.Message}");
        }
    }

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
