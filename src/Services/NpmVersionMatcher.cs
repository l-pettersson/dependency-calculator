namespace DependencyCalculator.Services;

/// <summary>
/// Matches npm version ranges against specific versions following semantic versioning rules.
/// </summary>
public class NpmVersionMatcher : INpmVersionMatcher
{
    /// <summary>
    /// Finds the best matching version from a list of available versions that satisfies the version range.
    /// Returns the latest version that matches the range.
    /// </summary>
    /// <param name="versionRange">The version range (e.g., "^1.0.0", "~1.2.3", ">=2.0.0", "1.0.0")</param>
    /// <param name="availableVersions">List of available versions to choose from</param>
    /// <returns>The best matching version, or null if no match is found</returns>
    public string? FindBestMatch(string versionRange, IEnumerable<string> availableVersions)
    {
        if (string.IsNullOrWhiteSpace(versionRange) || availableVersions == null)
        {
            return null;
        }

        // Filter versions that match the range
        var matchingVersions = availableVersions
            .Where(v => Matches(versionRange, v))
            .ToList();

        if (!matchingVersions.Any())
        {
            return null;
        }

        // Return the latest matching version
        return matchingVersions
            .OrderByDescending(v => ParseVersion(v), new VersionPartsComparer())
            .FirstOrDefault();
    }

    /// <summary>
    /// Determines whether a specific version satisfies a version range.
    /// </summary>
    /// <param name="versionRange">The version range (e.g., "^1.0.0", "~1.2.3", ">=2.0.0", "1.0.0", ">=1.0.0 <2.0.0", "1.x || 2.x")</param>
    /// <param name="version">The specific version to check (e.g., "1.2.3")</param>
    /// <returns>True if the version satisfies the range, false otherwise</returns>
    public bool Matches(string versionRange, string version)
    {
        if (string.IsNullOrWhiteSpace(versionRange) || string.IsNullOrWhiteSpace(version))
        {
            return false;
        }

        versionRange = versionRange.Trim();
        version = version.Trim();

        // Handle OR operator (||)
        if (versionRange.Contains("||"))
        {
            return MatchesOrRange(versionRange, version);
        }

        // Handle AND operator (space-separated or &&)
        if (ContainsAndOperator(versionRange))
        {
            return MatchesAndRange(versionRange, version);
        }

        // Handle single range expression
        return MatchesSingleRange(versionRange, version);
    }

    /// <summary>
    /// Determines if a version range contains an AND operator (multiple conditions).
    /// </summary>
    private bool ContainsAndOperator(string versionRange)
    {
        // Check for explicit && operator
        if (versionRange.Contains("&&"))
        {
            return true;
        }

        // Check for space-separated ranges (multiple conditions)
        // This is tricky because we need to distinguish between a space in a single operator (like ">= 1.0.0")
        // and spaces separating multiple conditions (like ">=1.0.0 <2.0.0")
        var trimmed = versionRange.Trim();
        
        // Count the number of comparison operators or range specifiers
        int operatorCount = 0;
        int i = 0;
        
        while (i < trimmed.Length)
        {
            if (i < trimmed.Length - 1 && (trimmed.Substring(i, 2) == ">=" || trimmed.Substring(i, 2) == "<="))
            {
                operatorCount++;
                i += 2;
                // Skip past the version number
                while (i < trimmed.Length && !char.IsWhiteSpace(trimmed[i]))
                {
                    i++;
                }
            }
            else if (trimmed[i] == '>' || trimmed[i] == '<')
            {
                operatorCount++;
                i++;
                // Skip past the version number
                while (i < trimmed.Length && !char.IsWhiteSpace(trimmed[i]))
                {
                    i++;
                }
            }
            else if (trimmed[i] == '^' || trimmed[i] == '~')
            {
                operatorCount++;
                i++;
                // Skip past the version number
                while (i < trimmed.Length && !char.IsWhiteSpace(trimmed[i]))
                {
                    i++;
                }
            }
            else
            {
                i++;
            }
        }

        return operatorCount > 1;
    }

    /// <summary>
    /// Matches OR range - version must match at least one of the alternatives.
    /// Example: "1.x || 2.x" or "^1.0.0 || ^2.0.0"
    /// </summary>
    private bool MatchesOrRange(string versionRange, string version)
    {
        var alternatives = versionRange.Split(new[] { "||" }, StringSplitOptions.RemoveEmptyEntries);
        
        foreach (var alternative in alternatives)
        {
            if (Matches(alternative.Trim(), version))
            {
                return true;
            }
        }
        
        return false;
    }

    /// <summary>
    /// Matches AND range - version must satisfy all conditions.
    /// Example: ">=1.0.0 <2.0.0" or ">=1.2.7 <1.3.0"
    /// </summary>
    private bool MatchesAndRange(string versionRange, string version)
    {
        var conditions = SplitAndConditions(versionRange);
        
        foreach (var condition in conditions)
        {
            if (!MatchesSingleRange(condition.Trim(), version))
            {
                return false;
            }
        }
        
        return true;
    }

    /// <summary>
    /// Splits a version range into individual AND conditions.
    /// Handles both && and space-separated conditions.
    /// </summary>
    private List<string> SplitAndConditions(string versionRange)
    {
        var conditions = new List<string>();
        
        // First split by && if present
        var parts = versionRange.Split(new[] { "&&" }, StringSplitOptions.RemoveEmptyEntries);
        
        foreach (var part in parts)
        {
            var trimmed = part.Trim();
            
            // If the part contains space-separated conditions, split them
            // We need to carefully identify where one condition ends and another begins
            var subConditions = ExtractSpaceSeparatedConditions(trimmed);
            conditions.AddRange(subConditions);
        }
        
        return conditions;
    }

    /// <summary>
    /// Extracts space-separated conditions from a version range string.
    /// Example: ">=1.0.0 <2.0.0" -> [">=1.0.0", "<2.0.0"]
    /// </summary>
    private List<string> ExtractSpaceSeparatedConditions(string range)
    {
        var conditions = new List<string>();
        var currentCondition = "";
        var i = 0;
        
        while (i < range.Length)
        {
            // Skip leading whitespace
            while (i < range.Length && char.IsWhiteSpace(range[i]))
            {
                i++;
            }
            
            if (i >= range.Length)
            {
                break;
            }
            
            // Check if this is the start of a new operator
            if (i < range.Length - 1 && (range.Substring(i, 2) == ">=" || range.Substring(i, 2) == "<="))
            {
                if (!string.IsNullOrWhiteSpace(currentCondition))
                {
                    conditions.Add(currentCondition.Trim());
                    currentCondition = "";
                }
                currentCondition += range.Substring(i, 2);
                i += 2;
            }
            else if (range[i] == '>' || range[i] == '<' || range[i] == '^' || range[i] == '~')
            {
                if (!string.IsNullOrWhiteSpace(currentCondition))
                {
                    conditions.Add(currentCondition.Trim());
                    currentCondition = "";
                }
                currentCondition += range[i];
                i++;
            }
            else
            {
                // Part of the current condition (version number or whitespace)
                currentCondition += range[i];
                i++;
            }
        }
        
        if (!string.IsNullOrWhiteSpace(currentCondition))
        {
            conditions.Add(currentCondition.Trim());
        }
        
        // If we only found one condition, return it as-is
        if (conditions.Count <= 1 && !string.IsNullOrWhiteSpace(range))
        {
            return new List<string> { range };
        }
        
        return conditions;
    }

    /// <summary>
    /// Matches a single range expression (no logical operators).
    /// </summary>
    private bool MatchesSingleRange(string versionRange, string version)
    {
        versionRange = versionRange.Trim();
        
        // Handle wildcard (*)
        if (versionRange == "*" || versionRange == "x" || versionRange == "X")
        {
            return true;
        }

        // Handle partial wildcards like 1.x, 1.2.x, 1.X, etc.
        if (versionRange.Contains("x") || versionRange.Contains("X"))
        {
            // Check if it's a comparison operator with wildcard (e.g., >=16.x)
            if (versionRange.StartsWith(">=") || versionRange.StartsWith(">") ||
                versionRange.StartsWith("<=") || versionRange.StartsWith("<"))
            {
                return MatchesComparisonWithWildcard(versionRange, version);
            }
            
            return MatchesPartialWildcard(versionRange, version);
        }

        // Handle caret range (^)
        if (versionRange.StartsWith("^"))
        {
            return MatchesCaretRange(versionRange.Substring(1), version);
        }

        // Handle tilde range (~)
        if (versionRange.StartsWith("~"))
        {
            return MatchesTildeRange(versionRange.Substring(1), version);
        }

        // Handle >= operator
        if (versionRange.StartsWith(">="))
        {
            return CompareVersions(version, versionRange.Substring(2).Trim()) >= 0;
        }

        // Handle > operator
        if (versionRange.StartsWith(">"))
        {
            return CompareVersions(version, versionRange.Substring(1).Trim()) > 0;
        }

        // Handle <= operator
        if (versionRange.StartsWith("<="))
        {
            return CompareVersions(version, versionRange.Substring(2).Trim()) <= 0;
        }

        // Handle < operator
        if (versionRange.StartsWith("<"))
        {
            return CompareVersions(version, versionRange.Substring(1).Trim()) < 0;
        }

        // Handle exact version match (including partial versions like 1.2 or 1)
        return MatchesExactOrPartial(versionRange, version);
    }

    /// <summary>
    /// Matches comparison operators with wildcards (e.g., >=16.x, <2.x).
    /// The wildcard is replaced with 0 for comparison purposes.
    /// </summary>
    private bool MatchesComparisonWithWildcard(string versionRange, string version)
    {
        string op = "";
        string rangeVersion = "";
        
        if (versionRange.StartsWith(">="))
        {
            op = ">=";
            rangeVersion = versionRange.Substring(2).Trim();
        }
        else if (versionRange.StartsWith("<="))
        {
            op = "<=";
            rangeVersion = versionRange.Substring(2).Trim();
        }
        else if (versionRange.StartsWith(">"))
        {
            op = ">";
            rangeVersion = versionRange.Substring(1).Trim();
        }
        else if (versionRange.StartsWith("<"))
        {
            op = "<";
            rangeVersion = versionRange.Substring(1).Trim();
        }
        
        // Replace x/X with 0 for comparison
        var normalizedRange = rangeVersion.Replace("x", "0").Replace("X", "0");
        
        // Perform the comparison
        switch (op)
        {
            case ">=":
                return CompareVersions(version, normalizedRange) >= 0;
            case ">":
                return CompareVersions(version, normalizedRange) > 0;
            case "<=":
                return CompareVersions(version, normalizedRange) <= 0;
            case "<":
                return CompareVersions(version, normalizedRange) < 0;
            default:
                return false;
        }
    }

    /// <summary>
    /// Matches partial wildcard patterns like 1.x, 1.2.x, etc.
    /// Examples:
    /// - 1.x matches any 1.*.* version
    /// - 1.2.x matches any 1.2.* version
    /// </summary>
    private bool MatchesPartialWildcard(string rangeVersion, string version)
    {
        // Replace x or X with a marker to parse
        var normalized = rangeVersion.Replace("x", "0").Replace("X", "0");
        var rangeParts = ParseVersion(normalized);
        var versionParts = ParseVersion(version);

        if (rangeParts == null || versionParts == null)
        {
            return false;
        }

        // Count how many parts are specified (not wildcards)
        var originalParts = rangeVersion.Split('.');
        
        // Check major version
        if (!originalParts[0].Equals("x", StringComparison.OrdinalIgnoreCase) &&
            !originalParts[0].Equals("X", StringComparison.OrdinalIgnoreCase))
        {
            if (rangeParts.Major != versionParts.Major)
            {
                return false;
            }
        }

        // Check minor version if specified
        if (originalParts.Length >= 2 && 
            !originalParts[1].Equals("x", StringComparison.OrdinalIgnoreCase) &&
            !originalParts[1].Equals("X", StringComparison.OrdinalIgnoreCase))
        {
            if (rangeParts.Minor != versionParts.Minor)
            {
                return false;
            }
        }

        // Check patch version if specified
        if (originalParts.Length >= 3 && 
            !originalParts[2].Equals("x", StringComparison.OrdinalIgnoreCase) &&
            !originalParts[2].Equals("X", StringComparison.OrdinalIgnoreCase))
        {
            if (rangeParts.Patch != versionParts.Patch)
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>
    /// Matches caret range (^) - allows changes that do not modify the left-most non-zero digit.
    /// ^1.2.3 := >=1.2.3 <2.0.0
    /// ^0.2.3 := >=0.2.3 <0.3.0
    /// ^0.0.3 := >=0.0.3 <0.0.4
    /// </summary>
    private bool MatchesCaretRange(string rangeVersion, string version)
    {
        var rangeParts = ParseVersion(rangeVersion);
        var versionParts = ParseVersion(version);

        if (rangeParts == null || versionParts == null)
        {
            return false;
        }

        // Version must be >= range version
        if (CompareVersions(version, rangeVersion) < 0)
        {
            return false;
        }

        // If major version is not 0, minor and patch can vary
        if (rangeParts.Major > 0)
        {
            return versionParts.Major == rangeParts.Major;
        }

        // If major is 0, minor is not 0, patch can vary
        if (rangeParts.Minor > 0)
        {
            return versionParts.Major == rangeParts.Major && 
                   versionParts.Minor == rangeParts.Minor;
        }

        // If major and minor are 0, patch must match exactly
        return versionParts.Major == rangeParts.Major && 
               versionParts.Minor == rangeParts.Minor && 
               versionParts.Patch == rangeParts.Patch;
    }

    /// <summary>
    /// Matches tilde range (~) - allows patch-level changes if a minor version is specified.
    /// ~1.2.3 := >=1.2.3 <1.3.0
    /// ~1.2 := >=1.2.0 <1.3.0
    /// ~1 := >=1.0.0 <2.0.0
    /// </summary>
    private bool MatchesTildeRange(string rangeVersion, string version)
    {
        var rangeParts = ParseVersion(rangeVersion);
        var versionParts = ParseVersion(version);

        if (rangeParts == null || versionParts == null)
        {
            return false;
        }

        // Version must be >= range version
        if (CompareVersions(version, rangeVersion) < 0)
        {
            return false;
        }

        // Count how many parts are specified in the range
        var rangeDots = rangeVersion.Count(c => c == '.');

        // If only major version is specified (~1), allow any minor version within that major version
        if (rangeDots == 0)
        {
            return versionParts.Major == rangeParts.Major;
        }

        // If major and minor are specified (~1.2), require both to match
        return versionParts.Major == rangeParts.Major && 
               versionParts.Minor == rangeParts.Minor;
    }

    /// <summary>
    /// Matches exact version or partial version specification.
    /// Handles cases like "1.2.3", "1.2", "1" matching against full versions.
    /// </summary>
    private bool MatchesExactOrPartial(string rangeVersion, string version)
    {
        var rangeParts = ParseVersion(rangeVersion);
        var versionParts = ParseVersion(version);

        if (rangeParts == null || versionParts == null)
        {
            return false;
        }

        // Check major version
        if (rangeParts.Major != versionParts.Major)
        {
            return false;
        }

        // If range has minor specified, it must match
        var rangeDots = rangeVersion.Count(c => c == '.');
        if (rangeDots >= 1 && rangeParts.Minor != versionParts.Minor)
        {
            return false;
        }

        // If range has patch specified, it must match
        if (rangeDots >= 2 && rangeParts.Patch != versionParts.Patch)
        {
            return false;
        }

        return true;
    }

    /// <summary>
    /// Compares two version strings.
    /// </summary>
    /// <returns>-1 if version1 < version2, 0 if equal, 1 if version1 > version2</returns>
    private int CompareVersions(string version1, string version2)
    {
        var v1 = ParseVersion(version1);
        var v2 = ParseVersion(version2);

        if (v1 == null || v2 == null)
        {
            return 0;
        }

        if (v1.Major != v2.Major)
            return v1.Major.CompareTo(v2.Major);

        if (v1.Minor != v2.Minor)
            return v1.Minor.CompareTo(v2.Minor);

        return v1.Patch.CompareTo(v2.Patch);
    }

    /// <summary>
    /// Parses a version string into major, minor, and patch components.
    /// </summary>
    private VersionParts? ParseVersion(string version)
    {
        version = NormalizeVersion(version);

        var parts = version.Split('.');
        
        if (parts.Length == 0)
        {
            return null;
        }

        int major = 0, minor = 0, patch = 0;

        if (parts.Length >= 1 && int.TryParse(parts[0], out var majorVal))
        {
            major = majorVal;
        }
        else
        {
            return null;
        }

        if (parts.Length >= 2 && int.TryParse(parts[1], out var minorVal))
        {
            minor = minorVal;
        }

        if (parts.Length >= 3)
        {
            // Handle patch with pre-release or build metadata (e.g., "1.2.3-beta" or "1.2.3+build")
            var patchPart = parts[2].Split(new[] { '-', '+' }, 2)[0];
            if (int.TryParse(patchPart, out var patchVal))
            {
                patch = patchVal;
            }
        }

        return new VersionParts(major, minor, patch);
    }

    /// <summary>
    /// Normalizes a version string by removing 'v' prefix and whitespace.
    /// </summary>
    private string NormalizeVersion(string version)
    {
        version = version.Trim();
        
        if (version.StartsWith("v", StringComparison.OrdinalIgnoreCase))
        {
            version = version.Substring(1);
        }

        return version;
    }

    private record VersionParts(int Major, int Minor, int Patch);

    /// <summary>
    /// Finds the latest version from a list of available versions.
    /// </summary>
    /// <param name="versions">List of versions to choose from</param>
    /// <returns>The latest version, or null if the list is empty</returns>
    public string? FindLatestVersion(IEnumerable<string> versions)
    {
        if (versions == null || !versions.Any())
        {
            return null;
        }

        // Return the latest version by sorting in descending order
        return versions
            .OrderByDescending(v => ParseVersion(v), new VersionPartsComparer())
            .FirstOrDefault();
    }

    /// <summary>
    /// Comparer for VersionParts to enable sorting
    /// </summary>
    private class VersionPartsComparer : IComparer<VersionParts?>
    {
        public int Compare(VersionParts? x, VersionParts? y)
        {
            if (x == null && y == null) return 0;
            if (x == null) return -1;
            if (y == null) return 1;

            if (x.Major != y.Major)
                return x.Major.CompareTo(y.Major);

            if (x.Minor != y.Minor)
                return x.Minor.CompareTo(y.Minor);

            return x.Patch.CompareTo(y.Patch);
        }
    }
}

