using System.Text.Json;
using DependencyCalculator.Data;
using DependencyCalculator.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;

namespace DependencyCalculator.Services;

/// <summary>
/// Service that provides dual-layer caching: in-memory (fast) and SQLite (persistent)
/// </summary>
public class CveCacheService : ICveCacheService
{
    private readonly IMemoryCache _memoryCache;
    private readonly CveCacheDbContext _dbContext;
    private readonly MemoryCacheEntryOptions _cacheOptions;
    private readonly SemaphoreSlim _dbLock = new SemaphoreSlim(1, 1);
    private readonly bool _useMemoryCache;

    public CveCacheService(IMemoryCache memoryCache, CveCacheDbContext dbContext, bool useMemoryCache = true)
    {
        _memoryCache = memoryCache ?? throw new ArgumentNullException(nameof(memoryCache));
        _dbContext = dbContext ?? throw new ArgumentNullException(nameof(dbContext));
        _useMemoryCache = useMemoryCache;

        // Configure cache options: cache for 1 hour with sliding expiration
        _cacheOptions = new MemoryCacheEntryOptions()
            .SetSlidingExpiration(TimeSpan.FromHours(1))
            .SetAbsoluteExpiration(TimeSpan.FromHours(24))
            .SetPriority(CacheItemPriority.Normal);
    }

    /// <summary>
    /// Loads all cached entries from SQLite into memory cache on startup
    /// </summary>
    public async Task LoadCacheFromDatabaseAsync()
    {
        if (!_useMemoryCache)
        {
            Console.WriteLine("Memory caching disabled - skipping cache preload");
            return;
        }

        Console.WriteLine("Loading package cache from SQLite database...");

        await _dbLock.WaitAsync();
        try
        {
            var entries = await _dbContext.CacheEntries.ToListAsync();

            foreach (var entry in entries)
            {
                var cacheKey = $"package_{entry.PackageName}@{entry.Version}";

                try
                {
                    // Deserialize the metadata and store in memory cache
                    var metadata = JsonSerializer.Deserialize<List<CveItem>>(entry.MetadataJson);
                    if (metadata != null)
                    {
                        _memoryCache.Set(cacheKey, metadata, _cacheOptions);
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Warning: Failed to deserialize cache entry for {entry.PackageName}@{entry.Version}: {ex.Message}");
                }
            }

            Console.WriteLine($"Loaded {entries.Count} NPM package versions from cache database");
        }
        finally
        {
            _dbLock.Release();
        }
    }

    /// <summary>
    /// Try to get a value from the cache (checks memory first, then database)
    /// </summary>
    public async Task<List<CveItem>?> TryGetValueAsync(string packageName, string version)
    {
        var cacheKey = $"npm_package_{packageName}@{version}";

        // First, try memory cache (fast) if enabled
        if (_useMemoryCache && _memoryCache.TryGetValue(cacheKey, out List<CveItem>? cachedData))
        {
            Console.WriteLine($"Retrieved from memory cache: {packageName}@{version}");
            return cachedData;
        }

        // If not in memory (or memory cache disabled), try database (persistent)
        await _dbLock.WaitAsync();
        try
        {
            var entry = await _dbContext.CacheEntries
                .FirstOrDefaultAsync(e => e.PackageName == packageName && e.Version == version);

            if (entry != null)
            {
                // Found in database, deserialize and add to memory cache if enabled
                var metadata = JsonSerializer.Deserialize<List<CveItem>>(entry.MetadataJson);
                if (metadata != null)
                {
                    if (_useMemoryCache)
                    {
                        _memoryCache.Set(cacheKey, metadata, _cacheOptions);
                    }
                    Console.WriteLine($"Retrieved from database cache: {packageName}@{version}");
                    return metadata;
                }
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error reading from cache database for {packageName}@{version}: {ex.Message}");
        }
        finally
        {
            _dbLock.Release();
        }

        return null;
    }

    /// <summary>
    /// Set a value in both memory cache and database
    /// </summary>
    public async Task SetValueAsync(string packageName, string version, List<CveItem> metadata)
    {
        var cacheKey = $"npm_package_{packageName}@{version}";

        // Store in memory cache if enabled
        if (_useMemoryCache)
        {
            _memoryCache.Set(cacheKey, metadata, _cacheOptions);
        }

        // Store in database (persistent)
        await _dbLock.WaitAsync();
        try
        {
            var metadataJson = JsonSerializer.Serialize(metadata);
            var now = DateTime.UtcNow;

            var existingEntry = await _dbContext.CacheEntries
                .FirstOrDefaultAsync(e => e.PackageName == packageName && e.Version == version);

            if (existingEntry != null)
            {
                // Update existing entry
                existingEntry.MetadataJson = metadataJson;
                existingEntry.LastUpdated = now;
            }
            else
            {
                // Create new entry
                var newEntry = new CveCacheEntry
                {
                    PackageName = packageName,
                    Version = version,
                    MetadataJson = metadataJson,
                    CreatedAt = now,
                    LastUpdated = now
                };
                _dbContext.CacheEntries.Add(newEntry);
            }

            await _dbContext.SaveChangesAsync();
            Console.WriteLine($"Cached package metadata to database: {packageName}@{version}");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error writing to cache database for {packageName}@{version}: {ex.Message}");
        }
        finally
        {
            _dbLock.Release();
        }
    }

    /// <summary>
    /// Get CVE information for a package from cache
    /// </summary>
    public async Task<CveInfo?> GetCveInfoAsync(string packageName, string version)
    {
        var cveItems = await TryGetValueAsync(packageName, version);
        if (cveItems == null)
        {
            return null;
        }

        var cveInfo = new CveInfo(packageName, version);
        cveInfo.Vulnerabilities.AddRange(cveItems);
        return cveInfo;
    }

    /// <summary>
    /// Cache CVE information for a package
    /// </summary>
    public async Task CacheCveInfoAsync(CveInfo cveInfo)
    {
        await SetValueAsync(cveInfo.PackageName, cveInfo.Version, cveInfo.Vulnerabilities);
    }

    /// <summary>
    /// Retrieves cached CVE data for a package version asynchronously
    /// </summary>
    public async Task<List<CveItem>?> GetValueAsync(string packageName, string version)
    {
        var cacheKey = $"{packageName}@{version}";

        // Check in-memory cache first if enabled
        if (_useMemoryCache && _memoryCache.TryGetValue(cacheKey, out List<CveItem>? cachedValue))
        {
            return cachedValue;
        }

        // Check SQLite database if not in memory
        await _dbLock.WaitAsync();
        try
        {
            var dbEntry = await _dbContext.CacheEntries
                .AsNoTracking()
                .FirstOrDefaultAsync(e => e.PackageName == packageName && e.Version == version);

            if (dbEntry != null)
            {
                var cveItems = JsonSerializer.Deserialize<List<CveItem>>(dbEntry.MetadataJson);

                // Store in memory cache for future use if enabled
                if (_useMemoryCache)
                {
                    _memoryCache.Set(cacheKey, cveItems, _cacheOptions);
                }

                return cveItems;
            }
        }
        finally
        {
            _dbLock.Release();
        }

        return null; // No cached data found
    }

    /// <summary>
    /// Get cache statistics
    /// </summary>
    public (int MemoryCacheCount, int DatabaseCacheCount) GetCacheStats()
    {
        // For memory cache count, we can't easily get the count without additional tracking
        // For now, return database count
        var dbCount = _dbContext.CacheEntries.Count();
        return (0, dbCount); // Memory cache count not tracked
    }
}