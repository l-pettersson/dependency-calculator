using DependencyCalculator.Models;

namespace DependencyCalculator.Services;

/// <summary>
/// Interface for NPM registry client
/// </summary>
public interface INpmService
{
    /// <summary>
    /// Recursively fetches package information from the NPM registry up to a specified depth
    /// </summary>
    Task<(List<NpmPackageInfo> Packages, HashSet<string> MaxDepthPackages)> GetPackageInfoRecursiveAsync(
        List<NpmPackageRequest> packageRequests,
        int maxDepth = 5,
        DependencyType dependencyType = DependencyType.Dependencies);

    /// <summary>
    /// Get all available versions for a package
    /// </summary>
    Task<List<string>> GetAvailableVersionsAsync(string packageName);

    /// <summary>
    /// Get package info for a specific version
    /// </summary>
    Task<NpmPackageInfo?> GetPackageInfoAsync(string packageName, string version);
}
