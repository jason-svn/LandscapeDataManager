using System.Globalization;
using WWP.LandscapeDataManager.Contracts;

namespace WWP.LandscapeDataManager.Shared.Services;

/// <summary>One column of a detail table: a two-line header (name, then unit or qualifier).</summary>
public sealed record SiteDetailColumn(string Name, string Unit, double Width, SiteDetailAlign Align = SiteDetailAlign.Left);

public enum SiteDetailAlign
{
    Left,
    Right,
    Center
}

/// <summary>
/// Columns that share a swim lane. <see cref="Colour"/> names a LimLane brush (Green, Blue, Amber,
/// Grey, Violet, Teal); null for the plain columns that name each row.
/// </summary>
public sealed record SiteDetailLane(string? Title, string? Colour, IReadOnlyList<SiteDetailColumn> Columns);

/// <summary>
/// A Site &amp; Biodiversity card's breakdown, in the table language every LIM table uses: what the
/// figure is made of, row by row. <see cref="Rows"/> hold one cell per column, lanes in order;
/// <see cref="Total"/> is the closing row, if any.
/// </summary>
public sealed record SiteDetailTable(
    string Key,
    string Title,
    string Explanation,
    IReadOnlyList<SiteDetailLane> Lanes,
    IReadOnlyList<IReadOnlyList<string>> Rows,
    IReadOnlyList<string>? Total = null,
    string? EmptyText = null)
{
    public IReadOnlyList<SiteDetailColumn> Columns => [.. Lanes.SelectMany(lane => lane.Columns)];
}

/// <summary>
/// What a card's figure is computed from, as the Dashboard has it for the scenario shown: the trees
/// drawn for the growth year, the floors standing (or that stood, for the existing site), every floor
/// in the scenario for BNG (lost ones are the baseline), the lighting fixtures, the site boundary and
/// the Connectivity tool's saved results.
/// </summary>
public sealed record SiteKpiDetailInputs(
    IReadOnlyList<NormalizedTreeMetrics> Trees,
    IReadOnlyList<DashboardFloorItem> Floors,
    IReadOnlyList<DashboardFloorItem> BngFloors,
    IReadOnlyList<DashboardLightingItem> Lighting,
    double? SiteAreaSquareMeters,
    IReadOnlyList<ConnectivityScenarioResult> ConnectivityResults,
    string ScenarioName,
    BngMetricCatalog Catalog);

/// <summary>The breakdown behind each Site &amp; Biodiversity card (see <see cref="SiteDetailTable"/>).</summary>
public static class SiteKpiDetails
{
    public sealed record Kpi(string Key, string Title);

    /// <summary>Every card, in the page's order.</summary>
    public static readonly IReadOnlyList<Kpi> Kpis =
    [
        new("Canopy", "Canopy cover"),
        new("Softscape", "Softscape surface ratio"),
        new("Greenspace", "Greenspace ratio"),
        new("Species", "Biodiversity added"),
        new("Native", "Native species ratio"),
        new("Phenology", "Phenological resilience"),
        new("Functions", "Functional diversity index"),
        new("Connectivity", "Habitat connectivity"),
        new("Lighting", "Lighting impact reduction"),
        new("Pollen", "Pollen load"),
        new("Allergenic", "Allergenic trees"),
        new("Irrigation", "Irrigation demand"),
        new("BngNet", "BNG net change"),
        new("BngBaseline", "BNG baseline units"),
        new("BngPost", "BNG post-intervention units"),
        new("BngAttention", "BNG needs attention")
    ];

    private static readonly string[] MonthNames = ["Jan", "Feb", "Mar", "Apr", "May", "Jun", "Jul", "Aug", "Sep", "Oct", "Nov", "Dec"];

    public static SiteDetailTable Build(string key, SiteKpiDetailInputs inputs)
    {
        var title = Kpis.FirstOrDefault(kpi => kpi.Key == key)?.Title ?? key;
        return key switch
        {
            "Canopy" => Canopy(title, inputs),
            "Softscape" or "Greenspace" => Surfaces(key, title, inputs),
            "Species" => Species(title, inputs),
            "Native" => Native(title, inputs),
            "Phenology" => Phenology(title, inputs),
            "Functions" => Functions(title, inputs),
            "Connectivity" => Connectivity(title, inputs),
            "Lighting" => Lighting(title, inputs),
            "Pollen" => Pollen(title, inputs),
            "Allergenic" => Allergenic(title, inputs),
            "Irrigation" => Irrigation(title, inputs),
            "BngAttention" => BngAttention(title, inputs),
            _ => Bng(key, title, inputs)
        };
    }

    // ---- Trees, by species ----

    private static SiteDetailColumn SpeciesColumn => new("Species", "code · common name", 280);

    private static SiteDetailColumn CountColumn(string name) => new(name, string.Empty, 70, SiteDetailAlign.Right);

    private static IEnumerable<IGrouping<string, NormalizedTreeMetrics>> BySpecies(IEnumerable<NormalizedTreeMetrics> trees) =>
        trees.GroupBy(tree => tree.Source.SpeciesCode ?? tree.Source.CommonName ?? "Unassigned", StringComparer.OrdinalIgnoreCase);

    private static string SpeciesLabel(IGrouping<string, NormalizedTreeMetrics> group)
    {
        var source = group.First().Source;
        var name = source.CommonName ?? source.ScientificName;
        return source.SpeciesCode is null ? name ?? "Unassigned" : name is null ? source.SpeciesCode : $"{source.SpeciesCode} · {name}";
    }

    private static SiteDetailTable Canopy(string title, SiteKpiDetailInputs inputs)
    {
        var trees = inputs.Trees;
        var union = DashboardAggregationService.CanopyAreaSquareMeters(trees.Select(tree => tree.Source));
        var summed = trees.Sum(tree => tree.Source.CanopyAreaSquareMeters);
        var rows = BySpecies(trees)
            .Select(group => (Group: group, Area: group.Sum(tree => tree.Source.CanopyAreaSquareMeters)))
            .OrderByDescending(pair => pair.Area)
            .Select(pair => Row(
                SpeciesLabel(pair.Group),
                Count(pair.Group.Count()),
                Number(pair.Group.Average(tree => CrownWidth(tree.Source.CanopyAreaSquareMeters)), 1),
                Number(pair.Area, 0),
                Percent(pair.Area, summed)))
            .ToList();
        var site = inputs.SiteAreaSquareMeters is > 0 ? $" over the {Number(inputs.SiteAreaSquareMeters.Value, 0)} m² site boundary" : " (pick a site boundary on Site & Biodiversity)";
        return new SiteDetailTable("Canopy", title,
            $"Every tree's crown, a circle as wide as !_S_PLT_TreeFoliage_Width, overlapping crowns counted once: {Number(union, 0)} m²{site}. " +
            $"By species the crowns are summed ({Number(summed, 0)} m²), so overlaps count twice here.",
            [
                new(null, null, [SpeciesColumn, CountColumn("Trees")]),
                new("CANOPY", "Green", [new("Crown width", "m, average", 100, SiteDetailAlign.Right), new("Canopy", "m²", 110, SiteDetailAlign.Right), new("Share", "% of canopy", 100, SiteDetailAlign.Right)])
            ],
            rows,
            Row("Total", Count(trees.Count), string.Empty, Number(summed, 0), rows.Count > 0 ? "100" : string.Empty),
            "No trees in this scenario.");
    }

    private static SiteDetailTable Species(string title, SiteKpiDetailInputs inputs)
    {
        var total = inputs.Trees.Count;
        var rows = BySpecies(inputs.Trees)
            .OrderByDescending(group => group.Count())
            .Select(group => Row(
                SpeciesLabel(group),
                group.First().Source.ScientificName ?? string.Empty,
                Count(group.Count()),
                Percent(group.Count(), total),
                group.First().Source.NativeStatus ?? "—"))
            .ToList();
        return new SiteDetailTable("Species", title,
            "Distinct species among the scenario's trees: by species code, else common name. Types with neither count once, as Unassigned. Design scenarios compares the count with the reference scenario.",
            [
                new(null, null, [SpeciesColumn, new("Scientific name", string.Empty, 220), CountColumn("Trees")]),
                new("SHARE", "Green", [new("Share", "% of trees", 100, SiteDetailAlign.Right), new("Native status", "WWP sheet", 120)])
            ],
            rows,
            Row($"{rows.Count:N0} species", string.Empty, Count(total), rows.Count > 0 ? "100" : string.Empty, string.Empty),
            "No trees in this scenario.");
    }

    private static SiteDetailTable Native(string title, SiteKpiDetailInputs inputs)
    {
        var tagged = inputs.Trees.Count(tree => !string.IsNullOrWhiteSpace(tree.Source.NativeStatus));
        static int Order(string? status) => status?.Trim().ToLowerInvariant() switch
        {
            "native" => 0,
            "adaptive" => 1,
            null or "" => 3,
            _ => 2
        };
        var rows = BySpecies(inputs.Trees)
            .OrderBy(group => Order(group.First().Source.NativeStatus))
            .ThenByDescending(group => group.Count())
            .Select(group =>
            {
                var status = group.First().Source.NativeStatus;
                return Row(SpeciesLabel(group), Count(group.Count()), string.IsNullOrWhiteSpace(status) ? "— not tagged" : status.Trim(),
                    string.IsNullOrWhiteSpace(status) ? string.Empty : Percent(group.Count(), tagged));
            })
            .ToList();
        var native = inputs.Trees.Count(tree => string.Equals(tree.Source.NativeStatus?.Trim(), "Native", StringComparison.OrdinalIgnoreCase));
        return new SiteDetailTable("Native", title,
            "Native trees ÷ trees with a native status. The status is native to the row's region in the WWP sheet (Native_Status column, imported onto !_S_PLT_iTreeSpecies_NativeStatus_Text); " +
            "Adaptive = long naturalised there. Untagged trees are left out of the ratio.",
            [
                new(null, null, [SpeciesColumn, CountColumn("Trees")]),
                new("NATIVE STATUS", "Green", [new("Status", "WWP sheet", 140), new("Share", "% of tagged trees", 110, SiteDetailAlign.Right)])
            ],
            rows,
            Row("Native", Count(native), $"{Count(tagged)} tagged", Percent(native, tagged)),
            "No trees in this scenario.");
    }

    private static SiteDetailTable Phenology(string title, SiteKpiDetailInputs inputs)
    {
        var groups = BySpecies(inputs.Trees).OrderByDescending(group => group.Count()).ToList();
        var monthsBySpecies = groups.Select(group => DashboardAggregationService.ParseMonthList(group.First().Source.BloomMonths).ToHashSet()).ToList();
        var rows = groups
            .Select((group, index) => Row([SpeciesLabel(group), Count(group.Count()), .. Enumerable.Range(1, 12).Select(month => monthsBySpecies[index].Contains(month) ? "●" : string.Empty)]))
            .ToList();
        var covered = Enumerable.Range(1, 12).Count(month => monthsBySpecies.Any(months => months.Contains(month)));
        return new SiteDetailTable("Phenology", title,
            $"Months in which at least one species flowers or carries fruit or seed: {covered} of 12. From the WWP sheet's Bloom_Months column (imported onto !_S_PLT_iTreeSpecies_BloomMonths_Text).",
            [
                new(null, null, [SpeciesColumn, CountColumn("Trees")]),
                new("IN FLOWER, FRUIT OR SEED", "Violet", [.. MonthNames.Select(name => new SiteDetailColumn(name, string.Empty, 34, SiteDetailAlign.Center))])
            ],
            rows,
            Row(["Species per month", string.Empty, .. Enumerable.Range(1, 12).Select(month => Count(monthsBySpecies.Count(months => months.Contains(month))))]),
            "No trees in this scenario.");
    }

    private static SiteDetailTable Functions(string title, SiteKpiDetailInputs inputs)
    {
        var trees = inputs.Trees;
        var tagged = trees
            .SelectMany(tree => DashboardAggregationService.ParseTagList(tree.Source.EcologicalFunctions).Select(tag => (Tag: tag, Tree: tree)))
            .GroupBy(pair => pair.Tag, StringComparer.OrdinalIgnoreCase)
            .Select(group => (
                Function: group.Key,
                Species: group.Select(pair => pair.Tree.Source.SpeciesCode ?? pair.Tree.Source.CommonName ?? "Unassigned").Distinct(StringComparer.OrdinalIgnoreCase).Order().ToList(),
                Trees: group.Count(),
                Canopy: group.Sum(pair => pair.Tree.Source.CanopyAreaSquareMeters)))
            .OrderByDescending(entry => entry.Trees)
            .ToList();
        var rows = tagged
            .Select(entry => Row(entry.Function, string.Join(", ", entry.Species), Count(entry.Trees), Number(entry.Canopy, 0), Percent(entry.Trees, trees.Count)))
            .ToList();
        var untagged = trees.Where(tree => DashboardAggregationService.ParseTagList(tree.Source.EcologicalFunctions).Count == 0).ToList();
        if (untagged.Count > 0)
        {
            rows.Add(Row("— none tagged", string.Join(", ", untagged.Select(tree => tree.Source.SpeciesCode ?? tree.Source.CommonName ?? "Unassigned").Distinct(StringComparer.OrdinalIgnoreCase).Order()),
                Count(untagged.Count), Number(untagged.Sum(tree => tree.Source.CanopyAreaSquareMeters), 0), Percent(untagged.Count, trees.Count)));
        }

        var multi = trees.Where(tree => DashboardAggregationService.ParseTagList(tree.Source.EcologicalFunctions).Count >= 2).Sum(tree => tree.Source.CanopyAreaSquareMeters);
        return new SiteDetailTable("Functions", title,
            $"Distinct ecological functions among the scenario's trees: {tagged.Count}. From the WWP sheet's Ecological_Functions column (imported onto !_S_PLT_iTreeSpecies_EcologicalFunctions_Text). " +
            $"A tree with several functions counts under each. Canopy of species with two or more: {Number(multi, 0)} m².",
            [
                new(null, null, [new("Function", string.Empty, 170), new("Species", "codes", 380)]),
                new("TREES", "Teal", [CountColumn("Trees"), new("Canopy", "m²", 110, SiteDetailAlign.Right), new("Share", "% of trees", 100, SiteDetailAlign.Right)])
            ],
            rows,
            null,
            "No trees in this scenario.");
    }

    private static SiteDetailTable Allergenic(string title, SiteKpiDetailInputs inputs)
    {
        var total = inputs.Trees.Count;
        var rows = BySpecies(inputs.Trees)
            .OrderByDescending(group => DashboardAggregationService.IsAllergenic(group.First().Source.Allergenic))
            .ThenByDescending(group => group.Count())
            .Select(group =>
            {
                var value = group.First().Source.Allergenic;
                return Row(SpeciesLabel(group), Count(group.Count()), string.IsNullOrWhiteSpace(value) ? "—" : value.Trim(),
                    DashboardAggregationService.IsAllergenic(value) ? "Yes" : "No", Percent(group.Count(), total));
            })
            .ToList();
        var allergenic = inputs.Trees.Count(tree => DashboardAggregationService.IsAllergenic(tree.Source.Allergenic));
        return new SiteDetailTable("Allergenic", title,
            "Trees whose species the WWP sheet flags allergenic ÷ all trees. The sheet's Allergenic column (imported onto !_S_PLT_iTreeSpecies_Allergenic_Text) counts when it's Yes, True or a number above 0.",
            [
                new(null, null, [SpeciesColumn, CountColumn("Trees")]),
                new("ALLERGEN", "Amber", [new("Sheet value", "Allergenic", 110, SiteDetailAlign.Right), new("Allergenic", string.Empty, 100), new("Share", "% of trees", 100, SiteDetailAlign.Right)])
            ],
            rows,
            Row("Allergenic", Count(allergenic), string.Empty, string.Empty, Percent(allergenic, total)),
            "No trees in this scenario.");
    }

    private static SiteDetailTable Irrigation(string title, SiteKpiDetailInputs inputs)
    {
        var total = inputs.Trees.Count;
        static string Demand(double? factor) => factor switch
        {
            null or <= 0 => "—",
            <= DashboardAggregationService.LowWaterIrrigationFactor => "Low",
            < 0.7 => "Moderate",
            _ => "High"
        };
        var rows = BySpecies(inputs.Trees)
            .Select(group => (Group: group, Factor: group.First().Source.IrrigationDemandFactor))
            .OrderBy(pair => pair.Factor is > 0 ? pair.Factor.Value : double.MaxValue)
            .Select(pair => Row(SpeciesLabel(pair.Group), Count(pair.Group.Count()), pair.Factor is > 0 ? Number(pair.Factor.Value, 2) : "—", Demand(pair.Factor), Percent(pair.Group.Count(), total)))
            .ToList();
        var factors = inputs.Trees.Select(tree => tree.Source.IrrigationDemandFactor).Where(factor => factor is > 0).Select(factor => factor!.Value).ToList();
        return new SiteDetailTable("Irrigation", title,
            "The average plant factor of the trees that have one: the share of reference evapotranspiration a plant needs (WUCOLS: up to 0.3 low, 0.4–0.6 moderate, 0.7 and up high). " +
            "From the WWP sheet's Irrigation_Demand_Plant Factor column, on the tree types.",
            [
                new(null, null, [SpeciesColumn, CountColumn("Trees")]),
                new("WATER", "Blue", [new("Plant factor", "0–1", 100, SiteDetailAlign.Right), new("Demand", string.Empty, 110), new("Share", "% of trees", 100, SiteDetailAlign.Right)])
            ],
            rows,
            Row("Average", Count(factors.Count), factors.Count > 0 ? Number(factors.Average(), 2) : "—", Demand(factors.Count > 0 ? factors.Average() : null), string.Empty),
            "No trees in this scenario.");
    }

    private static SiteDetailTable Pollen(string title, SiteKpiDetailInputs inputs)
    {
        var treeRows = BySpecies(inputs.Trees)
            .Select(group => (Source: "Trees", Name: SpeciesLabel(group), Count: group.Count(), Pollen: group.Sum(tree => tree.Source.PollenAnnual)));
        var floorRows = inputs.Floors
            .GroupBy(FloorLabel, StringComparer.OrdinalIgnoreCase)
            .Select(group => (Source: "Planting areas", Name: group.Key, Count: group.Count(), Pollen: group.Sum(floor => floor.PollenAnnual)));
        var all = treeRows.Concat(floorRows).Where(row => row.Pollen > 0).OrderByDescending(row => row.Pollen).ToList();
        var total = all.Sum(row => row.Pollen);
        return new SiteDetailTable("Pollen", title,
            "Pollen a year from the WWP sheet's Pollen column: per tree, and per m² × area for planting areas, stored on each element (!_S_PLT_Result_PollenAnnual_Mass) by Tree Calculator and Floor Calculator.",
            [
                new(null, null, [new("Source", string.Empty, 120), new("Species or landscape type", string.Empty, 280), CountColumn("Count")]),
                new("POLLEN", "Amber", [new("Pollen", "kg/yr", 110, SiteDetailAlign.Right), new("Share", "% of total", 100, SiteDetailAlign.Right)])
            ],
            [.. all.Select(row => Row(row.Source, row.Name, Count(row.Count), Number(row.Pollen, 1), Percent(row.Pollen, total)))],
            all.Count > 0 ? Row("Total", string.Empty, string.Empty, Number(total, 1), "100") : null,
            "No pollen stored yet: fill trees from the WWP sheet and calculate planting areas.");
    }

    // ---- Floors ----

    private static string FloorLabel(DashboardFloorItem floor) =>
        string.IsNullOrWhiteSpace(floor.LdsType) ? $"{floor.FamilyName} : {floor.TypeName}" : floor.LdsType.Trim();

    private static SiteDetailTable Surfaces(string key, string title, SiteKpiDetailInputs inputs)
    {
        var floors = inputs.Floors.Select(floor => (Floor: floor, Class: DashboardAggregationService.ClassifySurface(floor))).ToList();
        var pervious = floors.Where(pair => pair.Class == SurfaceClassCatalog.Pervious).Sum(pair => pair.Floor.AreaSquareMeters);
        var classified = floors.Where(pair => pair.Class is SurfaceClassCatalog.Pervious or SurfaceClassCatalog.Impervious).Sum(pair => pair.Floor.AreaSquareMeters);
        var site = inputs.SiteAreaSquareMeters;
        static int Order(string? surface) => surface switch
        {
            SurfaceClassCatalog.Pervious => 0,
            SurfaceClassCatalog.Impervious => 1,
            SurfaceClassCatalog.Building => 2,
            _ => 3
        };
        var rows = floors
            .GroupBy(pair => (Label: FloorLabel(pair.Floor), pair.Class))
            .OrderBy(group => Order(group.Key.Class))
            .ThenByDescending(group => group.Sum(pair => pair.Floor.AreaSquareMeters))
            .Select(group =>
            {
                var area = group.Sum(pair => pair.Floor.AreaSquareMeters);
                var countsInRatio = group.Key.Class is SurfaceClassCatalog.Pervious or SurfaceClassCatalog.Impervious;
                return Row(group.Key.Label, Count(group.Count()), group.Key.Class ?? "Unclassified", Number(area, 0),
                    site is > 0 ? Percent(area, site.Value) : string.Empty,
                    countsInRatio ? Percent(area, classified) : "left out");
            })
            .ToList();
        var explanation = key == "Softscape"
            ? "Pervious floor area ÷ the site boundary. A floor's class comes from !_S_PLT_LDS_SurfaceClass_Text, else its landscape type, else its type name; building slabs are neither."
            : "Pervious ÷ (pervious + impervious) floor area. Building slabs and floors whose class nothing says are left out.";
        return new SiteDetailTable(key, title, explanation,
            [
                new(null, null, [new("Landscape type", "or Family : Type", 300), CountColumn("Floors")]),
                new("SURFACE", "Teal", [new("Class", string.Empty, 110), new("Area", "m²", 110, SiteDetailAlign.Right), new("Share of site", "%", 100, SiteDetailAlign.Right), new("Share of classified", "%", 120, SiteDetailAlign.Right)])
            ],
            rows,
            Row("Pervious", string.Empty, string.Empty, Number(pervious, 0), site is > 0 ? Percent(pervious, site.Value) : string.Empty, Percent(pervious, classified)),
            "No floors in this scenario.");
    }

    // ---- Lighting ----

    private static SiteDetailTable Lighting(string title, SiteKpiDetailInputs inputs)
    {
        var total = inputs.Lighting.Count;
        var rows = inputs.Lighting
            .GroupBy(fixture => (fixture.FamilyName, fixture.TypeName, Compliant: fixture.DarkSkyCompliant?.Trim()))
            .OrderByDescending(group => group.Count())
            .Select(group => Row($"{group.Key.FamilyName} : {group.Key.TypeName}", Count(group.Count()),
                string.IsNullOrWhiteSpace(group.Key.Compliant) ? "— not tagged" : group.Key.Compliant, Percent(group.Count(), total)))
            .ToList();
        var compliant = inputs.Lighting.Count(fixture => string.Equals(fixture.DarkSkyCompliant?.Trim(), "Yes", StringComparison.OrdinalIgnoreCase));
        return new SiteDetailTable("Lighting", title,
            "Lighting Fixtures whose type is dark-sky compliant (!_S_PLT_Lighting_DarkSkyCompliant_Text = Yes: shielded or full cut-off) ÷ all Lighting Fixtures in the scenario. Untagged types count as not compliant.",
            [
                new(null, null, [new("Family : Type", string.Empty, 340), CountColumn("Fixtures")]),
                new("DARK SKY", "Violet", [new("Compliant", "Yes / No", 120), new("Share", "% of fixtures", 110, SiteDetailAlign.Right)])
            ],
            rows,
            total > 0 ? Row("Compliant", Count(compliant), string.Empty, Percent(compliant, total)) : null,
            "No Lighting Fixtures in this scenario. Lights modelled in another category (Lighting Devices, Site, Generic Models) aren't counted.");
    }

    // ---- Connectivity, by scenario ----

    private static SiteDetailTable Connectivity(string title, SiteKpiDetailInputs inputs)
    {
        var rows = inputs.ConnectivityResults
            .OrderByDescending(result => result.ConnectedPercent)
            .Select(result => Row(
                result.Scenario + (string.Equals(result.Scenario, inputs.ScenarioName, StringComparison.OrdinalIgnoreCase) ? "  (shown)" : string.Empty),
                Number(result.ConnectedPercent, 0),
                Number(result.EquivalentConnectedArea, 0),
                Number(result.HabitatAreaSquareMeters, 0),
                Count(result.Patches),
                result.IsExistingSite ? "existing" : result.DesignYear.ToString(CultureInfo.CurrentCulture),
                Calculated(result.CalculatedUtc)))
            .ToList();
        return new SiteDetailTable("Connectivity", title,
            "Connected habitat = equivalent connected area (EC) ÷ habitat area, from the Connectivity tool: 100% when every patch reaches every other. " +
            "EC is averaged over the dispersal distances. New trees are drawn at the design year. Compare scenarios in Connectivity scores every scenario.",
            [
                new(null, null, [new("Scenario", string.Empty, 300)]),
                new("CONNECTED HABITAT", "Green", [new("Connected", "%", 100, SiteDetailAlign.Right), new("Equivalent area", "m²", 130, SiteDetailAlign.Right), new("Habitat", "m²", 120, SiteDetailAlign.Right)]),
                new("RUN", "Grey", [new("Patches", string.Empty, 80, SiteDetailAlign.Right), new("Trees at", "year", 90, SiteDetailAlign.Right), new("Calculated", string.Empty, 160)])
            ],
            rows,
            null,
            "No connectivity results saved yet: run Connectivity (Compare scenarios scores every scenario).");
    }

    private static string Calculated(string utc) =>
        DateTimeOffset.TryParse(utc, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var calculated)
            ? calculated.ToLocalTime().ToString("g", CultureInfo.CurrentCulture)
            : utc;

    // ---- BNG ----

    private static (DashboardFloorItem Floor, string State, string Role)[] ClassifiedFloors(SiteKpiDetailInputs inputs) =>
        [.. inputs.BngFloors.Select(floor =>
        {
            var (state, role) = DashboardAggregationService.ClassifyBngFloor(floor, inputs.Catalog);
            return (floor, state, role);
        })];

    private static string HabitatOf(DashboardFloorBng bng, string role) => role switch
    {
        BngPhaseRoles.Created => bng.ProposedHabitat ?? "Unassigned",
        BngPhaseRoles.Enhanced => $"{bng.BaselineHabitat ?? "Unassigned"} → {bng.ProposedHabitat ?? "Unassigned"}",
        _ => bng.BaselineHabitat ?? "Unassigned"
    };

    private static SiteDetailTable Bng(string key, string title, SiteKpiDetailInputs inputs)
    {
        var current = ClassifiedFloors(inputs)
            .Where(entry => entry.State is BngFloorStates.Current or BngFloorStates.CheckData && entry.Floor.Bng is not null)
            .ToList();
        static int Order(string role) => role switch
        {
            BngPhaseRoles.Retained => 0,
            BngPhaseRoles.Enhanced => 1,
            BngPhaseRoles.Lost => 2,
            _ => 3
        };
        var groups = current
            .GroupBy(entry => (Habitat: HabitatOf(entry.Floor.Bng!, entry.Role).Trim(), entry.Role))
            .Select(group => (
                group.Key.Habitat,
                group.Key.Role,
                Floors: group.Count(),
                Hectares: group.Sum(entry => entry.Floor.AreaSquareMeters) / BngFloorStatusEvaluator.SquareMetresPerHectare,
                Baseline: group.Key.Role == BngPhaseRoles.Created ? 0 : group.Sum(entry => entry.Floor.Bng!.BaselineUnits),
                Post: group.Key.Role == BngPhaseRoles.Lost ? 0 : group.Sum(entry => entry.Floor.Bng!.HabitatUnits)))
            .OrderBy(group => Order(group.Role))
            .ThenByDescending(group => Math.Max(group.Baseline, group.Post))
            .ToList();
        static string Units(double value) => value.ToString("N2", CultureInfo.CurrentCulture);
        static string Change(double value) => value.ToString("+0.00;-0.00;0.00", CultureInfo.CurrentCulture);
        var rows = groups
            .Select(group => Row(group.Habitat, group.Role, Count(group.Floors), group.Hectares.ToString("N3", CultureInfo.CurrentCulture),
                group.Role == BngPhaseRoles.Created ? "—" : Units(group.Baseline),
                group.Role == BngPhaseRoles.Lost ? "—" : Units(group.Post),
                Change(group.Post - group.Baseline)))
            .ToList();
        var baseline = groups.Sum(group => group.Baseline);
        var post = groups.Sum(group => group.Post);
        var explanation = key switch
        {
            "BngBaseline" => "Baseline = the metric's A-1 units of the floors created in the first phase (retained, enhanced or lost). Current results only: stale and unassessed floors are left out (see BNG needs attention).",
            "BngPost" => "Post-intervention = retained (A-1) + enhanced (A-3) + created (A-2) habitat units; lost floors deliver nothing. Current results only.",
            _ => "Net change = (post-intervention − baseline) ÷ baseline, the Statutory Biodiversity Metric's on-site headline; the statutory minimum is a 10% gain. Current results only."
        };
        return new SiteDetailTable(key, title, explanation,
            [
                new(null, null, [new("Habitat", "baseline → proposed", 320), new("Role", "from phases", 100), CountColumn("Floors"), new("Area", "ha", 90, SiteDetailAlign.Right)]),
                new("BASELINE · A-1", "Amber", [new("Units", "HU", 100, SiteDetailAlign.Right)]),
                new("POST-INTERVENTION", "Green", [new("Units", "HU", 100, SiteDetailAlign.Right), new("Change", "HU", 100, SiteDetailAlign.Right)])
            ],
            rows,
            rows.Count > 0 ? Row("Total", string.Empty, Count(groups.Sum(group => group.Floors)), groups.Sum(group => group.Hectares).ToString("N3", CultureInfo.CurrentCulture), Units(baseline), Units(post), Change(post - baseline)) : null,
            "No current BNG results in this scenario: calculate floors in Floor Calculator's BNG tab.");
    }

    private static SiteDetailTable BngAttention(string title, SiteKpiDetailInputs inputs)
    {
        static string Fix(string state) => state switch
        {
            BngFloorStates.Stale => "Its area, phases or inputs changed since it was written: recalculate it in Floor Calculator's BNG tab.",
            BngFloorStates.CheckData => "The metric flags the row (Check details / Check Data): review its habitat, condition and significance.",
            _ => "Not assessed yet: fill its habitat and condition in Floor Calculator's BNG tab, then write it."
        };
        static int Order(string state) => state switch
        {
            BngFloorStates.Stale => 0,
            BngFloorStates.CheckData => 1,
            _ => 2
        };
        var rows = ClassifiedFloors(inputs)
            .Where(entry => entry.State is BngFloorStates.Stale or BngFloorStates.CheckData or BngFloorStates.NotAssessed)
            .OrderBy(entry => Order(entry.State))
            .ThenBy(entry => $"{entry.Floor.FamilyName} : {entry.Floor.TypeName}", StringComparer.OrdinalIgnoreCase)
            .Select(entry => Row($"{entry.Floor.FamilyName} : {entry.Floor.TypeName}", entry.Role, Number(entry.Floor.AreaSquareMeters, 0), entry.State, Fix(entry.State)))
            .ToList();
        return new SiteDetailTable("BngAttention", title,
            "Floors left out of the BNG totals, or flagged by the metric: stale ones (changed since written), Check data ones (still counted) and ones never assessed. Floors demolished in the phase they were built in aren't listed.",
            [
                new(null, null, [new("Family : Type", string.Empty, 320), new("Role", "from phases", 100), new("Area", "m²", 100, SiteDetailAlign.Right)]),
                new("STATUS", "Amber", [new("State", string.Empty, 120), new("What to do", string.Empty, 460)])
            ],
            rows,
            null,
            "Nothing needs attention: every floor in this scenario has a current BNG result.");
    }

    // ---- Formatting ----

    private static IReadOnlyList<string> Row(params string[] cells) => cells;

    private static string Count(int value) => value.ToString("N0", CultureInfo.CurrentCulture);

    private static string Number(double value, int decimals) => value.ToString("N" + decimals.ToString(CultureInfo.InvariantCulture), CultureInfo.CurrentCulture);

    private static string Percent(double part, double whole) => whole > 0 ? (100d * part / whole).ToString("N0", CultureInfo.CurrentCulture) : string.Empty;

    private static double CrownWidth(double crownArea) => crownArea > 0 ? 2 * Math.Sqrt(crownArea / Math.PI) : 0;
}
