using System.Text.Json;
using WWP.LandscapeDataManager.Contracts;
using WWP.LandscapeDataManager.Shared.Services;

namespace WWP.LandscapeDataManager.App.Dashboard;

/// <summary>
/// The Dashboard's own project settings as they were when the snapshot was cached: the saved
/// schemes (Scheme A, Scheme B, …), the reference scheme, connectivity results, site boundary and
/// design year. A reopened Dashboard offers them straight away, before Revit answers.
/// </summary>
internal sealed record CachedDashboardSettings(
    IReadOnlyList<DesignScenario>? DesignScenarios,
    string? ReferenceScenario,
    IReadOnlyList<ConnectivityScenarioResult>? ConnectivityResults,
    string? SiteBoundary,
    int? GrowthDesignYear);

internal sealed record CachedDashboardSnapshot(DateTimeOffset SavedAt, DashboardReportResult Report, CachedDashboardSettings? Settings = null);

/// <summary>
/// Keeps one non-secret reporting snapshot so the visual dashboard can still be opened when the
/// Revit pipe is unavailable: the report, and a copy of the Dashboard's own settings. Project
/// Information stays their home (ProjectSettingsSync); the copy only bridges the moment before
/// Revit is asked, or a session without Revit.
/// </summary>
internal static class DashboardSnapshotCache
{
    private static readonly string CachePath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "WWP",
        "LandscapeDataManager",
        "Dashboard",
        "last-dashboard.json");

    public static async Task SaveAsync(DashboardReportResult report, CachedDashboardSettings? settings) =>
        await WriteAsync(new CachedDashboardSnapshot(DateTimeOffset.Now, report, settings));

    /// <summary>Replaces the cached settings, keeping the cached report and when it was taken; nothing to do without a snapshot.</summary>
    public static async Task SaveSettingsAsync(CachedDashboardSettings settings)
    {
        if (await TryLoadAsync() is { } cached)
        {
            await WriteAsync(cached with { Settings = settings });
        }
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

    private static async Task WriteAsync(CachedDashboardSnapshot snapshot)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(CachePath)!);
        var json = JsonSerializer.Serialize(snapshot, JsonDefaults.Options);
        await File.WriteAllTextAsync(CachePath, json);
    }
}
