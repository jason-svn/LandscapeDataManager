using WWP.LandscapeDataManager.Contracts;
using Xunit;

namespace WWP.LandscapeDataManager.Tests;

/// <summary>The heatmap view's ten score steps: each score in exactly one, blue at the bottom, red at the top.</summary>
public sealed class ConnectivityHeatmapTests
{
    [Theory]
    [InlineData(0.0, 0.0)]
    [InlineData(0.0999, 0.0)]
    [InlineData(0.1, 0.1)]
    [InlineData(0.55, 0.5)]
    [InlineData(0.9, 0.9)]
    [InlineData(1.0, 0.9)]
    public void A_score_falls_in_one_step(double score, double stepFrom) =>
        Assert.Equal(stepFrom, ConnectivityHeatmap.StepOf(score).From, 6);

    [Fact]
    public void Ten_steps_cover_zero_to_one_and_run_from_blue_to_red()
    {
        var steps = ConnectivityHeatmap.Steps;

        Assert.Equal(10, steps.Count);
        Assert.Equal(0.0, steps[0].From);
        Assert.Equal(1.0, steps[^1].To);
        Assert.All(steps.Zip(steps.Skip(1)), pair => Assert.Equal(pair.First.To, pair.Second.From, 6));
        Assert.True(steps[0].Blue > steps[0].Red);
        Assert.True(steps[^1].Red > steps[^1].Blue);
        Assert.Equal("WWP_Connectivity 0.9-1.0 score", steps[^1].FilterName);
    }
}
