namespace DependencyCalculator.Models;

/// <summary>
/// Represents CVE (Common Vulnerabilities and Exposures) information for a package
/// </summary>
public class CveInfo
{
    public string PackageName { get; set; }
    public string Version { get; set; }
    public List<CveItem> Vulnerabilities { get; set; }

    public CveInfo(string packageName, string version)
    {
        PackageName = packageName ?? throw new ArgumentNullException(nameof(packageName));
        Version = version ?? throw new ArgumentNullException(nameof(version));
        Vulnerabilities = new List<CveItem>();
    }

    public int TotalVulnerabilities => Vulnerabilities.Count;

    public int CriticalCount => Vulnerabilities.Count(v => v.Severity == "CRITICAL");
    public int HighCount => Vulnerabilities.Count(v => v.Severity == "HIGH");
    public int MediumCount => Vulnerabilities.Count(v => v.Severity == "MEDIUM");
    public int LowCount => Vulnerabilities.Count(v => v.Severity == "LOW");

    public override string ToString()
    {
        return $"{PackageName}@{Version}: {TotalVulnerabilities} vulnerabilities (Critical: {CriticalCount}, High: {HighCount}, Medium: {MediumCount}, Low: {LowCount})";
    }
}

/// <summary>
/// Represents a single CVE item with detailed information
/// </summary>
public class CveItem
{
    public string CveId { get; set; }
    public string Description { get; set; }
    public string Severity { get; set; }
    public double? CvssScore { get; set; }
    public DateTime? PublishedDate { get; set; }
    public DateTime? LastModifiedDate { get; set; }
    public List<string> References { get; set; }

    public CveItem(string cveId, string description, string severity)
    {
        CveId = cveId ?? throw new ArgumentNullException(nameof(cveId));
        Description = description ?? string.Empty;
        Severity = severity ?? "UNKNOWN";
        References = new List<string>();
    }

    public override string ToString()
    {
        var scoreInfo = CvssScore.HasValue ? $" (CVSS: {CvssScore:F1})" : "";
        return $"{CveId} - {Severity}{scoreInfo}: {Description}";
    }
}
