using WWP.LandscapeDataManager.Shared.Services;
using Xunit;

namespace WWP.LandscapeDataManager.Tests;

public class ThreeWayDiffClassifierTests
{
    [Fact]
    public void Never_synced_and_current_already_matches_latest_is_unchanged()
    {
        var result = ThreeWayDiffClassifier.Classify(null, "10", "10");
        Assert.Equal("Unchanged", result.Category);
        Assert.False(result.SafeToAutoApply);
    }

    [Fact]
    public void Never_synced_and_current_differs_from_latest_is_changed_and_safe_to_apply()
    {
        var result = ThreeWayDiffClassifier.Classify(null, "5", "10");
        Assert.Equal("Changed", result.Category);
        Assert.True(result.SafeToAutoApply);
    }

    [Fact]
    public void All_three_values_equal_is_unchanged()
    {
        var result = ThreeWayDiffClassifier.Classify("10", "10", "10");
        Assert.Equal("Unchanged", result.Category);
    }

    [Fact]
    public void Only_the_source_changed_is_changed_and_safe_to_auto_apply()
    {
        var result = ThreeWayDiffClassifier.Classify("10", "10", "12");
        Assert.Equal("Changed", result.Category);
        Assert.True(result.SafeToAutoApply);
    }

    [Fact]
    public void Only_revit_was_manually_edited_is_a_conflict_not_ready_for_auto_apply()
    {
        var result = ThreeWayDiffClassifier.Classify("10", "15", "10");
        Assert.Equal("Conflict", result.Category);
        Assert.False(result.SafeToAutoApply);
    }

    [Fact]
    public void Both_revit_and_source_diverged_to_different_values_is_a_conflict()
    {
        var result = ThreeWayDiffClassifier.Classify("10", "15", "20");
        Assert.Equal("Conflict", result.Category);
        Assert.False(result.SafeToAutoApply);
    }

    [Fact]
    public void Both_diverged_but_coincidentally_agree_is_unchanged()
    {
        var result = ThreeWayDiffClassifier.Classify("10", "20", "20");
        Assert.Equal("Unchanged", result.Category);
    }
}
