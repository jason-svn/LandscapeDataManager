using Microsoft.UI;
using Microsoft.UI.Xaml.Media;

namespace WWP.LandscapeDataManager.App.SyncAudit;

/// <summary>
/// One line of the audit report. <see cref="TypeWriteIndex"/>/<see cref="InstanceWriteIndex"/>
/// point back into the pending write batches built alongside the report, so "Reapply selected"
/// can re-apply exactly the rows the user checked without rebuilding the comparison.
/// </summary>
public sealed class AuditRow
{
    // Plain settable properties, not init/required: the WinUI x:Bind XAML type-info generator
    // constructs instances via a bare parameterless new() and assigns properties afterward, which
    // is incompatible with init-only or required members.
    public string Category { get; set; } = string.Empty;
    public string Scope { get; set; } = string.Empty;
    public string Target { get; set; } = string.Empty;
    public string Parameter { get; set; } = "—";
    public string PreviousValue { get; set; } = "—";
    public string CurrentValue { get; set; } = "—";
    public string LatestValue { get; set; } = "—";
    public string ProposedAction { get; set; } = "—";
    public string Message { get; set; } = string.Empty;
    public int? TypeWriteIndex { get; set; }
    public int? InstanceWriteIndex { get; set; }
    public string? UniqueId { get; set; }

    /// <summary>The synced-value-history key (type:{id} or a UniqueId) this row's field belongs to — needed to record "Keep Revit value."</summary>
    public string? TargetKey { get; set; }

    public SolidColorBrush CategoryBrush => Category switch
    {
        "Added" => new SolidColorBrush(Colors.SeaGreen),
        "Changed" => new SolidColorBrush(Colors.SteelBlue),
        "Conflict" => new SolidColorBrush(Colors.DarkOrange),
        "Removed from source" => new SolidColorBrush(Colors.Crimson),
        "Calculation stale" => new SolidColorBrush(Colors.MediumPurple),
        "Catalogue stale" => new SolidColorBrush(Colors.Goldenrod),
        "Not yet paired" => new SolidColorBrush(Colors.Gray),
        _ => new SolidColorBrush(Colors.Gray)
    };
}
