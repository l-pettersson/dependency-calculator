using System.Diagnostics;
using System.Globalization;
using System.Text;
using DependencyCalculator.Data;
using DependencyCalculator.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace DependencyCalculator.Services;

/// <summary>
/// Performance tests for NpmVersionCalculator that measure execution time, 
/// dependency counts, and reward values across different parameter configurations.
/// Results are output in LaTeX table format for easy inclusion in academic papers.
/// </summary>
public class NpmVersionCalculatorPerformanceTests
{
    private readonly ServiceProvider _serviceProvider;
    private readonly string _npmTestDbPath;
    private readonly string _cveTestDbPath;
    private readonly string _resultsOutputPath;

    public NpmVersionCalculatorPerformanceTests()
    {
        var services = new ServiceCollection();

        // Use unique database files for each test run
        _npmTestDbPath = Path.Combine(Directory.GetCurrentDirectory(), $"npm_perf_test_{Guid.NewGuid()}.db");
        _cveTestDbPath = Path.Combine(Directory.GetCurrentDirectory(), $"cve_perf_test_{Guid.NewGuid()}.db");
        _resultsOutputPath = Path.Combine(Directory.GetCurrentDirectory(), "performance_results.tex");

        // Register memory cache
        services.AddMemoryCache();

        // Register NPM cache database context
        services.AddDbContext<NpmCacheDbContext>(options =>
        {
            options.UseSqlite($"Data Source={_npmTestDbPath}");
            Console.WriteLine($"SQLite NPM cache database location: {_npmTestDbPath}");
        }, ServiceLifetime.Singleton);

        // Register CVE cache database context
        services.AddDbContext<CveCacheDbContext>(options =>
        {
            options.UseSqlite($"Data Source={_cveTestDbPath}");
            Console.WriteLine($"SQLite CVE cache database location: {_cveTestDbPath}");
        }, ServiceLifetime.Singleton);

        // Register NPM cache service
        services.AddSingleton<INpmCacheService>(sp =>
        {
            var memoryCache = sp.GetRequiredService<Microsoft.Extensions.Caching.Memory.IMemoryCache>();
            var dbContext = sp.GetRequiredService<NpmCacheDbContext>();
            return new NpmCacheService(memoryCache, dbContext, useMemoryCache: true);
        });

        // Register CVE cache service
        services.AddSingleton<ICveCacheService>(sp =>
        {
            var memoryCache = sp.GetRequiredService<Microsoft.Extensions.Caching.Memory.IMemoryCache>();
            var dbContext = sp.GetRequiredService<CveCacheDbContext>();
            return new CveCacheService(memoryCache, dbContext, useMemoryCache: true);
        });

        // Register NPM version matcher
        services.AddSingleton<INpmVersionMatcher, NpmVersionMatcher>();

        // Register NPM config reader
        services.AddSingleton<NpmConfigReader>(provider =>
        {
            var projectRoot = Path.Combine(Directory.GetCurrentDirectory(), "..", "..", "..", "..");
            var npmrcPath = Path.Combine(projectRoot, ".npmrc");

            if (File.Exists(npmrcPath))
            {
                Console.WriteLine($"Using .npmrc from: {npmrcPath}");
                return NpmConfigReader.ReadFromFile(npmrcPath);
            }

            Console.WriteLine("No .npmrc file found, using default NPM registry");
            return new NpmConfigReader();
        });

        // Register HttpClient and services
        services.AddSingleton<INpmService, NpmService>(serviceProvider =>
        {
            var httpClient = new HttpClient();
            var config = serviceProvider.GetRequiredService<NpmConfigReader>();
            var versionMatcher = serviceProvider.GetRequiredService<INpmVersionMatcher>();
            var cacheService = serviceProvider.GetRequiredService<INpmCacheService>();
            return new NpmService(httpClient, config, versionMatcher, cacheService);
        });

        services.AddSingleton<ICveService, CveService>(serviceProvider =>
        {
            var httpClient = new HttpClient();
            var cacheService = serviceProvider.GetRequiredService<ICveCacheService>();
            return new CveService(httpClient, cacheService);
        });

        _serviceProvider = services.BuildServiceProvider();

        // Initialize databases
        using (var scope = _serviceProvider.CreateScope())
        {
            var npmDbContext = scope.ServiceProvider.GetRequiredService<NpmCacheDbContext>();
            Console.WriteLine("Initializing NPM cache database...");
            npmDbContext.Database.EnsureCreated();

            var cveDbContext = scope.ServiceProvider.GetRequiredService<CveCacheDbContext>();
            Console.WriteLine("Initializing CVE cache database...");
            cveDbContext.Database.EnsureCreated();

            // Load caches from database into memory
            var npmCacheService = _serviceProvider.GetRequiredService<INpmCacheService>();
            _ = npmCacheService.LoadCacheFromDatabaseAsync();

            var cveCacheService = _serviceProvider.GetRequiredService<ICveCacheService>();
            _ = cveCacheService.LoadCacheFromDatabaseAsync();
        }
    }

    // Constants for test configuration
    private const int NumberOfTrials = 5; // Number of times to repeat each test
    private const int RandomSeed = 42; // Fixed seed for reproducibility

    // Diverse test packages with varying complexity and dependency counts
    private static readonly List<Dictionary<string, string>> TestPackages = new List<Dictionary<string, string>>
    {
        // Small packages with few dependencies
        // new Dictionary<string, string> { { "lodash", "^4.17.0" } },
        new Dictionary<string, string> { { "axios", "^0.21.0" } },
        // new Dictionary<string, string> { { "chalk", "^4.0.0" } },
        // new Dictionary<string, string> { { "uuid", "^8.0.0" } },
        // new Dictionary<string, string> { { "dotenv", "^8.0.0" } },
        
        // Medium packages with moderate dependencies
        new Dictionary<string, string> { { "express", "^4.17.0" } },
        // new Dictionary<string, string> { { "react", "^17.0.0" } },
        // new Dictionary<string, string> { { "commander", "^7.0.0" } },
        // new Dictionary<string, string> { { "moment", "^2.29.0" } },
        new Dictionary<string, string> { { "yargs", "^16.0.0" } },
        
        // Larger packages with more dependencies
        new Dictionary<string, string> { { "webpack", "^5.0.0" } },
        new Dictionary<string, string> { { "eslint", "^7.0.0" } },
        new Dictionary<string, string> { { "react-scripts", "^5.0.01" } },
        // new Dictionary<string, string> { { "typescript", "^4.0.0" } },
        new Dictionary<string, string> { { "jest", "^26.0.0" } },
        new Dictionary<string, string> { { "babel-core", "^6.26.0" } },
        
        // Packages with specific version constraints
        new Dictionary<string, string> { { "react-dom", "^16.13.0" } },
        // new Dictionary<string, string> { { "redux", "^4.0.0" } },
        new Dictionary<string, string> { { "vue", "^2.6.0" } },
        // new Dictionary<string, string> { { "angular", "^11.0.0" } },
        new Dictionary<string, string> { { "next", "^10.0.0" } },
        
        // Additional variety
        // new Dictionary<string, string> { { "prettier", "^2.0.0" } },
        new Dictionary<string, string> { { "inquirer", "^7.0.0" } },
    };

    /// <summary>
    /// Test data for varying Max Depth parameter
    /// </summary>
    public static IEnumerable<object[]> GetMaxDepthTestParameters()
    {
        var depths = new[] { 2, 3, 4 };
        foreach (var depth in depths)
        {
            yield return new object[] { depth, 100, 50, 2.0 };
        }
    }

    /// <summary>
    /// Test data for varying Max Iterations parameter
    /// </summary>
    public static IEnumerable<object[]> GetMaxIterationsTestParameters()
    {
        var iterations = new[] { 50, 100, 200, 500, 1000 };
        foreach (var iter in iterations)
        {
            yield return new object[] { 3, iter, 50, 2.0 };
        }
    }

    /// <summary>
    /// Test data for varying Simulation Depth parameter
    /// </summary>
    public static IEnumerable<object[]> GetSimulationDepthTestParameters()
    {
        var simDepths = new[] { 5, 10, 15 };
        foreach (var simDepth in simDepths)
        {
            yield return new object[] { 3, 100, simDepth, 2.0 };
        }
    }

    /// <summary>
    /// Test data for varying Lambda (exploration) parameter
    /// </summary>
    public static IEnumerable<object[]> GetLambdaTestParameters()
    {
        var lambdas = new[] { 0.5, 1.0, 1.5, 2.0, 3.0, 5.0 };
        foreach (var lambda in lambdas)
        {
            yield return new object[] { 3, 100, 50, lambda };
        }
    }

    [Fact]
    public async Task RunAllParameterTests_AndGenerateAllTables()
    {
        Console.WriteLine("\n" + new string('=', 80));
        Console.WriteLine("RUNNING COMPREHENSIVE PERFORMANCE TEST SUITE");
        Console.WriteLine($"Trials per parameter: {NumberOfTrials}, Random Seed: {RandomSeed}");
        Console.WriteLine(new string('=', 80) + "\n");

        var allResults = new List<PerformanceTestResult>();

        // Test Max Depth variations
        Console.WriteLine("\n=== Testing Max Depth Variations ===\n");
        foreach (var testData in GetMaxDepthTestParameters())
        {
            var maxDepth = (int)testData[0];
            var maxIterations = (int)testData[1];
            var maxSimulationDepth = (int)testData[2];
            var lambda = (double)testData[3];

            var results = await RunTrialsAsync("MaxDepth", maxDepth, maxIterations, maxSimulationDepth, lambda);
            allResults.AddRange(results);
        }

        // Test Max Iterations variations
        Console.WriteLine("\n=== Testing Max Iterations Variations ===\n");
        foreach (var testData in GetMaxIterationsTestParameters())
        {
            var maxDepth = (int)testData[0];
            var maxIterations = (int)testData[1];
            var maxSimulationDepth = (int)testData[2];
            var lambda = (double)testData[3];

            var results = await RunTrialsAsync("MaxIterations", maxDepth, maxIterations, maxSimulationDepth, lambda);
            allResults.AddRange(results);
        }

        // Test Simulation Depth variations
        Console.WriteLine("\n=== Testing Simulation Depth Variations ===\n");
        foreach (var testData in GetSimulationDepthTestParameters())
        {
            var maxDepth = (int)testData[0];
            var maxIterations = (int)testData[1];
            var maxSimulationDepth = (int)testData[2];
            var lambda = (double)testData[3];

            var results = await RunTrialsAsync("SimulationDepth", maxDepth, maxIterations, maxSimulationDepth, lambda);
            allResults.AddRange(results);
        }

        // Test Lambda variations
        Console.WriteLine("\n=== Testing Lambda Variations ===\n");
        foreach (var testData in GetLambdaTestParameters())
        {
            var maxDepth = (int)testData[0];
            var maxIterations = (int)testData[1];
            var maxSimulationDepth = (int)testData[2];
            var lambda = (double)testData[3];

            var results = await RunTrialsAsync("Lambda", maxDepth, maxIterations, maxSimulationDepth, lambda);
            allResults.AddRange(results);
        }

        // Generate graphs
        Dictionary<string, List<AggregatedResult>> aggregatedData = AggregateAllResultsByFields(allResults);
        string latexCode = GenerateLatexDocument(aggregatedData);
        File.WriteAllText(_resultsOutputPath, latexCode);

        Console.WriteLine("LaTeX file generated. Compile with pdflatex.");
    }

    /// <summary>
    /// Run multiple trials across all test packages with the same parameters
    /// </summary>
    private async Task<List<PerformanceTestResult>> RunTrialsAsync(
        string parameterName,
        int maxDepth,
        int maxIterations,
        int maxSimulationDepth,
        double lambda)
    {
        var results = new List<PerformanceTestResult>();
        var random = new Random(RandomSeed);

        Console.WriteLine($"\n{'=',-80}");
        Console.WriteLine($"Testing: {parameterName} Variation");
        Console.WriteLine($"Parameters: Depth={maxDepth}, Iter={maxIterations}, SimDepth={maxSimulationDepth}, Lambda={lambda:F1}");
        Console.WriteLine($"Testing {TestPackages.Count} packages × {NumberOfTrials} trials = {TestPackages.Count * NumberOfTrials} total runs");
        Console.WriteLine($"Random Seed: {RandomSeed}");
        Console.WriteLine($"{'=',-80}\n");

        int totalRuns = 0;
        int totalRunsExpected = TestPackages.Count * NumberOfTrials;

        // Test each package multiple times
        for (int packageIndex = 0; packageIndex < TestPackages.Count; packageIndex++)
        {
            var packages = TestPackages[packageIndex];
            var packageName = packages.First().Key;
            var packageVersion = packages.First().Value;

            Console.WriteLine($"\nPackage {packageIndex + 1}/{TestPackages.Count}: {packageName}@{packageVersion}");

            for (int trial = 1; trial <= NumberOfTrials; trial++)
            {
                totalRuns++;
                Console.WriteLine($"  Trial {trial}/{NumberOfTrials} (Overall: {totalRuns}/{totalRunsExpected})...");

                var npmService = _serviceProvider.GetRequiredService<INpmService>();
                var cveService = _serviceProvider.GetRequiredService<ICveService>();
                var versionMatcher = _serviceProvider.GetRequiredService<INpmVersionMatcher>();

                var calculator = new NpmVersionCalculator(
                    versionMatcher,
                    npmService,
                    cveService,
                    maxIterations,
                    maxSimulationDepth,
                    maxDepth,
                    maxCompareVersion: 20,
                    lambda,
                    initVersions: false,
                    DependencyType.PeerDependencies,
                    cveThreshold: null
                );

                var stopwatch = Stopwatch.StartNew();
                var result = await calculator.CalculateOptimalVersionsAsync(packages);
                stopwatch.Stop();

                var success = result.Solution != null && result.Solution.Any() && string.IsNullOrEmpty(result.ErrorMessage);
                var totalDependencies = result.Solution?.Count ?? 0;

                var perfResult = new PerformanceTestResult
                {
                    TrialId = trial,
                    PackageName = packageName,
                    PackageVersion = packageVersion,
                    MaxDepth = maxDepth,
                    MaxIterations = maxIterations,
                    MaxSimulationDepth = maxSimulationDepth,
                    Lambda = lambda,
                    ExecutionTimeMs = stopwatch.ElapsedMilliseconds,
                    TotalDependenciesResolved = totalDependencies,
                    Success = success,
                    ErrorMessage = result.ErrorMessage
                };

                results.Add(perfResult);

                Console.WriteLine($"    {(success ? "✓" : "✗")} {perfResult.ExecutionTimeMs} ms - {perfResult.TotalDependenciesResolved} deps");
            }
        }

        Console.WriteLine($"\nCompleted {totalRuns} runs across {TestPackages.Count} packages");
        Console.WriteLine();
        return results;
    }


    private AggregatedResult AggregateResults(string parameterName, string parameterValue, List<PerformanceTestResult> results)
    {
        var successfulTrials = results.Where(r => r.Success).ToList();
        var successCount = successfulTrials.Count;
        var totalTrials = results.Count;

        // Calculate the dependency/time trend for successful trials
        var dependencyTimeTrend = successfulTrials
            .GroupBy(r => r.TotalDependenciesResolved) // Group all successful trials by the dependency count
            .Select(g => new DependencyTimeMetrics
            {
                TotalDependenciesResolved = g.Key,
                AvgTimeMs = g.Average(r => r.ExecutionTimeMs), // Calculate average time for that dependency count
                Count = g.Count()
            })
            .OrderBy(m => m.TotalDependenciesResolved) // Order for easier trend visualization
            .ToList();

        return new AggregatedResult
        {
            ParameterName = parameterName,
            ParameterValue = parameterValue,
            AvgTimeMs = results.Average(r => r.ExecutionTimeMs),
            SuccessCount = successCount,
            TotalTrials = totalTrials,
            SuccessRate = totalTrials > 0 ? (successCount / (double)totalTrials * 100) : 0,
            DependencyTimeTrend = dependencyTimeTrend
        };
    }

    public Dictionary<string, List<AggregatedResult>> AggregateAllResultsByFields(List<PerformanceTestResult> allResults)
    {
        var aggregatedResults = new Dictionary<string, List<AggregatedResult>>();

        // Aggregate by PackageName
        aggregatedResults["PackageName"] = allResults
            .GroupBy(r => r.PackageName)
            .Select(group => AggregateResults("PackageName", group.Key, group.ToList()))
            .ToList();

        // Aggregate by PackageVersion
        aggregatedResults["PackageVersion"] = allResults
            .GroupBy(r => r.PackageVersion)
            .Select(group => AggregateResults("PackageVersion", group.Key, group.ToList()))
            .ToList();

        // Aggregate by MaxDepth
        aggregatedResults["MaxDepth"] = allResults
            .GroupBy(r => r.MaxDepth)
            .Select(group => AggregateResults("MaxDepth", group.Key.ToString(), group.ToList()))
            .ToList();

        // Aggregate by MaxIterations
        aggregatedResults["MaxIterations"] = allResults
            .GroupBy(r => r.MaxIterations)
            .Select(group => AggregateResults("MaxIterations", group.Key.ToString(), group.ToList()))
            .ToList();

        // Aggregate by MaxSimulationDepth
        aggregatedResults["MaxSimulationDepth"] = allResults
            .GroupBy(r => r.MaxSimulationDepth)
            .Select(group => AggregateResults("MaxSimulationDepth", group.Key.ToString(), group.ToList()))
            .ToList();

        // Aggregate by Lambda
        aggregatedResults["Lambda"] = allResults
            .GroupBy(r => r.Lambda)
            .Select(group => AggregateResults("Lambda", group.Key.ToString(), group.ToList()))
            .ToList();

        return aggregatedResults;
    }

    public string GenerateLatexDocument(Dictionary<string, List<AggregatedResult>> aggregatedData)
    {
        var sb = new StringBuilder();

        // 1. LaTeX Preamble
        sb.AppendLine(@"\documentclass{article}");
        sb.AppendLine(@"\usepackage{pgfplots}");
        sb.AppendLine(@"\pgfplotsset{compat=1.18}");
        sb.AppendLine(@"\usepackage[margin=1in]{geometry}");
        sb.AppendLine(@"\begin{document}");
        sb.AppendLine(@"\section*{Performance Trends: Time vs Dependencies}");
        sb.AppendLine(@"\p{These graphs visualize how Execution Time scales with Dependencies, split by specific configuration parameters.}");

        // 2. Generate Graphs for the 4 specific parameters
        sb.AppendLine(GenerateGraphForParameter("MaxDepth", aggregatedData));
        sb.AppendLine(GenerateGraphForParameter("MaxIterations", aggregatedData));
        sb.AppendLine(GenerateGraphForParameter("MaxSimulationDepth", aggregatedData));
        sb.AppendLine(GenerateGraphForParameter("Lambda", aggregatedData));

        sb.AppendLine(@"\end{document}");

        return sb.ToString();
    }

    private string GenerateGraphForParameter(string paramKey, Dictionary<string, List<AggregatedResult>> data)
    {
        if (!data.ContainsKey(paramKey)) return string.Empty;

        var results = data[paramKey];

        // Sort results to ensure the Legend is in a logical order (e.g. Lambda 0.1, 0.5, 0.9)
        var sortedResults = results.OrderBy(r => TryParseDouble(r.ParameterValue)).ToList();

        var sb = new StringBuilder();

        sb.AppendLine(@"\begin{figure}[h!]");
        sb.AppendLine(@"\centering");
        sb.AppendLine(@"\begin{tikzpicture}");
        sb.AppendLine(@"\begin{axis}[");
        sb.AppendLine($"    title={{Impact of {paramKey} on Performance}},");
        sb.AppendLine(@"    xlabel={Total Dependencies Resolved},");
        sb.AppendLine(@"    ylabel={Avg Execution Time (ms)},");
        sb.AppendLine(@"    grid=major,");
        sb.AppendLine(@"    width=0.85\textwidth,");
        sb.AppendLine(@"    height=8cm,");
        sb.AppendLine(@"    legend pos=outer north east,");
        sb.AppendLine(@"    legend cell align={left}");
        sb.AppendLine(@"]");

        foreach (var res in sortedResults)
        {
            // If there is no trend data, skip this line
            if (res.DependencyTimeTrend == null || !res.DependencyTimeTrend.Any())
                continue;

            sb.AppendLine();
            sb.Append($"\\addplot coordinates {{");

            // Add coordinates (x=Dependencies, y=Time)
            foreach (var point in res.DependencyTimeTrend)
            {
                // Format numbers to ensure standard decimal points for LaTeX
                string x = point.TotalDependenciesResolved.ToString(CultureInfo.InvariantCulture);
                string y = point.AvgTimeMs.ToString("F2", CultureInfo.InvariantCulture);
                sb.Append($"({x},{y}) ");
            }

            sb.AppendLine("};");

            // Add Legend Entry
            sb.AppendLine($"\\addlegendentry{{{res.ParameterName}={res.ParameterValue}}}");
        }

        sb.AppendLine(@"\end{axis}");
        sb.AppendLine(@"\end{tikzpicture}");
        sb.AppendLine($"\\caption{{Trend analysis for varying {paramKey}.}}");
        sb.AppendLine(@"\end{figure}");
        sb.AppendLine(@"\newpage"); // Force page break for readability

        return sb.ToString();
    }

    // Helper to sort numeric strings correctly (so "10" comes after "2", not before)
    private double TryParseDouble(string input)
    {
        if (double.TryParse(input, NumberStyles.Any, CultureInfo.InvariantCulture, out double result))
        {
            return result;
        }
        return 0; // Fallback
    }

    private string GenerateSummaryTable(List<AggregatedResult> results)
    {
        var sb = new StringBuilder();

        sb.AppendLine("% Performance Test Summary Results");
        sb.AppendLine($"% Generated: {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
        sb.AppendLine($"% Random Seed: {RandomSeed}, Trials per parameter: {NumberOfTrials}");
        sb.AppendLine();
        sb.AppendLine("\\begin{sidewaystable}[htbp]");
        sb.AppendLine("\\centering");
        sb.AppendLine("\\caption{Aggregated Performance Results Across All Parameter Variations}");
        sb.AppendLine("\\label{tab:performance-summary}");
        sb.AppendLine("\\begin{tabular}{|l|l|r|r|r|}");
        sb.AppendLine("\\hline");
        sb.AppendLine("\\textbf{Parameter} & \\textbf{Value} & \\textbf{Avg Time (ms)} & " +
                      "\\textbf{Success Rate (\\%)} & \\textbf{Avg Deps} \\\\");
        sb.AppendLine("\\hline");

        // Group by parameter name
        var grouped = results.GroupBy(r => r.ParameterName);
        foreach (var group in grouped)
        {
            foreach (var result in group)
            {
                foreach (var depMetric in result.DependencyTimeTrend)
                {
                    sb.AppendLine($"%   Deps: {depMetric.TotalDependenciesResolved}, " +
                                  $"Avg Time: {depMetric.AvgTimeMs:F2} ms, " +
                                  $"Count: {depMetric.Count}");
                }
            }
            sb.AppendLine("\\hline");
        }

        sb.AppendLine("\\end{tabular}");
        sb.AppendLine("\\end{sidewaystable}");

        return sb.ToString();
    }

}

/// <summary>
/// Data structure to hold performance test results
/// </summary>
public class PerformanceTestResult
{
    public int TrialId { get; set; }
    public string PackageName { get; set; } = string.Empty;
    public string PackageVersion { get; set; } = string.Empty;
    public int MaxDepth { get; set; }
    public int MaxIterations { get; set; }
    public int MaxSimulationDepth { get; set; }
    public double Lambda { get; set; }
    public long ExecutionTimeMs { get; set; }
    public int TotalDependenciesResolved { get; set; }
    public bool Success { get; set; }
    public string? ErrorMessage { get; set; }
}

/// <summary>
/// Holds the average execution time for a specific number of dependencies resolved.
/// This allows analyzing the trend: (Dependencies resolved) vs (Average Time).
/// </summary>
public class DependencyTimeMetrics
{
    public int TotalDependenciesResolved { get; set; }
    public double AvgTimeMs { get; set; }
    public int Count { get; set; } // How many successful trials resulted in this dependency count
}

/// <summary>
/// Data structure to hold aggregated results across multiple trials
/// </summary>
public class AggregatedResult
{
    public string ParameterName { get; set; } = string.Empty;
    public string ParameterValue { get; set; } = string.Empty;
    public double AvgTimeMs { get; set; }
    public int SuccessCount { get; set; }
    public int TotalTrials { get; set; }
    public double SuccessRate { get; set; }
    public List<DependencyTimeMetrics> DependencyTimeTrend { get; set; } = new List<DependencyTimeMetrics>();
}
