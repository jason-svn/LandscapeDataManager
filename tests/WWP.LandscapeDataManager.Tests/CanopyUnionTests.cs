using WWP.LandscapeDataManager.Contracts;
using WWP.LandscapeDataManager.Shared.Services;
using Xunit;

namespace WWP.LandscapeDataManager.Tests;

/// <summary>Canopy cover counts overlapping crowns once.</summary>
public sealed class CanopyUnionTests
{
    [Fact]
    public void One_canopy_is_its_circle()
    {
        Assert.Equal(Math.PI * 9, CanopyUnion.AreaSquareMeters([new CanopyCircle(10, -4, 3)]), 9);
    }

    [Fact]
    public void Separate_canopies_add_up()
    {
        Assert.Equal(Math.PI * 5, CanopyUnion.AreaSquareMeters([new CanopyCircle(0, 0, 1), new CanopyCircle(10, 0, 2)]), 9);
    }

    [Fact]
    public void The_same_canopy_twice_counts_once()
    {
        Assert.Equal(Math.PI * 4, CanopyUnion.AreaSquareMeters([new CanopyCircle(1, 1, 2), new CanopyCircle(1, 1, 2)]), 9);
    }

    [Fact]
    public void A_canopy_under_a_bigger_one_adds_nothing()
    {
        Assert.Equal(Math.PI * 25, CanopyUnion.AreaSquareMeters([new CanopyCircle(0, 0, 5), new CanopyCircle(1, 1, 2)]), 9);
    }

    [Fact]
    public void Two_overlapping_canopies_lose_their_lens()
    {
        const double radius = 2, distance = 3;
        var lens = 2 * radius * radius * Math.Acos(distance / (2 * radius)) - distance / 2 * Math.Sqrt(4 * radius * radius - distance * distance);

        var area = CanopyUnion.AreaSquareMeters([new CanopyCircle(0, 0, radius), new CanopyCircle(distance, 0, radius)]);

        Assert.Equal(2 * Math.PI * radius * radius - lens, area, 9);
    }

    [Fact]
    public void A_ring_of_canopies_leaves_its_hole_uncovered()
    {
        // Six crowns in a ring around an open centre, checked against a fine grid count.
        var circles = Enumerable.Range(0, 6)
            .Select(index => new CanopyCircle(4 * Math.Cos(index * Math.PI / 3), 4 * Math.Sin(index * Math.PI / 3), 2.3))
            .ToList();

        var exact = CanopyUnion.AreaSquareMeters(circles);

        const double step = 0.01;
        var cells = 0;
        for (var x = -7d; x < 7; x += step)
        {
            for (var y = -7d; y < 7; y += step)
            {
                double cx = x + step / 2, cy = y + step / 2;
                if (circles.Any(circle => Math.Pow(cx - circle.X, 2) + Math.Pow(cy - circle.Y, 2) <= circle.Radius * circle.Radius))
                {
                    cells++;
                }
            }
        }

        Assert.Equal(cells * step * step, exact, 1);
        Assert.True(exact < 6 * Math.PI * 2.3 * 2.3);
    }

    [Fact]
    public void Trees_without_a_position_add_their_own_area()
    {
        DashboardTreeItem Tree(double? x, double? y, double? radius, double area) => DashboardAggregationTestTrees.Item(area) with
        {
            CanopyCenterX = x,
            CanopyCenterY = y,
            CanopyRadiusMeters = radius
        };

        var area = DashboardAggregationService.CanopyAreaSquareMeters(
        [
            Tree(0, 0, 2, Math.PI * 4),
            Tree(0, 0, 2, Math.PI * 4),   // same spot: counted once
            Tree(null, null, null, 10),   // older snapshot without a position
        ]);

        Assert.Equal(Math.PI * 4 + 10, area, 9);
    }
}

internal static class DashboardAggregationTestTrees
{
    public static DashboardTreeItem Item(double canopyAreaSquareMeters, string status = "Calculated") =>
        new(Guid.NewGuid().ToString(), 1, "Tree", "Oak", "QURO", "Oak", null, null, null, null, null,
            canopyAreaSquareMeters, "L0", new DesignOptionInfo(null, null, true), status, "Metric", "USD", 1,
            0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0);
}
