namespace DependencyCalculator.Models;

public enum DependencyType
{
    Dependencies,
    DevDependencies,
    PeerDependencies
}

public class DependencyGraphRequest
{
    public List<PackageInput> Packages { get; set; } = new();
}

public class RecursiveDependencyGraphRequest
{
    public List<PackageInput> Packages { get; set; } = new();
    public int MaxDepth { get; set; } = 5;
}

public class PackageInput
{
    public string Name { get; set; } = "";
    public string Version { get; set; } = "";
}

public class DependencyGraphResponse
{
    public List<GraphNode> Nodes { get; set; } = new();
    public List<GraphEdge> Edges { get; set; } = new();
}

public class GraphNode
{
    public int Id { get; set; }
    public string Label { get; set; } = "";
    public string Version { get; set; } = "";
    public bool IsRoot { get; set; }
    public int DependencyCount { get; set; }
    public bool IsFoundInRepository { get; set; } = true;
    public bool ReachedMaxDepth { get; set; } = false;
}

public class GraphEdge
{
    public int From { get; set; }
    public int To { get; set; }
    public string Label { get; set; } = "";
}

public class OptimalVersionsRequest
{
    public List<PackageInput> Packages { get; set; } = new();
    public int MaxDepth { get; set; } = 5;
    public int MaxIterations { get; set; } = 1000;
    public int MaxSimulationDepth { get; set; } = 100;
    public int MaxCompareVersion { get; set; } = 20;

    public int Lambda { get; set; } = 2;
    public bool InitVersions { get; set; } = false;
    public string? CveThreshold { get; set; }
}

public class OptimalVersionsResponse
{
    public bool Success { get; set; }
    public string Message { get; set; } = "";
    public Dictionary<string, string> ResolvedVersions { get; set; } = new();
    public string? Error { get; set; }
}
