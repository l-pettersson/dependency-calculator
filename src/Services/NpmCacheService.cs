using System.Text.Json;
using System.Text.Json.Serialization;
using DependencyCalculator.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;

namespace DependencyCalculator.Services;

/// <summary>
/// Service that provides dual-layer caching: in-memory (fast) and SQLite (persistent)
/// </summary>
public class NpmCacheService : INpmCacheService
{
    private readonly IMemoryCache _memoryCache;
    private readonly NpmCacheDbContext _dbContext;
    private readonly MemoryCacheEntryOptions _cacheOptions;
    private readonly SemaphoreSlim _dbLock = new SemaphoreSlim(1, 1);
    private readonly bool _useMemoryCache;

    public NpmCacheService(IMemoryCache memoryCache, NpmCacheDbContext dbContext, bool useMemoryCache = true)
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

        Console.WriteLine("Loading NPM package cache from SQLite database...");
        
        await _dbLock.WaitAsync();
        try
        {
            var entries = await _dbContext.CacheEntries.ToListAsync();
            
            foreach (var entry in entries)
            {
                var cacheKey = $"npm_package_{entry.PackageName}@{entry.Version}";
                
                try
                {
                    // Deserialize the metadata and store in memory cache
                    var metadata = JsonSerializer.Deserialize<NpmPackageMetadata>(entry.MetadataJson);
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
    public async Task<NpmPackageMetadata?> TryGetValueAsync(string packageName, string version)
    {
        var cacheKey = $"npm_package_{packageName}@{version}";
        
        // First, try memory cache (fast) if enabled
        if (_useMemoryCache && _memoryCache.TryGetValue(cacheKey, out NpmPackageMetadata? cachedData))
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
                var metadata = JsonSerializer.Deserialize<NpmPackageMetadata>(entry.MetadataJson);
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
    public async Task SetValueAsync(string packageName, string version, NpmPackageMetadata metadata)
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
                var newEntry = new NpmCacheEntry
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
    /// Get cache statistics
    /// </summary>
    public (int MemoryCacheCount, int DatabaseCacheCount) GetCacheStats()
    {
        // For memory cache count, we can't easily get the count without additional tracking
        // For now, return database count
        var dbCount = _dbContext.CacheEntries.Count();
        return (0, dbCount); // Memory cache count not tracked
    }

    /// <summary>
    /// Internal class for deserializing NPM registry API responses (same structure as in NpmClient)
    /// </summary>
    public class NpmPackageMetadata
    {
        [JsonPropertyName("versions")]
        public Dictionary<string, NpmPackageResponse>? Versions { get; set; }
    }

    public class NpmPackageResponse
    {
        [JsonPropertyName("name")]
        public string? Name { get; set; }
        
        [JsonPropertyName("version")]
        public string? Version { get; set; }
        
        [JsonPropertyName("dependencies")]
        public Dictionary<string, string>? Dependencies { get; set; }
        
        [JsonPropertyName("devDependencies")]
        public Dictionary<string, string>? DevDependencies { get; set; }
        
        [JsonPropertyName("peerDependencies")]
        public Dictionary<string, string>? PeerDependencies { get; set; }
    }
}
