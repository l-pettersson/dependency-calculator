using Microsoft.EntityFrameworkCore;

namespace DependencyCalculator.Data;

/// <summary>
/// Database context for NPM package cache
/// </summary>
public class CveCacheDbContext : DbContext
{
    public DbSet<CveCacheEntry> CacheEntries { get; set; }

    public CveCacheDbContext(DbContextOptions<CveCacheDbContext> options)
        : base(options)
    {
    }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        modelBuilder.Entity<CveCacheEntry>(entity =>
        {
            entity.HasKey(e => new { e.PackageName, e.Version });
            entity.HasIndex(e => e.LastUpdated);
        });
    }
}
