namespace DependencyCalculator.Models;

/// <summary>
/// Represents NPM package information including dependencies
/// </summary>
public class NpmPackageInfo
{
    public string PackageName { get; set; }
    public string Version { get; set; }
    public Dictionary<string, string> Dependencies { get; set; }
    public Dictionary<string, string> DevDependencies { get; set; }
    public Dictionary<string, string> PeerDependencies { get; set; }

    public NpmPackageInfo(string packageName, string version, Dictionary<string, string> dependencies, Dictionary<string, string>? devDependencies = null, Dictionary<string, string>? peerDependencies = null)
    {
        PackageName = packageName ?? throw new ArgumentNullException(nameof(packageName));
        Version = version ?? throw new ArgumentNullException(nameof(version));
        Dependencies = dependencies ?? new Dictionary<string, string>();
        DevDependencies = devDependencies ?? new Dictionary<string, string>();
        PeerDependencies = peerDependencies ?? new Dictionary<string, string>();
    }

    public override string ToString()
    {
        var depCount = Dependencies.Count;
        var devDepCount = DevDependencies.Count;
        var peerDepCount = PeerDependencies.Count;
        return $"{PackageName}@{Version} ({depCount} dependencies, {devDepCount} devDependencies, {peerDepCount} peerDependencies)";
    }
}
