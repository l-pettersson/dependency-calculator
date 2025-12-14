using System.Text.RegularExpressions;
using DependencyCalculator.Models;

namespace DependencyCalculator.Services;

/// <summary>
/// Service for calculating NPM version that satisfies all dependency constraints.
/// This calculator is based on Monte Carlo Tree Search (MCTS) principles.
/// </summary>
public class NpmVersionCalculator
{
    private readonly INpmService _npmClient;
    private readonly ICveService _cveClient;
    private readonly INpmVersionMatcher _versionMatcher;
    private readonly int _maxIterations;
    private readonly int _maxSimulationDepth;
    private readonly int _maxDepth;
    private readonly int _maxCompareVersion;
    private readonly double _lambda;
    private readonly bool _initVersions;
    private readonly DependencyType _dependencyType;
    private readonly CveThreshold? _cveThreshold;

    // Track last constraint violation details for error reporting (keep last 10)
    private readonly Queue<string> _lastConstraintViolationLogs = new Queue<string>();
    private const int MaxStoredLogs = 10;

    // Track best complete solution found during simulations (as fallback if tree doesn't reach terminal state)
    // This handles cases where simulations find valid solutions but tree exploration doesn't reach them
    // due to limited iterations or exploration/exploitation trade-offs
    private MCTSState? _bestSimulationSolution = null;
    private double _bestSimulationReward = double.MinValue;

    // Constants for MCTS algorithm
    private const double UcbExplorationParameter = 1.41421356237; // sqrt(2)
    private const int MaxDisplayedVersions = 10;
    private const int IterationLogInterval = 100;

    public NpmVersionCalculator(INpmVersionMatcher versionMatcher,
      INpmService npmClient,
      ICveService cveClient,
      int maxIterations = 1000,
      int maxSimulationDepth = 100,
      int maxDepth = 5,
      int maxCompareVersion = 20,
      double lambda = 2,
      bool initVersions = false,
      DependencyType dependencyType = DependencyType.Dependencies,
      CveThreshold? cveThreshold = null)
    {
        _versionMatcher = versionMatcher ?? throw new ArgumentNullException(nameof(versionMatcher));
        _npmClient = npmClient ?? throw new ArgumentNullException(nameof(npmClient));
        _cveClient = cveClient ?? throw new ArgumentNullException(nameof(cveClient));
        _maxIterations = maxIterations;
        _maxSimulationDepth = maxSimulationDepth;
        _maxDepth = maxDepth;
        _maxCompareVersion = maxCompareVersion;
        _lambda = lambda;
        _initVersions = initVersions;
        _dependencyType = dependencyType;
        _cveThreshold = cveThreshold;
    }

    /// <summary>
    /// Calculate optimal versions using Monte Carlo Tree Search
    /// </summary>
    public async Task<VersionCalculationResult> CalculateOptimalVersionsAsync(Dictionary<string, string> initialDependencies)
    {
        if (initialDependencies == null || !initialDependencies.Any())
        {
            return new VersionCalculationResult(new Dictionary<string, string>());
        }

        // Normalize initial dependencies (convert singular versions to caret ranges)
        var normalizedDependencies = initialDependencies.Select(d =>
            new PendingDependency(d.Key, NormalizeVersionConstraint(d.Value), null)
        );

        // Create root node
        var rootState = new MCTSState(
            new Dictionary<string, string>(),
            new Queue<PendingDependency>(normalizedDependencies),
            new Dictionary<string, List<ConstraintInfo>>()
        );

        var rootNode = new MCTSNode(rootState);

        // Run MCTS iterations
        Console.WriteLine($"🔄 Starting MCTS with {_maxIterations} iterations, max depth: {_maxDepth}, simulation depth: {_maxSimulationDepth}");
        for (int i = 0; i < _maxIterations; i++)
        {
            // Selection: Navigate to most promising node
            var selectedNode = Select(rootNode);

            // Expansion: Add child nodes if not fully expanded
            var expandedNode = await ExpandAsync(selectedNode);

            // Simulation: Run a random playout from the expanded node
            var reward = await SimulateAsync(expandedNode.State);

            // Backpropagation: Update statistics
            Backpropagate(expandedNode, reward);

            // Log progress every 100 iterations
            if ((i + 1) % IterationLogInterval == 0)
            {
                Console.WriteLine($"  Progress: {i + 1}/{_maxIterations} iterations completed");
            }
        }

        Console.WriteLine($"✅ MCTS completed. Extracting best solution...");

        // Extract best solution from tree
        return ExtractBestSolution(rootNode);
    }

    /// <summary>
    /// Selection phase: Use UCB1 to select the most promising node
    /// </summary>
    private MCTSNode Select(MCTSNode node)
    {
        while (!node.State.IsTerminal())
        {
            if (!node.IsFullyExpanded())
            {
                return node;
            }
            else
            {
                node = SelectBestChild(node);
            }
        }
        return node;
    }

    /// <summary>
    /// Select best child using UCB1 formula
    /// </summary>
    private MCTSNode SelectBestChild(MCTSNode node)
    {
        double bestValue = double.MinValue;
        MCTSNode? bestChild = null;

        foreach (var child in node.Children)
        {
            double ucbValue = CalculateUCB1(child, node.VisitCount);

            if (ucbValue > bestValue)
            {
                bestValue = ucbValue;
                bestChild = child;
            }
        }

        return bestChild ?? node.Children.First();
    }

    /// <summary>
    /// Calculate UCB1 value for a node
    /// </summary>
    private double CalculateUCB1(MCTSNode node, int parentVisits)
    {
        if (node.VisitCount == 0)
        {
            return double.MaxValue; // Prioritize unvisited nodes
        }

        double exploitation = node.TotalReward / node.VisitCount;
        double exploration = UcbExplorationParameter * Math.Sqrt(Math.Log(parentVisits) / node.VisitCount);

        return exploitation + exploration;
    }

    /// <summary>
    /// Expansion phase: Create child nodes for unvisited actions
    /// </summary>
    private async Task<MCTSNode> ExpandAsync(MCTSNode node)
    {
        if (node.State.IsTerminal())
        {
            return node;
        }

        // Get next pending dependency
        var pending = node.State.Pending.Peek();

        // Get available versions that satisfy constraints
        var availableVersions = await GetAvailableVersionsAsync(pending.PackageName);
        var constraints = GetConstraintsWithPendingDependency(node.State, pending);

        // Filter valid versions based on dependency type and CVE threshold
        var validVersions = await FilterValidVersionsAsync(pending.PackageName, availableVersions, constraints);

        if (!validVersions.Any())
        {
            // No valid versions - mark as terminal failure
            LogConstraintViolation(pending, availableVersions, constraints, validVersions);
            return node;
        }

        // Log successful constraint resolution
        if (constraints.Count > 1)
        {
            Console.WriteLine($"✅ Found {validVersions.Count} valid versions for '{pending.PackageName}' satisfying {constraints.Count} constraints");
        }

        // Create child node for the first unexplored version
        foreach (var version in validVersions)
        {
            if (!node.HasChildForVersion(pending.PackageName, version))
            {
                var childState = await CreateChildStateAsync(node.State, pending.PackageName, version);
                var childNode = new MCTSNode(childState, node);
                node.AddChild(childNode);
                return childNode;
            }
        }

        // All versions explored, return existing child
        return node.Children.FirstOrDefault() ?? node;
    }

    /// <summary>
    /// Get constraints for a package including the pending dependency constraint
    /// Only builds constraints for peer dependencies.
    /// </summary>
    private List<ConstraintInfo> GetConstraintsWithPendingDependency(MCTSState state, PendingDependency pending)
    {
        // Only consider constraints for peer dependencies
        if (_dependencyType != DependencyType.PeerDependencies)
        {
            return new List<ConstraintInfo>();
        }

        var constraints = state.GetConstraintsForPackage(pending.PackageName);

        // Include root constraint if initializing versions
        if (_initVersions && pending.RequiredBy == null)
        {
            var requiredBySource = "root";
            var requiredByVersion = pending.VersionRange;
            var normalizedPendingRange = NormalizeVersionConstraint(pending.VersionRange);
            constraints.Add(new ConstraintInfo(normalizedPendingRange, requiredBySource, requiredByVersion));
        }

        return constraints;
    }

    /// <summary>
    /// Filter valid versions based on dependency type constraints and CVE threshold
    /// </summary>
    /// <param name="packageName">The name of the package</param>
    /// <param name="availableVersions">List of available versions</param>
    /// <param name="constraints">List of constraints to satisfy</param>
    private async Task<List<string>> FilterValidVersionsAsync(string packageName, List<string> availableVersions, List<ConstraintInfo> constraints)
    {
        List<string> filteredVersions;

        if (_dependencyType == DependencyType.PeerDependencies)
        {
            // Peer dependencies: ALL constraints must be satisfied
            // Note: availableVersions are already sorted from latest to oldest by GetAvailableVersionsAsync
            filteredVersions = availableVersions
                .Where(v => constraints.All(constraint => _versionMatcher.Matches(constraint.Constraint, v)))
                .Take(_maxCompareVersion)
                .ToList();
        }
        else
        {
            // Regular dependencies and dev dependencies: ANY constraint match is acceptable
            filteredVersions = availableVersions
                .Take(_maxCompareVersion)
                .ToList();
        }

        // Apply CVE filtering if threshold is specified
        if (_cveThreshold != null)
        {
            var cveLogBuilder = new System.Text.StringBuilder();
            cveLogBuilder.AppendLine($"🔍 CVE Filtering for {packageName}: Checking {filteredVersions.Count} versions against threshold {_cveThreshold}");
            Console.WriteLine($"🔍 CVE Filtering for {packageName}: Checking {filteredVersions.Count} versions against threshold {_cveThreshold}");

            var cveSafeVersions = new List<string>();
            var rejectedCount = 0;

            foreach (var version in filteredVersions)
            {
                // Check if version meets CVE threshold
                var (meetsThreshold, versionLog) = await MeetsCveThresholdAsync(packageName, version);
                cveLogBuilder.Append(versionLog);

                if (meetsThreshold)
                {
                    cveSafeVersions.Add(version);
                }
                else
                {
                    rejectedCount++;
                    Console.WriteLine($"❌ Version rejected due to CVE: {version}");
                }
            }

            cveLogBuilder.AppendLine($"✅ CVE Filter Result: {packageName} - {cveSafeVersions.Count} versions passed, {rejectedCount} rejected");
            Console.WriteLine($"✅ CVE Filter Result: {packageName} - {cveSafeVersions.Count} versions passed, {rejectedCount} rejected");

            if (cveSafeVersions.Count == 0 && filteredVersions.Count > 0)
            {
                cveLogBuilder.AppendLine($"⚠️  WARNING: All {filteredVersions.Count} versions were rejected due to CVE threshold. Consider relaxing the threshold.");
                Console.WriteLine($"⚠️  WARNING: All {filteredVersions.Count} versions were rejected due to CVE threshold. Consider relaxing the threshold.");
            }

            // Add the complete CVE log to constraint violation logs
            AddToConstraintLogs(cveLogBuilder.ToString());

            return cveSafeVersions;
        }

        return filteredVersions;
    }

    /// <summary>
    /// Fetch CVE data for a package version
    /// </summary>
    private async Task<CveInfo> GetCveDataAsync(string packageName, string version)
    {
        try
        {
            return await _cveClient.GetCvesForPackageAsync(packageName, version);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"   ⚠️  Warning: Failed to fetch CVE data for {packageName}@{version}: {ex.Message}");
            throw;
        }
    }

    /// <summary>
    /// Check if a package version meets the CVE threshold
    /// </summary>
    private bool MatchesCveThreshold(CveInfo cveInfo, out string logMessage)
    {
        var logBuilder = new System.Text.StringBuilder();
        logBuilder.AppendLine($"      Total CVEs: {cveInfo.TotalVulnerabilities} (Critical: {cveInfo.CriticalCount}, High: {cveInfo.HighCount}, Medium: {cveInfo.MediumCount}, Low: {cveInfo.LowCount})");
        logBuilder.AppendLine($"      Threshold: {_cveThreshold}");

        bool meetsThreshold = _cveThreshold!.MeetsThreshold(cveInfo);
        if (!meetsThreshold)
        {
            var failures = new List<string>();
            if (cveInfo.CriticalCount > _cveThreshold.CriticalCount) failures.Add($"Critical: {cveInfo.CriticalCount} > {_cveThreshold.CriticalCount}");
            if (cveInfo.HighCount > _cveThreshold.HighCount) failures.Add($"High: {cveInfo.HighCount} > {_cveThreshold.HighCount}");
            if (cveInfo.MediumCount > _cveThreshold.MediumCount) failures.Add($"Medium: {cveInfo.MediumCount} > {_cveThreshold.MediumCount}");
            if (cveInfo.LowCount > _cveThreshold.LowCount) failures.Add($"Low: {cveInfo.LowCount} > {_cveThreshold.LowCount}");

            logBuilder.AppendLine($"      ❌ REJECTED: {string.Join(", ", failures)}");
        }
        else
        {
            logBuilder.AppendLine($"      ✅ ACCEPTED");
        }

        logMessage = logBuilder.ToString();
        return meetsThreshold;
    }

    /// <summary>
    /// Check if a package version meets the CVE threshold
    /// Returns a tuple of (meetsThreshold, logMessage)
    /// </summary>
    private async Task<(bool, string)> MeetsCveThresholdAsync(string packageName, string version)
    {
        try
        {
            var cveInfo = await GetCveDataAsync(packageName, version);
            return (MatchesCveThreshold(cveInfo, out var logMessage), logMessage);
        }
        catch (Exception ex)
        {
            var errorLog = $"   ⚠️  Warning: Failed to check CVE for {packageName}@{version}: {ex.Message}\n      Allowing version (fail-open policy)";
            Console.WriteLine(errorLog);
            return (true, errorLog);
        }
    }

    /// <summary>
    /// Add a log entry to the constraint violation logs
    /// </summary>
    private void AddToConstraintLogs(string logMessage)
    {
        if (_lastConstraintViolationLogs.Count >= MaxStoredLogs)
        {
            _lastConstraintViolationLogs.Dequeue();
        }
        _lastConstraintViolationLogs.Enqueue(logMessage);
        Console.Write(logMessage);
    }

    /// <summary>
    /// Log detailed constraint violation information
    /// </summary>
    private void LogConstraintViolation(PendingDependency pending, List<string> availableVersions,
        List<ConstraintInfo> constraints, List<string> validVersions)
    {
        var logBuilder = new System.Text.StringBuilder();
        logBuilder.AppendLine($"❌ No valid versions found for package '{pending.PackageName}'");
        logBuilder.AppendLine($"   Dependency Type: {_dependencyType}");
        logBuilder.AppendLine($"   Available versions count: {availableVersions.Count}");
        logBuilder.AppendLine($"   Constraint matching mode: {(_dependencyType == DependencyType.PeerDependencies ? "ALL constraints must match" : "ANY constraint can match")}");
        logBuilder.AppendLine($"   Conflicting constraints ({constraints.Count}):");
        foreach (var constraint in constraints)
        {
            logBuilder.AppendLine($"      • {constraint}");
        }

        // Show why each version was rejected
        if (availableVersions.Count > 0 && availableVersions.Count <= MaxDisplayedVersions)
        {
            logBuilder.AppendLine($"   Why versions were rejected:");
            foreach (var version in availableVersions.Take(MaxDisplayedVersions))
            {
                var matchingConstraints = constraints.Where(c => _versionMatcher.Matches(c.Constraint, version)).ToList();
                var failingConstraints = constraints.Where(c => !_versionMatcher.Matches(c.Constraint, version)).ToList();

                if (_dependencyType == DependencyType.PeerDependencies)
                {
                    logBuilder.AppendLine($"      Version {version}: {matchingConstraints.Count}/{constraints.Count} constraints satisfied");
                    if (failingConstraints.Any())
                    {
                        logBuilder.AppendLine($"         Failed: {string.Join(", ", failingConstraints.Select(c => c.Constraint))}");
                    }
                }
                else
                {
                    logBuilder.AppendLine($"      Version {version}: Matched {matchingConstraints.Count} constraint(s)");
                }
            }
        }

        // Store the log for error reporting
        var logMessage = logBuilder.ToString();

        // Keep only the last MaxStoredLogs logs
        if (_lastConstraintViolationLogs.Count >= MaxStoredLogs)
        {
            _lastConstraintViolationLogs.Dequeue();
        }
        _lastConstraintViolationLogs.Enqueue(logMessage);

        // Also print to console
        Console.Write(logMessage);
    }

    /// <summary>
    /// Create a new state by applying a version selection
    /// </summary>
    private async Task<MCTSState> CreateChildStateAsync(MCTSState parentState, string packageName, string version)
    {
        var newResolved = new Dictionary<string, string>(parentState.Resolved)
        {
            [packageName] = version
        };

        var newConstraints = new Dictionary<string, List<ConstraintInfo>>();
        foreach (var kvp in parentState.Constraints)
        {
            newConstraints[kvp.Key] = new List<ConstraintInfo>(kvp.Value);
        }

        var newPending = new Queue<PendingDependency>(parentState.Pending);
        newPending.Dequeue(); // Remove the package we just resolved

        // Get dependencies of the selected version
        var packageInfo = await GetPackageInfoAsync(packageName, version);

        // Select the appropriate dependencies based on dependency type
        var dependencies = _dependencyType switch
        {
            DependencyType.DevDependencies => packageInfo?.DevDependencies,
            DependencyType.PeerDependencies => packageInfo?.PeerDependencies,
            _ => packageInfo?.Dependencies
        };

        if (packageInfo != null && dependencies != null)
        {
            foreach (var dep in dependencies)
            {
                if (!newResolved.ContainsKey(dep.Key))
                {
                    // Normalize the constraint (convert singular versions to caret ranges)
                    var normalizedConstraint = NormalizeVersionConstraint(dep.Value);

                    // Add to pending if not already resolved
                    if (!newPending.Any(p => p.PackageName == dep.Key))
                    {
                        newPending.Enqueue(new PendingDependency(dep.Key, normalizedConstraint, packageName));
                    }

                    // Add constraint with source tracking (only for peer dependencies)
                    if (_dependencyType == DependencyType.PeerDependencies)
                    {
                        if (!newConstraints.ContainsKey(dep.Key))
                        {
                            newConstraints[dep.Key] = new List<ConstraintInfo>();
                        }
                        newConstraints[dep.Key].Add(new ConstraintInfo(normalizedConstraint, packageName, version));
                    }
                }
                else
                {
                    // Check if constraint is satisfied (only for peer dependencies)
                    if (_dependencyType == DependencyType.PeerDependencies)
                    {
                        var resolvedVersion = newResolved[dep.Key];
                        var normalizedConstraint = NormalizeVersionConstraint(dep.Value);

                        if (!_versionMatcher.Matches(normalizedConstraint, resolvedVersion))
                        {
                            // Constraint violation - this is a dead end
                            Console.WriteLine($"⚠️  Constraint violation detected:");
                            Console.WriteLine($"   Package: {dep.Key}");
                            Console.WriteLine($"   Required by: {packageName}@{version}");
                            Console.WriteLine($"   Constraint: {normalizedConstraint}");
                            Console.WriteLine($"   Resolved version: {resolvedVersion}");
                            Console.WriteLine($"   Match result: FAILED");
                            newConstraints[dep.Key] = new List<ConstraintInfo> { new ConstraintInfo("INVALID", packageName, version) };
                        }
                    }
                }
            }
        }

        return new MCTSState(newResolved, newPending, newConstraints);
    }

    /// <summary>
    /// Normalize version constraints by converting singular versions (e.g., "1.2.3") to caret ranges (e.g., "^1.2.3")
    /// This allows matching of compatible versions within the same major version.
    /// </summary>
    private string NormalizeVersionConstraint(string versionRange)
    {
        if (string.IsNullOrWhiteSpace(versionRange))
        {
            return versionRange;
        }

        var trimmed = versionRange.Trim();

        // Check if it's already a range operator (^, ~, >, <, >=, <=, ||, &&, *, x, X)
        if (trimmed.StartsWith("^") ||
            trimmed.StartsWith("~") ||
            trimmed.StartsWith(">") ||
            trimmed.StartsWith("<") ||
            trimmed.Contains("||") ||
            trimmed.Contains("&&") ||
            trimmed.Contains("*") ||
            trimmed.Contains("x") ||
            trimmed.Contains("X"))
        {
            return trimmed;
        }

        // Check if it's a simple version number (e.g., "1.2.3", "2.0.0")
        // Pattern matches: digits.digits.digits or digits.digits or just digits
        var simpleVersionPattern = @"^\d+(\.\d+)?(\.\d+)?$";
        if (Regex.IsMatch(trimmed, simpleVersionPattern))
        {
            // Convert to caret range
            Console.WriteLine($"Normalizing singular version constraint '{trimmed}' to '^{trimmed}'");
            return $"^{trimmed}";
        }

        // Return as-is if it doesn't match known patterns
        return trimmed;
    }

    /// <summary>
    /// Simulation phase: Random playout using version selection heuristic
    /// </summary>
    private async Task<double> SimulateAsync(MCTSState state)
    {
        var currentState = state.Clone();
        int depth = 0;

        while (!currentState.IsTerminal() && depth < _maxSimulationDepth)
        {
            if (!currentState.Pending.Any())
            {
                break;
            }

            var pending = currentState.Pending.Peek();

            // Get available versions
            var availableVersions = await GetAvailableVersionsAsync(pending.PackageName);
            var constraints = GetConstraintsWithPendingDependency(currentState, pending);

            // Filter valid versions based on dependency type and CVE threshold
            var validVersions = await FilterValidVersionsAsync(pending.PackageName, availableVersions, constraints);

            if (!validVersions.Any())
            {
                // No valid version found - terminal failure
                return 0.0;
            }

            // Select version using biased probability distribution
            var selectedVersion = SelectVersionBiased(validVersions);

            // Create new state
            currentState = await CreateChildStateAsync(currentState, pending.PackageName, selectedVersion);
            depth++;
        }

        // Calculate reward
        return await CalculateReward(currentState);
    }

    /// <summary>
    /// Select version using biased probability distribution favoring latest versions
    /// P(v) = exp(lambda * rank(v)) / sum(exp(lambda * rank(vi)))
    /// Uses log-sum-exp trick for numerical stability.
    /// </summary>
    private string SelectVersionBiased(List<string> versions)
    {
        if (versions == null || versions.Count == 0)
            throw new ArgumentException("No versions available");
        if (versions.Count == 1)
            return versions[0];

        int n = versions.Count;
        double[] scores = new double[n];

        // Compute raw scores: lambda * rank
        for (int i = 0; i < n; i++)
        {
            int rank = n - i; // Latest version has highest rank
            scores[i] = _lambda * rank;
        }

        // Find max for log-sum-exp trick
        double maxScore = scores.Max();

        // Compute denominator in a stable way
        double sumExp = 0.0;
        for (int i = 0; i < n; i++)
        {
            sumExp += Math.Exp(scores[i] - maxScore);
        }
        double logZ = maxScore + Math.Log(sumExp);

        // Compute normalized probabilities
        double[] probabilities = new double[n];
        for (int i = 0; i < n; i++)
        {
            probabilities[i] = Math.Exp(scores[i] - logZ);
        }

        // Sample based on probabilities
        var random = new Random();
        double r = random.NextDouble();
        double cumulative = 0.0;

        for (int i = 0; i < n; i++)
        {
            cumulative += probabilities[i];
            if (r <= cumulative)
            {
                return versions[i];
            }
        }

        return versions[0]; // Fallback
    }

    /// <summary>
    /// Calculate reward for a terminal state
    /// Reward is based on: validity (0 if invalid) and version recency (average across packages)
    /// </summary>
    private async Task<double> CalculateReward(MCTSState state)
    {
        // Check if state is valid
        if (state.HasConstraintViolation())
        {
            var violatedPackages = state.Constraints
                .Where(c => c.Value.Any(ci => ci.Constraint == "INVALID"))
                .Select(c => c.Key)
                .ToList();
            Console.WriteLine($"⚠️  Reward: 0.0 (Constraint violation in {violatedPackages.Count} package(s): {string.Join(", ", violatedPackages)})");
            return 0.0;
        }

        // Check if all dependencies are resolved
        if (state.Pending.Any())
        {
            Console.WriteLine($"⚠️  Reward: 0.0 (Incomplete solution - {state.Pending.Count} packages still pending)");
            return 0.0;
        }

        // Calculate reward based on version recency
        double totalScore = 0;
        int count = 0;

        foreach (var resolved in state.Resolved)
        {
            var availableVersions = await _npmClient.GetAvailableVersionsAsync(resolved.Key);
            if (availableVersions != null && availableVersions.Any())
            {
                // Find rank of selected version (0 = latest, higher = older)
                int rank = availableVersions.IndexOf(resolved.Value);
                if (rank >= 0)
                {
                    // Score: 1.0 for latest, decreasing for older versions
                    double score = 1.0 - (rank / (double)availableVersions.Count);
                    totalScore += score;
                    count++;
                }
            }
        }

        var finalReward = count > 0 ? totalScore / count : 0.0;
        Console.WriteLine($"✨ Reward: {finalReward:F4} (Complete solution with {state.Resolved.Count} packages, avg version recency: {finalReward:P1})");

        // Track the best simulation solution as a fallback if the tree doesn't reach a complete solution
        if (finalReward > _bestSimulationReward)
        {
            _bestSimulationReward = finalReward;
            _bestSimulationSolution = state.Clone();
        }

        return finalReward;
    }

    /// <summary>
    /// Backpropagation phase: Update node statistics
    /// </summary>
    private void Backpropagate(MCTSNode node, double reward)
    {
        var current = node;
        while (current != null)
        {
            current.VisitCount++;
            current.TotalReward += reward;
            current = current.Parent;
        }
    }

    /// <summary>
    /// Extract the best solution from the search tree
    /// </summary>
    private VersionCalculationResult ExtractBestSolution(MCTSNode rootNode)
    {
        Console.WriteLine($"\n🎯 Extracting best solution from MCTS tree...");

        // Find the best terminal node from the tree (not from simulations)
        var (bestNode, errorInfo) = FindBestTerminalNode(rootNode);

        // If no complete solution exists in the tree, use the best simulation result as fallback
        if (bestNode == null || bestNode.State.HasConstraintViolation() || bestNode.State.Pending.Any())
        {
            if (_bestSimulationSolution != null && !_bestSimulationSolution.HasConstraintViolation() && !_bestSimulationSolution.Pending.Any())
            {
                Console.WriteLine($"⚠️  Tree did not reach a complete solution, using best result from simulations");
                Console.WriteLine($"   Simulation solution: {_bestSimulationSolution.Resolved.Count} packages, reward: {_bestSimulationReward:F4}");
                return new VersionCalculationResult(_bestSimulationSolution.Resolved);
            }
        }

        if (bestNode == null)
        {
            var errorMessage = "❌ No terminal node found - search tree did not reach any complete solutions";
            Console.WriteLine(errorMessage);
            if (!string.IsNullOrEmpty(errorInfo))
            {
                Console.WriteLine($"Error details: {errorInfo}");
            }

            // Include last constraint violation logs if available
            string fullErrorMessage = errorMessage;

            if (_lastConstraintViolationLogs.Any())
            {
                fullErrorMessage += "\n\n📋 Last constraint violation attempts:\n";
                int logIndex = 1;
                foreach (var log in _lastConstraintViolationLogs)
                {
                    fullErrorMessage += $"\n--- Attempt {logIndex} ---\n{log}";
                    logIndex++;
                }
            }

            if (!string.IsNullOrEmpty(errorInfo))
            {
                fullErrorMessage += $"\n{errorInfo}";
            }

            return new VersionCalculationResult(fullErrorMessage);
        }

        if (bestNode.State.HasConstraintViolation())
        {
            var errorMessage = $"❌ Best terminal node has constraint violations - no valid solution found";
            Console.WriteLine(errorMessage);
            if (!string.IsNullOrEmpty(errorInfo))
            {
                Console.WriteLine($"Error details: {errorInfo}");
            }

            string fullErrorMessage = errorMessage;
            if (!string.IsNullOrEmpty(errorInfo))
            {
                fullErrorMessage += $"\n{errorInfo}";
            }

            return new VersionCalculationResult(bestNode.State.Resolved, fullErrorMessage);
        }

        if (bestNode.State.Pending.Any())
        {
            var errorMessage = $"❌ Best terminal node is incomplete ({bestNode.State.Pending.Count} packages pending) - no valid solution found";
            Console.WriteLine(errorMessage);
            var pendingPackages = string.Join(", ", bestNode.State.Pending.Select(p => p.PackageName));
            Console.WriteLine($"   Pending packages: {pendingPackages}");
            if (!string.IsNullOrEmpty(errorInfo))
            {
                Console.WriteLine($"Error details: {errorInfo}");
            }

            string fullErrorMessage = errorMessage + $"\nPending packages: {pendingPackages}";
            if (!string.IsNullOrEmpty(errorInfo))
            {
                fullErrorMessage += $"\n{errorInfo}";
            }

            return new VersionCalculationResult(bestNode.State.Resolved, fullErrorMessage);
        }

        var avgReward = bestNode.VisitCount > 0 ? bestNode.TotalReward / bestNode.VisitCount : 0;
        Console.WriteLine($"✅ Found valid solution with {bestNode.State.Resolved.Count} resolved packages");
        Console.WriteLine($"   Terminal node stats: {bestNode.VisitCount} visits, avg reward: {avgReward:F4}");

        return new VersionCalculationResult(bestNode.State.Resolved);
    }

    /// <summary>
    /// Find the terminal node with the highest average reward, returning both the node and error information
    /// </summary>
    private (MCTSNode? Node, string? ErrorInfo) FindBestTerminalNode(MCTSNode node)
    {
        if (node.State.IsTerminal())
        {
            string? errorInfo = null;
            if (node.State.HasConstraintViolation())
            {
                var violations = node.State.Constraints
                    .Where(c => c.Value.Any(ci => ci.Constraint == "INVALID"))
                    .Select(c => c.Key)
                    .ToList();
                errorInfo = $"Constraint violations: {string.Join(", ", violations)}";
            }
            else if (node.State.Pending.Any())
            {
                var pending = string.Join(", ", node.State.Pending.Select(p => p.PackageName));
                errorInfo = $"Incomplete resolution, pending: {pending}";
            }
            return (node, errorInfo);
        }

        (MCTSNode? bestTerminal, string? bestErrorInfo) = (null, null);
        double bestValue = double.MinValue;
        int terminalNodesFound = 0;
        var allErrors = new List<string>();

        foreach (var child in node.Children)
        {
            var (terminal, errorInfo) = FindBestTerminalNode(child);
            if (terminal != null)
            {
                terminalNodesFound++;
                double value = terminal.VisitCount > 0 ? terminal.TotalReward / terminal.VisitCount : 0;
                if (value > bestValue)
                {
                    bestValue = value;
                    bestTerminal = terminal;
                    bestErrorInfo = errorInfo;
                }
                if (!string.IsNullOrEmpty(errorInfo))
                {
                    allErrors.Add(errorInfo);
                }
            }
        }

        if (node.State.Resolved.Count == 0 && terminalNodesFound > 0) // Root node check
        {
            Console.WriteLine($"📊 Terminal node search complete:");
            Console.WriteLine($"   Found {terminalNodesFound} terminal nodes in search tree");
            Console.WriteLine($"   Best terminal node: avg reward = {bestValue:F4}");
            if (!string.IsNullOrEmpty(bestErrorInfo))
            {
                Console.WriteLine($"   Best node error: {bestErrorInfo}");
            }
        }

        return (bestTerminal, bestErrorInfo);
    }

    /// <summary>
    /// Get available versions for a package (with caching)
    /// </summary>
    private async Task<List<string>> GetAvailableVersionsAsync(string packageName)
    {
        return await _npmClient.GetAvailableVersionsAsync(packageName);
    }

    /// <summary>
    /// Get package info for a specific version (with caching)
    /// </summary>
    private async Task<NpmPackageInfo?> GetPackageInfoAsync(string packageName, string version)
    {
        return await _npmClient.GetPackageInfoAsync(packageName, version);
    }
}

/// <summary>
/// Represents a version constraint with information about which package added it
/// </summary>
public class ConstraintInfo
{
    public string Constraint { get; }
    public string RequiredBy { get; }
    public string? RequiredByVersion { get; }

    public ConstraintInfo(string constraint, string requiredBy, string? requiredByVersion = null)
    {
        Constraint = constraint;
        RequiredBy = requiredBy;
        RequiredByVersion = requiredByVersion;
    }

    public override string ToString()
    {
        if (!string.IsNullOrEmpty(RequiredByVersion))
        {
            return $"{Constraint} (required by {RequiredBy}@{RequiredByVersion})";
        }
        return $"{Constraint} (required by {RequiredBy})";
    }
}

/// <summary>
/// Represents the state in the MCTS search
/// </summary>
public class MCTSState
{
    public Dictionary<string, string> Resolved { get; }
    public Queue<PendingDependency> Pending { get; }
    public Dictionary<string, List<ConstraintInfo>> Constraints { get; }

    public MCTSState(Dictionary<string, string> resolved, Queue<PendingDependency> pending, Dictionary<string, List<ConstraintInfo>> constraints)
    {
        Resolved = resolved;
        Pending = pending;
        Constraints = constraints;
    }

    public bool IsTerminal()
    {
        return !Pending.Any() || HasConstraintViolation();
    }

    public bool HasConstraintViolation()
    {
        return Constraints.Any(c => c.Value.Any(ci => ci.Constraint == "INVALID"));
    }

    public List<ConstraintInfo> GetConstraintsForPackage(string packageName)
    {
        return Constraints.TryGetValue(packageName, out var constraints)
            ? new List<ConstraintInfo>(constraints)
            : new List<ConstraintInfo>();
    }

    public MCTSState Clone()
    {
        return new MCTSState(
            new Dictionary<string, string>(Resolved),
            new Queue<PendingDependency>(Pending),
            Constraints.ToDictionary(kvp => kvp.Key, kvp => new List<ConstraintInfo>(kvp.Value))
        );
    }

    public int GetStateHash()
    {
        // Create a hash based on resolved packages for deduplication
        var hash = new HashCode();
        foreach (var kvp in Resolved.OrderBy(x => x.Key))
        {
            hash.Add(kvp.Key);
            hash.Add(kvp.Value);
        }
        return hash.ToHashCode();
    }
}

/// <summary>
/// Represents a pending dependency to be resolved
/// </summary>
public class PendingDependency
{
    public string PackageName { get; }
    public string VersionRange { get; }
    public string? RequiredBy { get; }

    public PendingDependency(string packageName, string versionRange, string? requiredBy)
    {
        PackageName = packageName;
        VersionRange = versionRange;
        RequiredBy = requiredBy;
    }
}

/// <summary>
/// Represents a node in the MCTS tree
/// </summary>
public class MCTSNode
{
    public MCTSState State { get; }
    public MCTSNode? Parent { get; }
    public List<MCTSNode> Children { get; }
    public int VisitCount { get; set; }
    public double TotalReward { get; set; }

    private readonly HashSet<string> _expandedVersions;

    public MCTSNode(MCTSState state, MCTSNode? parent = null)
    {
        State = state;
        Parent = parent;
        Children = new List<MCTSNode>();
        VisitCount = 0;
        TotalReward = 0;
        _expandedVersions = new HashSet<string>();
    }

    public void AddChild(MCTSNode child)
    {
        Children.Add(child);
        if (child.State.Resolved.Count > State.Resolved.Count)
        {
            var newPackage = child.State.Resolved.Keys.Except(State.Resolved.Keys).FirstOrDefault();
            if (newPackage != null)
            {
                _expandedVersions.Add($"{newPackage}@{child.State.Resolved[newPackage]}");
            }
        }
    }

    public bool HasChildForVersion(string packageName, string version)
    {
        return _expandedVersions.Contains($"{packageName}@{version}");
    }

    public bool IsFullyExpanded()
    {
        // A node is fully expanded if it's terminal or has explored all possible moves
        return State.IsTerminal() || (Children.Any() && Children.All(c => c.VisitCount > 0));
    }
}

/// <summary>
/// Result wrapper for MCTS version calculation including error information
/// </summary>
public class VersionCalculationResult
{
    public Dictionary<string, string>? Solution { get; set; }
    public string? ErrorMessage { get; set; }

    public VersionCalculationResult(Dictionary<string, string> solution)
    {
        Solution = solution;
        ErrorMessage = null;
    }

    public VersionCalculationResult(string errorMessage)
    {
        Solution = null;
        ErrorMessage = errorMessage;
    }

    public VersionCalculationResult(Dictionary<string, string> solution, string errorMessage)
    {
        Solution = solution;
        ErrorMessage = errorMessage;
    }
}

/// <summary>
/// Represents CVE threshold configuration for filtering package versions
/// Specifies the maximum allowed count for each severity level
/// </summary>
public class CveThreshold
{
    public int CriticalCount { get; set; }
    public int HighCount { get; set; }
    public int MediumCount { get; set; }
    public int LowCount { get; set; }

    public CveThreshold(int criticalCount = int.MaxValue, int highCount = int.MaxValue, int mediumCount = int.MaxValue, int lowCount = int.MaxValue)
    {
        CriticalCount = criticalCount;
        HighCount = highCount;
        MediumCount = mediumCount;
        LowCount = lowCount;
    }

    /// <summary>
    /// Check if a CveInfo meets this threshold
    /// </summary>
    public bool MeetsThreshold(CveInfo cveInfo)
    {
        return cveInfo.CriticalCount <= CriticalCount &&
               cveInfo.HighCount <= HighCount &&
               cveInfo.MediumCount <= MediumCount &&
               cveInfo.LowCount <= LowCount;
    }

    /// <summary>
    /// Create a preset threshold from a severity level string
    /// </summary>
    public static CveThreshold? FromSeverityString(string? severity)
    {
        if (string.IsNullOrEmpty(severity))
            return null;

        // Check for custom format: "CUSTOM:critical,high,medium,low"
        if (severity.StartsWith("CUSTOM:", StringComparison.OrdinalIgnoreCase))
        {
            var parts = severity.Substring(7).Split(',');
            if (parts.Length == 4 &&
                int.TryParse(parts[0], out int critical) &&
                int.TryParse(parts[1], out int high) &&
                int.TryParse(parts[2], out int medium) &&
                int.TryParse(parts[3], out int low))
            {
                return new CveThreshold(critical, high, medium, low);
            }
        }

        return severity.ToUpperInvariant() switch
        {
            "CRITICAL" => new CveThreshold(criticalCount: 0),
            "HIGH" => new CveThreshold(criticalCount: 0, highCount: 0),
            "MEDIUM" => new CveThreshold(criticalCount: 0, highCount: 0, mediumCount: 0),
            "LOW" => new CveThreshold(criticalCount: 0, highCount: 0, mediumCount: 0, lowCount: 0),
            _ => null
        };
    }

    public override string ToString()
    {
        return $"CveThreshold(Critical≤{CriticalCount}, High≤{HighCount}, Medium≤{MediumCount}, Low≤{LowCount})";
    }
}