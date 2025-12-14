using System.Text.Json;
using System.Text.Json.Serialization;
using DependencyCalculator.Models;

namespace DependencyCalculator.Services;

/// <summary>
/// Client for fetching CVE (Common Vulnerabilities and Exposures) information from various sources
/// Currently supports the NVD (National Vulnerability Database) API
/// </summary>
public class CveService : ICveService
{
    private readonly HttpClient _httpClient;
    private readonly ICveCacheService _cacheService;
    private const string NVD_API_BASE_URL = "https://services.nvd.nist.gov/rest/json/cves/2.0";

    // Note: For production use, you should get an API key from https://nvd.nist.gov/developers/request-an-api-key
    // Without an API key, you're limited to 5 requests per 30 seconds
    private readonly string? _apiKey;

    public CveService(HttpClient httpClient, ICveCacheService cveCacheService, string? apiKey = null)
    {
        _httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
        _cacheService = cveCacheService ?? throw new ArgumentNullException(nameof(cveCacheService));
        _apiKey = apiKey;

        // Configure headers
        _httpClient.DefaultRequestHeaders.Add("User-Agent", "dependency-calculator/1.0");

        if (!string.IsNullOrEmpty(_apiKey))
        {
            _httpClient.DefaultRequestHeaders.Add("apiKey", _apiKey);
        }
    }

    /// <summary>
    /// Fetches CVE information for a list of packages
    /// </summary>
    /// <param name="packages">List of package information</param>
    /// <returns>List of CVE information for each package</returns>
    public async Task<List<CveInfo>> GetCvesForPackagesAsync(List<NpmPackageInfo> packages)
    {
        var results = new List<CveInfo>();

        foreach (var package in packages)
        {
            var cveInfo = await GetCvesForPackageAsync(package.PackageName, package.Version);
            results.Add(cveInfo);

            // Rate limiting: wait between requests to avoid hitting API limits
            // With API key: 50 requests per 30 seconds
            // Without API key: 5 requests per 30 seconds
            var delayMs = string.IsNullOrEmpty(_apiKey) ? 6000 : 600;
            await Task.Delay(delayMs);
        }

        return results;
    }

    /// <summary>
    /// Fetches CVE information for a single package
    /// </summary>
    /// <param name="packageName">Name of the package (e.g., "express", "react")</param>
    /// <param name="version">Version of the package (e.g., "4.18.2")</param>
    /// <returns>CVE information for the package</returns>
    public async Task<CveInfo> GetCvesForPackageAsync(string packageName, string version)
    {
        try
        {
            // Try to get from cache first
            var cachedCveInfo = await _cacheService.GetValueAsync(packageName, version);
            if (cachedCveInfo != null)
            {
                var cveInfo = new CveInfo(packageName, version);
                foreach (var cveItem in cachedCveInfo)
                {
                    cveInfo.Vulnerabilities.Add(cveItem);
                }
                Console.WriteLine($"   CVE Check (cached): {packageName}@{version}");
                return cveInfo;
            }
            else
            {
                // Not in cache, fetch from API
                Console.WriteLine($"   CVE Check (fetching): {packageName}@{version}");
                var fetchedCveInfo = await FetchCveDataFromApiAsync(packageName, version);
                
                // Cache the result
                await _cacheService.SetValueAsync(packageName, version, fetchedCveInfo.Vulnerabilities);
                
                return fetchedCveInfo;
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"   ⚠️  Warning: Failed to fetch CVE data for {packageName}@{version}: {ex.Message}");
            throw;
        }
    }

    /// <summary>
    /// Fetches CVE information from the API (not cached)
    /// </summary>
    private async Task<CveInfo> FetchCveDataFromApiAsync(string packageName, string version)
    {
        var cveInfo = new CveInfo(packageName, version);

        try
        {
            // Search for CVEs related to the package
            // NPM packages are typically referenced as "npm:packageName" in CPE format
            var keyword = $"npm {packageName}";
            var encodedKeyword = Uri.EscapeDataString(keyword);

            var url = $"{NVD_API_BASE_URL}?keywordSearch={encodedKeyword}";

            Console.WriteLine($"Fetching CVEs for {packageName}@{version}...");

            var response = await _httpClient.GetAsync(url);

            if (!response.IsSuccessStatusCode)
            {
                Console.WriteLine($"Warning: Failed to fetch CVEs for {packageName}: {response.StatusCode}");
                return cveInfo;
            }

            var content = await response.Content.ReadAsStringAsync();
            var nvdResponse = JsonSerializer.Deserialize<NvdResponse>(content, new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true
            });

            if (nvdResponse?.Vulnerabilities != null)
            {
                foreach (var vuln in nvdResponse.Vulnerabilities)
                {
                    if (vuln?.Cve != null)
                    {
                        var cveItem = ParseNvdVulnerability(vuln);
                        if (cveItem != null)
                        {
                            cveInfo.Vulnerabilities.Add(cveItem);
                        }
                    }
                }
            }

            Console.WriteLine($"Found {cveInfo.TotalVulnerabilities} CVEs for {packageName}@{version}");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error fetching CVEs for {packageName}: {ex.Message}");
        }

        return cveInfo;
    }

    /// <summary>
    /// Parses NVD vulnerability data into a CveItem
    /// </summary>
    private CveItem? ParseNvdVulnerability(NvdVulnerability vuln)
    {
        try
        {
            var cveId = vuln.Cve?.Id ?? "UNKNOWN";
            var description = vuln.Cve?.Descriptions?.FirstOrDefault(d => d.Lang == "en")?.Value ?? "No description available";

            // Extract CVSS score and severity
            var cvssScore = vuln.Cve?.Metrics?.CvssMetricV31?.FirstOrDefault()?.CvssData?.BaseScore
                           ?? vuln.Cve?.Metrics?.CvssMetricV30?.FirstOrDefault()?.CvssData?.BaseScore
                           ?? vuln.Cve?.Metrics?.CvssMetricV2?.FirstOrDefault()?.CvssData?.BaseScore;

            var severity = vuln.Cve?.Metrics?.CvssMetricV31?.FirstOrDefault()?.CvssData?.BaseSeverity
                          ?? vuln.Cve?.Metrics?.CvssMetricV30?.FirstOrDefault()?.CvssData?.BaseSeverity
                          ?? DetermineSeverityFromScore(cvssScore)
                          ?? "UNKNOWN";

            var cveItem = new CveItem(cveId, description, severity)
            {
                CvssScore = cvssScore,
                PublishedDate = vuln.Cve?.Published,
                LastModifiedDate = vuln.Cve?.LastModified
            };

            // Extract references
            if (vuln.Cve?.References != null)
            {
                cveItem.References = vuln.Cve.References
                    .Select(r => r.Url)
                    .Where(url => !string.IsNullOrEmpty(url))
                    .ToList()!;
            }

            return cveItem;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error parsing CVE: {ex.Message}");
            return null;
        }
    }

    /// <summary>
    /// Determines severity based on CVSS score when severity is not explicitly provided
    /// </summary>
    private string? DetermineSeverityFromScore(double? score)
    {
        if (!score.HasValue) return null;

        return score.Value switch
        {
            >= 9.0 => "CRITICAL",
            >= 7.0 => "HIGH",
            >= 4.0 => "MEDIUM",
            >= 0.1 => "LOW",
            _ => "NONE"
        };
    }

    #region NVD API Response Models

    private class NvdResponse
    {
        [JsonPropertyName("vulnerabilities")]
        public List<NvdVulnerability>? Vulnerabilities { get; set; }
    }

    private class NvdVulnerability
    {
        [JsonPropertyName("cve")]
        public NvdCve? Cve { get; set; }
    }

    private class NvdCve
    {
        [JsonPropertyName("id")]
        public string? Id { get; set; }

        [JsonPropertyName("descriptions")]
        public List<NvdDescription>? Descriptions { get; set; }

        [JsonPropertyName("metrics")]
        public NvdMetrics? Metrics { get; set; }

        [JsonPropertyName("published")]
        public DateTime? Published { get; set; }

        [JsonPropertyName("lastModified")]
        public DateTime? LastModified { get; set; }

        [JsonPropertyName("references")]
        public List<NvdReference>? References { get; set; }
    }

    private class NvdDescription
    {
        [JsonPropertyName("lang")]
        public string? Lang { get; set; }

        [JsonPropertyName("value")]
        public string? Value { get; set; }
    }

    private class NvdMetrics
    {
        [JsonPropertyName("cvssMetricV31")]
        public List<NvdCvssMetric>? CvssMetricV31 { get; set; }

        [JsonPropertyName("cvssMetricV30")]
        public List<NvdCvssMetric>? CvssMetricV30 { get; set; }

        [JsonPropertyName("cvssMetricV2")]
        public List<NvdCvssMetric>? CvssMetricV2 { get; set; }
    }

    private class NvdCvssMetric
    {
        [JsonPropertyName("cvssData")]
        public NvdCvssData? CvssData { get; set; }
    }

    private class NvdCvssData
    {
        [JsonPropertyName("baseScore")]
        public double? BaseScore { get; set; }

        [JsonPropertyName("baseSeverity")]
        public string? BaseSeverity { get; set; }
    }

    private class NvdReference
    {
        [JsonPropertyName("url")]
        public string? Url { get; set; }
    }

    #endregion
}
