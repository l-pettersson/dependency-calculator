using DependencyCalculator.Models;

namespace DependencyCalculator.Services;

/// <summary>
/// Interface for CVE caching service
/// </summary>
public interface ICveCacheService
{
    /// <summary>
    /// Loads all cached entries from SQLite into memory cache on startup
    /// </summary>
    Task LoadCacheFromDatabaseAsync();

    /// <summary>
    /// Try to get a value from the cache (checks memory first, then database)
    /// </summary>
    Task<List<CveItem>?> TryGetValueAsync(string packageName, string version);

    /// <summary>
    /// Set a value in both memory cache and database
    /// </summary>
    Task SetValueAsync(string packageName, string version, List<CveItem> metadata);

    /// <summary>
    /// Get CVE data from cache or return null if not found
    /// </summary>
    Task<List<CveItem>?> GetValueAsync(string packageName, string version);

    /// <summary>
    /// Get cache statistics
    /// </summary>
    (int MemoryCacheCount, int DatabaseCacheCount) GetCacheStats();
}
