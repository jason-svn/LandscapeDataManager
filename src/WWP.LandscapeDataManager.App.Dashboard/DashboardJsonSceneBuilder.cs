using WWP.LandscapeDataManager.Contracts;
using WWP.LandscapeDataManager.Shared.Services;

namespace WWP.LandscapeDataManager.App.Dashboard;

/// <summary>A tree in the 3D view: positions relative to <see cref="DashboardJsonScene.Origin"/>; sizes are [trunk height, crown height, crown radius, trunk radius] in metres.</summary>
internal sealed record DashboardJsonSceneTree(
    string Id,
    string SpeciesCode,
    string? CommonName,
    string Type,
    IReadOnlyList<double> Xyz,
    IReadOnlyList<double> Drawn,
    int? Profile,
    string? PhaseRole,
    string? DesignOption);

/// <summary>A floor's top face in the 3D view, holes cut out: x, y, z per vertex relative to the origin, three vertex indices per triangle.</summary>
internal sealed record DashboardJsonSceneFloor(
    string Id,
    string LdsType,
    string Type,
    string? SurfaceClass,
    double AreaM2,
    int Holes,
    string? PhaseRole,
    string? DesignOption,
    IReadOnlyList<double> Positions,
    IReadOnlyList<int> Indices);

/// <summary>
/// Schema 9's 3D scene. <see cref="Origin"/> is in Revit internal coordinates (metres, X east, Y north,
/// Z up). <see cref="Growth"/> says how a scenario entry sizes its trees: "forecast" — a tree's
/// <see cref="Profiles"/> row for the entry's year; "worksets" or "drawn" — as drawn.
/// </summary>
internal sealed record DashboardJsonScene(
    string Units,
    IReadOnlyList<double> Origin,
    string Growth,
    int MaxYear,
    IReadOnlyList<IReadOnlyList<IReadOnlyList<double>>> Profiles,
    IReadOnlyList<DashboardJsonSceneTree> Trees,
    IReadOnlyList<DashboardJsonSceneFloor> Floors);

/// <summary>How the export's growth years are modelled.</summary>
internal enum SceneGrowth
{
    /// <summary>No growth years: one entry per scheme, trees as drawn.</summary>
    Drawn,

    /// <summary>Growth Projections' copies on growth-year worksets: each year's trees as drawn.</summary>
    Worksets,

    /// <summary>No copies: every tree grown to each year by the planting family's formulas (<see cref="TreeSizeForecast"/>).</summary>
    Forecast
}

/// <summary>
/// Builds schema 9's scene: Revit's tree positions and floor top faces (<see cref="SceneGeometryResult"/>)
/// joined to the report's trees and floors by UniqueId, and the indices each scenario entry shows.
/// With <see cref="SceneGrowth.Forecast"/>, <see cref="Grown"/> gives a tree the size the 3D view
/// draws for a year, so the entry's canopy figures are built from the same sizes.
/// </summary>
internal sealed class DashboardJsonSceneBuilder
{
    /// <summary>The growth years the export offers without Growth Projections copies.</summary>
    public static IReadOnlyList<int> ForecastYears(int designYear) =>
        new[] { 0 }.Concat(GrowthYears.StandardYears).Append(designYear)
            .Where(year => year is >= 0 and <= GrowthYearNames.MaximumYear)
            .Distinct()
            .Order()
            .ToList();

    /// <summary>
    /// Forecast growth needs the scene's growth types and no Growth Projections copies (with copies,
    /// the copies are the growth years), and something to grow from: a type's growth rate or an
    /// i-Tree forecast with sizes.
    /// </summary>
    public static bool CanForecast(SceneGeometryResult geometry, DashboardReportResult report) =>
        !report.Trees.Any(tree => tree.IsGrowthCopy) &&
        (geometry.TreeTypes.Any(type => type.HeightRateMPerYear > 0 || type.WidthRateMPerYear > 0) ||
         (report.TreeTimelines ?? []).Any(TreeTimelines.HasSizes));

    private readonly SceneGrowth _growth;
    private readonly int _lastYear;
    private readonly double[] _origin;
    private readonly Dictionary<string, int> _treeIndex = new(StringComparer.Ordinal);
    private readonly Dictionary<string, int> _floorIndex = new(StringComparer.Ordinal);
    private readonly List<DashboardJsonSceneTree> _trees = [];
    private readonly List<DashboardJsonSceneFloor> _floors = [];
    private readonly List<IReadOnlyList<TreeSize>> _profiles = [];
    private readonly Dictionary<string, IReadOnlyList<TreeSize>> _treeProfiles = new(StringComparer.Ordinal);

    public DashboardJsonSceneBuilder(SceneGeometryResult geometry, DashboardReportResult report, SceneGrowth growth, int lastYear)
    {
        _growth = growth;
        _lastYear = Math.Max(0, lastYear);
        var treeGeometry = geometry.Trees.GroupBy(tree => tree.UniqueId, StringComparer.Ordinal).ToDictionary(group => group.Key, group => group.First(), StringComparer.Ordinal);
        var floorGeometry = geometry.Floors.GroupBy(floor => floor.UniqueId, StringComparer.Ordinal).ToDictionary(group => group.Key, group => group.First(), StringComparer.Ordinal);
        var types = geometry.TreeTypes.GroupBy(TypeKey, StringComparer.Ordinal).ToDictionary(group => group.Key, group => group.First(), StringComparer.Ordinal);
        var timelines = (report.TreeTimelines ?? []).GroupBy(timeline => timeline.Key, StringComparer.Ordinal).ToDictionary(group => group.Key, group => group.Last(), StringComparer.Ordinal);

        var placedTrees = report.Trees.Where(tree => treeGeometry.ContainsKey(tree.UniqueId)).ToList();
        var placedFloors = report.Floors.Where(floor => floorGeometry.ContainsKey(floor.UniqueId)).ToList();
        _origin = Origin(placedTrees.Select(tree => treeGeometry[tree.UniqueId]), placedFloors.Select(floor => floorGeometry[floor.UniqueId]));

        var profileIndex = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (var tree in placedTrees)
        {
            var placed = treeGeometry[tree.UniqueId];
            int? profile = null;
            if (growth == SceneGrowth.Forecast && types.TryGetValue(TypeKey(placed), out var type))
            {
                var timeline = tree.TimelineKey is { } key && timelines.TryGetValue(key, out var found) ? found : null;
                var profileKey = $"{TypeKey(type)}|{placed.AgeAtPlanting:R}|{timeline?.Key}";
                if (!profileIndex.TryGetValue(profileKey, out var index))
                {
                    index = _profiles.Count;
                    _profiles.Add(TreeSizeForecast.Profile(type, placed.AgeAtPlanting, timeline, _lastYear));
                    profileIndex[profileKey] = index;
                }

                profile = index;
                _treeProfiles[tree.UniqueId] = _profiles[index];
            }

            _treeIndex[tree.UniqueId] = _trees.Count;
            _trees.Add(new DashboardJsonSceneTree(
                tree.UniqueId,
                tree.SpeciesCode ?? "Unassigned",
                tree.CommonName,
                tree.TypeName,
                Relative(placed.X, placed.Y, placed.Z),
                [Round(placed.TrunkHeightM, 3), Round(placed.CrownHeightM, 3), Round(placed.CrownRadiusM, 3), Round(placed.TrunkRadiusM, 3)],
                profile,
                tree.PhaseRole,
                OptionLabel(tree.DesignOption)));
        }

        foreach (var floor in placedFloors)
        {
            var placed = floorGeometry[floor.UniqueId];
            var positions = new double[placed.Positions.Count];
            for (var index = 0; index + 2 < positions.Length; index += 3)
            {
                positions[index] = Round(placed.Positions[index] - _origin[0], 2);
                positions[index + 1] = Round(placed.Positions[index + 1] - _origin[1], 2);
                positions[index + 2] = Round(placed.Positions[index + 2] - _origin[2], 2);
            }

            _floorIndex[floor.UniqueId] = _floors.Count;
            _floors.Add(new DashboardJsonSceneFloor(
                floor.UniqueId,
                floor.LdsType ?? "Unassigned",
                floor.TypeName,
                floor.SurfaceClass,
                Round(floor.AreaSquareMeters, 2),
                placed.HoleCount,
                SitePhases.RoleOf(floor),
                OptionLabel(floor.DesignOption),
                positions,
                placed.Indices));
        }
    }

    /// <summary>
    /// <paramref name="tree"/> with its canopy as the 3D view draws it <paramref name="year"/> years
    /// after planting (forecast growth only; any other tree is returned as it is).
    /// </summary>
    public NormalizedTreeMetrics Grown(NormalizedTreeMetrics tree, int year)
    {
        if (_growth != SceneGrowth.Forecast || !_treeProfiles.TryGetValue(tree.Source.UniqueId, out var profile))
        {
            return tree;
        }

        var size = profile[Math.Clamp(year, 0, profile.Count - 1)];
        return tree with { Source = tree.Source with { CanopyRadiusMeters = size.CrownRadiusM, CanopyAreaSquareMeters = size.CrownAreaM2 } };
    }

    /// <summary>The scene indices of the trees an entry shows.</summary>
    public IReadOnlyList<int> TreeIndices(IEnumerable<NormalizedTreeMetrics> trees) =>
        trees.Select(tree => _treeIndex.TryGetValue(tree.Source.UniqueId, out var index) ? index : -1).Where(index => index >= 0).Distinct().Order().ToList();

    /// <summary>The scene indices of the floors an entry shows.</summary>
    public IReadOnlyList<int> FloorIndices(IEnumerable<DashboardFloorItem> floors) =>
        floors.Select(floor => _floorIndex.TryGetValue(floor.UniqueId, out var index) ? index : -1).Where(index => index >= 0).Distinct().Order().ToList();

    public DashboardJsonScene ToJson() => new(
        "m",
        _origin,
        _growth.ToString().ToLowerInvariant(),
        _lastYear,
        _profiles.Select(profile => (IReadOnlyList<IReadOnlyList<double>>)profile
            .Select(size => (IReadOnlyList<double>)[Round(size.TrunkHeightM, 3), Round(size.CrownHeightM, 3), Round(size.CrownRadiusM, 3), Round(size.TrunkRadiusM, 3)])
            .ToList()).ToList(),
        _trees,
        _floors);

    /// <summary>The middle of the scene in plan, at its lowest point, rounded to the metre: keeps the exported numbers short.</summary>
    private static double[] Origin(IEnumerable<SceneTreeItem> trees, IEnumerable<SceneFloorItem> floors)
    {
        var points = trees.Select(tree => (tree.X, tree.Y, tree.Z))
            .Concat(floors.SelectMany(floor => Enumerable.Range(0, floor.Positions.Count / 3)
                .Select(vertex => (X: floor.Positions[vertex * 3], Y: floor.Positions[vertex * 3 + 1], Z: floor.Positions[vertex * 3 + 2]))))
            .ToList();
        if (points.Count == 0)
        {
            return [0, 0, 0];
        }

        return
        [
            Math.Round((points.Min(point => point.X) + points.Max(point => point.X)) / 2d),
            Math.Round((points.Min(point => point.Y) + points.Max(point => point.Y)) / 2d),
            Math.Floor(points.Min(point => point.Z))
        ];
    }

    private double[] Relative(double x, double y, double z) =>
        [Round(x - _origin[0], 2), Round(y - _origin[1], 2), Round(z - _origin[2], 2)];

    private static string TypeKey(SceneTreeItem tree) => $"{tree.FamilyName}|{tree.TypeName}";

    private static string TypeKey(TreeGrowthType type) => $"{type.FamilyName}|{type.TypeName}";

    private static string? OptionLabel(DesignOptionInfo? option) =>
        option is null || DesignScenarios.IsMainModel(option)
            ? null
            : $"{option.SetName?.Trim()} : {DesignScenarios.CleanOptionName(option.OptionName)}";

    private static double Round(double value, int digits) => Math.Round(value, digits);
}
