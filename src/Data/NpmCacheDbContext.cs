using Microsoft.EntityFrameworkCore;

namespace DependencyCalculator.Data;

/// <summary>
/// Database context for NPM package cache
/// </summary>
public class NpmCacheDbContext : DbContext
{
    public DbSet<NpmCacheEntry> CacheEntries { get; set; }

    public NpmCacheDbContext(DbContextOptions<NpmCacheDbContext> options)
        : base(options)
    {
    }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        modelBuilder.Entity<NpmCacheEntry>(entity =>
        {
            entity.HasKey(e => new { e.PackageName, e.Version });
            entity.HasIndex(e => e.LastUpdated);
        });
    }
}
