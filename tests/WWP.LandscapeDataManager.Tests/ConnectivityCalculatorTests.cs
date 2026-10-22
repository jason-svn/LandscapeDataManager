using WWP.LandscapeDataManager.Contracts;
using WWP.LandscapeDataManager.Shared.Services;
using Xunit;

namespace WWP.LandscapeDataManager.Tests;

public class ConnectivityCalculatorTests
{
    private static ConnectivityPatch Tree(string id, double x, double y, double radius = 1) =>
        new(id, Math.PI * radius * radius, new PlanPoint(x, y), radius);

    [Fact]
    public void Dpc_MatchesBruteForceRemovalOfEachPatch()
    {
        var random = new Random(7);
        var patches = Enumerable.Range(0, 40)
            .Select(index => Tree($"t{index}", random.NextDouble() * 200, random.NextDouble() * 120, 0.5 + random.NextDouble() * 4))
            .Append(new ConnectivityPatch("bed", 600, new PlanPoint(100, 60), 0,
                [new PlanPoint(80, 50), new PlanPoint(120, 50), new PlanPoint(120, 65), new PlanPoint(80, 65)]))
            .ToList();
        var options = new ConnectivityOptions([20, 80], NegligibleProbability: 1e-12);

        var result = ConnectivityCalculator.Calculate(patches, options);

        for (var m = 0; m < options.DispersalDistancesMeters.Count; m++)
        {
            var decay = -Math.Log(0.5) / options.DispersalDistancesMeters[m];
            var full = BruteForcePcNumerator(patches, decay, removed: -1);
            Assert.Equal(Math.Sqrt(full), result.EquivalentConnectedAreaByDistance[m], 6);
            for (var k = 0; k < patches.Count; k++)
            {
                var expected = (full - BruteForcePcNumerator(patches, decay, removed: k)) / full;
                Assert.Equal(expected, result.Patches[k].DpcByDistance[m], 9);
            }
        }
    }

    [Fact]
    public void SteppingStoneBetweenTwoClusters_IsCritical()
    {
        // Two groups of trees 40 m apart, linked through one large-canopy tree in the gap.
        var patches = new List<ConnectivityPatch>();
        for (var index = 0; index < 5; index++)
        {
            patches.Add(Tree($"west{index}", 0, index * 3));
            patches.Add(Tree($"east{index}", 40, index * 3));
        }

        patches.Add(Tree("bridge", 20, 6, 6));
        // A lone tree far away contributes almost nothing.
        patches.Add(Tree("lone", 500, 500));

        var result = ConnectivityCalculator.Calculate(patches, new ConnectivityOptions([10]));

        var bridge = result.Patches.Single(patch => patch.Id == "bridge");
        var lone = result.Patches.Single(patch => patch.Id == "lone");
        Assert.Equal(ConnectivityCalculator.CriticalBand, bridge.Band);
        Assert.Equal(1d, bridge.Score);
        Assert.Equal(ConnectivityCalculator.LowBand, lone.Band);
        Assert.Equal(0d, lone.Score);
    }

    [Fact]
    public void EdgeToEdgeDistance_SubtractsCanopiesAndMeasuresToPolygonEdges()
    {
        var bed = new ConnectivityPatch("bed", 100, new PlanPoint(5, 5), 0,
            [new PlanPoint(0, 0), new PlanPoint(10, 0), new PlanPoint(10, 10), new PlanPoint(0, 10)]);

        Assert.Equal(6, ConnectivityCalculator.EdgeToEdgeDistance(Tree("a", 0, 0, 2), Tree("b", 10, 0, 2)), 9);
        Assert.Equal(0, ConnectivityCalculator.EdgeToEdgeDistance(Tree("a", 0, 0, 6), Tree("b", 10, 0, 6)), 9);
        Assert.Equal(3, ConnectivityCalculator.EdgeToEdgeDistance(Tree("a", 15, 5, 2), bed), 9);
        Assert.Equal(0, ConnectivityCalculator.EdgeToEdgeDistance(Tree("inside", 5, 5, 1), bed), 9);

        var otherBed = new ConnectivityPatch("other", 100, new PlanPoint(25, 5), 0,
            [new PlanPoint(20, 0), new PlanPoint(30, 0), new PlanPoint(30, 10), new PlanPoint(20, 10)]);
        Assert.Equal(10, ConnectivityCalculator.EdgeToEdgeDistance(bed, otherBed), 9);
    }

    [Fact]
    public void PercentileScores_SpreadsZeroToOneAndSharesTiedRanks()
    {
        var scores = ConnectivityCalculator.PercentileScores([0.3, 0.1, 0.1, 0.9, 0.5]);

        Assert.Equal([0.5, 0.125, 0.125, 1, 0.75], scores);
    }

    [Theory]
    [InlineData(1.0, 4)]
    [InlineData(0.8, 4)]
    [InlineData(0.79, 3)]
    [InlineData(0.5, 3)]
    [InlineData(0.49, 2)]
    [InlineData(0.2, 2)]
    [InlineData(0.19, 1)]
    [InlineData(0, 1)]
    public void ToBand_UsesPointTwoFiveEightBands(double score, int band) =>
        Assert.Equal(band, ConnectivityCalculator.ToBand(score));

    [Fact]
    public void ParseDistances_AcceptsCommaOrSpaceListsAndRejectsJunk()
    {
        Assert.Equal([50d, 250d, 1000d], ConnectivityCalculator.ParseDistances("1000, 50; 250 50"));
        Assert.Null(ConnectivityCalculator.ParseDistances("50, -3"));
        Assert.Null(ConnectivityCalculator.ParseDistances("fifty"));
        Assert.Null(ConnectivityCalculator.ParseDistances(" "));
    }

    [Fact]
    public void Uncertain_WhenBandSwingsTwoOrMoreBetweenDistances()
    {
        // At 2 m dispersal the big isolated bed only counts for its own area (high dPC); at 2 km
        // everything is one network and the small linking trees matter more.
        var patches = new List<ConnectivityPatch>
        {
            new("bed", 2000, new PlanPoint(300, 0), 0,
                [new PlanPoint(280, -25), new PlanPoint(320, -25), new PlanPoint(320, 25), new PlanPoint(280, 25)])
        };
        for (var index = 0; index < 12; index++)
        {
            patches.Add(Tree($"t{index}", index * 2.5, 0, 1));
        }

        var result = ConnectivityCalculator.Calculate(patches, new ConnectivityOptions([2, 2000]));

        foreach (var patch in result.Patches)
        {
            Assert.Equal(patch.BandByDistance.Max() - patch.BandByDistance.Min() >= 2, patch.Uncertain);
        }
    }

    [Fact]
    public void Selector_KeepsTreesAndPerviousFloorsOnly()
    {
        var settings = ConnectivitySettings.Default;
        ConnectivityPatchItem Floor(string? ldsType, string? surfaceClass) =>
            new("f", 1, "Floor", "Floor", "Type", 50, new ConnectivityPoint(0, 0), 0, [], ldsType, surfaceClass, false, true, null, null);

        Assert.Null(ConnectivityPatchSelector.ExclusionReason(
            new ConnectivityPatchItem("t", 1, "Planting", "Tree", "Oak", 12, new ConnectivityPoint(0, 0), 2, null, null, null, false, true, null, null), settings));
        Assert.Null(ConnectivityPatchSelector.ExclusionReason(Floor("Wildflower meadow", null), settings));
        Assert.NotNull(ConnectivityPatchSelector.ExclusionReason(Floor("Granite paving", null), settings));
        Assert.Null(ConnectivityPatchSelector.ExclusionReason(Floor("Granite paving", "Pervious"), settings));
        Assert.NotNull(ConnectivityPatchSelector.ExclusionReason(Floor(null, null), settings));
        Assert.Null(ConnectivityPatchSelector.ExclusionReason(Floor(null, null), settings with { IncludeUnclassifiedFloors = true }));
        Assert.NotNull(ConnectivityPatchSelector.ExclusionReason(Floor("Lawn", null), settings with { IncludeFloors = false }));
    }

    [Fact]
    public void Selector_LeavesOutGrowthYearCopies()
    {
        ConnectivityPatchItem Tree(string id) =>
            new(id, 1, "Planting", "Tree", "Oak", 12, new ConnectivityPoint(0, 0), 2, null, null, null, false, true, null, null);
        var copies = new HashSet<string> { "copy" };

        Assert.Null(ConnectivityPatchSelector.ExclusionReason(Tree("original"), ConnectivitySettings.Default, copies));
        Assert.StartsWith("Growth-year copy", ConnectivityPatchSelector.ExclusionReason(Tree("copy"), ConnectivitySettings.Default, copies));
    }

    /// <summary>Σᵢ Σⱼ aᵢ·aⱼ·p*ᵢⱼ by Floyd–Warshall over every pair, optionally with one patch removed.</summary>
    private static double BruteForcePcNumerator(IReadOnlyList<ConnectivityPatch> patches, double decay, int removed)
    {
        var count = patches.Count;
        var distance = new double[count, count];
        for (var i = 0; i < count; i++)
        {
            for (var j = 0; j < count; j++)
            {
                distance[i, j] = i == j ? 0 : ConnectivityCalculator.EdgeToEdgeDistance(patches[i], patches[j]);
            }
        }

        for (var via = 0; via < count; via++)
        {
            if (via == removed)
            {
                continue;
            }

            for (var i = 0; i < count; i++)
            {
                for (var j = 0; j < count; j++)
                {
                    distance[i, j] = Math.Min(distance[i, j], distance[i, via] + distance[via, j]);
                }
            }
        }

        var sum = 0d;
        for (var i = 0; i < count; i++)
        {
            for (var j = 0; j < count; j++)
            {
                if (i != removed && j != removed)
                {
                    sum += patches[i].AreaSquareMeters * patches[j].AreaSquareMeters * Math.Exp(-decay * distance[i, j]);
                }
            }
        }

        return sum;
    }
}
