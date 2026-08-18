using WWP.LandscapeDataManager.Contracts;
using WWP.LandscapeDataManager.Shared.Services;

namespace WWP.LandscapeDataManager.App.Importer;

/// <summary>Presentation row for the Instance mapping result list — either a match-plan issue or a write-preview row.</summary>
public sealed class InstanceSyncRow
{
    public string Status { get; private init; } = string.Empty;
    public string UniqueId { get; private init; } = string.Empty;
    public string FamilyType { get; private init; } = string.Empty;
    public string Parameter { get; private init; } = "—";
    public string CurrentValue { get; private init; } = "—";
    public string ProposedValue { get; private init; } = "—";
    public string Message { get; private init; } = string.Empty;

    public static InstanceSyncRow FromIssue(InstanceMatch match) => new()
    {
        Status = match.Status,
        UniqueId = match.Instance.UniqueId,
        FamilyType = $"{match.Instance.FamilyName} : {match.Instance.TypeName}",
        Message = match.Message ?? string.Empty
    };

    public static InstanceSyncRow FromWrite(InstanceParameterWritePreviewRow row, string familyType) => new()
    {
        Status = row.Status,
        UniqueId = row.UniqueId,
        FamilyType = familyType,
        Parameter = row.RevitParameter,
        CurrentValue = row.CurrentValue,
        ProposedValue = row.ProposedValue,
        Message = row.Message ?? string.Empty
    };

    /// <summary>A matched instance whose mapped Airtable column has no value for this record, so there was nothing to write.</summary>
    public static InstanceSyncRow FromSkippedField(InstanceMatch match, string airtableField, string revitParameter) => new()
    {
        Status = "Skipped",
        UniqueId = match.Instance.UniqueId,
        FamilyType = $"{match.Instance.FamilyName} : {match.Instance.TypeName}",
        Parameter = revitParameter,
        Message = $"Source column '{airtableField}' is empty or missing on the matched Airtable record — nothing to write."
    };

    /// <summary>A matched instance with a non-empty source value that could not be converted to the target parameter's unit.</summary>
    public static InstanceSyncRow FromNormalizationFailure(InstanceMatch match, string revitParameter, string message) => new()
    {
        Status = "Skipped",
        UniqueId = match.Instance.UniqueId,
        FamilyType = $"{match.Instance.FamilyName} : {match.Instance.TypeName}",
        Parameter = revitParameter,
        Message = message
    };
}
