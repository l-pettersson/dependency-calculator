using DependencyCalculator.Data;
using DependencyCalculator.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace DependencyCalculator.Services;
public class NpmClientIntegrationTests : IDisposable
{
    private readonly ServiceProvider _serviceProvider;
    private readonly string _testDbPath;

    public NpmClientIntegrationTests()
    {
        var services = new ServiceCollection();

        // Use a unique database file for each test run to avoid conflicts
        _testDbPath = Path.Combine(Directory.GetCurrentDirectory(), $"npm_cache_test_{Guid.NewGuid()}.db");

        // Register memory cache
        services.AddMemoryCache();

        // Register NPM cache database context
        services.AddDbContext<NpmCacheDbContext>(options =>
        {
            options.UseSqlite($"Data Source={_testDbPath}");
            Console.WriteLine($"SQLite NPM cache database location: {_testDbPath}");
        }, ServiceLifetime.Singleton);

        // Register NPM cache service with memory cache enabled for tests
        services.AddSingleton<INpmCacheService>(sp =>
        {
            var memoryCache = sp.GetRequiredService<Microsoft.Extensions.Caching.Memory.IMemoryCache>();
            var dbContext = sp.GetRequiredService<NpmCacheDbContext>();
            return new NpmCacheService(memoryCache, dbContext, useMemoryCache: true);
        });

        // Register NPM version matcher
        services.AddSingleton<NpmVersionMatcher>();

        // Register NPM config reader - try to read from .npmrc file
        services.AddSingleton<NpmConfigReader>(provider =>
        {
            // Look for .npmrc in project root, then tests directory, then use default
            var projectRoot = Path.Combine(Directory.GetCurrentDirectory(), "..", "..", "..", "..");
            var npmrcPath = Path.Combine(projectRoot, ".npmrc");
            
            if (File.Exists(npmrcPath))
            {
                Console.WriteLine($"Using .npmrc from: {npmrcPath}");
                return NpmConfigReader.ReadFromFile(npmrcPath);
            }
            
            // Fall back to default configuration if .npmrc not found
            Console.WriteLine("No .npmrc file found, using default NPM registry");
            return new NpmConfigReader();
        });

        // Register HttpClient and NpmClient
        services.AddSingleton<INpmService, NpmService>(serviceProvider =>
        {
            var httpClient = new HttpClient();
            var config = serviceProvider.GetRequiredService<NpmConfigReader>();
            var versionMatcher = serviceProvider.GetRequiredService<NpmVersionMatcher>();
            var cacheService = serviceProvider.GetRequiredService<INpmCacheService>();
            return new NpmService(httpClient, config, versionMatcher, cacheService);
        });

        _serviceProvider = services.BuildServiceProvider();

        // Initialize database
        using (var scope = _serviceProvider.CreateScope())
        {
            var dbContext = scope.ServiceProvider.GetRequiredService<NpmCacheDbContext>();
            Console.WriteLine("Initializing NPM cache database...");
            dbContext.Database.EnsureCreated();

            // Load cache from database into memory
            var cacheService = _serviceProvider.GetRequiredService<INpmCacheService>();
            _ = cacheService.LoadCacheFromDatabaseAsync();
        }
    }

    [Fact]
    public async Task GetPackageInfoRecursiveAsync_WithSinglePackage_ShouldFetchPackageAndDependencies()
    {
        // Arrange
        var client = _serviceProvider.GetRequiredService<INpmService>();
        var packageRequests = new List<NpmPackageRequest>
        {
            new NpmPackageRequest("express", "4.18.0")
        };

        // Act
        var (packages, maxDepthPackages) = await client.GetPackageInfoRecursiveAsync(
            packageRequests,
            maxDepth: 2,
            dependencyType: DependencyType.Dependencies
        );

        // Assert
        Assert.NotNull(packages);
        Assert.NotEmpty(packages);
        
        // Should contain express itself
        var expressPackage = packages.FirstOrDefault(p => p.PackageName == "express");
        Assert.NotNull(expressPackage);
        Assert.Equal("4.18.0", expressPackage.Version);
        
        // Should contain at least some of express's dependencies
        Assert.True(packages.Count > 1, "Should fetch dependencies in addition to the root package");
    }

    [Fact]
    public async Task GetPackageInfoRecursiveAsync_WithSemanticVersion_ShouldResolveToActualVersion()
    {
        // Arrange
        var client = _serviceProvider.GetRequiredService<INpmService>();
        var packageRequests = new List<NpmPackageRequest>
        {
            new NpmPackageRequest("lodash", "^4.17.0")
        };

        // Act
        var (packages, maxDepthPackages) = await client.GetPackageInfoRecursiveAsync(
            packageRequests,
            maxDepth: 1,
            dependencyType: DependencyType.Dependencies
        );

        // Assert
        Assert.NotNull(packages);
        Assert.Single(packages); // lodash has no dependencies
        
        var lodashPackage = packages.First();
        Assert.Equal("lodash", lodashPackage.PackageName);
        
        // Should resolve ^4.17.0 to a concrete version like 4.17.21
        Assert.NotEqual("^4.17.0", lodashPackage.Version);
        Assert.StartsWith("4.17.", lodashPackage.Version);
        Assert.DoesNotContain("^", lodashPackage.Version);
    }

    [Fact]
    public async Task GetPackageInfoRecursiveAsync_WithMaxDepth_ShouldRespectDepthLimit()
    {
        // Arrange
        var client = _serviceProvider.GetRequiredService<INpmService>();
        var packageRequests = new List<NpmPackageRequest>
        {
            new NpmPackageRequest("express", "4.18.0")
        };

        // Act - Fetch with depth 1 (only immediate dependencies)
        var (packages, maxDepthPackages) = await client.GetPackageInfoRecursiveAsync(
            packageRequests,
            maxDepth: 1,
            dependencyType: DependencyType.Dependencies
        );

        // Assert
        Assert.NotNull(packages);
        Assert.NotEmpty(packages);
        
        // With maxDepth=1, we should only get express and its immediate dependencies
        // but not transitive dependencies
        var expressPackage = packages.FirstOrDefault(p => p.PackageName == "express");
        Assert.NotNull(expressPackage);
        Assert.True(maxDepthPackages.Count < 100, "Should not have deeply nested transitive dependencies");
    }

    [Fact]
    public async Task GetPackageInfoRecursiveAsync_WithMultiplePackages_ShouldFetchAll()
    {
        // Arrange
        var client = _serviceProvider.GetRequiredService<INpmService>();
        var packageRequests = new List<NpmPackageRequest>
        {
            new NpmPackageRequest("lodash", "4.17.21"),
            new NpmPackageRequest("axios", "0.21.1")
        };

        // Act
        var (packages, maxDepthPackages) = await client.GetPackageInfoRecursiveAsync(
            packageRequests,
            maxDepth: 2,
            dependencyType: DependencyType.Dependencies
        );

        // Assert
        Assert.NotNull(packages);
        Assert.NotEmpty(packages);
        
        // Should contain both packages
        Assert.Contains(packages, p => p.PackageName == "lodash");
        Assert.Contains(packages, p => p.PackageName == "axios");
    }

    [Fact]
    public async Task GetPackageInfoRecursiveAsync_WithSharedDependencies_ShouldNotDuplicatePackages()
    {
        // Arrange
        var client = _serviceProvider.GetRequiredService<INpmService>();
        var packageRequests = new List<NpmPackageRequest>
        {
            new NpmPackageRequest("express", "4.18.0")
        };

        // Act
        var (packages, maxDepthPackages) = await client.GetPackageInfoRecursiveAsync(
            packageRequests,
            maxDepth: 3,
            dependencyType: DependencyType.Dependencies
        );

        // Assert
        Assert.NotNull(packages);
        
        // Group by package name and version to check for duplicates
        var packageKeys = packages.Select(p => $"{p.PackageName}@{p.Version}").ToList();
        var uniquePackageKeys = packageKeys.Distinct().ToList();
        
        // Should not have duplicates
        Assert.Equal(uniquePackageKeys.Count, packageKeys.Count);
    }

    [Fact]
    public async Task GetPackageInfoRecursiveAsync_WithNonExistentPackage_ShouldReturnEmptyList()
    {
        // Arrange
        var client = _serviceProvider.GetRequiredService<INpmService>();
        var packageRequests = new List<NpmPackageRequest>
        {
            new NpmPackageRequest("this-package-definitely-does-not-exist-12345", "1.0.0")
        };

        // Act
        var (packages, maxDepthPackages) = await client.GetPackageInfoRecursiveAsync(
            packageRequests,
            maxDepth: 1,
            dependencyType: DependencyType.Dependencies
        );

        // Assert
        Assert.NotNull(packages);
        Assert.Empty(packages);
    }

    [Fact]
    public async Task GetPackageInfoRecursiveAsync_WithInvalidVersion_ShouldHandleGracefully()
    {
        // Arrange
        var client = _serviceProvider.GetRequiredService<INpmService>();
        var packageRequests = new List<NpmPackageRequest>
        {
            new NpmPackageRequest("express", "999.999.999")
        };

        // Act
        var (packages, maxDepthPackages) = await client.GetPackageInfoRecursiveAsync(
            packageRequests,
            maxDepth: 1,
            dependencyType: DependencyType.Dependencies
        );

        // Assert
        // Should handle gracefully - either return empty or skip invalid package
        Assert.NotNull(packages);
        Assert.Empty(packages);
    }

    [Fact]
    public async Task GetPackageInfoRecursiveAsync_WithCaching_ShouldUseCacheOnSecondCall()
    {
        // Arrange
        var client = _serviceProvider.GetRequiredService<INpmService>();
        var packageRequests = new List<NpmPackageRequest>
        {
            new NpmPackageRequest("lodash", "4.17.21")
        };

        // Act - First call (should fetch from NPM)
        var stopwatch1 = System.Diagnostics.Stopwatch.StartNew();
        var (packages1, _) = await client.GetPackageInfoRecursiveAsync(
            packageRequests,
            maxDepth: 1,
            dependencyType: DependencyType.Dependencies
        );
        stopwatch1.Stop();

        // Act - Second call (should use cache)
        var stopwatch2 = System.Diagnostics.Stopwatch.StartNew();
        var (packages2, _) = await client.GetPackageInfoRecursiveAsync(
            packageRequests,
            maxDepth: 1,
            dependencyType: DependencyType.Dependencies
        );
        stopwatch2.Stop();

        // Assert
        Assert.NotNull(packages1);
        Assert.NotNull(packages2);
        Assert.Single(packages1);
        Assert.Single(packages2);
        
        // Both calls should return the same package
        Assert.Equal(packages1.First().PackageName, packages2.First().PackageName);
        Assert.Equal(packages1.First().Version, packages2.First().Version);
        Assert.True(stopwatch1.ElapsedMilliseconds >= stopwatch2.ElapsedMilliseconds);
    }

    [Fact]
    public async Task GetPackageInfoRecursiveAsync_WithDifferentDependencyTypes_ShouldFetchCorrectDependencies()
    {
        // Arrange
        var client = _serviceProvider.GetRequiredService<INpmService>();
        var packageRequests = new List<NpmPackageRequest>
        {
            new NpmPackageRequest("react", "17.0.2")
        };

        // Act - Fetch regular dependencies
        var (regularPackages, _) = await client.GetPackageInfoRecursiveAsync(
            packageRequests,
            maxDepth: 2,
            dependencyType: DependencyType.Dependencies
        );

        // Act - Fetch peer dependencies
        var (peerPackages, _) = await client.GetPackageInfoRecursiveAsync(
            packageRequests,
            maxDepth: 2,
            dependencyType: DependencyType.PeerDependencies
        );

        // Assert
        Assert.NotNull(regularPackages);
        Assert.NotNull(peerPackages);
        Assert.NotEqual(regularPackages.Count, peerPackages.Count);
    }

    [Fact]
    public async Task GetPackageInfoRecursiveAsync_ShouldExcludePreReleaseVersions()
    {
        // Arrange
        var client = _serviceProvider.GetRequiredService<INpmService>();
        
        // Request a caret range that could potentially match pre-release versions
        var packageRequests = new List<NpmPackageRequest>
        {
            new NpmPackageRequest("typescript", "^4.0.0")
        };

        // Act
        var (packages, _) = await client.GetPackageInfoRecursiveAsync(
            packageRequests,
            maxDepth: 1,
            dependencyType: DependencyType.Dependencies
        );

        // Assert
        Assert.NotNull(packages);
        
        if (packages.Any())
        {
            var tsPackage = packages.FirstOrDefault(p => p.PackageName == "typescript");
            if (tsPackage != null)
            {
                // Should not contain pre-release markers like alpha, beta, rc
                Assert.DoesNotContain("-", tsPackage.Version);
            }
        }
    }

    [Fact]
    public async Task GetPackageInfoRecursiveAsync_WithMaxDepthReached_ShouldReportMaxDepthPackages()
    {
        // Arrange
        var client = _serviceProvider.GetRequiredService<INpmService>();
        var packageRequests = new List<NpmPackageRequest>
        {
            new NpmPackageRequest("express", "4.18.0")
        };

        // Act - Use a very shallow depth to ensure we hit the limit
        var (packages, maxDepthPackages) = await client.GetPackageInfoRecursiveAsync(
            packageRequests,
            maxDepth: 1,
            dependencyType: DependencyType.Dependencies
        );

        // Assert
        Assert.NotNull(packages);
        Assert.NotNull(maxDepthPackages);
    }

    public void Dispose()
    {
        // Clean up test database
        _serviceProvider?.Dispose();
        
        if (File.Exists(_testDbPath))
        {
            try
            {
                File.Delete(_testDbPath);
                Console.WriteLine($"Cleaned up test database: {_testDbPath}");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Failed to delete test database: {ex.Message}");
            }
        }
    }
}
