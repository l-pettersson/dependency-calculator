using DependencyCalculator.Models;

namespace DependencyCalculator.Services;

/// <summary>
/// Interface for CVE (Common Vulnerabilities and Exposures) client
/// </summary>
/// Rename all clients to services.
public interface ICveService
{
    /// <summary>
    /// Get CVE information for multiple packages
    /// </summary>
    Task<List<CveInfo>> GetCvesForPackagesAsync(List<NpmPackageInfo> packages);

    /// <summary>
    /// Get CVE information for a single package version
    /// </summary>
    Task<CveInfo> GetCvesForPackageAsync(string packageName, string version);
}
