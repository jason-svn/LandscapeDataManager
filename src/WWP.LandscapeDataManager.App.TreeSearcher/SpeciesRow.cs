using WWP.LandscapeDataManager.Contracts;

namespace WWP.LandscapeDataManager.App.TreeSearcher;

public sealed class SpeciesRow(SpeciesCatalogueRecord record)
{
    public SpeciesCatalogueRecord Record { get; } = record;
    public string SpeciesCode { get; } = record.SpeciesCode;
    public string CommonName { get; } = record.CommonName;
    public string ScientificName { get; } = record.ScientificName;
    public string SpeciesType { get; } = record.SpeciesType;

    public string DeprecatedNote { get; } = string.IsNullOrWhiteSpace(record.ReplaceBy)
        ? string.Empty
        : $"Deprecated — replaced by {record.ReplaceBy}";

    /// <summary>Compact label for the per-row AutoSuggestBox dropdown and selected-text display.</summary>
    public string Display { get; } = $"{record.CommonName} ({record.SpeciesCode}) — {record.ScientificName}";
}
