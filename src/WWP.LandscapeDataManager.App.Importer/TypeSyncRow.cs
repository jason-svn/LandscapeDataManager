using WWP.LandscapeDataManager.Contracts;
using WWP.LandscapeDataManager.Shared.Services;

namespace WWP.LandscapeDataManager.App.Importer;

/// <summary>Presentation row for the Type mapping result list — either a match-plan issue or a write-preview row.</summary>
public sealed class TypeSyncRow
{
    public string Status { get; private init; } = string.Empty;
    public string TypeName { get; private init; } = string.Empty;
    public string Parameter { get; private init; } = "—";
    public string CurrentValue { get; private init; } = "—";
    public string ProposedValue { get; private init; } = "—";
    public string Message { get; private init; } = string.Empty;

    public static TypeSyncRow FromIssue(TypeMatch match) => new()
    {
        Status = match.Status,
        TypeName = $"{match.RevitType.FamilyName} : {match.RevitType.TypeName}",
        Message = match.Message ?? string.Empty
    };

    public static TypeSyncRow FromWrite(ParameterWritePreviewRow row) => new()
    {
        Status = row.Status,
        TypeName = row.TypeName,
        Parameter = row.RevitParameter,
        CurrentValue = row.CurrentValue,
        ProposedValue = row.ProposedValue,
        Message = row.Message ?? string.Empty
    };

    /// <summary>A matched type whose mapped Airtable column has no value for this record, so there was nothing to write.</summary>
    public static TypeSyncRow FromSkippedField(TypeMatch match, string airtableField, string revitParameter) => new()
    {
        Status = "Skipped",
        TypeName = $"{match.RevitType.FamilyName} : {match.RevitType.TypeName}",
        Parameter = revitParameter,
        Message = $"Source column '{airtableField}' is empty or missing on the matched Airtable record — nothing to write."
    };

    /// <summary>A matched type with a non-empty source value that could not be converted to the target parameter's unit.</summary>
    public static TypeSyncRow FromNormalizationFailure(TypeMatch match, string revitParameter, string message) => new()
    {
        Status = "Skipped",
        TypeName = $"{match.RevitType.FamilyName} : {match.RevitType.TypeName}",
        Parameter = revitParameter,
        Message = message
    };
}
