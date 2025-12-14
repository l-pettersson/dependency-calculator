using DependencyCalculator.Models;
using Moq;

namespace DependencyCalculator.Services;

public class NpmVersionCalculatorTests
{
    private readonly INpmVersionMatcher _versionMatcher;
    private readonly Mock<INpmService> _mockNpmClient;
    private readonly Mock<ICveService> _mockCveClient;

    public NpmVersionCalculatorTests()
    {
        _versionMatcher = new NpmVersionMatcher();
        _mockNpmClient = new Mock<INpmService>();
        _mockCveClient = new Mock<ICveService>();
    }

    private NpmVersionCalculator CreateCalculator(
        int maxIterations = 100,
        int maxSimulationDepth = 50,
        int maxDepth = 5,
        int maxCompareVersion = 20,
        double lambda = 2,
        bool initVersions = false,
        DependencyType dependencyType = DependencyType.Dependencies,
        CveThreshold? cveThreshold = null)
    {
        return new NpmVersionCalculator(
            _versionMatcher,
            _mockNpmClient.Object,
            _mockCveClient.Object,
            maxIterations,
            maxSimulationDepth,
            maxDepth,
            maxCompareVersion,
            lambda,
            initVersions,
            dependencyType,
            cveThreshold
        );
    }

    #region CalculateOptimalVersionsAsync Tests with Mocked Services

    [Fact]
    public async Task CalculateOptimalVersionsAsync_WithNullDependencies_ReturnsEmptyResult()
    {
        // Arrange
        var calculator = CreateCalculator();

        // Act
        var result = await calculator.CalculateOptimalVersionsAsync(null!);

        // Assert
        Assert.NotNull(result);
        Assert.NotNull(result.Solution);
        Assert.Empty(result.Solution);
        Assert.Null(result.ErrorMessage);
    }

    [Fact]
    public async Task CalculateOptimalVersionsAsync_WithEmptyDependencies_ReturnsEmptyResult()
    {
        // Arrange
        var calculator = CreateCalculator();
        var dependencies = new Dictionary<string, string>();

        // Act
        var result = await calculator.CalculateOptimalVersionsAsync(dependencies);

        // Assert
        Assert.NotNull(result);
        Assert.NotNull(result.Solution);
        Assert.Empty(result.Solution);
        Assert.Null(result.ErrorMessage);
    }

    [Fact]
    public async Task CalculateOptimalVersionsAsync_WithSingleDependency_ReturnsResolvedVersion()
    {
        // Arrange
        var calculator = CreateCalculator(maxIterations: 50);
        var dependencies = new Dictionary<string, string>
        {
            { "lodash", "^4.17.0" }
        };

        // Setup npm client to return available versions
        _mockNpmClient
            .Setup(x => x.GetAvailableVersionsAsync(It.IsAny<string>()))
            .ReturnsAsync((string name) =>
            {
                if (name == "lodash")
                {
                    return new List<string> { "4.17.21", "4.17.20", "4.17.19" };
                }
                return new List<string>();
            });

        _mockNpmClient
            .Setup(x => x.GetPackageInfoAsync(It.IsAny<string>(), It.IsAny<string>()))
            .ReturnsAsync((string name, string version) =>
            {
                if (name == "lodash" && (version == "4.17.21" || version == "4.17.20" || version == "4.17.19"))
                {
                    return new NpmPackageInfo(name, version, new Dictionary<string, string>(), null, null);
                }
                return null;
            });

        // Act
        var result = await calculator.CalculateOptimalVersionsAsync(dependencies);

        // Assert
        Assert.NotNull(result);
        Assert.NotNull(result.Solution);
        Assert.Single(result.Solution);
        Assert.Equal("4.17.21", result.Solution["lodash"]);
    }

    [Fact]
    public async Task CalculateOptimalVersionsAsync_WithNonExistentPackage_ReturnsError()
    {
        // Arrange
        var calculator = CreateCalculator(maxIterations: 50);
        var dependencies = new Dictionary<string, string>
        {
            { "non-existent-package-xyz", "^1.0.0" }
        };

        // Setup npm client to return empty list (package not found)
        _mockNpmClient
            .Setup(x => x.GetAvailableVersionsAsync("non-existent-package-xyz"))
            .ReturnsAsync(new List<string>());

        // Act
        var result = await calculator.CalculateOptimalVersionsAsync(dependencies);

        // Assert
        Assert.NotNull(result);
        Assert.NotNull(result.ErrorMessage);
        Assert.Contains("No terminal node found", result.ErrorMessage);
    }

    [Fact]
    public async Task CalculateOptimalVersionsAsync_WithCveThreshold_FiltersVulnerableVersions()
    {
        // Arrange
        var cveThreshold = new CveThreshold(criticalCount: 0, highCount: 0);
        var calculator = CreateCalculator(maxIterations: 50, cveThreshold: cveThreshold);

        var dependencies = new Dictionary<string, string>
        {
            { "lodash", "^4.17.0" }
        };

        // Setup npm client to return available versions
        _mockNpmClient
            .Setup(x => x.GetAvailableVersionsAsync("lodash"))
            .ReturnsAsync(new List<string> { "4.17.21", "4.17.20", "4.17.19" });

        _mockNpmClient
            .Setup(x => x.GetPackageInfoAsync("lodash", It.IsAny<string>()))
            .ReturnsAsync((string name, string version) =>
            {
                return new NpmPackageInfo(name, version, new Dictionary<string, string>(), null, null);
            });

        // Setup CVE client to return vulnerabilities for new versions
        _mockCveClient
            .Setup(x => x.GetCvesForPackageAsync("lodash", "4.17.21"))
            .ReturnsAsync(new CveInfo("lodash", "4.17.21")
            {
                Vulnerabilities = new List<CveItem>
                {
                    new CveItem("CVE-2020-1234", "Prototype pollution", "HIGH")
                }
            });
        _mockCveClient
            .Setup(x => x.GetCvesForPackageAsync("lodash", "4.17.20"))
            .ReturnsAsync(new CveInfo("lodash", "4.17.20")
            {
                Vulnerabilities = new List<CveItem>
                {
                    new CveItem("CVE-2020-1234", "Prototype pollution", "HIGH")
                }
            });
        _mockCveClient
            .Setup(x => x.GetCvesForPackageAsync("lodash", "4.17.19"))
            .ReturnsAsync(new CveInfo("lodash", "4.17.19"));


        // Act
        var result = await calculator.CalculateOptimalVersionsAsync(dependencies);

        // Assert
        Assert.NotNull(result);
        Assert.NotNull(result.Solution);
        Assert.Single(result.Solution);
        Assert.Equal("4.17.19", result.Solution["lodash"]);
    }

    [Fact]
    public async Task CalculateOptimalVersionsAsync_WithCveThresholdCritical_RejectsAllVersions()
    {
        // Arrange
        var cveThreshold = new CveThreshold(criticalCount: 0);
        var calculator = CreateCalculator(maxIterations: 50, cveThreshold: cveThreshold);

        var dependencies = new Dictionary<string, string>
        {
            { "vulnerable-package", "^1.0.0" }
        };

        // Setup npm client to return available versions
        _mockNpmClient
            .Setup(x => x.GetAvailableVersionsAsync("vulnerable-package"))
            .ReturnsAsync(new List<string> { "1.0.0" });

        _mockNpmClient
            .Setup(x => x.GetPackageInfoAsync("vulnerable-package", It.IsAny<string>()))
            .ReturnsAsync((string name, string version) =>
            {
                return new NpmPackageInfo(name, version, new Dictionary<string, string>(), null, null);
            });

        // All versions have critical vulnerabilities
        _mockCveClient
            .Setup(x => x.GetCvesForPackageAsync("vulnerable-package", It.IsAny<string>()))
            .ReturnsAsync((string name, string version) =>
            {
                return new CveInfo(name, version)
                {
                    Vulnerabilities = new List<CveItem>
                    {
                        new CveItem("CVE-2021-5678", "Critical vulnerability", "CRITICAL")
                    }
                };
            });

        // Act
        var result = await calculator.CalculateOptimalVersionsAsync(dependencies);

        // Assert - Should indicate that no valid versions were found due to CVE filtering
        Assert.NotNull(result);
        Assert.NotNull(result.ErrorMessage);
    }

    [Fact]
    public async Task CalculateOptimalVersionsAsync_WithPeerDependencies_EnforcesAllConstraints()
    {
        // Arrange
        var calculator = CreateCalculator(
            maxIterations: 50,
            dependencyType: DependencyType.PeerDependencies
        );

        var dependencies = new Dictionary<string, string>
        {
            { "react", "^17.0.0" }
        };

        // Setup npm client
        _mockNpmClient
            .Setup(x => x.GetAvailableVersionsAsync("react"))
            .ReturnsAsync(new List<string> { "17.0.2", "17.0.1" });

        _mockNpmClient
            .Setup(x => x.GetPackageInfoAsync("react", It.IsAny<string>()))
            .ReturnsAsync((string name, string version) =>
            {
                return new NpmPackageInfo(name, version, new Dictionary<string, string>(), null, new Dictionary<string, string>());
            });

        // Act
        var result = await calculator.CalculateOptimalVersionsAsync(dependencies);

        // Assert
        Assert.NotNull(result);
        Assert.NotNull(result.Solution);
        Assert.Single(result.Solution);
        Assert.Equal("17.0.2", result.Solution["react"]);
    }

    #endregion

    #region CveThreshold Tests

    [Fact]
    public void CveThreshold_FromSeverityString_Critical_ReturnsCorrectThreshold()
    {
        // Act
        var threshold = CveThreshold.FromSeverityString("CRITICAL");

        // Assert
        Assert.NotNull(threshold);
        Assert.Equal(0, threshold.CriticalCount);
        Assert.Equal(int.MaxValue, threshold.HighCount);
        Assert.Equal(int.MaxValue, threshold.MediumCount);
        Assert.Equal(int.MaxValue, threshold.LowCount);
    }

    [Fact]
    public void CveThreshold_FromSeverityString_High_ReturnsCorrectThreshold()
    {
        // Act
        var threshold = CveThreshold.FromSeverityString("HIGH");

        // Assert
        Assert.NotNull(threshold);
        Assert.Equal(0, threshold.CriticalCount);
        Assert.Equal(0, threshold.HighCount);
        Assert.Equal(int.MaxValue, threshold.MediumCount);
        Assert.Equal(int.MaxValue, threshold.LowCount);
    }

    [Fact]
    public void CveThreshold_FromSeverityString_Medium_ReturnsCorrectThreshold()
    {
        // Act
        var threshold = CveThreshold.FromSeverityString("MEDIUM");

        // Assert
        Assert.NotNull(threshold);
        Assert.Equal(0, threshold.CriticalCount);
        Assert.Equal(0, threshold.HighCount);
        Assert.Equal(0, threshold.MediumCount);
        Assert.Equal(int.MaxValue, threshold.LowCount);
    }

    [Fact]
    public void CveThreshold_FromSeverityString_Low_ReturnsCorrectThreshold()
    {
        // Act
        var threshold = CveThreshold.FromSeverityString("LOW");

        // Assert
        Assert.NotNull(threshold);
        Assert.Equal(0, threshold.CriticalCount);
        Assert.Equal(0, threshold.HighCount);
        Assert.Equal(0, threshold.MediumCount);
        Assert.Equal(0, threshold.LowCount);
    }

    [Fact]
    public void CveThreshold_FromSeverityString_Custom_ReturnsCorrectThreshold()
    {
        // Act
        var threshold = CveThreshold.FromSeverityString("CUSTOM:1,2,3,4");

        // Assert
        Assert.NotNull(threshold);
        Assert.Equal(1, threshold.CriticalCount);
        Assert.Equal(2, threshold.HighCount);
        Assert.Equal(3, threshold.MediumCount);
        Assert.Equal(4, threshold.LowCount);
    }

    [Theory]
    [InlineData("CUSTOM:invalid")]
    [InlineData("UNKNOWN")]
    public void CveThreshold_FromSeverityString_InvalidCustom_ReturnsNull(string? input)
    {
        // Act
        var threshold = CveThreshold.FromSeverityString(input);

        // Assert
        Assert.Null(threshold);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("  ")]
    public void CveThreshold_FromSeverityString_NullOrEmpty_ReturnsNull(string? input)
    {
        // Act & Assert
        var threshold = CveThreshold.FromSeverityString(input);

        // Assert
        Assert.Null(threshold);
    }

    [Fact]
    public void CveThreshold_MeetsThreshold_WithMatchingCounts_ReturnsTrue()
    {
        // Arrange
        var threshold = new CveThreshold(criticalCount: 1, highCount: 1, mediumCount: 1, lowCount: 1);
        var cveInfo = new CveInfo("test-package", "1.0.0");
        cveInfo.Vulnerabilities.Add(new CveItem("CVE-1", "Test", "CRITICAL"));
        cveInfo.Vulnerabilities.Add(new CveItem("CVE-2", "Test", "HIGH"));
        cveInfo.Vulnerabilities.Add(new CveItem("CVE-3", "Test", "MEDIUM"));
        cveInfo.Vulnerabilities.Add(new CveItem("CVE-4", "Test", "low"));

        // Act
        var result = threshold.MeetsThreshold(cveInfo);

        // Assert
        Assert.True(result);
    }

    [Fact]
    public void CveThreshold_MeetsThreshold_ExceedingCritical_ReturnsFalse()
    {
        // Arrange
        var threshold = new CveThreshold(criticalCount: 0);
        var cveInfo = new CveInfo("test-package", "1.0.0");
        cveInfo.Vulnerabilities.Add(new CveItem("CVE-1", "Test", "CRITICAL"));

        // Act
        var result = threshold.MeetsThreshold(cveInfo);

        // Assert
        Assert.False(result);
    }

    [Fact]
    public void CveThreshold_MeetsThreshold_ExceedingHigh_ReturnsFalse()
    {
        // Arrange
        var threshold = new CveThreshold(highCount: 0);
        var cveInfo = new CveInfo("test-package", "1.0.0");
        cveInfo.Vulnerabilities.Add(new CveItem("CVE-1", "Test", "HIGH"));

        // Act
        var result = threshold.MeetsThreshold(cveInfo);

        // Assert
        Assert.False(result);
    }

    [Fact]
    public void CveThreshold_MeetsThreshold_ExceedingMedium_ReturnsFalse()
    {
        // Arrange
        var threshold = new CveThreshold(mediumCount: 0);
        var cveInfo = new CveInfo("test-package", "1.0.0");
        cveInfo.Vulnerabilities.Add(new CveItem("CVE-1", "Test", "MEDIUM"));

        // Act
        var result = threshold.MeetsThreshold(cveInfo);

        // Assert
        Assert.False(result);
    }

    [Fact]
    public void CveThreshold_MeetsThreshold_ExceedingLow_ReturnsFalse()
    {
        // Arrange
        var threshold = new CveThreshold(lowCount: 0);
        var cveInfo = new CveInfo("test-package", "1.0.0");
        cveInfo.Vulnerabilities.Add(new CveItem("CVE-1", "Test", "LOW"));

        // Act
        var result = threshold.MeetsThreshold(cveInfo);

        // Assert
        Assert.False(result);
    }

    [Fact]
    public void CveThreshold_ToString_ReturnsFormattedString()
    {
        // Arrange
        var threshold = new CveThreshold(criticalCount: 1, highCount: 2, mediumCount: 3, lowCount: 4);

        // Act
        var result = threshold.ToString();

        // Assert
        Assert.Equal("CveThreshold(Critical≤1, High≤2, Medium≤3, Low≤4)", result);
    }

    #endregion

    #region ConstraintInfo Tests

    [Fact]
    public void ConstraintInfo_WithRequiredByVersion_FormatsCorrectly()
    {
        // Arrange
        var constraint = new ConstraintInfo("^1.0.0", "package-a", "1.5.0");

        // Act
        var result = constraint.ToString();

        // Assert
        Assert.Equal("^1.0.0 (required by package-a@1.5.0)", result);
    }

    [Fact]
    public void ConstraintInfo_WithoutRequiredByVersion_FormatsCorrectly()
    {
        // Arrange
        var constraint = new ConstraintInfo("^1.0.0", "package-a", null);

        // Act
        var result = constraint.ToString();

        // Assert
        Assert.Equal("^1.0.0 (required by package-a)", result);
    }

    [Fact]
    public void ConstraintInfo_ConstructorSetsProperties()
    {
        // Arrange & Act
        var constraint = new ConstraintInfo(">=1.0.0", "root", "1.0.0");

        // Assert
        Assert.Equal(">=1.0.0", constraint.Constraint);
        Assert.Equal("root", constraint.RequiredBy);
        Assert.Equal("1.0.0", constraint.RequiredByVersion);
    }

    #endregion

    #region MCTSState Tests

    [Fact]
    public void MCTSState_IsTerminal_WithNoPending_ReturnsTrue()
    {
        // Arrange
        var state = new MCTSState(
            new Dictionary<string, string>(),
            new Queue<PendingDependency>(),
            new Dictionary<string, List<ConstraintInfo>>());

        // Act
        var result = state.IsTerminal();

        // Assert
        Assert.True(result);
    }

    [Fact]
    public void MCTSState_IsTerminal_WithPending_ReturnsFalse()
    {
        // Arrange
        var pending = new Queue<PendingDependency>();
        pending.Enqueue(new PendingDependency("package-a", "^1.0.0", null));

        var state = new MCTSState(
            new Dictionary<string, string>(),
            pending,
            new Dictionary<string, List<ConstraintInfo>>());

        // Act
        var result = state.IsTerminal();

        // Assert
        Assert.False(result);
    }

    [Fact]
    public void MCTSState_IsTerminal_WithConstraintViolation_ReturnsTrue()
    {
        // Arrange
        var constraints = new Dictionary<string, List<ConstraintInfo>>
        {
            { "package-a", new List<ConstraintInfo> { new ConstraintInfo("INVALID", "root", "1.0.0") } }
        };

        var state = new MCTSState(
            new Dictionary<string, string>(),
            new Queue<PendingDependency>(),
            constraints);

        // Act
        var result = state.IsTerminal();

        // Assert
        Assert.True(result);
    }

    [Fact]
    public void MCTSState_HasConstraintViolation_WithInvalidConstraint_ReturnsTrue()
    {
        // Arrange
        var constraints = new Dictionary<string, List<ConstraintInfo>>
        {
            { "package-a", new List<ConstraintInfo> { new ConstraintInfo("INVALID", "root", "1.0.0") } }
        };

        var state = new MCTSState(
            new Dictionary<string, string>(),
            new Queue<PendingDependency>(),
            constraints);

        // Act
        var result = state.HasConstraintViolation();

        // Assert
        Assert.True(result);
    }

    [Fact]
    public void MCTSState_HasConstraintViolation_WithValidConstraints_ReturnsFalse()
    {
        // Arrange
        var constraints = new Dictionary<string, List<ConstraintInfo>>
        {
            { "package-a", new List<ConstraintInfo> { new ConstraintInfo("^1.0.0", "root", "1.0.0") } }
        };

        var state = new MCTSState(
            new Dictionary<string, string>(),
            new Queue<PendingDependency>(),
            constraints);

        // Act
        var result = state.HasConstraintViolation();

        // Assert
        Assert.False(result);
    }

    [Fact]
    public void MCTSState_HasConstraintViolation_WithMultipleConstraintsOneInvalid_ReturnsTrue()
    {
        // Arrange
        var constraints = new Dictionary<string, List<ConstraintInfo>>
        {
            {
                "package-a",
                new List<ConstraintInfo>
                {
                    new ConstraintInfo("^1.0.0", "root", "1.0.0"),
                    new ConstraintInfo("INVALID", "package-b", "2.0.0")
                }
            }
        };

        var state = new MCTSState(
            new Dictionary<string, string>(),
            new Queue<PendingDependency>(),
            constraints);

        // Act
        var result = state.HasConstraintViolation();

        // Assert
        Assert.True(result);
    }

    [Fact]
    public void MCTSState_GetConstraintsForPackage_ReturnsCorrectConstraints()
    {
        // Arrange
        var constraints = new Dictionary<string, List<ConstraintInfo>>
        {
            { "package-a", new List<ConstraintInfo> { new ConstraintInfo("^1.0.0", "root", "1.0.0") } },
            { "package-b", new List<ConstraintInfo> { new ConstraintInfo("~2.0.0", "package-a", "1.5.0") } }
        };

        var state = new MCTSState(
            new Dictionary<string, string>(),
            new Queue<PendingDependency>(),
            constraints);

        // Act
        var resultA = state.GetConstraintsForPackage("package-a");
        var resultB = state.GetConstraintsForPackage("package-b");

        // Assert
        Assert.Single(resultA);
        Assert.Equal("^1.0.0", resultA[0].Constraint);
        Assert.Single(resultB);
        Assert.Equal("~2.0.0", resultB[0].Constraint);
    }

    [Fact]
    public void MCTSState_GetConstraintsForPackage_WithNonExistentPackage_ReturnsEmptyList()
    {
        // Arrange
        var state = new MCTSState(
            new Dictionary<string, string>(),
            new Queue<PendingDependency>(),
            new Dictionary<string, List<ConstraintInfo>>());

        // Act
        var result = state.GetConstraintsForPackage("non-existent");

        // Assert
        Assert.Empty(result);
    }

    [Fact]
    public void MCTSState_Clone_CreatesDeepCopy()
    {
        // Arrange
        var resolved = new Dictionary<string, string> { { "package-a", "1.0.0" } };
        var pending = new Queue<PendingDependency>();
        pending.Enqueue(new PendingDependency("package-b", "^2.0.0", "package-a"));
        var constraints = new Dictionary<string, List<ConstraintInfo>>
        {
            { "package-a", new List<ConstraintInfo> { new ConstraintInfo("^1.0.0", "root", null) } }
        };

        var state = new MCTSState(resolved, pending, constraints);

        // Act
        var cloned = state.Clone();

        // Assert
        Assert.NotSame(state.Resolved, cloned.Resolved);
        Assert.NotSame(state.Pending, cloned.Pending);
        Assert.NotSame(state.Constraints, cloned.Constraints);
        Assert.Equal(state.Resolved.Count, cloned.Resolved.Count);
        Assert.Equal(state.Pending.Count, cloned.Pending.Count);
        Assert.Equal(state.Constraints.Count, cloned.Constraints.Count);
    }

    [Fact]
    public void MCTSState_Clone_ModifyingClonedDoesNotAffectOriginal()
    {
        // Arrange
        var resolved = new Dictionary<string, string> { { "package-a", "1.0.0" } };
        var state = new MCTSState(resolved, new Queue<PendingDependency>(), new Dictionary<string, List<ConstraintInfo>>());

        // Act
        var cloned = state.Clone();
        cloned.Resolved["package-b"] = "2.0.0";

        // Assert
        Assert.Single(state.Resolved);
        Assert.Equal(2, cloned.Resolved.Count);
    }

    [Fact]
    public void MCTSState_GetStateHash_GeneratesConsistentHash()
    {
        // Arrange
        var resolved = new Dictionary<string, string>
        {
            { "package-a", "1.0.0" },
            { "package-b", "2.0.0" }
        };

        var state1 = new MCTSState(resolved, new Queue<PendingDependency>(), new Dictionary<string, List<ConstraintInfo>>());
        var state2 = new MCTSState(resolved, new Queue<PendingDependency>(), new Dictionary<string, List<ConstraintInfo>>());

        // Act
        var hash1 = state1.GetStateHash();
        var hash2 = state2.GetStateHash();

        // Assert
        Assert.Equal(hash1, hash2);
    }

    [Fact]
    public void MCTSState_GetStateHash_DifferentResolvedProducesDifferentHash()
    {
        // Arrange
        var resolved1 = new Dictionary<string, string> { { "package-a", "1.0.0" } };
        var resolved2 = new Dictionary<string, string> { { "package-a", "2.0.0" } };

        var state1 = new MCTSState(resolved1, new Queue<PendingDependency>(), new Dictionary<string, List<ConstraintInfo>>());
        var state2 = new MCTSState(resolved2, new Queue<PendingDependency>(), new Dictionary<string, List<ConstraintInfo>>());

        // Act
        var hash1 = state1.GetStateHash();
        var hash2 = state2.GetStateHash();

        // Assert
        Assert.NotEqual(hash1, hash2);
    }

    #endregion

    #region PendingDependency Tests

    [Fact]
    public void PendingDependency_Constructor_SetsPropertiesCorrectly()
    {
        // Act
        var pending = new PendingDependency("package-a", "^1.0.0", "root");

        // Assert
        Assert.Equal("package-a", pending.PackageName);
        Assert.Equal("^1.0.0", pending.VersionRange);
        Assert.Equal("root", pending.RequiredBy);
    }

    [Fact]
    public void PendingDependency_Constructor_WithNullRequiredBy_AllowsNull()
    {
        // Act
        var pending = new PendingDependency("package-a", "^1.0.0", null);

        // Assert
        Assert.Equal("package-a", pending.PackageName);
        Assert.Equal("^1.0.0", pending.VersionRange);
        Assert.Null(pending.RequiredBy);
    }

    [Fact]
    public void PendingDependency_Constructor_WithEmptyStrings_AllowsEmptyStrings()
    {
        // Act
        var pending = new PendingDependency("", "", "");

        // Assert
        Assert.Equal("", pending.PackageName);
        Assert.Equal("", pending.VersionRange);
        Assert.Equal("", pending.RequiredBy);
    }

    #endregion

    #region MCTSNode Tests

    [Fact]
    public void MCTSNode_Constructor_InitializesCorrectly()
    {
        // Arrange
        var state = new MCTSState(
            new Dictionary<string, string>(),
            new Queue<PendingDependency>(),
            new Dictionary<string, List<ConstraintInfo>>());

        // Act
        var node = new MCTSNode(state);

        // Assert
        Assert.NotNull(node);
        Assert.Equal(state, node.State);
        Assert.Null(node.Parent);
        Assert.Empty(node.Children);
        Assert.Equal(0, node.VisitCount);
        Assert.Equal(0, node.TotalReward);
    }

    [Fact]
    public void MCTSNode_Constructor_WithParent_SetsParent()
    {
        // Arrange
        var parentState = new MCTSState(
            new Dictionary<string, string>(),
            new Queue<PendingDependency>(),
            new Dictionary<string, List<ConstraintInfo>>());

        var childState = new MCTSState(
            new Dictionary<string, string> { { "package-a", "1.0.0" } },
            new Queue<PendingDependency>(),
            new Dictionary<string, List<ConstraintInfo>>());

        var parent = new MCTSNode(parentState);

        // Act
        var child = new MCTSNode(childState, parent);

        // Assert
        Assert.Equal(parent, child.Parent);
    }

    [Fact]
    public void MCTSNode_AddChild_AddsChildCorrectly()
    {
        // Arrange
        var parentState = new MCTSState(
            new Dictionary<string, string>(),
            new Queue<PendingDependency>(),
            new Dictionary<string, List<ConstraintInfo>>());

        var childState = new MCTSState(
            new Dictionary<string, string> { { "package-a", "1.0.0" } },
            new Queue<PendingDependency>(),
            new Dictionary<string, List<ConstraintInfo>>());

        var parent = new MCTSNode(parentState);
        var child = new MCTSNode(childState, parent);

        // Act
        parent.AddChild(child);

        // Assert
        Assert.Single(parent.Children);
        Assert.Equal(child, parent.Children[0]);
    }

    [Fact]
    public void MCTSNode_AddChild_MultipleChildren_AddsAllCorrectly()
    {
        // Arrange
        var parentState = new MCTSState(
            new Dictionary<string, string>(),
            new Queue<PendingDependency>(),
            new Dictionary<string, List<ConstraintInfo>>());

        var parent = new MCTSNode(parentState);

        var child1State = new MCTSState(
            new Dictionary<string, string> { { "package-a", "1.0.0" } },
            new Queue<PendingDependency>(),
            new Dictionary<string, List<ConstraintInfo>>());

        var child2State = new MCTSState(
            new Dictionary<string, string> { { "package-a", "2.0.0" } },
            new Queue<PendingDependency>(),
            new Dictionary<string, List<ConstraintInfo>>());

        var child1 = new MCTSNode(child1State, parent);
        var child2 = new MCTSNode(child2State, parent);

        // Act
        parent.AddChild(child1);
        parent.AddChild(child2);

        // Assert
        Assert.Equal(2, parent.Children.Count);
        Assert.Contains(child1, parent.Children);
        Assert.Contains(child2, parent.Children);
    }

    [Fact]
    public void MCTSNode_HasChildForVersion_WithExistingChild_ReturnsTrue()
    {
        // Arrange
        var parentState = new MCTSState(
            new Dictionary<string, string>(),
            new Queue<PendingDependency>(),
            new Dictionary<string, List<ConstraintInfo>>());

        var childState = new MCTSState(
            new Dictionary<string, string> { { "package-a", "1.0.0" } },
            new Queue<PendingDependency>(),
            new Dictionary<string, List<ConstraintInfo>>());

        var parent = new MCTSNode(parentState);
        var child = new MCTSNode(childState, parent);
        parent.AddChild(child);

        // Act
        var result = parent.HasChildForVersion("package-a", "1.0.0");

        // Assert
        Assert.True(result);
    }

    [Fact]
    public void MCTSNode_HasChildForVersion_WithNonExistingChild_ReturnsFalse()
    {
        // Arrange
        var parentState = new MCTSState(
            new Dictionary<string, string>(),
            new Queue<PendingDependency>(),
            new Dictionary<string, List<ConstraintInfo>>());

        var parent = new MCTSNode(parentState);

        // Act
        var result = parent.HasChildForVersion("package-a", "1.0.0");

        // Assert
        Assert.False(result);
    }

    [Fact]
    public void MCTSNode_HasChildForVersion_WithDifferentVersion_ReturnsFalse()
    {
        // Arrange
        var parentState = new MCTSState(
            new Dictionary<string, string>(),
            new Queue<PendingDependency>(),
            new Dictionary<string, List<ConstraintInfo>>());

        var childState = new MCTSState(
            new Dictionary<string, string> { { "package-a", "1.0.0" } },
            new Queue<PendingDependency>(),
            new Dictionary<string, List<ConstraintInfo>>());

        var parent = new MCTSNode(parentState);
        var child = new MCTSNode(childState, parent);
        parent.AddChild(child);

        // Act
        var result = parent.HasChildForVersion("package-a", "2.0.0");

        // Assert
        Assert.False(result);
    }

    [Fact]
    public void MCTSNode_IsFullyExpanded_WithTerminalState_ReturnsTrue()
    {
        // Arrange - terminal state (no pending)
        var state = new MCTSState(
            new Dictionary<string, string>(),
            new Queue<PendingDependency>(),
            new Dictionary<string, List<ConstraintInfo>>());

        var node = new MCTSNode(state);

        // Act
        var result = node.IsFullyExpanded();

        // Assert
        Assert.True(result);
    }

    [Fact]
    public void MCTSNode_IsFullyExpanded_WithNoChildren_ReturnsFalse()
    {
        // Arrange - non-terminal state with pending
        var pending = new Queue<PendingDependency>();
        pending.Enqueue(new PendingDependency("package-a", "^1.0.0", null));

        var state = new MCTSState(
            new Dictionary<string, string>(),
            pending,
            new Dictionary<string, List<ConstraintInfo>>());

        var node = new MCTSNode(state);

        // Act
        var result = node.IsFullyExpanded();

        // Assert
        Assert.False(result);
    }

    [Fact]
    public void MCTSNode_IsFullyExpanded_WithVisitedChildren_ReturnsTrue()
    {
        // Arrange
        var pending = new Queue<PendingDependency>();
        pending.Enqueue(new PendingDependency("package-a", "^1.0.0", null));

        var parentState = new MCTSState(
            new Dictionary<string, string>(),
            pending,
            new Dictionary<string, List<ConstraintInfo>>());

        var parent = new MCTSNode(parentState);

        var childState = new MCTSState(
            new Dictionary<string, string> { { "package-a", "1.0.0" } },
            new Queue<PendingDependency>(),
            new Dictionary<string, List<ConstraintInfo>>());

        var child = new MCTSNode(childState, parent);
        child.VisitCount = 1;
        parent.AddChild(child);

        // Act
        var result = parent.IsFullyExpanded();

        // Assert
        Assert.True(result);
    }

    [Fact]
    public void MCTSNode_IsFullyExpanded_WithUnvisitedChildren_ReturnsFalse()
    {
        // Arrange
        var pending = new Queue<PendingDependency>();
        pending.Enqueue(new PendingDependency("package-a", "^1.0.0", null));

        var parentState = new MCTSState(
            new Dictionary<string, string>(),
            pending,
            new Dictionary<string, List<ConstraintInfo>>());

        var parent = new MCTSNode(parentState);

        var childState = new MCTSState(
            new Dictionary<string, string> { { "package-a", "1.0.0" } },
            new Queue<PendingDependency>(),
            new Dictionary<string, List<ConstraintInfo>>());

        var child = new MCTSNode(childState, parent);
        child.VisitCount = 0; // Unvisited
        parent.AddChild(child);

        // Act
        var result = parent.IsFullyExpanded();

        // Assert
        Assert.False(result);
    }

    [Fact]
    public void MCTSNode_VisitCount_CanBeIncremented()
    {
        // Arrange
        var state = new MCTSState(
            new Dictionary<string, string>(),
            new Queue<PendingDependency>(),
            new Dictionary<string, List<ConstraintInfo>>());

        var node = new MCTSNode(state);

        // Act
        node.VisitCount++;
        node.VisitCount++;

        // Assert
        Assert.Equal(2, node.VisitCount);
    }

    [Fact]
    public void MCTSNode_TotalReward_CanBeAccumulated()
    {
        // Arrange
        var state = new MCTSState(
            new Dictionary<string, string>(),
            new Queue<PendingDependency>(),
            new Dictionary<string, List<ConstraintInfo>>());

        var node = new MCTSNode(state);

        // Act
        node.TotalReward += 0.5;
        node.TotalReward += 0.3;

        // Assert
        Assert.Equal(0.8, node.TotalReward, precision: 5);
    }

    #endregion

    #region VersionCalculationResult Tests

    [Fact]
    public void VersionCalculationResult_WithSolution_CreatesSuccessResult()
    {
        // Arrange
        var solution = new Dictionary<string, string> { { "package-a", "1.0.0" } };

        // Act
        var result = new VersionCalculationResult(solution);

        // Assert
        Assert.NotNull(result.Solution);
        Assert.Null(result.ErrorMessage);
        Assert.Single(result.Solution);
        Assert.Equal("1.0.0", result.Solution["package-a"]);
    }

    [Fact]
    public void VersionCalculationResult_WithErrorMessage_CreatesErrorResult()
    {
        // Arrange
        var errorMessage = "Test error";

        // Act
        var result = new VersionCalculationResult(errorMessage);

        // Assert
        Assert.Null(result.Solution);
        Assert.NotNull(result.ErrorMessage);
        Assert.Equal(errorMessage, result.ErrorMessage);
    }

    [Fact]
    public void VersionCalculationResult_WithSolutionAndError_CreatesBothProperties()
    {
        // Arrange
        var solution = new Dictionary<string, string> { { "package-a", "1.0.0" } };
        var errorMessage = "Warning message";

        // Act
        var result = new VersionCalculationResult(solution, errorMessage);

        // Assert
        Assert.NotNull(result.Solution);
        Assert.NotNull(result.ErrorMessage);
        Assert.Single(result.Solution);
        Assert.Equal(errorMessage, result.ErrorMessage);
    }

    [Fact]
    public void VersionCalculationResult_WithEmptySolution_AllowsEmptyDictionary()
    {
        // Arrange
        var solution = new Dictionary<string, string>();

        // Act
        var result = new VersionCalculationResult(solution);

        // Assert
        Assert.NotNull(result.Solution);
        Assert.Empty(result.Solution);
        Assert.Null(result.ErrorMessage);
    }

    #endregion
}
