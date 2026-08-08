using System.Globalization;
using System.Net;
using System.Text;

namespace WWP.LandscapeDataManager.App.Dashboard;

internal sealed record DashboardSvgSnapshot(
    string ProjectTitle,
    string Scenario,
    string Outlook,
    string EcosystemValue,
    string CarbonBenefit,
    string WaterManaged,
    string PollutionRemoved,
    string PlantingArea,
    string DataReadiness,
    IReadOnlyList<KpiBarRow> ProjectionRows,
    IReadOnlyList<KpiBarRow> SpeciesRows,
    DateTimeOffset GeneratedAt);

internal static class DashboardSvgExportService
{
    private const int Width = 1600;
    private const int Height = 900;

    public static async Task ExportAsync(string path, DashboardSvgSnapshot snapshot)
    {
        var svg = Build(snapshot);
        await File.WriteAllTextAsync(path, svg, new UTF8Encoding(false));
    }

    internal static string Build(DashboardSvgSnapshot snapshot)
    {
        static string E(string value) => WebUtility.HtmlEncode(value);
        static string N(double value) => value.ToString("0.##", CultureInfo.InvariantCulture);

        var builder = new StringBuilder();
        builder.AppendLine($"<svg xmlns=\"http://www.w3.org/2000/svg\" width=\"{Width}\" height=\"{Height}\" viewBox=\"0 0 {Width} {Height}\">");
        builder.AppendLine("<rect width=\"1600\" height=\"900\" fill=\"#f5f7f2\"/>");
        builder.AppendLine("<rect x=\"34\" y=\"32\" width=\"1532\" height=\"116\" rx=\"28\" fill=\"#ffffff\"/>");
        builder.AppendLine("<rect x=\"34\" y=\"32\" width=\"22\" height=\"116\" rx=\"11\" fill=\"#176b2b\"/>");
        builder.AppendLine($"<text x=\"84\" y=\"82\" font-family=\"Segoe UI,Arial\" font-size=\"34\" font-weight=\"700\" fill=\"#16231a\">ENVIRONMENTAL KPIs</text>");
        builder.AppendLine($"<text x=\"84\" y=\"120\" font-family=\"Segoe UI,Arial\" font-size=\"18\" fill=\"#667067\">{E(snapshot.ProjectTitle)}  ·  {E(snapshot.Scenario)}  ·  {E(snapshot.Outlook)}</text>");
        builder.AppendLine("<rect x=\"34\" y=\"172\" width=\"390\" height=\"692\" rx=\"30\" fill=\"#176b2b\"/>");

        AddHeroMetric(builder, 74, 238, "PROJECTED ECOSYSTEM VALUE", snapshot.EcosystemValue, "Tree benefits on the selected outlook");
        AddHeroMetric(builder, 74, 402, "CARBON BENEFIT", snapshot.CarbonBenefit, "Trees and planting areas");
        AddHeroMetric(builder, 74, 566, "WATER MANAGED", snapshot.WaterManaged, "Avoided runoff");
        builder.AppendLine($"<text x=\"74\" y=\"748\" font-family=\"Segoe UI,Arial\" font-size=\"15\" fill=\"#cbe5cf\">POLLUTION REMOVED</text>");
        builder.AppendLine($"<text x=\"74\" y=\"784\" font-family=\"Segoe UI,Arial\" font-size=\"27\" font-weight=\"700\" fill=\"#ffffff\">{E(snapshot.PollutionRemoved)}</text>");

        AddCard(builder, 454, 172, 540, 332, "CARBON OUTLOOK");
        var projectionRows = snapshot.ProjectionRows.Take(4).ToList();
        for (var index = 0; index < projectionRows.Count; index++)
        {
            var row = projectionRows[index];
            var y = 252 + index * 56;
            builder.AppendLine($"<text x=\"482\" y=\"{y}\" font-family=\"Segoe UI,Arial\" font-size=\"15\" fill=\"#465048\">{E(row.Label)}</text>");
            builder.AppendLine($"<rect x=\"560\" y=\"{y - 17}\" width=\"310\" height=\"13\" rx=\"6.5\" fill=\"#dfe7dc\"/>");
            builder.AppendLine($"<rect x=\"560\" y=\"{y - 17}\" width=\"{N(310d * row.Percent / 100d)}\" height=\"13\" rx=\"6.5\" fill=\"#62bd47\"/>");
            builder.AppendLine($"<text x=\"886\" y=\"{y - 4}\" font-family=\"Segoe UI,Arial\" font-size=\"14\" font-weight=\"600\" fill=\"#263229\">{E(row.ValueText)}</text>");
        }

        AddCard(builder, 1024, 172, 542, 332, "PROJECT SNAPSHOT");
        AddSnapshotLine(builder, 1055, 252, "PLANTING AREA", snapshot.PlantingArea);
        AddSnapshotLine(builder, 1055, 326, "DATA READINESS", snapshot.DataReadiness);
        AddSnapshotLine(builder, 1055, 400, "POLLUTION REMOVED", snapshot.PollutionRemoved);

        AddCard(builder, 454, 532, 1112, 332, "TREE PALETTE");
        var speciesRows = snapshot.SpeciesRows.Take(5).ToList();
        for (var index = 0; index < speciesRows.Count; index++)
        {
            var row = speciesRows[index];
            var y = 612 + index * 45;
            builder.AppendLine($"<text x=\"482\" y=\"{y}\" font-family=\"Segoe UI,Arial\" font-size=\"15\" fill=\"#38443b\">{E(row.Label)}</text>");
            builder.AppendLine($"<rect x=\"790\" y=\"{y - 15}\" width=\"560\" height=\"12\" rx=\"6\" fill=\"#e3e9df\"/>");
            builder.AppendLine($"<rect x=\"790\" y=\"{y - 15}\" width=\"{N(560d * row.Percent / 100d)}\" height=\"12\" rx=\"6\" fill=\"#176b2b\"/>");
            builder.AppendLine($"<text x=\"1370\" y=\"{y - 2}\" font-family=\"Segoe UI,Arial\" font-size=\"14\" font-weight=\"600\" fill=\"#263229\">{E(row.ValueText)}</text>");
        }

        builder.AppendLine($"<text x=\"1538\" y=\"884\" text-anchor=\"end\" font-family=\"Segoe UI,Arial\" font-size=\"12\" fill=\"#7d877f\">LIM · {E(snapshot.GeneratedAt.ToString("yyyy-MM-dd HH:mm"))}</text>");
        builder.AppendLine("</svg>");
        return builder.ToString();
    }

    private static void AddCard(StringBuilder builder, int x, int y, int width, int height, string title)
    {
        builder.AppendLine($"<rect x=\"{x}\" y=\"{y}\" width=\"{width}\" height=\"{height}\" rx=\"28\" fill=\"#ffffff\" stroke=\"#dfe5dc\"/>");
        builder.AppendLine($"<text x=\"{x + 28}\" y=\"{y + 48}\" font-family=\"Segoe UI,Arial\" font-size=\"19\" font-weight=\"700\" fill=\"#253128\">{title}</text>");
    }

    private static void AddHeroMetric(StringBuilder builder, int x, int y, string label, string value, string note)
    {
        builder.AppendLine($"<text x=\"{x}\" y=\"{y}\" font-family=\"Segoe UI,Arial\" font-size=\"15\" font-weight=\"600\" fill=\"#cbe5cf\">{label}</text>");
        builder.AppendLine($"<text x=\"{x}\" y=\"{y + 48}\" font-family=\"Segoe UI,Arial\" font-size=\"34\" font-weight=\"700\" fill=\"#ffc400\">{WebUtility.HtmlEncode(value)}</text>");
        builder.AppendLine($"<text x=\"{x}\" y=\"{y + 80}\" font-family=\"Segoe UI,Arial\" font-size=\"15\" fill=\"#ffffff\">{note}</text>");
        builder.AppendLine($"<rect x=\"{x}\" y=\"{y + 100}\" width=\"305\" height=\"8\" rx=\"4\" fill=\"#8dbb94\"/>");
        builder.AppendLine($"<rect x=\"{x}\" y=\"{y + 100}\" width=\"205\" height=\"8\" rx=\"4\" fill=\"#ffc400\"/>");
    }

    private static void AddSnapshotLine(StringBuilder builder, int x, int y, string label, string value)
    {
        builder.AppendLine($"<text x=\"{x}\" y=\"{y}\" font-family=\"Segoe UI,Arial\" font-size=\"13\" font-weight=\"600\" fill=\"#758077\">{label}</text>");
        builder.AppendLine($"<text x=\"{x}\" y=\"{y + 31}\" font-family=\"Segoe UI,Arial\" font-size=\"25\" font-weight=\"700\" fill=\"#176b2b\">{WebUtility.HtmlEncode(value)}</text>");
        builder.AppendLine($"<line x1=\"{x}\" y1=\"{y + 48}\" x2=\"{x + 470}\" y2=\"{y + 48}\" stroke=\"#e5e9e2\"/>");
    }
}
