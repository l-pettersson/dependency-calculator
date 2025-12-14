namespace DependencyCalculator.Models;

/// <summary>
/// Represents a request for NPM package information
/// </summary>
public class NpmPackageRequest
{
    public string PackageName { get; set; }
    public string Version { get; set; }

    public NpmPackageRequest(string packageName, string version)
    {
        PackageName = packageName ?? throw new ArgumentNullException(nameof(packageName));
        Version = version ?? throw new ArgumentNullException(nameof(version));
    }

    public override string ToString() => $"{PackageName}@{Version}";
}
