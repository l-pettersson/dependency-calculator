# NPM Dependency Calculator

A smart dependency calculator for NPM packages that maximizes dependency versions while satisfying all constraints and ensuring security. Built with ASP.NET Core and Blazor, this tool uses Monte Carlo Tree Search (MCTS) to find optimal package versions that meet both dependency requirements and security constraints.

## 🎯 Features

- **Intelligent Version Resolution**: Automatically calculates the highest compatible versions for NPM packages while satisfying all dependency constraints
- **Security-First Approach**: Integrates CVE (Common Vulnerabilities and Exposures) checking to ensure dependencies meet security thresholds
- **Recursive Dependency Analysis**: Analyzes transitive dependencies to understand the complete dependency tree
- **Interactive Web UI**: User-friendly Blazor interface for managing packages and visualizing dependency graphs
- **Caching System**: SQLite-based caching for NPM package metadata and CVE information to improve performance
- **Multiple Dependency Types**: Support for `dependencies`, `devDependencies`, and `peerDependencies`
- **Flexible Configuration**: Customizable parameters for the MCTS algorithm and CVE thresholds

## 🧠 How It Works

The calculator uses a Monte Carlo Tree Search (MCTS) algorithm to explore the space of possible package versions:

1. **Selection**: Navigates the tree using the Upper Confidence Bound (UCB) formula to balance exploration and exploitation
2. **Expansion**: Creates new child nodes representing different version choices
3. **Simulation**: Randomly samples version combinations to estimate their quality
4. **Backpropagation**: Updates node statistics based on simulation results

### Security Constraints

The calculator integrates CVE data from the National Vulnerability Database (NVD) to filter out insecure package versions. You can configure thresholds for different severity levels:

- **Critical**: Maximum allowed critical vulnerabilities
- **High**: Maximum allowed high-severity vulnerabilities  
- **Medium**: Maximum allowed medium-severity vulnerabilities
- **Low**: Maximum allowed low-severity vulnerabilities

### Version Maximization

The algorithm aims to find the highest possible versions for each package while:
- Satisfying all semantic version constraints (^, ~, >=, etc.)
- Meeting security requirements (CVE thresholds)
- Resolving transitive dependencies correctly
- Avoiding conflicts between different dependency requirements

## 🚀 Getting Started

### Prerequisites

- .NET 9.0 SDK or later
- Docker (optional, for containerized deployment)

### Running Locally

1. **Clone the repository**
   ```powershell
   git clone https://github.com/l-pettersson/dependency-calculator.git
   cd dependency-calculator
   ```

2. **Build the project**
   ```powershell
   dotnet build
   ```

3. **Run the application**
   ```powershell
   dotnet run --project src/src.csproj
   ```

4. **Open your browser**
   Navigate to `http://localhost:5000`

### Running with Docker

1. **Build and run using Docker Compose**
   ```powershell
   docker-compose up -d
   ```

2. **Access the application**
   Navigate to `http://localhost:5000`

3. **Stop the application**
   ```powershell
   docker-compose down
   ```

The Docker setup includes:
- Persistent SQLite databases for caching (mounted to `./data` directory)
- Support for custom `.npmrc` configuration
- Automatic restart on failure

## 📖 Usage

### Web Interface

1. **Add Packages**: Enter package names and version constraints (e.g., `express`, `^4.18.2`)
2. **Configure Parameters**:
   - **Max Recursion Depth** (1-10): How deep to traverse the dependency tree
   - **Iterations** (1-10000): Number of MCTS iterations (more = better quality, slower)
   - **Simulation Depth** (1-10): Depth of random simulations during MCTS
   - **Max Compare Version** (1-100): Maximum number of versions to compare during expansion/ simulation during MCTS
   - **Lambda** (-100 to 100): Reward function parameter balancing version maximization

3. **Set Security Thresholds**: Choose a CVE preset or configure custom limits:
   - **None**: No CVE filtering
   - **Low or higher**: Zero vulnerabilities of any severity
   - **Medium or higher**: Zero critical/high/medium vulnerabilities
   - **High or higher**: Zero critical/high vulnerabilities
   - **Critical only**: Zero critical vulnerabilities
   - **Custom**: Set specific thresholds for each severity level

4. **Calculate**: Click "Calculate Optimal Versions" to run the algorithm
5. **View Results**: 
   - See the calculated optimal versions for each package
   - Explore the interactive dependency graph
   - View CVE information for packages
   - Download results as JSON

### Configuration Parameters

#### MCTS Parameters

- **maxIterations**: Number of MCTS iterations. Higher values provide better solutions but take longer (default: 1000)
- **maxSimulationDepth**: Maximum depth for random simulations (default: 5)
- **maxCompareVersion**: Maximum number of versions to compare during expansion/ simulation (default: 20)
- **maxDepth**: Maximum recursion depth for dependency traversal (default: 2)
- **lambda**: Reward function parameter. Positive values favor newer versions, negative values are more conservative (default: 2)

#### CVE Thresholds

Configure maximum allowed vulnerabilities per severity level:

```csharp
var cveThreshold = new CveThreshold
{
    Critical = 0,  // No critical vulnerabilities allowed
    High = 0,      // No high-severity vulnerabilities allowed
    Medium = 2,    // Up to 2 medium-severity vulnerabilities allowed
    Low = 5        // Up to 5 low-severity vulnerabilities allowed
};
```

## 🏗️ Architecture

### Project Structure

```
dependency-calculator/
├── src/                          # Main application
│   ├── Controllers/              # API controllers
│   │   └── DependencyController.cs
│   ├── Services/                 # Core business logic
│   │   ├── NpmVersionCalculator.cs    # MCTS algorithm
│   │   ├── NpmService.cs              # NPM registry service
│   │   ├── CveService.cs              # NVD API service
│   │   ├── NpmCacheService.cs         # Package caching
│   │   ├── CveCacheService.cs         # CVE caching
│   │   ├── NpmVersionMatcher.cs       # Semver matching
│   │   └── DependencyGraphBuilder.cs  # Graph construction
│   ├── Models/                   # Data models
│   │   ├── NpmPackageInfo.cs
│   │   ├── CveInfo.cs
│   │   └── DependencyGraphModels.cs
│   ├── Data/                     # Database contexts
│   │   ├── NpmCacheDbContext.cs
│   │   └── CveCacheDbContext.cs
│   ├── Pages/                    # Blazor pages
│   │   └── Index.razor
│   └── Program.cs                # Application startup
├── tests/                        # Unit and integration tests
├── Dockerfile                    # Docker configuration
├── docker-compose.yml            # Docker Compose setup
└── README.md                     # This file
```

### Key Components

- **NpmVersionCalculator**: Implements the MCTS algorithm for version optimization
- **NpmService**: Interfaces with the NPM registry API to fetch package metadata
- **CveService**: Retrieves vulnerability data from the National Vulnerability Database
- **NpmVersionMatcher**: Handles semantic version matching and constraint resolution
- **Cache Services**: SQLite-based caching to reduce API calls and improve performance

## 🔬 Testing

Run the test suite:

```powershell
dotnet test
```

The test suite includes:
- Unit tests for version matching logic
- Integration tests for NPM and CVE API services
- Tests for the MCTS algorithm components

## 📊 Performance Considerations

- **Caching**: Both NPM package data and CVE information are cached in SQLite databases to minimize API calls
- **Concurrent Requests**: The calculator handles multiple concurrent requests efficiently
- **Iteration Count**: Higher iteration counts improve solution quality but increase computation time
- **Recursion Depth**: Deeper dependency traversal provides more accurate results but requires more API calls

Performance varies based on:
- Number of packages and dependencies
- Cache hit rate
- Network latency to NPM registry and NVD API
- Configured iteration count

## 🛠️ Configuration

### Environment Variables

- `ENABLE_MEMORY_CACHE`: Enable in memory cache (default: `0`)
- `CVE_CACHE_DB_PATH`: Path to the CVE cache SQLite database (default: `./cve_cache.db`)
- `NPM_CACHE_DB_PATH`: Path to the NPM cache SQLite database (default: `./npm_cache.db`)
- `ASPNETCORE_URLS`: URLs the application listens on (default: `http://+:5000`)
- `ASPNETCORE_ENVIRONMENT`: Environment name (Development, Production, etc.)

### Database Files

The application creates two SQLite databases for caching:
- `cve_cache.db`: Stores CVE vulnerability data
- `npm_cache.db`: Stores NPM package metadata

These databases are automatically created on first run and persist between sessions.

## 🤝 Contributing

Contributions are welcome! Please feel free to submit issues or pull requests.

## 📝 License

This project is open source. See the LICENSE file for details.

## 🔗 Resources

- [NPM Registry API](https://github.com/npm/registry/blob/master/docs/REGISTRY-API.md)
- [National Vulnerability Database](https://nvd.nist.gov/)
- [Semantic Versioning](https://semver.org/)
- [Monte Carlo Tree Search](https://en.wikipedia.org/wiki/Monte_Carlo_tree_search)

## 👤 Author

Lucas Pettersson ([@l-pettersson](https://github.com/l-pettersson))

---

**Note**: This tool queries public APIs (NPM registry and NVD). Please be mindful of rate limits and consider running your own registry mirror for heavy usage.
