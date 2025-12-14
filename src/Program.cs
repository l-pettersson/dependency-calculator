using DependencyCalculator.Data;
using DependencyCalculator.Services;
using Microsoft.EntityFrameworkCore;
using MudBlazor.Services;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container
builder.Services.AddControllers()
    .AddJsonOptions(options =>
    {
        // Configure JSON serialization to handle camelCase from frontend
        options.JsonSerializerOptions.PropertyNameCaseInsensitive = true;
    });
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddRazorPages();
builder.Services.AddServerSideBlazor();
builder.Services.AddMudServices();

// Configure HttpClient with base address for Blazor components
builder.Services.AddHttpClient("API", client =>
{
    // Use environment variable for API base address, default to container's own service name
    var apiBaseAddress = Environment.GetEnvironmentVariable("API_BASE_URL") ?? "http://localhost:5000";
    client.BaseAddress = new Uri(apiBaseAddress);
});
builder.Services.AddScoped(sp =>
{
    var clientFactory = sp.GetRequiredService<IHttpClientFactory>();
    return clientFactory.CreateClient("API");
});

// Check if memory caching should be enabled (defaults to false to avoid memory issues)
var enableMemoryCache = Environment.GetEnvironmentVariable("ENABLE_MEMORY_CACHE");
var useMemoryCache = !string.IsNullOrEmpty(enableMemoryCache) && 
                     (enableMemoryCache.Equals("true", StringComparison.OrdinalIgnoreCase) || 
                      enableMemoryCache.Equals("1", StringComparison.OrdinalIgnoreCase));

Console.WriteLine($"Memory caching: {(useMemoryCache ? "ENABLED" : "DISABLED (database only)")}");

// Add memory caching services only if enabled
if (useMemoryCache)
{
    builder.Services.AddMemoryCache();
}
else
{
    // Register a null implementation to satisfy dependencies
    builder.Services.AddSingleton<Microsoft.Extensions.Caching.Memory.IMemoryCache>(sp => 
        new Microsoft.Extensions.Caching.Memory.MemoryCache(
            new Microsoft.Extensions.Caching.Memory.MemoryCacheOptions { SizeLimit = 0 }));
}

// Add SQLite database for persistent caching
builder.Services.AddDbContext<CveCacheDbContext>(options =>
{
    var dbPath = Environment.GetEnvironmentVariable("CVE_CACHE_DB_PATH") 
        ?? Path.Combine(Directory.GetCurrentDirectory(), "cve_cache.db");
    
    // Ensure directory exists
    var directory = Path.GetDirectoryName(dbPath);
    if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
    {
        Directory.CreateDirectory(directory);
    }
    
    options.UseSqlite($"Data Source={dbPath}");
    Console.WriteLine($"SQLite CVE cache database location: {dbPath}");
}, ServiceLifetime.Singleton);

// Register CveCacheService as a singleton with memory cache flag
builder.Services.AddSingleton<ICveCacheService>(sp =>
{
    var memoryCache = sp.GetRequiredService<Microsoft.Extensions.Caching.Memory.IMemoryCache>();
    var dbContext = sp.GetRequiredService<CveCacheDbContext>();
    return new CveCacheService(memoryCache, dbContext, useMemoryCache);
});

// Configure NpmClient as a singleton service
builder.Services.AddSingleton<ICveService, CveService>(serviceProvider =>
{
    var handler = new HttpClientHandler();
    var httpClient = new HttpClient(handler);
    var cacheService = serviceProvider.GetRequiredService<ICveCacheService>();
    return new CveService(httpClient, cacheService);
});


// Add SQLite database for persistent caching
builder.Services.AddDbContext<NpmCacheDbContext>(options =>
{
    var dbPath = Environment.GetEnvironmentVariable("NPM_CACHE_DB_PATH") 
        ?? Path.Combine(Directory.GetCurrentDirectory(), "npm_cache.db");
    
    // Ensure directory exists
    var directory = Path.GetDirectoryName(dbPath);
    if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
    {
        Directory.CreateDirectory(directory);
    }
    
    options.UseSqlite($"Data Source={dbPath}");
    Console.WriteLine($"SQLite NPM cache database location: {dbPath}");
}, ServiceLifetime.Singleton);

// Register NpmCacheService as a singleton with memory cache flag
builder.Services.AddSingleton<INpmCacheService>(sp =>
{
    var memoryCache = sp.GetRequiredService<Microsoft.Extensions.Caching.Memory.IMemoryCache>();
    var dbContext = sp.GetRequiredService<NpmCacheDbContext>();
    return new NpmCacheService(memoryCache, dbContext, useMemoryCache);
});

// Add CORS
builder.Services.AddCors(options =>
{
    options.AddDefaultPolicy(policy =>
    {
        policy.AllowAnyOrigin()
              .AllowAnyMethod()
              .AllowAnyHeader();
    });
});

// Register NpmVersionMatcher as a singleton
builder.Services.AddSingleton<INpmVersionMatcher, NpmVersionMatcher>();

// Configure NpmClient as a singleton service
builder.Services.AddSingleton<INpmService, NpmService>(serviceProvider =>
{
    // Look for .npmrc in multiple locations:
    // 1. Project root (parent of src directory)
    // 2. Current directory
    // 3. User home directory
    var possiblePaths = new[]
    {
        Path.Combine(Directory.GetCurrentDirectory(), "..", ".npmrc"),
        Path.Combine(Directory.GetCurrentDirectory(), ".npmrc"),
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".npmrc")
    };

    string? npmrcPath = null;
    foreach (var path in possiblePaths)
    {
        var normalizedPath = Path.GetFullPath(path);
        if (File.Exists(normalizedPath))
        {
            npmrcPath = normalizedPath;
            Console.WriteLine($"Found .npmrc at: {npmrcPath}");
            break;
        }
    }

    if (npmrcPath == null)
    {
        Console.WriteLine("Warning: No .npmrc file found in any of the following locations:");
        foreach (var path in possiblePaths)
        {
            Console.WriteLine($"  - {Path.GetFullPath(path)}");
        }
        throw new Exception("Cannot proceed without valid .npmrc configuration. Please create a .npmrc file.");
    }

    NpmConfigReader npmConfig;
    try
    {
        npmConfig = NpmConfigReader.ReadFromFile(npmrcPath);
        Console.WriteLine($"Successfully loaded NPM configuration:");
        Console.WriteLine($"  Registry: {npmConfig.Registry}");
        Console.WriteLine($"  Username: {npmConfig.Username}");
        Console.WriteLine($"  Strict SSL: {npmConfig.StrictSsl}");
    }
    catch (Exception ex)
    {
        Console.WriteLine($"Error reading .npmrc from {npmrcPath}: {ex.Message}");
        throw new Exception($"Failed to parse .npmrc configuration: {ex.Message}");
    }

    var handler = new HttpClientHandler();
    if (!npmConfig.StrictSsl)
    {
        handler.ServerCertificateCustomValidationCallback = (message, cert, chain, sslPolicyErrors) => true;
        Console.WriteLine("Note: SSL certificate validation is disabled");
    }

    var httpClient = new HttpClient(handler);
    var versionMatcher = serviceProvider.GetRequiredService<INpmVersionMatcher>();
    var cacheService = serviceProvider.GetRequiredService<INpmCacheService>();
    return new NpmService(httpClient, npmConfig, versionMatcher, cacheService);
});

var app = builder.Build();

// Initialize database and load cache
using (var scope = app.Services.CreateScope())
{
    var dbContext = scope.ServiceProvider.GetRequiredService<NpmCacheDbContext>();

    // Ensure database is created
    Console.WriteLine("Initializing NPM cache database...");
    dbContext.Database.EnsureCreated();

    // Load cache from database into memory
    var cacheService = app.Services.GetRequiredService<INpmCacheService>();
    await cacheService.LoadCacheFromDatabaseAsync();
}

using (var scope = app.Services.CreateScope())
{
    var dbContext = scope.ServiceProvider.GetRequiredService<CveCacheDbContext>();

    // Ensure database is created
    Console.WriteLine("Initializing CVE cache database...");
    dbContext.Database.EnsureCreated();

    // Load cache from database into memory
    var cacheService = app.Services.GetRequiredService<ICveCacheService>();
    await cacheService.LoadCacheFromDatabaseAsync();
}

// Configure the HTTP request pipeline
app.UseCors();
app.UseStaticFiles();
app.UseRouting();
app.MapControllers();
app.MapBlazorHub();
app.MapFallbackToPage("/_Host");

Console.WriteLine("NPM Dependency Checker Web API is running!");

app.Run();

