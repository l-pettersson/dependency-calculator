using Microsoft.AspNetCore.Mvc;
using DependencyCalculator.Models;
using DependencyCalculator.Services;

namespace DependencyCalculator.Controllers;

[ApiController]
[Route("api/[controller]")]
public class DependencyController : ControllerBase
{
    private readonly INpmService _npmService;
    private readonly ICveService _cveService;
    private readonly INpmVersionMatcher _versionMatcher;

    public DependencyController(INpmService npmService, ICveService cveService, INpmVersionMatcher versionMatcher)
    {
        _npmService = npmService;
        _cveService = cveService;
        _versionMatcher = versionMatcher;
    }

    [HttpPost("recursive-graph")]
    public async Task<ActionResult<DependencyGraphResponse>> GetRecursiveDependencyGraph([FromBody] RecursiveDependencyGraphRequest request)
    {

        return await GetRecursiveDependencyGraphInternal(request, DependencyType.Dependencies);
    }


    [HttpPost("recursive-dev-graph")]
    public async Task<ActionResult<DependencyGraphResponse>> GetRecursiveDevDependencyGraph([FromBody] RecursiveDependencyGraphRequest request)
    {
        return await GetRecursiveDependencyGraphInternal(request, DependencyType.DevDependencies);
    }

    [HttpPost("recursive-peer-graph")]
    public async Task<ActionResult<DependencyGraphResponse>> GetRecursivePeerDependencyGraph([FromBody] RecursiveDependencyGraphRequest request)
    {
        return await GetRecursiveDependencyGraphInternal(request, DependencyType.PeerDependencies);
    }

    private async Task<ActionResult<DependencyGraphResponse>> GetRecursiveDependencyGraphInternal(RecursiveDependencyGraphRequest request, DependencyType dependencyType)
    {
        if (request.Packages == null || request.Packages.Count == 0)
        {
            return BadRequest("At least one package must be specified");
        }

        var packageRequests = request.Packages
            .Select(p => new NpmPackageRequest(p.Name, p.Version))
            .ToList();

        // Get max depth from request or use default
        var maxDepth = request.MaxDepth > 0 ? request.MaxDepth : 5;

        // Fetch all packages recursively
        var (packageInfos, maxDepthPackages) = await _npmService.GetPackageInfoRecursiveAsync(packageRequests, maxDepth, dependencyType);

        // Build graph data for all transitive peer dependencies
        var rootPackageNames = request.Packages.Select(p => p.Name).ToHashSet();
        var rootPackageRequests = request.Packages.Select(p => (p.Name, p.Version)).ToList();

        // Create a graph builder with version matcher for semantic version resolution
        var graphBuilder = new DependencyGraphBuilder(_versionMatcher);
        var (nodes, edges) = graphBuilder.BuildRecursiveGraph(
            packageInfos,
            rootPackageNames,
            rootPackageRequests,
            maxDepthPackages,
            dependencyType);

        return Ok(new DependencyGraphResponse
        {
            Nodes = nodes,
            Edges = edges
        });
    }

    [HttpPost("optimal-versions")]
    public async Task<ActionResult<OptimalVersionsResponse>> CalculateOptimalVersions([FromBody] OptimalVersionsRequest request)
    {
        return await CalculateOptimalVersionsInternal(request, DependencyType.Dependencies);
    }

    [HttpPost("optimal-dev-versions")]
    public async Task<ActionResult<OptimalVersionsResponse>> CalculateOptimalDevVersions([FromBody] OptimalVersionsRequest request)
    {
        return await CalculateOptimalVersionsInternal(request, DependencyType.DevDependencies);
    }

    [HttpPost("optimal-peer-versions")]
    public async Task<ActionResult<OptimalVersionsResponse>> CalculateOptimalPeerVersions([FromBody] OptimalVersionsRequest request)
    {
        return await CalculateOptimalVersionsInternal(request, DependencyType.PeerDependencies);
    }


    private async Task<ActionResult<OptimalVersionsResponse>> CalculateOptimalVersionsInternal(OptimalVersionsRequest request, DependencyType dependencyType)
    {
        if (request.Packages == null || !request.Packages.Any())
        {

            return BadRequest("At least one package must be specified");
        }

        try
        {
            Console.WriteLine($"[CalculateOptimalVersions] Processing {request.Packages.Count} packages with MaxDepth={request.MaxDepth}, MaxIterations={request.MaxIterations}");
            // Get max depth from request or use default
            var maxDepth = request.MaxDepth > 0 ? request.MaxDepth : 5;
            var maxIterations = request.MaxIterations > 0 ? request.MaxIterations : 1000;
            var maxSimulationDepth = request.MaxSimulationDepth > 0 ? request.MaxSimulationDepth : 100;
            var maxCompareVersion = request.MaxCompareVersion > 0 ? request.MaxCompareVersion : 20;
            var lambda = request.Lambda != 0 ? request.Lambda : 2;
            var InitVersions = request.InitVersions;
            var cveThreshold = CveThreshold.FromSeverityString(request.CveThreshold);

            // Prepare initial root versions
            var initRoot = new Dictionary<string, string>();
            if (InitVersions)
            {
                initRoot = request.Packages.ToDictionary(
                    p => p.Name,
                    p => p.Version);
            }
            else
            {
                initRoot = request.Packages.ToDictionary(p => p.Name, _ => "*");
            }

            // Create version calculator with the specified parameters
            var versionCalculator = new NpmVersionCalculator(
                _versionMatcher,
                _npmService,
                _cveService,
                maxIterations: maxIterations,
                maxSimulationDepth: maxSimulationDepth,
                maxCompareVersion: maxCompareVersion,
                maxDepth: maxDepth,
                lambda: lambda,
                initVersions: InitVersions,
                dependencyType: dependencyType,
                cveThreshold: cveThreshold);

            // Run MCTS to find optimal versions
            var calculationResult = await versionCalculator.CalculateOptimalVersionsAsync(initRoot);


            if (calculationResult.Solution == null && calculationResult.ErrorMessage != null)
            {
                return Ok(new OptimalVersionsResponse
                {
                    Success = false,
                    Message = "Could not find a valid solution that satisfies all dependency constraints.",
                    ResolvedVersions = new Dictionary<string, string>(),
                    Error = calculationResult.ErrorMessage
                });
            }
            else if (calculationResult.ErrorMessage != null)
            {
                return Ok(new OptimalVersionsResponse
                {
                    Success = false,
                    Message = "Could not find a valid solution that satisfies all dependency constraints.",
                    ResolvedVersions = calculationResult.Solution ?? new Dictionary<string, string>(),
                    Error = calculationResult.ErrorMessage
                });
            }

            return Ok(new OptimalVersionsResponse
            {
                Success = true,
                Message = $"Successfully resolved {calculationResult.Solution!.Count} peer packages using MCTS.",
                ResolvedVersions = calculationResult.Solution
            });
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[CalculateOptimalVersions] Exception occurred: {ex.GetType().Name}");
            Console.WriteLine($"[CalculateOptimalVersions] Error message: {ex.Message}");
            Console.WriteLine($"[CalculateOptimalVersions] Stack trace: {ex.StackTrace}");

            return StatusCode(500, new OptimalVersionsResponse
            {
                Success = false,
                Message = $"Error calculating optimal versions: {ex.Message}",
                ResolvedVersions = new Dictionary<string, string>(),
                Error = ex.ToString()
            });
        }
    }

}
