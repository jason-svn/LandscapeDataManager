using WWP.LandscapeDataManager.Contracts;

namespace WWP.LandscapeDataManager.Shared.Services;

/// <summary>One choice in the Dashboard's site boundary dropdown; <see cref="AreaSquareMeters"/> is null when it has no usable area.</summary>
public sealed record SiteBoundaryOption(string Key, string Label, double? AreaSquareMeters);

/// <summary>
/// Which area Canopy Cover and Softscape Surface Ratio divide by: the Project Information site area
/// (<c>!_S_PLT_Site_TotalArea_Area</c>), one Revit property line, or every property line summed.
/// The chosen key is saved per project in <see cref="ProjectSettingsSnapshot.SiteBoundary"/>.
/// </summary>
public static class SiteBoundaryResolver
{
    public const string ProjectInformationKey = "project-information";
    public const string AllPropertyLinesKey = "all-property-lines";

    public static IReadOnlyList<SiteBoundaryOption> BuildOptions(double? projectInformationArea, IReadOnlyList<DashboardPropertyLine>? propertyLines)
    {
        var options = new List<SiteBoundaryOption>
        {
            projectInformationArea is > 0
                ? new(ProjectInformationKey, $"Project Information site area — {projectInformationArea.Value:N0} m²", projectInformationArea)
                : new(ProjectInformationKey, "Project Information site area (not set)", null)
        };

        var lines = propertyLines ?? [];
        foreach (var line in lines)
        {
            options.Add(line.IsClosed
                ? new SiteBoundaryOption(line.UniqueId, $"Property line: {line.Name} — {line.AreaSquareMeters:N0} m²", line.AreaSquareMeters)
                : new SiteBoundaryOption(line.UniqueId, $"Property line: {line.Name} (open — no area)", null));
        }

        var closed = lines.Where(line => line.IsClosed).ToList();
        if (closed.Count > 1)
        {
            var total = closed.Sum(line => line.AreaSquareMeters);
            options.Add(new SiteBoundaryOption(AllPropertyLinesKey, $"All property lines — {total:N0} m²", total));
        }

        return options;
    }

    /// <summary>
    /// The saved choice while it still exists in this model; otherwise the Project Information area
    /// if one was entered (what the KPIs used before property lines were offered), else the only
    /// closed property line, else all of them summed.
    /// </summary>
    public static SiteBoundaryOption Resolve(IReadOnlyList<SiteBoundaryOption> options, string? savedKey)
    {
        if (savedKey is not null && options.FirstOrDefault(option => option.Key == savedKey) is { } saved)
        {
            return saved;
        }

        var projectInformation = options.First(option => option.Key == ProjectInformationKey);
        if (projectInformation.AreaSquareMeters is not null)
        {
            return projectInformation;
        }

        var closedLines = options.Where(option => option.Key is not ProjectInformationKey and not AllPropertyLinesKey && option.AreaSquareMeters is not null).ToList();
        return closedLines.Count switch
        {
            1 => closedLines[0],
            > 1 => options.First(option => option.Key == AllPropertyLinesKey),
            _ => projectInformation
        };
    }
}
