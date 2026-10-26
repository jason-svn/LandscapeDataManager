using WWP.LandscapeDataManager.App.Dashboard;
using WWP.LandscapeDataManager.Contracts;
using WWP.LandscapeDataManager.Shared.Services;
using Xunit;

namespace WWP.LandscapeDataManager.Tests;

/// <summary>The JSON export's 3D scene: Revit's geometry joined to the report, and trees grown by the family's formulas.</summary>
public sealed class DashboardJsonSceneBuilderTests
{
    private static readonly TreeGrowthType FieldMaple = new("WWP_Vegetation_Tree", "Acer_Campestre", 15, 7, 0.41, 0.24, 0.339, 37);

    private static DashboardTreeItem Tree(string id, bool growthCopy = false) =>
        DashboardAggregationTestTrees.Item(Math.PI * 9) with
        {
            UniqueId = id,
            TypeName = "Acer_Campestre",
            SpeciesCode = "ACCA",
            CommonName = "Field maple",
            CanopyCenterX = 100,
            CanopyCenterY = 200,
            CanopyRadiusMeters = 3,
            IsGrowthCopy = growthCopy
        };

    private static DashboardFloorItem Floor(string id) =>
        new(id, 1, "Floor", "Meadow", "Meadow - Meadow", "Pervious", "L0", new DesignOptionInfo(null, null, true), 400, 0, 0, 0, 0, 0, 0, 0, 0);

    private static SceneTreeItem Placed(string id, double x) =>
        new(id, "WWP_Vegetation_Tree", "Acer_Campestre", x, 200, 5, 3.4167, 6.8333, 3, 0.0694, 0, 25, 0, 0);

    private static SceneGeometryResult Geometry(params SceneTreeItem[] trees) =>
        new("Doc", trees, [FieldMaple], [new SceneFloorItem("floor", [90, 190, 5, 130, 190, 5, 130, 210, 5], [0, 1, 2], 1)]);

    private static DashboardReportResult Report(params DashboardTreeItem[] trees) =>
        new("Doc", "Metric", "GBP", null, null, trees, [Floor("floor"), Floor("not in Revit")], []);

    [Fact]
    public void Forecast_years_run_from_planting_to_the_design_year()
    {
        Assert.Equal(new[] { 0, 5, 10, 15, 20, 25 }, DashboardJsonSceneBuilder.ForecastYears(25));
        Assert.Equal(new[] { 0, 5, 10, 15, 20, 25, 30 }, DashboardJsonSceneBuilder.ForecastYears(30));
    }

    [Fact]
    public void Growth_projections_copies_are_the_growth_years_so_nothing_is_forecast()
    {
        Assert.True(DashboardJsonSceneBuilder.CanForecast(Geometry(Placed("a", 100)), Report(Tree("a"))));
        Assert.False(DashboardJsonSceneBuilder.CanForecast(Geometry(Placed("a", 100)), Report(Tree("a"), Tree("copy", growthCopy: true))));
        Assert.False(DashboardJsonSceneBuilder.CanForecast(Geometry(Placed("a", 100)) with { TreeTypes = [] }, Report(Tree("a"))));
    }

    [Fact]
    public void A_forecast_tree_is_drawn_and_counted_at_each_years_size()
    {
        var scene = new DashboardJsonSceneBuilder(Geometry(Placed("a", 100)), Report(Tree("a")), SceneGrowth.Forecast, 25);
        var tree = DashboardAggregationService.NormalizeTree(Tree("a"), "Metric", "GBP", 1);

        var atTen = scene.Grown(tree, 10).Source;
        var json = scene.ToJson();

        var expected = TreeSizeForecast.At(FieldMaple, 0, 10, 0, 0);
        Assert.Equal(expected.CrownRadiusM, atTen.CanopyRadiusMeters!.Value, 9);
        Assert.Equal(expected.CrownAreaM2, atTen.CanopyAreaSquareMeters, 9);
        Assert.Equal("forecast", json.Growth);
        Assert.Equal(0, json.Trees[0].Profile);
        Assert.Equal(26, json.Profiles[0].Count);
        Assert.Equal(Math.Round(expected.CrownRadiusM, 3), json.Profiles[0][10][2]);
    }

    [Fact]
    public void Without_forecast_growth_trees_keep_their_drawn_size()
    {
        var scene = new DashboardJsonSceneBuilder(Geometry(Placed("a", 100)), Report(Tree("a")), SceneGrowth.Worksets, 25);
        var tree = DashboardAggregationService.NormalizeTree(Tree("a"), "Metric", "GBP", 1);

        Assert.Same(tree, scene.Grown(tree, 10));
        Assert.Null(scene.ToJson().Trees[0].Profile);
        Assert.Empty(scene.ToJson().Profiles);
    }

    [Fact]
    public void Positions_are_relative_to_the_scenes_middle_and_only_elements_revit_placed_are_indexed()
    {
        var scene = new DashboardJsonSceneBuilder(Geometry(Placed("a", 100), Placed("b", 120)), Report(Tree("a"), Tree("b"), Tree("not in Revit")), SceneGrowth.Drawn, 0);
        var trees = new[] { "b", "not in Revit" }.Select(id => DashboardAggregationService.NormalizeTree(Tree(id), "Metric", "GBP", 1));

        var json = scene.ToJson();

        Assert.Equal(new double[] { 110, 200, 5 }, json.Origin);
        Assert.Equal(new double[] { -10, 0, 0 }, json.Trees[0].Xyz);
        Assert.Equal(new[] { 1 }, scene.TreeIndices(trees));
        Assert.Equal(new[] { 0 }, scene.FloorIndices(Report().Floors));
        Assert.Single(json.Floors);
        Assert.Equal("Meadow - Meadow", json.Floors[0].LdsType);
        Assert.Equal(new double[] { -20, -10, 0, 20, -10, 0, 20, 10, 0 }, json.Floors[0].Positions);
        Assert.Equal(1, json.Floors[0].Holes);
    }

    private static SceneColourScheme OxygenView(IReadOnlyDictionary<string, int> matches) =>
        new("O2", "!_S_PLT_Result_OxygenProducedAnnual_Mass", "kg/yr",
            [new("0–4", "WWP_Tree O2 00-04", "#edef5d", true, false, 0, 4), new("≥ 4", "WWP_Tree O2 04+", "#245668", true, false, 4, null)],
            matches,
            new Dictionary<string, double> { ["a"] = 7.66789, ["b"] = 1.5 });

    [Fact]
    public void A_views_colours_follow_the_scene_order_with_each_trees_value()
    {
        var geometry = Geometry(Placed("a", 100), Placed("b", 120)) with { ColourSchemes = [OxygenView(new Dictionary<string, int> { ["a"] = 1 })] };

        var scheme = Assert.Single(new DashboardJsonSceneBuilder(geometry, Report(Tree("a"), Tree("b")), SceneGrowth.Drawn, 0).ToJson().ColourSchemes!);

        Assert.Equal("O2", scheme.Name);
        Assert.Equal("kg/yr", scheme.Unit);
        Assert.Equal(new[] { "0–4", "≥ 4" }, scheme.Rules.Select(rule => rule.Label));
        Assert.Equal(new[] { 1, -1 }, scheme.Trees);
        Assert.Null(scheme.Floors);
        Assert.Equal(new double?[] { 7.6679, 1.5 }, scheme.TreeValues);
        Assert.Null(scheme.FloorValues);
    }

    [Fact]
    public void A_view_that_colours_nothing_in_the_scene_is_left_out()
    {
        var geometry = Geometry(Placed("a", 100)) with { ColourSchemes = [OxygenView(new Dictionary<string, int> { ["not in Revit"] = 0 })] };

        Assert.Null(new DashboardJsonSceneBuilder(geometry, Report(Tree("a")), SceneGrowth.Drawn, 0).ToJson().ColourSchemes);
        Assert.Null(new DashboardJsonSceneBuilder(Geometry(Placed("a", 100)), Report(Tree("a")), SceneGrowth.Drawn, 0).ToJson().ColourSchemes);
    }
}
