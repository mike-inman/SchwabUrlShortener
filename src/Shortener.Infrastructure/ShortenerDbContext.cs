using Microsoft.EntityFrameworkCore;
using Shortener.Domain;

namespace Shortener.Infrastructure;

public sealed class ShortenerDbContext(DbContextOptions<ShortenerDbContext> options) : DbContext(options)
{
    public DbSet<UrlMapping> UrlMappings => Set<UrlMapping>();

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
            entity.Property(x => x.CreatedAtUtc).HasColumnName("created_at_utc");
            entity.Property(x => x.ExpiresAtUtc).HasColumnName("expires_at_utc");
            entity.Property(x => x.LastAccessedAtUtc).HasColumnName("last_accessed_at_utc");
            entity.Property(x => x.HitCount).HasColumnName("hit_count");
            entity.HasIndex(x => x.ShortCode).IsUnique().HasDatabaseName("ux_url_mappings_short_code");
            entity.HasIndex(x => new { x.TenantId, x.CreatedAtUtc }).HasDatabaseName("ix_url_mappings_tenant_created");
            entity.HasIndex(x => new { x.TenantId, x.OriginalUrl }).HasDatabaseName("ix_url_mappings_tenant_original");
        });
    }
}
