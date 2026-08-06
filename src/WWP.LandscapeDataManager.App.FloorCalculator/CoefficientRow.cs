using WWP.LandscapeDataManager.Contracts;

namespace WWP.LandscapeDataManager.App.FloorCalculator;

/// <summary>One WWP landscape data sheet row, wrapped for display in a search dropdown.</summary>
public sealed class CoefficientRow(WwpLdsCoefficientRecord record)
{
    public WwpLdsCoefficientRecord Record { get; } = record;

    public string DisplayName { get; } = string.IsNullOrWhiteSpace(record.TypeName)
        ? record.SubCategory.Replace('_', ' ')
        : $"{record.SubCategory.Replace('_', ' ')} - {record.TypeName.Replace('_', ' ')}";

    /// <summary>Compact label for the per-row AutoSuggestBox dropdown and selected-text display.</summary>
    public string Display => $"{DisplayName} ({Record.Category})";
}
