using WWP.LandscapeDataManager.Shared.Services;

namespace WWP.LandscapeDataManager.App.Importer;

/// <summary>A row of the "Add new tree types to Airtable" table: a Revit tree type the planting source has no row for.</summary>
public sealed class NewTreeTypeRow(NewTreeType type)
{
    public NewTreeType Type { get; } = type;

    public string FamilyType { get; } = $"{type.FamilyName} : {type.TypeName}";

    public string TreeCountText { get; } = type.TreeCount.ToString("N0");

    public string SpeciesCodeText { get; } = type.SpeciesCode ?? "—";

    public string AirtableText { get; } = type.Conflict ?? "No row yet";
}
