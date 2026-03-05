using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using NexusCoreDotNet.Data;
using NexusCoreDotNet.Data.Entities;
using NexusCoreDotNet.Enums;
using NexusCoreDotNet.Services;
using NSubstitute;

namespace NexusCoreDotNet.Tests;

public class UserServiceTests : IDisposable
{
    private readonly AppDbContext _db;
    private readonly EmailService _email;
    private readonly UserService _sut;
    private readonly Guid _orgId = Guid.NewGuid();
    private readonly Guid _actorId = Guid.NewGuid();

    public UserServiceTests()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        _db = new AppDbContext(options);

        _email = Substitute.For<EmailService>(
            Substitute.For<IConfiguration>(),
            Substitute.For<Microsoft.Extensions.Logging.ILogger<EmailService>>(),
            Substitute.For<System.Net.Http.IHttpClientFactory>());

        var audit = new AuditService(_db);
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["App:FrontendUrl"] = "http://localhost:5000"
            })
            .Build();

        _sut = new UserService(_db, _email, audit, config);

        var org = new Organization { Id = _orgId, Name = "Test Org", Slug = "test-org" };
        _db.Organizations.Add(org);
        _db.Users.Add(new User
        {
            Id = _actorId,
            FirebaseUid = "uid-actor",
            Email = "actor@test.com",
            Role = Role.ORG_MANAGER,
            OrganizationId = _orgId
        });
        _db.SaveChanges();
    }

    public void Dispose() => _db.Dispose();

    // ── FindAllAsync ──────────────────────────────────────────────────────────

    [Fact]
    public async Task FindAll_ReturnsMembersScopedToOrg()
    {
        _db.Users.Add(new User
        {
            FirebaseUid = "uid-m2",
            Email = "m2@test.com",
            OrganizationId = _orgId
        });
        _db.Users.Add(new User
        {
            FirebaseUid = "uid-other",
            Email = "other@test.com",
            OrganizationId = Guid.NewGuid()
        });
        _db.SaveChanges();

        var users = await _sut.FindAllAsync(_orgId);

        Assert.Equal(2, users.Count);
        Assert.All(users, u => Assert.Equal(_orgId, u.OrganizationId));
    }

    // ── CreateInviteAsync ─────────────────────────────────────────────────────

    [Fact]
    public async Task CreateInvite_HappyPath_CreatesInviteWithSevenDayExpiry()
    {
        var before = DateTime.UtcNow;
        var invite = await _sut.CreateInviteAsync("new@test.com", Role.VIEWER, _orgId, _actorId);

        Assert.Equal("new@test.com", invite.Email);
        Assert.Equal(Role.VIEWER, invite.Role);
        Assert.True(invite.ExpiresAt >= before.AddDays(7).AddSeconds(-5));
        Assert.True(invite.ExpiresAt <= DateTime.UtcNow.AddDays(7).AddSeconds(5));
    }

    [Fact]
    public async Task CreateInvite_ThrowsWhenUserAlreadyExists()
    {
        _db.Users.Add(new User
        {
            FirebaseUid = "uid-existing",
            Email = "existing@test.com",
            OrganizationId = _orgId
        });
        _db.SaveChanges();

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => _sut.CreateInviteAsync("existing@test.com", Role.VIEWER, _orgId, _actorId));
    }

    [Fact]
    public async Task CreateInvite_ThrowsWhenActivePendingInviteExists()
    {
        _db.Invites.Add(new Invite
        {
            Email = "pending@test.com",
            Role = Role.VIEWER,
            OrganizationId = _orgId,
            Token = Guid.NewGuid().ToString(),
            ExpiresAt = DateTime.UtcNow.AddDays(7)
        });
        _db.SaveChanges();

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => _sut.CreateInviteAsync("pending@test.com", Role.VIEWER, _orgId, _actorId));
    }

    // ── ListInvitesAsync ──────────────────────────────────────────────────────

    [Fact]
    public async Task ListInvites_ReturnsInvitesScopedToOrg()
    {
        _db.Invites.Add(new Invite
        {
            Email = "a@test.com",
            Role = Role.VIEWER,
            OrganizationId = _orgId,
            Token = "tok-a",
            ExpiresAt = DateTime.UtcNow.AddDays(7)
        });
        _db.Invites.Add(new Invite
        {
            Email = "b@test.com",
            Role = Role.VIEWER,
            OrganizationId = Guid.NewGuid(),
            Token = "tok-b",
            ExpiresAt = DateTime.UtcNow.AddDays(7)
        });
        _db.SaveChanges();

        var invites = await _sut.ListInvitesAsync(_orgId);

        Assert.Single(invites);
        Assert.Equal("a@test.com", invites[0].Email);
    }

    // ── DeleteInviteAsync ─────────────────────────────────────────────────────

    [Fact]
    public async Task DeleteInvite_RemovesInvite()
    {
        var invite = new Invite
        {
            Email = "del@test.com",
            Role = Role.VIEWER,
            OrganizationId = _orgId,
            Token = "del-tok",
            ExpiresAt = DateTime.UtcNow.AddDays(7)
        };
        _db.Invites.Add(invite);
        _db.SaveChanges();

        await _sut.DeleteInviteAsync(invite.Id, _orgId);

        Assert.Null(await _db.Invites.FindAsync(invite.Id));
    }

    [Fact]
    public async Task DeleteInvite_ThrowsWhenNotFound()
    {
        await Assert.ThrowsAsync<KeyNotFoundException>(
            () => _sut.DeleteInviteAsync(Guid.NewGuid(), _orgId));
    }

    [Fact]
    public async Task DeleteInvite_ThrowsWhenAlreadyAccepted()
    {
        var invite = new Invite
        {
            Email = "acc@test.com",
            Role = Role.VIEWER,
            OrganizationId = _orgId,
            Token = "acc-tok",
            ExpiresAt = DateTime.UtcNow.AddDays(7),
            AcceptedAt = DateTime.UtcNow.AddHours(-1)
        };
        _db.Invites.Add(invite);
        _db.SaveChanges();

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => _sut.DeleteInviteAsync(invite.Id, _orgId));
    }

    // ── UpdateRoleAsync ───────────────────────────────────────────────────────

    [Fact]
    public async Task UpdateRole_ChangesRole()
    {
        var member = new User
        {
            FirebaseUid = "uid-role",
            Email = "role@test.com",
            Role = Role.VIEWER,
            OrganizationId = _orgId
        };
        _db.Users.Add(member);
        _db.SaveChanges();

        var updated = await _sut.UpdateRoleAsync(member.Id, Role.ASSET_MANAGER, _orgId, _actorId);

        Assert.Equal(Role.ASSET_MANAGER, updated.Role);
    }

    [Fact]
    public async Task UpdateRole_ThrowsWhenAssigningSuperadmin()
    {
        var member = new User
        {
            FirebaseUid = "uid-sa",
            Email = "sa@test.com",
            Role = Role.VIEWER,
            OrganizationId = _orgId
        };
        _db.Users.Add(member);
        _db.SaveChanges();

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => _sut.UpdateRoleAsync(member.Id, Role.SUPERADMIN, _orgId, _actorId));
    }

    [Fact]
    public async Task UpdateRole_ThrowsWhenNotFound()
    {
        await Assert.ThrowsAsync<KeyNotFoundException>(
            () => _sut.UpdateRoleAsync(Guid.NewGuid(), Role.VIEWER, _orgId, _actorId));
    }

    // ── RemoveMemberAsync ─────────────────────────────────────────────────────

    [Fact]
    public async Task RemoveMember_RemovesUser()
    {
        var member = new User
        {
            FirebaseUid = "uid-rem",
            Email = "rem@test.com",
            Role = Role.VIEWER,
            OrganizationId = _orgId
        };
        _db.Users.Add(member);
        _db.SaveChanges();

        await _sut.RemoveMemberAsync(member.Id, _actorId, _orgId);

        Assert.Null(await _db.Users.FindAsync(member.Id));
    }

    [Fact]
    public async Task RemoveMember_ThrowsOnSelfRemoval()
    {
        await Assert.ThrowsAsync<InvalidOperationException>(
            () => _sut.RemoveMemberAsync(_actorId, _actorId, _orgId));
    }

    [Fact]
    public async Task RemoveMember_ThrowsWhenNotFound()
    {
        await Assert.ThrowsAsync<KeyNotFoundException>(
            () => _sut.RemoveMemberAsync(Guid.NewGuid(), _actorId, _orgId));
    }

    [Fact]
    public async Task RemoveMember_ThrowsWhenRemovingSuperadmin()
    {
        var superadmin = new User
        {
            FirebaseUid = "uid-super",
            Email = "super@test.com",
            Role = Role.SUPERADMIN,
            OrganizationId = _orgId
        };
        _db.Users.Add(superadmin);
        _db.SaveChanges();

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => _sut.RemoveMemberAsync(superadmin.Id, _actorId, _orgId));
    }
}
