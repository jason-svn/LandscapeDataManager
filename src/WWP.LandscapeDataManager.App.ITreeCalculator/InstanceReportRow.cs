using Microsoft.UI;
using Microsoft.UI.Xaml.Media;
using WWP.LandscapeDataManager.Contracts;
using WWP.LandscapeDataManager.Shared.Services;

namespace WWP.LandscapeDataManager.App.ITreeCalculator;

public sealed class InstanceReportRow(PlantingInstanceValidationItem item, PlantingInstanceStatus status)
{
    public string UniqueId { get; } = item.UniqueId;
    public long ElementId { get; } = item.ElementId;
    public string FamilyType { get; } = $"{item.FamilyName} : {item.TypeName}";
    public string SpeciesCode { get; } = item.SpeciesCode ?? "—";
    public string Years { get; } = item.Years?.ToString() ?? "—";
    public string Status { get; } = status.Status;
    public string Details { get; } = status.Details ?? string.Empty;
    public string LastUpdated { get; } = item.StoredLastUpdatedUtc;
    public string InputSignature { get; } = status.InputSignature;
    public PlantingInstanceValidationItem Item { get; } = item;

    public SolidColorBrush StatusBrush { get; } = status.Status switch
    {
        "Calculated" => new SolidColorBrush(Colors.SeaGreen),
        "Ready" => new SolidColorBrush(Colors.SteelBlue),
        "Stale" => new SolidColorBrush(Colors.MediumPurple),
        "MissingInput" => new SolidColorBrush(Colors.Gray),
        "InvalidInput" => new SolidColorBrush(Colors.DarkOrange),
        "APIWarning" => new SolidColorBrush(Colors.Goldenrod),
        "APIError" => new SolidColorBrush(Colors.Crimson),
        _ => new SolidColorBrush(Colors.Gray)
    };

    public bool CanCalculate => Status is "Ready" or "Stale";
}
