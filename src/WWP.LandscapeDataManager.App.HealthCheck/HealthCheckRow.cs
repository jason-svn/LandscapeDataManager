using Microsoft.UI;
using Microsoft.UI.Xaml.Media;
using WWP.LandscapeDataManager.Contracts;

namespace WWP.LandscapeDataManager.App.HealthCheck;

/// <summary>One reported Planting or Floor element, wrapping the pipe-protocol item with a display-ready brush.</summary>
public sealed class HealthCheckRow(HealthCheckItem item)
{
    public string UniqueId { get; } = item.UniqueId;
    public string Category { get; } = item.Category;
    public string DisplayName { get; } = $"{item.FamilyName} : {item.TypeName}";
    public string Status { get; } = item.Status;
    public string Reason { get; } = item.Reason;

    public SolidColorBrush StatusBrush => Status switch
    {
        "Success" => new SolidColorBrush(Colors.SeaGreen),
        "NeedsAttention" => new SolidColorBrush(Colors.Crimson),
        _ => new SolidColorBrush(Colors.Gray)
    };
}
