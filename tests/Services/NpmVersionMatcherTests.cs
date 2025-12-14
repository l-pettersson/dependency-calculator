namespace DependencyCalculator.Services;

public class NpmVersionMatcherTests
{
    private readonly NpmVersionMatcher _matcher;

    public NpmVersionMatcherTests()
    {
        _matcher = new NpmVersionMatcher();
    }

    #region Exact Version Matching

    [Fact]
    public void Matches_ExactVersion_ReturnsTrue()
    {
        Assert.True(_matcher.Matches("1.2.3", "1.2.3"));
    }

    [Fact]
    public void Matches_ExactVersion_WithVPrefix_ReturnsTrue()
    {
        Assert.True(_matcher.Matches("v1.2.3", "1.2.3"));
        Assert.True(_matcher.Matches("1.2.3", "v1.2.3"));
        Assert.True(_matcher.Matches("v1.2.3", "v1.2.3"));
    }

    [Fact]
    public void Matches_ExactVersion_DifferentVersion_ReturnsFalse()
    {
        Assert.False(_matcher.Matches("1.2.3", "1.2.4"));
        Assert.False(_matcher.Matches("1.2.3", "1.3.3"));
        Assert.False(_matcher.Matches("1.2.3", "2.2.3"));
    }

    #endregion

    #region Caret Range Tests (^)

    [Fact]
    public void Matches_CaretRange_AllowsMinorAndPatchUpdates()
    {
        // ^1.2.3 allows >=1.2.3 <2.0.0
        Assert.True(_matcher.Matches("^1.2.3", "1.2.3"));
        Assert.True(_matcher.Matches("^1.2.3", "1.2.4"));
        Assert.True(_matcher.Matches("^1.2.3", "1.3.0"));
        Assert.True(_matcher.Matches("^1.2.3", "1.99.99"));
    }

    [Fact]
    public void Matches_CaretRange_DisallowsMajorUpdates()
    {
        // ^1.2.3 does not allow 2.0.0
        Assert.False(_matcher.Matches("^1.2.3", "2.0.0"));
        Assert.False(_matcher.Matches("^1.2.3", "2.1.0"));
    }

    [Fact]
    public void Matches_CaretRange_DisallowsLowerVersions()
    {
        Assert.False(_matcher.Matches("^1.2.3", "1.2.2"));
        Assert.False(_matcher.Matches("^1.2.3", "1.1.9"));
        Assert.False(_matcher.Matches("^1.2.3", "0.9.9"));
    }

    [Fact]
    public void Matches_CaretRange_ZeroMajor_AllowsPatchUpdates()
    {
        // ^0.2.3 allows >=0.2.3 <0.3.0
        Assert.True(_matcher.Matches("^0.2.3", "0.2.3"));
        Assert.True(_matcher.Matches("^0.2.3", "0.2.4"));
        Assert.True(_matcher.Matches("^0.2.3", "0.2.99"));
    }

    [Fact]
    public void Matches_CaretRange_ZeroMajor_DisallowsMinorUpdates()
    {
        // ^0.2.3 does not allow 0.3.0
        Assert.False(_matcher.Matches("^0.2.3", "0.3.0"));
        Assert.False(_matcher.Matches("^0.2.3", "0.4.0"));
        Assert.False(_matcher.Matches("^0.2.3", "1.0.0"));
    }

    [Fact]
    public void Matches_CaretRange_ZeroMajorAndMinor_RequiresExactPatch()
    {
        // ^0.0.3 allows >=0.0.3 <0.0.4
        Assert.True(_matcher.Matches("^0.0.3", "0.0.3"));
        Assert.False(_matcher.Matches("^0.0.3", "0.0.4"));
        Assert.False(_matcher.Matches("^0.0.3", "0.0.2"));
        Assert.False(_matcher.Matches("^0.0.3", "0.1.0"));
    }

    #endregion

    #region Tilde Range Tests (~)

    [Fact]
    public void Matches_TildeRange_AllowsPatchUpdates()
    {
        // ~1.2.3 allows >=1.2.3 <1.3.0
        Assert.True(_matcher.Matches("~1.2.3", "1.2.3"));
        Assert.True(_matcher.Matches("~1.2.3", "1.2.4"));
        Assert.True(_matcher.Matches("~1.2.3", "1.2.99"));
    }

    [Fact]
    public void Matches_TildeRange_DisallowsMinorUpdates()
    {
        // ~1.2.3 does not allow 1.3.0
        Assert.False(_matcher.Matches("~1.2.3", "1.3.0"));
        Assert.False(_matcher.Matches("~1.2.3", "1.4.0"));
    }

    [Fact]
    public void Matches_TildeRange_DisallowsMajorUpdates()
    {
        Assert.False(_matcher.Matches("~1.2.3", "2.0.0"));
        Assert.False(_matcher.Matches("~1.2.3", "2.2.3"));
    }

    [Fact]
    public void Matches_TildeRange_DisallowsLowerVersions()
    {
        Assert.False(_matcher.Matches("~1.2.3", "1.2.2"));
        Assert.False(_matcher.Matches("~1.2.3", "1.1.5"));
        Assert.False(_matcher.Matches("~1.2.3", "0.9.9"));
    }

    [Fact]
    public void Matches_TildeRange_ZeroVersions()
    {
        // ~0.2.3 allows >=0.2.3 <0.3.0
        Assert.True(_matcher.Matches("~0.2.3", "0.2.3"));
        Assert.True(_matcher.Matches("~0.2.3", "0.2.4"));
        Assert.False(_matcher.Matches("~0.2.3", "0.3.0"));
    }

    #endregion

    #region Greater Than / Less Than Operators

    [Fact]
    public void Matches_GreaterThanOrEqual_ReturnsCorrectly()
    {
        Assert.True(_matcher.Matches(">=1.2.3", "1.2.3"));
        Assert.True(_matcher.Matches(">=1.2.3", "1.2.4"));
        Assert.True(_matcher.Matches(">=1.2.3", "1.3.0"));
        Assert.True(_matcher.Matches(">=1.2.3", "2.0.0"));
        Assert.False(_matcher.Matches(">=1.2.3", "1.2.2"));
        Assert.False(_matcher.Matches(">=1.2.3", "1.1.9"));
        Assert.False(_matcher.Matches(">=1.2.3", "0.9.9"));
    }

    [Fact]
    public void Matches_GreaterThan_ReturnsCorrectly()
    {
        Assert.False(_matcher.Matches(">1.2.3", "1.2.3"));
        Assert.True(_matcher.Matches(">1.2.3", "1.2.4"));
        Assert.True(_matcher.Matches(">1.2.3", "1.3.0"));
        Assert.True(_matcher.Matches(">1.2.3", "2.0.0"));
        Assert.False(_matcher.Matches(">1.2.3", "1.2.2"));
    }

    [Fact]
    public void Matches_LessThanOrEqual_ReturnsCorrectly()
    {
        Assert.True(_matcher.Matches("<=1.2.3", "1.2.3"));
        Assert.True(_matcher.Matches("<=1.2.3", "1.2.2"));
        Assert.True(_matcher.Matches("<=1.2.3", "1.1.0"));
        Assert.True(_matcher.Matches("<=1.2.3", "0.9.9"));
        Assert.False(_matcher.Matches("<=1.2.3", "1.2.4"));
        Assert.False(_matcher.Matches("<=1.2.3", "1.3.0"));
        Assert.False(_matcher.Matches("<=1.2.3", "2.0.0"));
    }

    [Fact]
    public void Matches_LessThan_ReturnsCorrectly()
    {
        Assert.False(_matcher.Matches("<1.2.3", "1.2.3"));
        Assert.True(_matcher.Matches("<1.2.3", "1.2.2"));
        Assert.True(_matcher.Matches("<1.2.3", "1.1.0"));
        Assert.True(_matcher.Matches("<1.2.3", "0.9.9"));
        Assert.False(_matcher.Matches("<1.2.3", "1.2.4"));
    }

    #endregion

    #region Wildcard Tests

    [Fact]
    public void Matches_Wildcard_MatchesAnyVersion()
    {
        Assert.True(_matcher.Matches("*", "0.0.1"));
        Assert.True(_matcher.Matches("*", "1.2.3"));
        Assert.True(_matcher.Matches("*", "99.99.99"));
    }

    [Fact]
    public void Matches_XWildcard_MatchesAnyVersion()
    {
        Assert.True(_matcher.Matches("x", "1.2.3"));
        Assert.True(_matcher.Matches("X", "1.2.3"));
    }

    #endregion

    #region Edge Cases and Invalid Input

    [Fact]
    public void Matches_NullOrEmptyRange_ReturnsFalse()
    {
        Assert.False(_matcher.Matches(null!, "1.2.3"));
        Assert.False(_matcher.Matches("", "1.2.3"));
        Assert.False(_matcher.Matches("   ", "1.2.3"));
    }

    [Fact]
    public void Matches_NullOrEmptyVersion_ReturnsFalse()
    {
        Assert.False(_matcher.Matches("^1.2.3", null!));
        Assert.False(_matcher.Matches("^1.2.3", ""));
        Assert.False(_matcher.Matches("^1.2.3", "   "));
    }

    [Fact]
    public void Matches_VersionWithPrerelease_HandlesCorrectly()
    {
        // Pre-release versions should use the numeric part for comparison
        Assert.True(_matcher.Matches("^1.2.3", "1.2.3-beta"));
        Assert.True(_matcher.Matches("^1.2.3", "1.3.0-alpha"));
    }

    [Fact]
    public void Matches_VersionWithBuildMetadata_HandlesCorrectly()
    {
        Assert.True(_matcher.Matches("^1.2.3", "1.2.3+build123"));
        Assert.True(_matcher.Matches("^1.2.3", "1.3.0+20231201"));
    }

    [Fact]
    public void Matches_TwoDigitVersion_HandlesCorrectly()
    {
        Assert.True(_matcher.Matches("1.2", "1.2.0"));
        Assert.True(_matcher.Matches("^1.2", "1.2.0"));
        Assert.True(_matcher.Matches("^1.2", "1.3.0"));
        Assert.False(_matcher.Matches("^1.2", "2.0.0"));
    }

    [Fact]
    public void Matches_SingleDigitVersion_HandlesCorrectly()
    {
        Assert.True(_matcher.Matches("1", "1.0.0"));
        Assert.True(_matcher.Matches("^1", "1.5.0"));
        Assert.False(_matcher.Matches("^1", "2.0.0"));
    }

    [Fact]
    public void Matches_WithWhitespace_HandlesCorrectly()
    {
        Assert.True(_matcher.Matches(" ^1.2.3 ", " 1.2.4 "));
        Assert.True(_matcher.Matches("  ~1.2.3  ", "  1.2.5  "));
        Assert.True(_matcher.Matches(" >= 1.2.3 ", " 1.3.0 "));
    }

    #endregion

    #region Logical Operators - AND

    [Fact]
    public void Matches_AndOperator_SpaceSeparated_BothConditionsMet()
    {
        // >=1.0.0 <2.0.0 should match 1.5.0
        Assert.True(_matcher.Matches(">=1.0.0 <2.0.0", "1.5.0"));
        Assert.True(_matcher.Matches(">=1.0.0 <2.0.0", "1.0.0"));
        Assert.True(_matcher.Matches(">=1.0.0 <2.0.0", "1.9.9"));
    }

    [Fact]
    public void Matches_AndOperator_SpaceSeparated_FirstConditionFails()
    {
        // >=1.0.0 <2.0.0 should not match 0.9.9
        Assert.False(_matcher.Matches(">=1.0.0 <2.0.0", "0.9.9"));
    }

    [Fact]
    public void Matches_AndOperator_SpaceSeparated_SecondConditionFails()
    {
        // >=1.0.0 <2.0.0 should not match 2.0.0
        Assert.False(_matcher.Matches(">=1.0.0 <2.0.0", "2.0.0"));
        Assert.False(_matcher.Matches(">=1.0.0 <2.0.0", "2.5.0"));
    }

    [Fact]
    public void Matches_AndOperator_ExplicitDoubleAmpersand()
    {
        // >=1.2.7 && <1.3.0
        Assert.True(_matcher.Matches(">=1.2.7 && <1.3.0", "1.2.7"));
        Assert.True(_matcher.Matches(">=1.2.7 && <1.3.0", "1.2.8"));
        Assert.True(_matcher.Matches(">=1.2.7 && <1.3.0", "1.2.99"));
        Assert.False(_matcher.Matches(">=1.2.7 && <1.3.0", "1.2.6"));
        Assert.False(_matcher.Matches(">=1.2.7 && <1.3.0", "1.3.0"));
    }

    [Fact]
    public void Matches_AndOperator_MultipleConditions()
    {
        // >1.0.0 <=2.0.0 >=1.5.0
        Assert.True(_matcher.Matches(">1.0.0 <=2.0.0 >=1.5.0", "1.5.0"));
        Assert.True(_matcher.Matches(">1.0.0 <=2.0.0 >=1.5.0", "2.0.0"));
        Assert.False(_matcher.Matches(">1.0.0 <=2.0.0 >=1.5.0", "1.4.9"));
        Assert.False(_matcher.Matches(">1.0.0 <=2.0.0 >=1.5.0", "1.0.0"));
        Assert.False(_matcher.Matches(">1.0.0 <=2.0.0 >=1.5.0", "2.0.1"));
    }

    [Fact]
    public void Matches_AndOperator_WithWhitespace()
    {
        Assert.True(_matcher.Matches(" >= 1.0.0   <  2.0.0 ", "1.5.0"));
        Assert.False(_matcher.Matches(" >= 1.0.0   <  2.0.0 ", "2.5.0"));
    }

    #endregion

    #region Logical Operators - OR

    [Fact]
    public void Matches_OrOperator_FirstAlternativeMatches()
    {
        // 1.x || 2.x should match 1.5.0
        Assert.True(_matcher.Matches("1.x || 2.x", "1.5.0"));
        Assert.True(_matcher.Matches("^1.0.0 || ^2.0.0", "1.5.0"));
    }

    [Fact]
    public void Matches_OrOperator_SecondAlternativeMatches()
    {
        // 1.x || 2.x should match 2.5.0
        Assert.True(_matcher.Matches("1.x || 2.x", "2.5.0"));
        Assert.True(_matcher.Matches("^1.0.0 || ^2.0.0", "2.5.0"));
    }

    [Fact]
    public void Matches_OrOperator_NoAlternativeMatches()
    {
        // 1.x || 2.x should not match 3.0.0
        Assert.False(_matcher.Matches("1.x || 2.x", "3.0.0"));
        Assert.False(_matcher.Matches("^1.0.0 || ^2.0.0", "3.0.0"));
        Assert.False(_matcher.Matches("^1.0.0 || ^2.0.0", "0.9.0"));
    }

    [Fact]
    public void Matches_OrOperator_MultipleAlternatives()
    {
        // ^1.0.0 || ^2.0.0 || ^3.0.0
        Assert.True(_matcher.Matches("^1.0.0 || ^2.0.0 || ^3.0.0", "1.5.0"));
        Assert.True(_matcher.Matches("^1.0.0 || ^2.0.0 || ^3.0.0", "2.5.0"));
        Assert.True(_matcher.Matches("^1.0.0 || ^2.0.0 || ^3.0.0", "3.5.0"));
        Assert.False(_matcher.Matches("^1.0.0 || ^2.0.0 || ^3.0.0", "4.0.0"));
    }

    [Fact]
    public void Matches_OrOperator_WithWhitespace()
    {
        Assert.True(_matcher.Matches(" ^1.0.0  ||  ^2.0.0 ", "1.5.0"));
        Assert.True(_matcher.Matches(" ^1.0.0  ||  ^2.0.0 ", "2.5.0"));
    }

    [Fact]
    public void Matches_OrOperator_TildeWithMajorVersionOnly()
    {
        // ~1 || ~6 should match 1.x.x or 6.x.x
        Assert.True(_matcher.Matches("~1 || ~6", "1.0.0"));
        Assert.True(_matcher.Matches("~1 || ~6", "1.5.0"));
        Assert.True(_matcher.Matches("~1 || ~6", "1.9.9"));
        Assert.True(_matcher.Matches("~1 || ~6", "6.0.0"));
        Assert.True(_matcher.Matches("~1 || ~6", "6.5.0"));
        Assert.True(_matcher.Matches("~1 || ~6", "6.9.9"));
        Assert.False(_matcher.Matches("~1 || ~6", "2.0.0"));
        Assert.False(_matcher.Matches("~1 || ~6", "5.0.0"));
        Assert.False(_matcher.Matches("~1 || ~6", "7.0.0"));
        Assert.False(_matcher.Matches("~1 || ~6", "0.9.0"));
    }

    [Fact]
    public void Matches_ComparisonOperator_WithWildcard()
    {
        // >=16.x should match any version >= 16.0.0
        Assert.True(_matcher.Matches(">=16.x", "16.0.0"));
        Assert.True(_matcher.Matches(">=16.x", "16.5.0"));
        Assert.True(_matcher.Matches(">=16.x", "17.0.0"));
        Assert.True(_matcher.Matches(">=16.x", "18.0.0"));
        Assert.True(_matcher.Matches(">=16.x", "100.0.0"));
        Assert.False(_matcher.Matches(">=16.x", "15.9.9"));
        Assert.False(_matcher.Matches(">=16.x", "15.0.0"));
        Assert.False(_matcher.Matches(">=16.x", "0.0.1"));
    }

    #endregion

    #region Complex Combinations - AND and OR

    [Fact]
    public void Matches_ComplexCombination_OrWithAndRanges()
    {
        // (>=1.0.0 <1.5.0) || (>=2.0.0 <2.5.0)
        Assert.True(_matcher.Matches(">=1.0.0 <1.5.0 || >=2.0.0 <2.5.0", "1.2.0"));
        Assert.True(_matcher.Matches(">=1.0.0 <1.5.0 || >=2.0.0 <2.5.0", "2.2.0"));
        Assert.False(_matcher.Matches(">=1.0.0 <1.5.0 || >=2.0.0 <2.5.0", "1.5.0"));
        Assert.False(_matcher.Matches(">=1.0.0 <1.5.0 || >=2.0.0 <2.5.0", "1.8.0"));
        Assert.False(_matcher.Matches(">=1.0.0 <1.5.0 || >=2.0.0 <2.5.0", "2.5.0"));
        Assert.False(_matcher.Matches(">=1.0.0 <1.5.0 || >=2.0.0 <2.5.0", "3.0.0"));
    }

    [Fact]
    public void Matches_ComplexCombination_AndWithDoubleAmpersand()
    {
        // >=1.0.0 && <2.0.0 && >=1.5.0
        Assert.True(_matcher.Matches(">=1.0.0 && <2.0.0 && >=1.5.0", "1.5.0"));
        Assert.True(_matcher.Matches(">=1.0.0 && <2.0.0 && >=1.5.0", "1.9.9"));
        Assert.False(_matcher.Matches(">=1.0.0 && <2.0.0 && >=1.5.0", "1.4.9"));
        Assert.False(_matcher.Matches(">=1.0.0 && <2.0.0 && >=1.5.0", "2.0.0"));
    }

    [Fact]
    public void Matches_ComplexCombination_MixedOperators()
    {
        // ^1.0.0 || >=2.0.0 <3.0.0
        Assert.True(_matcher.Matches("^1.0.0 || >=2.0.0 <3.0.0", "1.5.0"));
        Assert.True(_matcher.Matches("^1.0.0 || >=2.0.0 <3.0.0", "2.5.0"));
        Assert.False(_matcher.Matches("^1.0.0 || >=2.0.0 <3.0.0", "0.9.0"));
        Assert.False(_matcher.Matches("^1.0.0 || >=2.0.0 <3.0.0", "3.0.0"));
    }

    #endregion

    #region Real-World Scenarios

    [Fact]
    public void Matches_RealWorldScenario_React()
    {
        // React 18.x.x with caret range
        Assert.True(_matcher.Matches("^18.0.0", "18.0.0"));
        Assert.True(_matcher.Matches("^18.0.0", "18.2.0"));
        Assert.True(_matcher.Matches("^18.0.0", "18.3.1"));
        Assert.False(_matcher.Matches("^18.0.0", "17.0.2"));
        Assert.False(_matcher.Matches("^18.0.0", "19.0.0"));
    }

    [Fact]
    public void Matches_RealWorldScenario_PreReleasePackage()
    {
        // Pre-1.0.0 package with tilde
        Assert.True(_matcher.Matches("~0.5.2", "0.5.2"));
        Assert.True(_matcher.Matches("~0.5.2", "0.5.3"));
        Assert.False(_matcher.Matches("~0.5.2", "0.6.0"));
    }

    [Fact]
    public void Matches_RealWorldScenario_BetaVersion()
    {
        // Handling experimental packages
        Assert.True(_matcher.Matches("^0.0.5", "0.0.5"));
        Assert.False(_matcher.Matches("^0.0.5", "0.0.6"));
    }

    [Fact]
    public void Matches_RealWorldScenario_PeerDependency()
    {
        // Peer dependency with OR - supporting multiple major versions
        Assert.True(_matcher.Matches("^16.8.0 || ^17.0.0 || ^18.0.0", "16.8.0"));
        Assert.True(_matcher.Matches("^16.8.0 || ^17.0.0 || ^18.0.0", "17.0.2"));
        Assert.True(_matcher.Matches("^16.8.0 || ^17.0.0 || ^18.0.0", "18.2.0"));
        Assert.False(_matcher.Matches("^16.8.0 || ^17.0.0 || ^18.0.0", "15.0.0"));
        Assert.False(_matcher.Matches("^16.8.0 || ^17.0.0 || ^18.0.0", "19.0.0"));
    }

    [Fact]
    public void Matches_RealWorldScenario_SecurityPatch()
    {
        // Requiring a minimum version for security fix within a range
        Assert.True(_matcher.Matches(">=1.2.7 <1.3.0", "1.2.7"));
        Assert.True(_matcher.Matches(">=1.2.7 <1.3.0", "1.2.8"));
        Assert.False(_matcher.Matches(">=1.2.7 <1.3.0", "1.2.6"));
        Assert.False(_matcher.Matches(">=1.2.7 <1.3.0", "1.3.0"));
    }

    #endregion
}
