using Microsoft.EntityFrameworkCore;
using Shortener.Domain;

namespace Shortener.Infrastructure;

public sealed class ShortenerDbContext(DbContextOptions<ShortenerDbContext> options) : DbContext(options)
{
    public DbSet<UrlMapping> UrlMappings => Set<UrlMapping>();
    public DbSet<ApplicationUser> ApplicationUsers => Set<ApplicationUser>();
    public DbSet<ExternalIdentity> ExternalIdentities => Set<ExternalIdentity>();
    public DbSet<Workspace> Workspaces => Set<Workspace>();
    public DbSet<WorkspaceMembership> WorkspaceMemberships => Set<WorkspaceMembership>();
    public DbSet<WorkspaceInvitation> WorkspaceInvitations => Set<WorkspaceInvitation>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.HasPostgresExtension("citext");
        modelBuilder.Entity<UrlMapping>(entity =>
        {
            entity.ToTable("url_mappings", table => table.HasCheckConstraint(
                "ck_url_mappings_link_mode",
                "(mode = 0 AND tenant_id IS NULL AND expires_at_utc IS NOT NULL AND expires_at_utc = created_at_utc + INTERVAL '168 hours' AND last_accessed_at_utc IS NULL AND hit_count = 0) OR " +
                "(mode = 1 AND tenant_id IS NOT NULL AND tenant_id <> '00000000-0000-0000-0000-000000000000'::uuid AND expires_at_utc IS NULL AND hit_count >= 0)"));
            entity.HasKey(x => x.Id);
            entity.Property(x => x.Id).HasColumnName("id").ValueGeneratedNever();
            entity.Property(x => x.ShortCode).HasColumnName("short_code").HasColumnType("citext").HasMaxLength(12).IsRequired();
            entity.Property(x => x.OriginalUrl).HasColumnName("original_url").IsRequired();
            entity.Property(x => x.Mode).HasColumnName("mode").HasConversion<short>();
            entity.Property(x => x.TenantId).HasColumnName("tenant_id");
            entity.Property(x => x.OwnerWorkspaceId).HasColumnName("owner_workspace_id");
            entity.HasOne<Workspace>().WithMany().HasForeignKey(x => x.OwnerWorkspaceId).OnDelete(DeleteBehavior.Restrict);
            entity.HasIndex(x => x.OwnerWorkspaceId).HasDatabaseName("ix_url_mappings_owner_workspace");
            entity.Property(x => x.CreatedAtUtc).HasColumnName("created_at_utc");
            entity.Property(x => x.ExpiresAtUtc).HasColumnName("expires_at_utc");
            entity.Property(x => x.LastAccessedAtUtc).HasColumnName("last_accessed_at_utc");
            entity.Property(x => x.HitCount).HasColumnName("hit_count");
            entity.HasIndex(x => x.ShortCode).IsUnique().HasDatabaseName("ux_url_mappings_short_code");
            entity.HasIndex(x => new { x.TenantId, x.CreatedAtUtc }).HasDatabaseName("ix_url_mappings_tenant_created");
            entity.HasIndex(x => new { x.TenantId, x.OriginalUrl }).HasDatabaseName("ix_url_mappings_tenant_original");
        });

        modelBuilder.Entity<ApplicationUser>(entity =>
        {
            entity.ToTable("application_users");
            entity.HasKey(x => x.Id);
            entity.Property(x => x.Id).HasColumnName("id").ValueGeneratedNever();
            entity.Property(x => x.CreatedAtUtc).HasColumnName("created_at_utc");
            entity.Property(x => x.DisplayName).HasColumnName("display_name").HasMaxLength(200);
            entity.Property(x => x.ContactEmail).HasColumnName("contact_email").HasMaxLength(320);
        });
        modelBuilder.Entity<ExternalIdentity>(entity =>
        {
            entity.ToTable("external_identities", table => table.HasCheckConstraint("ck_external_identity_key", "length(trim(issuer)) > 0 AND length(trim(subject)) > 0"));
            entity.HasKey(x => x.Id);
            entity.Property(x => x.Id).HasColumnName("id").ValueGeneratedNever();
            entity.Property(x => x.UserId).HasColumnName("user_id");
            entity.Property(x => x.Issuer).HasColumnName("issuer").HasMaxLength(500).IsRequired();
            entity.Property(x => x.Subject).HasColumnName("subject").HasMaxLength(500).IsRequired();
            entity.Property(x => x.CreatedAtUtc).HasColumnName("created_at_utc");
            entity.HasOne<ApplicationUser>().WithMany().HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.Restrict);
            entity.HasIndex(x => new { x.Issuer, x.Subject }).IsUnique().HasDatabaseName("ux_external_identities_issuer_subject");
        });
        modelBuilder.Entity<Workspace>(entity =>
        {
            entity.ToTable("workspaces", table => table.HasCheckConstraint("ck_workspaces_kind_owner", "(kind = 0 AND personal_owner_user_id IS NOT NULL AND legacy_tenant_id IS NULL) OR (kind = 1 AND personal_owner_user_id IS NULL)"));
            entity.HasKey(x => x.Id);
            entity.Property(x => x.Id).HasColumnName("id").ValueGeneratedNever();
            entity.Property(x => x.Kind).HasColumnName("kind").HasConversion<short>();
            entity.Property(x => x.PersonalOwnerUserId).HasColumnName("personal_owner_user_id");
            entity.Property(x => x.LegacyTenantId).HasColumnName("legacy_tenant_id");
            entity.Property(x => x.Name).HasColumnName("name").HasMaxLength(200).IsRequired();
            entity.Property(x => x.CreatedAtUtc).HasColumnName("created_at_utc");
            entity.HasOne<ApplicationUser>().WithMany().HasForeignKey(x => x.PersonalOwnerUserId).OnDelete(DeleteBehavior.Restrict);
            entity.HasIndex(x => x.PersonalOwnerUserId).IsUnique().HasDatabaseName("ux_workspaces_personal_owner");
            entity.HasIndex(x => x.LegacyTenantId).IsUnique().HasDatabaseName("ux_workspaces_legacy_tenant");
        });
        modelBuilder.Entity<WorkspaceMembership>(entity =>
        {
            entity.ToTable("workspace_memberships", table => table.HasCheckConstraint("ck_workspace_memberships_role", "role IN (0, 1, 2)"));
            entity.HasKey(x => new { x.WorkspaceId, x.UserId });
            entity.Property(x => x.WorkspaceId).HasColumnName("workspace_id");
            entity.Property(x => x.UserId).HasColumnName("user_id");
            entity.Property(x => x.Role).HasColumnName("role").HasConversion<short>();
            entity.Property(x => x.CreatedAtUtc).HasColumnName("created_at_utc");
            entity.HasOne<Workspace>().WithMany().HasForeignKey(x => x.WorkspaceId).OnDelete(DeleteBehavior.Restrict);
            entity.HasOne<ApplicationUser>().WithMany().HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.Restrict);
            entity.HasIndex(x => x.WorkspaceId).IsUnique().HasFilter("role = 0").HasDatabaseName("ux_workspace_memberships_one_owner");
        });
        modelBuilder.Entity<WorkspaceInvitation>(entity =>
        {
            entity.ToTable("workspace_invitations", table =>
            {
                table.HasCheckConstraint("ck_workspace_invitations_expiry", "expires_at_utc > created_at_utc");
                table.HasCheckConstraint("ck_workspace_invitations_state", "(redeemed_at_utc IS NULL) = (redeemed_by_user_id IS NULL) AND NOT (redeemed_at_utc IS NOT NULL AND revoked_at_utc IS NOT NULL) AND (redeemed_at_utc IS NULL OR (redeemed_at_utc >= created_at_utc AND redeemed_at_utc <= expires_at_utc)) AND (revoked_at_utc IS NULL OR revoked_at_utc >= created_at_utc)");
                table.HasCheckConstraint("ck_workspace_invitations_verifier", "verifier_hash ~ '^[0-9a-f]{64}$'");
            });
            entity.HasKey(x => x.Id);
            entity.Property(x => x.Id).HasColumnName("id").ValueGeneratedNever();
            entity.Property(x => x.WorkspaceId).HasColumnName("workspace_id");
            entity.Property(x => x.VerifierHash).HasColumnName("verifier_hash").HasMaxLength(64).IsRequired();
            entity.Property(x => x.CreatedAtUtc).HasColumnName("created_at_utc");
            entity.Property(x => x.ExpiresAtUtc).HasColumnName("expires_at_utc");
            entity.Property(x => x.RedeemedAtUtc).HasColumnName("redeemed_at_utc");
            entity.Property(x => x.RedeemedByUserId).HasColumnName("redeemed_by_user_id");
            entity.Property(x => x.RevokedAtUtc).HasColumnName("revoked_at_utc");
            entity.HasOne<Workspace>().WithMany().HasForeignKey(x => x.WorkspaceId).OnDelete(DeleteBehavior.Restrict);
            entity.HasOne<ApplicationUser>().WithMany().HasForeignKey(x => x.RedeemedByUserId).OnDelete(DeleteBehavior.Restrict);
            entity.HasIndex(x => x.VerifierHash).IsUnique().HasDatabaseName("ux_workspace_invitations_verifier_hash");
        });
    }
}
