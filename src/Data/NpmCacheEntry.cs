using System.ComponentModel.DataAnnotations;

namespace DependencyCalculator.Data;

/// <summary>
/// Represents a cached NPM package metadata entry in the database
/// </summary>
public class NpmCacheEntry
{
    /// <summary>
    /// Primary key - the package name
    /// </summary>
    [Key]
    public string PackageName { get; set; } = string.Empty;

    /// <summary>
    /// Part of composite key - the package version (or version range)
    /// </summary>
    [Key]
    public string Version { get; set; } = string.Empty;

    /// <summary>
    /// JSON-serialized package metadata (NpmPackageMetadata)
    /// </summary>
    public string MetadataJson { get; set; } = string.Empty;

    /// <summary>
    /// When this entry was last updated
    /// </summary>
    public DateTime LastUpdated { get; set; }

    /// <summary>
    /// When this entry was created
    /// </summary>
    public DateTime CreatedAt { get; set; }
}
