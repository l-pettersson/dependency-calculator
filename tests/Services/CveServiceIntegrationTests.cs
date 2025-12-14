using DependencyCalculator.Data;
using DependencyCalculator.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace DependencyCalculator.Services;

public class CveClientIntegrationTests : IDisposable
{
    private readonly ServiceProvider _serviceProvider;
    private readonly string _testDbPath;

    public CveClientIntegrationTests()
    {
        var services = new ServiceCollection();

        // Use a unique database file for each test run to avoid conflicts
        _testDbPath = Path.Combine(Directory.GetCurrentDirectory(), $"cve_cache_{Guid.NewGuid()}.db");

        // Register memory cache
        services.AddMemoryCache();

        // Register CVE cache database context
        services.AddDbContext<CveCacheDbContext>(options =>
        {
            options.UseSqlite($"Data Source={_testDbPath}");
            Console.WriteLine($"SQLite cache database location: {_testDbPath}");
        }, ServiceLifetime.Singleton);

        // Register CVE cache service with memory cache enabled for tests
        services.AddSingleton<ICveCacheService>(sp =>
        {
            var memoryCache = sp.GetRequiredService<Microsoft.Extensions.Caching.Memory.IMemoryCache>();
            var dbContext = sp.GetRequiredService<CveCacheDbContext>();
            return new CveCacheService(memoryCache, dbContext, useMemoryCache: true);
        });

        // Register HttpClient and CVE Client
        services.AddSingleton<ICveService, CveService>(serviceProvider =>
        {
            var httpClient = new HttpClient();
            var cacheService = serviceProvider.GetRequiredService<ICveCacheService>();
            return new CveService(httpClient, cacheService);
        });

        _serviceProvider = services.BuildServiceProvider();

        // Initialize database
        using (var scope = _serviceProvider.CreateScope())
        {
            var dbContext = scope.ServiceProvider.GetRequiredService<CveCacheDbContext>();
            Console.WriteLine("Initializing CVE cache database...");
            dbContext.Database.EnsureCreated();

            // Load cache from database into memory
            var cacheService = _serviceProvider.GetRequiredService<ICveCacheService>();
            _ = cacheService.LoadCacheFromDatabaseAsync();
        }
    }

    [Fact]
    public async Task GetCvesForPackagesAsync_ShouldFetchRealCves()
    {
        // Resolve CveClient from DI
        var client = _serviceProvider.GetRequiredService<ICveService>();
        var packages = new List<NpmPackageInfo>
        {
            new NpmPackageInfo ("lodash", "4.17.19", new Dictionary<string, string>())
        };

        // Act
        var results = await client.GetCvesForPackagesAsync(packages);

        // Assert
        Assert.NotNull(results);
        Assert.NotEmpty(results); // Lodash has known CVEs
        Assert.Contains(results, r => r.PackageName == "lodash");
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