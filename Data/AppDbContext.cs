using System.Text.Json;
using Microsoft.AspNetCore.DataProtection.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;
using NexusCoreDotNet.Data.Entities;
using NexusCoreDotNet.Enums;

namespace NexusCoreDotNet.Data;

public class AppDbContext : DbContext, IDataProtectionKeyContext
{
    public AppDbContext(DbContextOptions<AppDbContext> options) : base(options) { }

    public DbSet<Organization> Organizations => Set<Organization>();
    public DbSet<User> Users => Set<User>();
    public DbSet<Asset> Assets => Set<Asset>();
    public DbSet<AuditLog> AuditLogs => Set<AuditLog>();
    public DbSet<Invite> Invites => Set<Invite>();
    public DbSet<KafkaEvent> KafkaEvents => Set<KafkaEvent>();
    public DbSet<DataProtectionKey> DataProtectionKeys => Set<DataProtectionKey>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        // ── Organization ──────────────────────────────────────────────────────
        modelBuilder.Entity<Organization>(e =>
        {
            e.ToTable("organizations");
            e.HasKey(o => o.Id);
            // IDs are stored as TEXT by Prisma (@id @default(uuid()) → text column).
            // HasConversion<string>() tells EF/Npgsql to treat the Guid as a string.
            e.Property(o => o.Id).HasColumnName("id").HasConversion<string>();
            e.Property(o => o.Name).HasColumnName("name").IsRequired();
            e.Property(o => o.Slug).HasColumnName("slug").IsRequired();
            // Prisma creates native PG enum types. MapEnum in Program.cs registers
            // Npgsql's type handler. HasColumnType (quoted, case-sensitive) tells EF
            // the column type for DDL/parameter binding. Do NOT add HasConversion<string>()
            // here — it would override MapEnum and send plain text, breaking writes.
            e.Property(o => o.Status)
                .HasColumnName("status")
                .HasColumnType("\"OrgStatus\"")
                .IsRequired();
            e.Property(o => o.CreatedAt).HasColumnName("createdAt");
            e.Property(o => o.UpdatedAt).HasColumnName("updatedAt");
            e.HasIndex(o => o.Slug).IsUnique();
        });

        // ── User ──────────────────────────────────────────────────────────────
        modelBuilder.Entity<User>(e =>
        {
            e.ToTable("users");
            e.HasKey(u => u.Id);
            e.Property(u => u.Id).HasColumnName("id").HasConversion<string>();
            e.Property(u => u.FirebaseUid).HasColumnName("firebaseUid").IsRequired();
            e.Property(u => u.Email).HasColumnName("email").IsRequired();
            e.Property(u => u.DisplayName).HasColumnName("displayName");
            e.Property(u => u.Role)
                .HasColumnName("role")
                .HasColumnType("\"Role\"")
                .IsRequired();
            e.Property(u => u.OrganizationId).HasColumnName("organizationId").HasConversion<string>();
            e.Property(u => u.CreatedAt).HasColumnName("createdAt");
            e.Property(u => u.UpdatedAt).HasColumnName("updatedAt");
            e.HasIndex(u => u.FirebaseUid).IsUnique();
            e.HasIndex(u => u.Email).IsUnique();
            e.HasIndex(u => u.OrganizationId);
            e.HasOne(u => u.Organization)
                .WithMany(o => o.Users)
                .HasForeignKey(u => u.OrganizationId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        // ── Asset ─────────────────────────────────────────────────────────────
        modelBuilder.Entity<Asset>(e =>
        {
            e.ToTable("assets");
            e.HasKey(a => a.Id);
            e.Property(a => a.Id).HasColumnName("id").HasConversion<string>();
            e.Property(a => a.Name).HasColumnName("name").IsRequired();
            e.Property(a => a.SKU).HasColumnName("sku").IsRequired();
            e.Property(a => a.Description).HasColumnName("description");
            e.Property(a => a.Status)
                .HasColumnName("status")
                .HasColumnType("\"AssetStatus\"")
                .IsRequired();
            e.Property(a => a.AssignedTo).HasColumnName("assignedTo");
            e.Property(a => a.OrganizationId).HasColumnName("organizationId").HasConversion<string>();
            e.Property(a => a.CreatedAt).HasColumnName("createdAt");
            e.Property(a => a.UpdatedAt).HasColumnName("updatedAt");
            e.HasIndex(a => a.SKU).IsUnique();
            e.HasIndex(a => a.OrganizationId);
            e.HasOne(a => a.Organization)
                .WithMany(o => o.Assets)
                .HasForeignKey(a => a.OrganizationId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        // ── AuditLog ──────────────────────────────────────────────────────────
        // Explicit converters for nullable Guid columns — HasConversion<string>() on a
        // nullable Guid can fail to map null → SQL NULL, causing audit writes to throw.
        var nullableGuidConverter = new ValueConverter<Guid?, string?>(
            v => v == null ? null : v.Value.ToString(),
            v => v == null ? null : Guid.Parse(v));

        modelBuilder.Entity<AuditLog>(e =>
        {
            e.ToTable("audit_logs");
            e.HasKey(al => al.Id);
            e.Property(al => al.Id).HasColumnName("id").HasConversion<string>();
            e.Property(al => al.Action).HasColumnName("action").IsRequired();
            e.Property(al => al.ActorId).HasColumnName("actorId").HasConversion(nullableGuidConverter);
            e.Property(al => al.AssetId).HasColumnName("assetId").HasConversion(nullableGuidConverter);
            // Json columns stored as jsonb — use HasColumnType("jsonb") plus a value
            // converter that round-trips through the raw JSON string.
            e.Property(al => al.Changes)
                .HasColumnName("changes")
                .HasColumnType("jsonb")
                .HasConversion(
                    v => v.RootElement.GetRawText(),
                    v => JsonDocument.Parse(v, (JsonDocumentOptions)default));
            e.Property(al => al.Timestamp).HasColumnName("timestamp");
            e.HasIndex(al => al.ActorId);
            e.HasIndex(al => al.AssetId);
            e.HasIndex(al => al.Timestamp);
            e.HasOne(al => al.Actor)
                .WithMany(u => u.AuditLogs)
                .HasForeignKey(al => al.ActorId)
                .OnDelete(DeleteBehavior.SetNull);
            e.HasOne(al => al.Asset)
                .WithMany(a => a.AuditLogs)
                .HasForeignKey(al => al.AssetId)
                .OnDelete(DeleteBehavior.SetNull);
        });

        // ── Invite ────────────────────────────────────────────────────────────
        modelBuilder.Entity<Invite>(e =>
        {
            e.ToTable("invites");
            e.HasKey(i => i.Id);
            e.Property(i => i.Id).HasColumnName("id").HasConversion<string>();
            e.Property(i => i.Email).HasColumnName("email").IsRequired();
            e.Property(i => i.Role)
                .HasColumnName("role")
                .HasColumnType("\"Role\"")
                .IsRequired();
            e.Property(i => i.OrganizationId).HasColumnName("organizationId").HasConversion<string>();
            e.Property(i => i.Token).HasColumnName("token").IsRequired();
            e.Property(i => i.ExpiresAt).HasColumnName("expiresAt");
            e.Property(i => i.AcceptedAt).HasColumnName("acceptedAt");
            e.Property(i => i.CreatedAt).HasColumnName("createdAt");
            e.HasIndex(i => i.Token).IsUnique();
            e.HasIndex(i => i.OrganizationId);
            e.HasOne(i => i.Organization)
                .WithMany(o => o.Invites)
                .HasForeignKey(i => i.OrganizationId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        // ── KafkaEvent ────────────────────────────────────────────────────────
        // Read-only table written by the kafka-consumer microservice.
        // All IDs are stored as text (Prisma convention). OrganizationId is Guid
        // in .NET but text in the DB — requires HasConversion<string>().
        // AssetId and ActorId are also text in the DB (stored as string here).
        modelBuilder.Entity<KafkaEvent>(e =>
        {
            e.ToTable("kafka_events");
            e.HasKey(k => k.Id);
            e.Property(k => k.Id).HasColumnName("id").HasConversion<string>();
            e.Property(k => k.OrganizationId).HasColumnName("organizationId").HasConversion<string>();
            e.Property(k => k.AssetId).HasColumnName("assetId").IsRequired();
            e.Property(k => k.AssetName).HasColumnName("assetName").IsRequired();
            e.Property(k => k.PreviousStatus).HasColumnName("previousStatus").IsRequired();
            e.Property(k => k.NewStatus).HasColumnName("newStatus").IsRequired();
            e.Property(k => k.ActorId).HasColumnName("actorId").IsRequired();
            e.Property(k => k.OccurredAt).HasColumnName("occurredAt");
            e.Property(k => k.CreatedAt).HasColumnName("createdAt");
            e.HasIndex(k => k.OrganizationId);
            e.HasIndex(k => new { k.OrganizationId, k.OccurredAt });
        });
    }
}
