using System.Text.Json;
using System.Text.Json.Serialization;
using DependencyCalculator.Models;

namespace DependencyCalculator.Services;

/// <summary>
/// Client for fetching NPM package information from the NPM registry
/// </summary>
public class NpmService : INpmService
{
    private readonly HttpClient _httpClient;
    private readonly string _npmRegistryBaseUrl;
    private readonly INpmVersionMatcher _versionMatcher;
    private readonly INpmCacheService _cacheService;

    public NpmService(HttpClient httpClient, NpmConfigReader config, INpmVersionMatcher versionMatcher, INpmCacheService cacheService)
    {
        _httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
        _versionMatcher = versionMatcher ?? throw new ArgumentNullException(nameof(versionMatcher));
        _cacheService = cacheService ?? throw new ArgumentNullException(nameof(cacheService));

        if (config == null)
        {
            throw new ArgumentNullException(nameof(config));
        }

        _npmRegistryBaseUrl = config.Registry ?? "https://registry.npmjs.org";

        // Configure authentication if credentials are provided
        if (!string.IsNullOrEmpty(config.Username) && !string.IsNullOrEmpty(config.Password))
        {
            var credentials = Convert.ToBase64String(
                System.Text.Encoding.ASCII.GetBytes($"{config.Username}:{config.Password}")
            );
            _httpClient.DefaultRequestHeaders.Add("Authorization", $"Basic {credentials}");
        }

        // Add required headers for NPM registry
        _httpClient.DefaultRequestHeaders.Add("User-Agent", "dependency-calculator/1.0");
        _httpClient.DefaultRequestHeaders.Add("Accept", "application/json");
    }

    /// <summary>
    /// Recursively fetches package information including all transitive dependencies
    /// </summary>
    /// <param name="packageRequests">List of root package name and version pairs</param>
    /// <param name="maxDepth">Maximum depth to traverse (default 5 to prevent infinite loops)</param>
    /// <param name="dependencyType">Type of dependencies to recursively fetch (Dependencies, DevDependencies, or PeerDependencies)</param>
    /// <returns>Tuple containing list of all NPM packages and set of dependencies that reached max depth</returns>
    public async Task<(List<NpmPackageInfo> Packages, HashSet<string> MaxDepthPackages)> GetPackageInfoRecursiveAsync(List<NpmPackageRequest> packageRequests, int maxDepth = 5, DependencyType dependencyType = DependencyType.Dependencies)
    {
        // Track packages by name@resolvedVersion to avoid duplicate fetches
        var allPackages = new Dictionary<string, NpmPackageInfo>();

        // Track which version ranges have been resolved for each package
        // This helps us avoid re-fetching when we encounter the same semantic version again
        var resolvedVersions = new Dictionary<string, Dictionary<string, string?>>();

        // Track packages that had dependencies but reached max depth
        var maxDepthPackages = new HashSet<string>();

        var queue = new Queue<(NpmPackageRequest request, int depth)>();

        // Initialize queue with root packages
        foreach (var request in packageRequests)
        {
            queue.Enqueue((request, 0));
        }

        while (queue.Count > 0)
        {
            var (currentRequest, depth) = queue.Dequeue();

            // Skip if max depth reached
            if (depth >= maxDepth)
            {
                continue;
            }

            // Check if we've already resolved this version range for this package
            if (resolvedVersions.TryGetValue(currentRequest.PackageName, out var packageVersions))
            {
                if (packageVersions.ContainsKey(currentRequest.Version))
                {
                    // Already processed this version range
                    continue;
                }
            }

            // Fetch package info (this will resolve semantic versions to actual versions)
            var packageInfo = await FetchPackageInfoAsync(currentRequest);

            // Register the resolved version
            if (!resolvedVersions.ContainsKey(currentRequest.PackageName))
            {
                resolvedVersions[currentRequest.PackageName] = new Dictionary<string, string?>();
            }
            resolvedVersions[currentRequest.PackageName][currentRequest.Version] = packageInfo?.Version;

            if (packageInfo == null)
            {
                continue;
            }

            // Add the package using the resolved version as the key
            var packageKey = $"{packageInfo.PackageName}@{packageInfo.Version}";
            if (!allPackages.ContainsKey(packageKey))
            {
                allPackages[packageKey] = packageInfo;

                // Get the appropriate dependencies based on dependency type
                var dependencies = dependencyType switch
                {
                    DependencyType.DevDependencies => packageInfo.DevDependencies,
                    DependencyType.PeerDependencies => packageInfo.PeerDependencies,
                    _ => packageInfo.Dependencies
                };

                // Queue all dependencies for processing
                if (depth + 1 < maxDepth)
                {
                    foreach (var dep in dependencies)
                    {
                        var depRequest = new NpmPackageRequest(dep.Key, dep.Value);
                        queue.Enqueue((depRequest, depth + 1));
                    }
                }
                else if (dependencies.Count > 0)
                {
                    // This package has dependencies but we've reached max depth
                    // Track its unfetched dependencies
                    foreach (var dep in dependencies)
                    {
                        var depKey = $"{dep.Key}@{dep.Value}";
                        maxDepthPackages.Add(depKey);
                    }
                }
            }
        }

        return (allPackages.Values.ToList(), maxDepthPackages);
    }

    /// <summary>
    /// Fetches package information for a single NPM package
    /// </summary>
    private async Task<NpmPackageInfo?> FetchPackageInfoAsync(NpmPackageRequest request)
    {
        try
        {
            // Try to get package metadata from cache first (checks both memory and database)
            var packageData = await _cacheService.TryGetValueAsync(request.PackageName, request.Version);

            if (packageData == null)
            {
                // Not in cache, fetch from NPM registry
                var url = $"{_npmRegistryBaseUrl}/{request.PackageName}";

                Console.WriteLine($"Fetching from NPM registry: {url}");

                var response = await _httpClient.GetAsync(url);

                if (!response.IsSuccessStatusCode)
                {
                    var errorContent = await response.Content.ReadAsStringAsync();
                    Console.WriteLine($"Failed to fetch {request}: {response.StatusCode}");
                    Console.WriteLine($"Response: {errorContent}");
                    return null;
                }

                var content = await response.Content.ReadAsStringAsync();
                var rawMetadata = JsonSerializer.Deserialize<NpmPackageMetadata>(content);

                // Convert to NpmCacheService.NpmPackageMetadata
                // Filter out pre-release versions (contain dash) like "1.0.0-alpha", "2.0.0-beta.1", etc.
                if (rawMetadata?.Versions != null)
                {
                    packageData = new NpmCacheService.NpmPackageMetadata
                    {
                        Versions = rawMetadata.Versions
                            .Where(kvp => !kvp.Key.Contains('-'))
                            .ToDictionary(
                                kvp => kvp.Key,
                                kvp => new NpmCacheService.NpmPackageResponse
                                {
                                    Name = kvp.Value.Name,
                                    Version = kvp.Value.Version,
                                    Dependencies = kvp.Value.Dependencies,
                                    DevDependencies = kvp.Value.DevDependencies,
                                    PeerDependencies = kvp.Value.PeerDependencies
                                }
                            )
                    };

                    // Cache the package metadata (to both memory and database)
                    await _cacheService.SetValueAsync(request.PackageName, request.Version, packageData);
                }
            }

            if (packageData?.Versions == null || packageData.Versions.Count == 0)
            {
                Console.WriteLine($"No versions found for package {request.PackageName}");
                return null;
            }

            // Filter available versions to exclude pre-release versions (just in case they exist in cache)
            var availableVersions = packageData.Versions.Keys
                .Where(v => !v.Contains('-'))
                .ToList();

            if (availableVersions.Count == 0)
            {
                Console.WriteLine($"No stable versions found for package {request.PackageName} (all versions are pre-release)");
                return null;
            }

            string? actualVersion = null;

            if (packageData.Versions.ContainsKey(request.Version) && !request.Version.Contains('-'))
            {
                // Exact version match (and it's not a pre-release)
                actualVersion = request.Version;
            }
            else
            {
                // Use version matcher to find best match
                actualVersion = _versionMatcher.FindBestMatch(request.Version, availableVersions);

                if (actualVersion == null)
                {
                    Console.WriteLine($"No matching version found for {request.PackageName}@{request.Version}");
                    Console.WriteLine($"Available stable versions: {string.Join(", ", availableVersions.Take(10))}...");
                    return null;
                }

                Console.WriteLine($"Resolved {request.PackageName}@{request.Version} to {actualVersion}");
            }

            var versionData = packageData.Versions[actualVersion];

            return new NpmPackageInfo(
                versionData.Name ?? request.PackageName,
                versionData.Version ?? actualVersion,
                versionData.Dependencies ?? new Dictionary<string, string>(),
                versionData.DevDependencies ?? new Dictionary<string, string>(),
                versionData.PeerDependencies ?? new Dictionary<string, string>()
            );
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error fetching {request}: {ex.Message}");
            Console.WriteLine($"Stack trace: {ex.StackTrace}");
            return null;
        }
    }

    /// <summary>
    /// Get all available versions for a package (ordered from latest to oldest)
    /// </summary>
    public async Task<List<string>> GetAvailableVersionsAsync(string packageName)
    {
        // Try to get from cache service first
        var metadata = await _cacheService.TryGetValueAsync(packageName, "*");

        if (metadata == null || metadata.Versions == null || !metadata.Versions.Any())
        {
            // Not in cache, fetch from NPM by making a request
            Console.WriteLine($"Package {packageName} not in cache, fetching from NPM registry...");

            // Use FetchPackageInfoAsync to fetch and cache the package data
            var packageRequest = new NpmPackageRequest(packageName, "*");
            await FetchPackageInfoAsync(packageRequest);

            // After fetching, try cache again
            metadata = await _cacheService.TryGetValueAsync(packageName, "*");
        }

        if (metadata?.Versions != null && metadata.Versions.Any())
        {
            var versions = metadata.Versions.Keys
                .OrderByDescending(v => ParseVersion(v))
                .ToList();
            Console.WriteLine($"Found {versions.Count} versions for {packageName}");
            return versions;
        }

        Console.WriteLine($"Warning: No versions found for package {packageName}");
        return new List<string>();
    }

    /// <summary>
    /// Get package info for a specific version
    /// </summary>
    public async Task<NpmPackageInfo?> GetPackageInfoAsync(string packageName, string version)
    {
        // Try to get from cache service
        var metadata = await _cacheService.TryGetValueAsync(packageName, version);

        if (metadata == null || metadata.Versions == null || !metadata.Versions.Any())
        {
            // Not in cache, fetch from NPM
            Console.WriteLine($"Package {packageName}@{version} not in cache, fetching from NPM registry...");

            var packageRequest = new NpmPackageRequest(packageName, version);
            await FetchPackageInfoAsync(packageRequest);

            // After fetching, try cache again
            metadata = await _cacheService.TryGetValueAsync(packageName, version);
        }

        if (metadata?.Versions != null && metadata.Versions.TryGetValue(version, out var versionData))
        {
            return new NpmPackageInfo(
                packageName,
                version,
                versionData.Dependencies ?? new Dictionary<string, string>(),
                versionData.DevDependencies,
                versionData.PeerDependencies
            );
        }

        Console.WriteLine($"Warning: Could not fetch package info for {packageName}@{version}");
        return null;
    }

    /// <summary>
    /// Parse version string into comparable parts for sorting
    /// </summary>
    private (int major, int minor, int patch) ParseVersion(string version)
    {
        var match = System.Text.RegularExpressions.Regex.Match(version, @"(\d+)\.?(\d*)\.?(\d*)");
        if (match.Success)
        {
            int.TryParse(match.Groups[1].Value, out int major);
            int.TryParse(match.Groups[2].Value, out int minor);
            int.TryParse(match.Groups[3].Value, out int patch);
            return (major, minor, patch);
        }
        return (0, 0, 0);
    }

    /// <summary>
    /// Internal class for deserializing NPM registry API responses
    /// </summary>
    private class NpmPackageMetadata
    {
        [JsonPropertyName("versions")]
        public Dictionary<string, NpmPackageResponse>? Versions { get; set; }
    }

    private class NpmPackageResponse
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
