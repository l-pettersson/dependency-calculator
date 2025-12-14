using DependencyCalculator.Services;

namespace DependencyCalculator.Services;

/// <summary>
/// Interface for NPM package caching service
/// </summary>
public interface INpmCacheService
{
    /// <summary>
    /// Loads all cached entries from SQLite into memory cache on startup
    /// </summary>
    Task LoadCacheFromDatabaseAsync();

    /// <summary>
    /// Try to get a value from the cache (checks memory first, then database)
    /// </summary>
    Task<NpmCacheService.NpmPackageMetadata?> TryGetValueAsync(string packageName, string version);

    /// <summary>
    /// Set a value in both memory cache and database
    /// </summary>
    Task SetValueAsync(string packageName, string version, NpmCacheService.NpmPackageMetadata metadata);

    /// <summary>
    /// Get cache statistics
    /// </summary>
    (int MemoryCacheCount, int DatabaseCacheCount) GetCacheStats();
}
