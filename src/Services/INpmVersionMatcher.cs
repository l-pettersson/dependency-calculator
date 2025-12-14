namespace DependencyCalculator.Services;

/// <summary>
/// Interface for matching npm version ranges against specific versions following semantic versioning rules.
/// </summary>
public interface INpmVersionMatcher
{
    /// <summary>
    /// Finds the best matching version from a list of available versions that satisfies the version range.
    /// Returns the latest version that matches the range.
    /// </summary>
    /// <param name="versionRange">The version range (e.g., "^1.0.0", "~1.2.3", ">=2.0.0", "1.0.0")</param>
    /// <param name="availableVersions">List of available versions to choose from</param>
    /// <returns>The best matching version, or null if no match is found</returns>
    string? FindBestMatch(string versionRange, IEnumerable<string> availableVersions);

    /// <summary>
    /// Determines whether a specific version satisfies a version range.
    /// </summary>
    /// <param name="versionRange">The version range (e.g., "^1.0.0", "~1.2.3", ">=2.0.0", "1.0.0", ">=1.0.0 <2.0.0", "1.x || 2.x")</param>
    /// <param name="version">The specific version to check (e.g., "1.2.3")</param>
    /// <returns>True if the version satisfies the range, false otherwise</returns>
    bool Matches(string versionRange, string version);
}
