using WWP.LandscapeDataManager.Contracts;
using WWP.LandscapeDataManager.Shared.Services;

namespace WWP.LandscapeDataManager.App.Importer;

/// <summary>Presentation row for the Instance mapping result list — either a match-plan issue or a write-preview row.</summary>
public sealed class InstanceSyncRow
{
    public string Status { get; private init; } = string.Empty;
    public string FamilyType { get; private init; } = string.Empty;
    public string Parameter { get; private init; } = "—";
    public string CurrentValue { get; private init; } = "—";
    public string ProposedValue { get; private init; } = "—";
    public string Message { get; private init; } = string.Empty;

    public static InstanceSyncRow FromIssue(InstanceMatch match) => new()
    {
        Status = match.Status,
        FamilyType = $"{match.Instance.FamilyName} : {match.Instance.TypeName}",
        Message = match.Message ?? string.Empty
    };

    public static InstanceSyncRow FromWrite(InstanceParameterWritePreviewRow row, string familyType) => new()
    {
        Status = row.Status,
        FamilyType = familyType,
        Parameter = row.RevitParameter,
        CurrentValue = row.CurrentValue,
        ProposedValue = row.ProposedValue,
        Message = row.Message ?? string.Empty
    };
}
