using System.Text.Json;
using WWP.LandscapeDataManager.Contracts;

namespace WWP.LandscapeDataManager.App.Dashboard;

internal sealed record CachedDashboardSnapshot(DateTimeOffset SavedAt, DashboardReportResult Report);

/// <summary>
/// Keeps one non-secret reporting snapshot so the visual dashboard can still be opened when the
/// Revit pipe is unavailable. This is report data only; project settings continue to round-trip
/// exclusively through Project Information via ProjectSettingsSync.
/// </summary>
internal static class DashboardSnapshotCache
{
    private static readonly string CachePath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "WWP",
        "LandscapeDataManager",
        "Dashboard",
        "last-dashboard.json");

    public static async Task SaveAsync(DashboardReportResult report)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(CachePath)!);
        var snapshot = new CachedDashboardSnapshot(DateTimeOffset.Now, report);
        var json = JsonSerializer.Serialize(snapshot, JsonDefaults.Options);
        await File.WriteAllTextAsync(CachePath, json);
    }

    public static async Task<CachedDashboardSnapshot?> TryLoadAsync()
    {
        if (!File.Exists(CachePath))
        {
            return null;
        }

        try
        {
            var json = await File.ReadAllTextAsync(CachePath);
            return JsonSerializer.Deserialize<CachedDashboardSnapshot>(json, JsonDefaults.Options);
        }
        catch
        {
            return null;
        }
    }
}
