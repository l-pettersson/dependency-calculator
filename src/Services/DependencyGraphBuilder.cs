using DependencyCalculator.Models;

namespace DependencyCalculator.Services;

/// <summary>
/// Helper class for building dependency graph nodes and edges from npm package information.
/// </summary>
public class DependencyGraphBuilder
{
    private readonly List<GraphNode> _nodes;
    private readonly List<GraphEdge> _edges;
    private readonly Dictionary<string, NpmPackageInfo> _packageLookup; // Maps "name@version" to package info
    private readonly Dictionary<string, int> _packageToNodeId;
    private readonly INpmVersionMatcher? _versionMatcher;
    private int _nodeIdCounter;

    public DependencyGraphBuilder(INpmVersionMatcher? versionMatcher = null)
    {
        _nodes = new List<GraphNode>();
        _edges = new List<GraphEdge>();
        _packageLookup = new Dictionary<string, NpmPackageInfo>();
        _packageToNodeId = new Dictionary<string, int>();
        _versionMatcher = versionMatcher;
        _nodeIdCounter = 0;
    }

    /// <summary>
    /// Builds a recursive dependency graph from a list of npm package information,
    /// including all transitive dependencies.
    /// </summary>
    /// <param name="packageInfos">List of all packages including transitive dependencies</param>
    /// <param name="rootPackageNames">Names of the root packages to mark them specially</param>
    /// <param name="rootPackageRequests">Original root package requests to ensure all are included</param>
    /// <param name="maxDepthPackages">Set of package keys that reached max depth during fetching</param>
    /// <param name="dependencyType">Type of dependencies to visualize</param>
    /// <returns>A tuple containing the list of nodes and edges</returns>
    public (List<GraphNode> Nodes, List<GraphEdge> Edges) BuildRecursiveGraph(
        List<NpmPackageInfo> packageInfos,
        HashSet<string> rootPackageNames,
        List<(string Name, string Version)>? rootPackageRequests = null,
        HashSet<string>? maxDepthPackages = null,
        DependencyType dependencyType = DependencyType.Dependencies)
    {
        // Build lookup dictionary for fast package resolution
        foreach (var pkg in packageInfos)
        {
            var key = $"{pkg.PackageName}@{pkg.Version}";
            _packageLookup[key] = pkg;
        }

        // First, ensure all requested root packages are in the graph, even if not found
        if (rootPackageRequests != null)
        {
            foreach (var (name, version) in rootPackageRequests)
            {
                var packageKeyString = $"{name}@{version}";
                
                // Check if this package was found by looking for a matching resolved version
                var foundPackage = FindMatchingPackage(name, version);
                
                if (foundPackage == null)
                {
                    // Add node for package that wasn't found in repository
                    GetOrCreateNode(
                        packageKeyString,
                        name,
                        version,
                        isRoot: true,
                        dependencyCount: 0,
                        isFoundInRepository: false);
                }
            }
        }

        // Process all packages
        foreach (var packageInfo in packageInfos)
        {
            var packageKeyString = $"{packageInfo.PackageName}@{packageInfo.Version}";
            
            var dependencies = dependencyType switch
            {
                DependencyType.DevDependencies => packageInfo.DevDependencies,
                DependencyType.PeerDependencies => packageInfo.PeerDependencies,
                _ => packageInfo.Dependencies
            };

            var isRoot = rootPackageNames.Contains(packageInfo.PackageName);

            // Add main package node
            var mainNodeId = GetOrCreateNode(
                packageKeyString,
                packageInfo.PackageName,
                packageInfo.Version,
                isRoot: isRoot,
                dependencyCount: dependencies.Count,
                isFoundInRepository: true);

            // Add dependency edges
            foreach (var dep in dependencies)
            {
                // Check if this dependency reached max depth (using the requested version)
                var depKeyRequested = $"{dep.Key}@{dep.Value}";
                var reachedMaxDepth = maxDepthPackages?.Contains(depKeyRequested) ?? false;
                
                // Find the actual resolved version for this dependency
                var resolvedPackage = FindMatchingPackage(dep.Key, dep.Value);
                
                // Determine if package was found and which version to use
                bool wasFound;
                string depVersion;
                
                if (reachedMaxDepth)
                {
                    // Package reached max depth - it exists but wasn't fetched fully
                    // However, it might still be in the package list (fetched but not traversed)
                    // Try to use the resolved version if available, otherwise use requested version
                    if (resolvedPackage != null)
                    {
                        depVersion = resolvedPackage.Version;
                    }
                    else
                    {
                        depVersion = dep.Value;
                    }
                    wasFound = true;  // The package exists, we just didn't fetch its dependencies
                }
                else if (resolvedPackage != null)
                {
                    // Package was found and fetched
                    wasFound = true;
                    depVersion = resolvedPackage.Version;
                }
                else
                {
                    // Package was not found at all
                    wasFound = false;
                    depVersion = dep.Value;
                }
                
                AddDependency(mainNodeId, dep.Key, depVersion, wasFound, reachedMaxDepth);
            }
        }

        return (_nodes, _edges);
    }

    /// <summary>
    /// Finds a package that matches the given name and version range.
    /// Uses the version matcher to check if any resolved package satisfies the version requirement.
    /// </summary>
    private NpmPackageInfo? FindMatchingPackage(string packageName, string versionRange)
    {
        // First try exact match
        var exactKey = $"{packageName}@{versionRange}";
        if (_packageLookup.TryGetValue(exactKey, out var exactMatch))
        {
            return exactMatch;
        }

        // If no exact match and we have a version matcher, find the best match
        if (_versionMatcher != null)
        {
            var matchingPackages = _packageLookup.Values
                .Where(p => p.PackageName == packageName && _versionMatcher.Matches(versionRange, p.Version))
                .ToList();

            if (matchingPackages.Any())
            {
                // Return the best (latest) matching version
                return matchingPackages
                    .OrderByDescending(p => p.Version)
                    .FirstOrDefault();
            }
        }

        return null;
    }

    /// <summary>
    /// Gets an existing node ID or creates a new node for the package.
    /// </summary>
    private int GetOrCreateNode(
        string packageKey, 
        string packageName, 
        string version, 
        bool isRoot, 
        int dependencyCount,
        bool isFoundInRepository = true,
        bool reachedMaxDepth = false)
    {
        if (!_packageToNodeId.ContainsKey(packageKey))
        {
            var nodeId = _nodeIdCounter++;
            _packageToNodeId[packageKey] = nodeId;
            _nodes.Add(new GraphNode
            {
                Id = nodeId,
                Label = packageName,
                Version = version,
                IsRoot = isRoot,
                DependencyCount = dependencyCount,
                IsFoundInRepository = isFoundInRepository,
                ReachedMaxDepth = reachedMaxDepth
            });
        }

        return _packageToNodeId[packageKey];
    }

    /// <summary>
    /// Adds a dependency node and creates an edge from the parent to the dependency.
    /// </summary>
    private void AddDependency(int parentNodeId, string dependencyName, string dependencyVersion, bool isFoundInRepository = true, bool reachedMaxDepth = false)
    {
        var depKey = $"{dependencyName}@{dependencyVersion}";
        
        var depNodeId = GetOrCreateNode(
            depKey, 
            dependencyName, 
            dependencyVersion, 
            isRoot: false, 
            dependencyCount: 0,
            isFoundInRepository: isFoundInRepository,
            reachedMaxDepth: reachedMaxDepth);

        _edges.Add(new GraphEdge
        {
            From = parentNodeId,
            To = depNodeId,
            Label = ""
        });
    }
}
