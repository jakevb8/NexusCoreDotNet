using Microsoft.EntityFrameworkCore;
using NexusCoreDotNet.Data;
using NexusCoreDotNet.Data.Entities;
using NexusCoreDotNet.Enums;
using NexusCoreDotNet.Services;
using NSubstitute;
using NSubstitute.ExceptionExtensions;
using System.Collections.Generic;

namespace NexusCoreDotNet.Tests;

public class AuthServiceTests : IDisposable
{
    private readonly AppDbContext _db;
    private readonly IFirebaseAuthService _firebase;
    private readonly AuthService _sut;

    public AuthServiceTests()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        _db = new AppDbContext(options);
        _firebase = Substitute.For<IFirebaseAuthService>();
        _sut = new AuthService(_db, _firebase);
    }

    public void Dispose() => _db.Dispose();

    // ── helpers ──────────────────────────────────────────────────────────────

    private static DecodedToken MakeToken(string uid, string email)
        => new(uid, new Dictionary<string, object> { ["email"] = email });

    private void SeedActiveOrgs(int count, bool approvedToday = false)
    {
        var date = approvedToday ? DateTime.UtcNow : DateTime.UtcNow.AddDays(-2);
        for (int i = 0; i < count; i++)
        {
            _db.Organizations.Add(new Organization
            {
                Name = $"Org{i}",
                Slug = $"org-{i}-{Guid.NewGuid()}",
                Status = OrgStatus.ACTIVE,
                UpdatedAt = date
            });
        }
        _db.SaveChanges();
    }

    // ── ShouldAutoApproveAsync ────────────────────────────────────────────────

    [Fact]
    public async Task ShouldAutoApprove_ReturnsTrueWhenUnderBothLimits()
    {
        SeedActiveOrgs(3, approvedToday: true);
        Assert.True(await _sut.ShouldAutoApproveAsync());
    }

    [Fact]
    public async Task ShouldAutoApprove_ReturnsFalseWhenDailyLimitReached()
    {
        SeedActiveOrgs(5, approvedToday: true); // 5 approved today
        Assert.False(await _sut.ShouldAutoApproveAsync());
    }

    [Fact]
    public async Task ShouldAutoApprove_ReturnsFalseWhenTotalLimitReached()
    {
        SeedActiveOrgs(50, approvedToday: false); // 50 total active, not today
        Assert.False(await _sut.ShouldAutoApproveAsync());
    }

    // ── RegisterNewOrganizationAsync ─────────────────────────────────────────
    // Token verification is now the caller's responsibility (Razor Pages use
    // FirebaseAuth.DefaultInstance; API controller uses IFirebaseAuthService).
    // AuthService.RegisterNewOrganizationAsync takes pre-decoded uid + email.

    [Fact]
    public async Task Register_CreatesOrgAndUser()
    {
        var user = await _sut.RegisterNewOrganizationAsync("uid1", "user@test.com", "Acme", "acme", "Alice");

        Assert.Equal("uid1", user.FirebaseUid);
        Assert.Equal("user@test.com", user.Email);
        Assert.Equal("Alice", user.DisplayName);
        Assert.Equal(Role.ORG_MANAGER, user.Role);
        var org = await _db.Organizations.FindAsync(user.OrganizationId);
        Assert.NotNull(org);
        Assert.Equal("Acme", org!.Name);
        Assert.Equal("acme", org.Slug);
    }

    [Fact]
    public async Task Register_AutoApprovesWhenUnderLimits()
    {
        var user = await _sut.RegisterNewOrganizationAsync("uid2", "user@test.com", "Beta", "beta", null);

        var org = await _db.Organizations.FindAsync(user.OrganizationId);
        Assert.Equal(OrgStatus.ACTIVE, org!.Status);
    }

    [Fact]
    public async Task Register_SetsPendingWhenDailyLimitReached()
    {
        SeedActiveOrgs(5, approvedToday: true);

        var user = await _sut.RegisterNewOrganizationAsync("uid3", "user@test.com", "Gamma", "gamma", null);

        var org = await _db.Organizations.FindAsync(user.OrganizationId);
        Assert.Equal(OrgStatus.PENDING, org!.Status);
    }

    [Fact]
    public async Task Register_SetsPendingWhenTotalLimitReached()
    {
        SeedActiveOrgs(50);

        var user = await _sut.RegisterNewOrganizationAsync("uid4", "user@test.com", "Delta", "delta", null);

        var org = await _db.Organizations.FindAsync(user.OrganizationId);
        Assert.Equal(OrgStatus.PENDING, org!.Status);
    }

    [Fact]
    public async Task Register_ThrowsOnDuplicateEmail()
    {
        var org = new Organization { Name = "Existing", Slug = "existing" };
        _db.Organizations.Add(org);
        _db.Users.Add(new User
        {
            FirebaseUid = "uid-original",
            Email = "dup@test.com",
            OrganizationId = org.Id
        });
        _db.SaveChanges();

        // Same email, different UID (e.g. different Firebase project / client)
        await Assert.ThrowsAsync<InvalidOperationException>(
            () => _sut.RegisterNewOrganizationAsync("uid-different-client", "dup@test.com", "NewOrg", "new-org", null));
    }

    [Fact]
    public async Task Register_ThrowsOnDuplicateSlug()
    {
        _db.Organizations.Add(new Organization { Name = "Taken", Slug = "taken" });
        _db.SaveChanges();

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => _sut.RegisterNewOrganizationAsync("uid-new", "new@test.com", "AnyName", "taken", null));
    }

    // ── AcceptInviteAsync ─────────────────────────────────────────────────────
    // Token verification is the caller's responsibility.
    // AcceptInviteAsync now takes pre-decoded (uid, email, displayName, token).

    [Fact]
    public async Task AcceptInvite_CreatesUserAndMarksAccepted()
    {
        var org = new Organization { Name = "TestOrg", Slug = "testorg" };
        _db.Organizations.Add(org);
        var invite = new Invite
        {
            Email = "inv@test.com",
            Role = Role.ASSET_MANAGER,
            OrganizationId = org.Id,
            Token = "valid-token",
            ExpiresAt = DateTime.UtcNow.AddDays(7)
        };
        _db.Invites.Add(invite);
        _db.SaveChanges();

        var user = await _sut.AcceptInviteAsync("uid-inv", "inv@test.com", "Invitee", "valid-token");

        Assert.Equal("uid-inv", user.FirebaseUid);
        Assert.Equal(Role.ASSET_MANAGER, user.Role);
        Assert.Equal(org.Id, user.OrganizationId);

        var updated = await _db.Invites.FindAsync(invite.Id);
        Assert.NotNull(updated!.AcceptedAt);
    }

    [Fact]
    public async Task AcceptInvite_ThrowsOnInvalidToken()
    {
        await Assert.ThrowsAsync<KeyNotFoundException>(
            () => _sut.AcceptInviteAsync("uid-x", "x@test.com", null, "no-such-token"));
    }

    [Fact]
    public async Task AcceptInvite_ThrowsOnAlreadyUsedInvite()
    {
        var org = new Organization { Name = "Org", Slug = "org" };
        _db.Organizations.Add(org);
        var invite = new Invite
        {
            Email = "used@test.com",
            Role = Role.VIEWER,
            OrganizationId = org.Id,
            Token = "used-token",
            ExpiresAt = DateTime.UtcNow.AddDays(7),
            AcceptedAt = DateTime.UtcNow.AddHours(-1)
        };
        _db.Invites.Add(invite);
        _db.SaveChanges();

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => _sut.AcceptInviteAsync("uid-u", "used@test.com", null, "used-token"));
    }

    [Fact]
    public async Task AcceptInvite_ThrowsOnExpiredInvite()
    {
        var org = new Organization { Name = "Org", Slug = "org-exp" };
        _db.Organizations.Add(org);
        var invite = new Invite
        {
            Email = "exp@test.com",
            Role = Role.VIEWER,
            OrganizationId = org.Id,
            Token = "expired-token",
            ExpiresAt = DateTime.UtcNow.AddDays(-1)
        };
        _db.Invites.Add(invite);
        _db.SaveChanges();

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => _sut.AcceptInviteAsync("uid-e", "exp@test.com", null, "expired-token"));
    }

    [Fact]
    public async Task AcceptInvite_ThrowsOnEmailMismatch()
    {
        var org = new Organization { Name = "Org", Slug = "org-mm" };
        _db.Organizations.Add(org);
        var invite = new Invite
        {
            Email = "invited@test.com",
            Role = Role.VIEWER,
            OrganizationId = org.Id,
            Token = "mm-token",
            ExpiresAt = DateTime.UtcNow.AddDays(7)
        };
        _db.Invites.Add(invite);
        _db.SaveChanges();

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => _sut.AcceptInviteAsync("uid-mm", "different@test.com", null, "mm-token"));
    }

    [Fact]
    public async Task AcceptInvite_ThrowsWhenUserAlreadyRegistered()
    {
        var org = new Organization { Name = "Org", Slug = "org-dup" };
        _db.Organizations.Add(org);
        _db.Users.Add(new User
        {
            FirebaseUid = "uid-existing",
            Email = "exists@test.com",
            OrganizationId = org.Id
        });
        var invite = new Invite
        {
            Email = "exists@test.com",
            Role = Role.VIEWER,
            OrganizationId = org.Id,
            Token = "dup-token",
            ExpiresAt = DateTime.UtcNow.AddDays(7)
        };
        _db.Invites.Add(invite);
        _db.SaveChanges();

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => _sut.AcceptInviteAsync("uid-existing", "exists@test.com", null, "dup-token"));
    }

    // ── GetUserByFirebaseUidAsync ─────────────────────────────────────────────

    [Fact]
    public async Task GetUserByFirebaseUid_ReturnsUserWithOrg()
    {
        var org = new Organization { Name = "Org", Slug = "org-get" };
        _db.Organizations.Add(org);
        _db.Users.Add(new User
        {
            FirebaseUid = "uid-get",
            Email = "get@test.com",
            OrganizationId = org.Id
        });
        _db.SaveChanges();

        var user = await _sut.GetUserByFirebaseUidAsync("uid-get");

        Assert.NotNull(user);
        Assert.NotNull(user!.Organization);
        Assert.Equal("Org", user.Organization.Name);
    }

    [Fact]
    public async Task GetUserByFirebaseUid_ReturnsNullWhenNotFound()
    {
        var user = await _sut.GetUserByFirebaseUidAsync("not-a-uid");
        Assert.Null(user);
    }

    // ── GetOrMigrateUserAsync ─────────────────────────────────────────────────

    [Fact]
    public async Task GetOrMigrateUser_ReturnsByUidOnFastPath()
    {
        var org = new Organization { Name = "Org", Slug = "org-migrate-fast" };
        _db.Organizations.Add(org);
        _db.Users.Add(new User { FirebaseUid = "uid-fast", Email = "fast@test.com", OrganizationId = org.Id });
        _db.SaveChanges();

        var user = await _sut.GetOrMigrateUserAsync("uid-fast", "fast@test.com");

        Assert.NotNull(user);
        Assert.Equal("uid-fast", user!.FirebaseUid);
    }

    [Fact]
    public async Task GetOrMigrateUser_FallsBackToEmailAndMigratesUid()
    {
        var org = new Organization { Name = "Org", Slug = "org-migrate-email" };
        _db.Organizations.Add(org);
        _db.Users.Add(new User { FirebaseUid = "uid-old", Email = "migrate@test.com", OrganizationId = org.Id });
        _db.SaveChanges();

        // New UID from a different Firebase project / client
        var user = await _sut.GetOrMigrateUserAsync("uid-new-client", "migrate@test.com");

        Assert.NotNull(user);
        Assert.Equal("uid-new-client", user!.FirebaseUid); // UID updated in DB
    }

    [Fact]
    public async Task GetOrMigrateUser_ReturnsNullWhenNeitherUidNorEmailFound()
    {
        var user = await _sut.GetOrMigrateUserAsync("uid-unknown", "nobody@test.com");
        Assert.Null(user);
    }

    // ── VerifyTokenAsync ──────────────────────────────────────────────────────
    // This method delegates to IFirebaseAuthService (used by REST API for rms tokens).

    [Fact]
    public async Task VerifyTokenAsync_DelegatesToFirebaseAuthService()
    {
        _firebase.VerifyIdTokenAsync("tok").Returns(MakeToken("uid1", "user@test.com"));

        var result = await _sut.VerifyTokenAsync("tok");

        Assert.Equal("uid1", result.Uid);
        Assert.Equal("user@test.com", result.Claims["email"].ToString());
    }

    [Fact]
    public async Task VerifyTokenAsync_ThrowsOnBadToken()
    {
        _firebase.VerifyIdTokenAsync("bad").Throws(new UnauthorizedAccessException("invalid"));

        await Assert.ThrowsAsync<UnauthorizedAccessException>(
            () => _sut.VerifyTokenAsync("bad"));
    }
}
