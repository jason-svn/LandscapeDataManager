using WWP.LandscapeDataManager.Contracts;
using WWP.LandscapeDataManager.Shared.Services;

namespace WWP.LandscapeDataManager.Shared.Models;

public sealed class SyncPreviewRow
{
    public SyncPreviewRow(
        string status,
        string typeName,
        string parameter,
        string scope,
        string currentValue,
        string proposedValue,
        string targets,
        string message)
    {
        Status = status;
        TypeName = typeName;
        Parameter = parameter;
        Scope = scope;
        CurrentValue = currentValue;
        ProposedValue = proposedValue;
        Targets = targets;
        Message = message;
    }

    public string Status { get; }
    public string TypeName { get; }
    public string Parameter { get; }
    public string Scope { get; }
    public string CurrentValue { get; }
    public string ProposedValue { get; }
    public string Targets { get; }
    public string Message { get; }

    public static SyncPreviewRow FromRevit(ParameterWritePreviewRow row) =>
        new(
            row.Status,
            row.TypeName,
            row.RevitParameter,
            row.Scope,
            row.CurrentValue,
            row.ProposedValue,
            row.TargetCount.ToString("N0"),
            row.Message ?? string.Empty);

    public static SyncPreviewRow FromIssue(ParameterSyncIssue issue) =>
        new(
            "Skipped",
            issue.TypeName,
            issue.Parameter,
            "—",
            "—",
            "—",
            "0",
            issue.Message);
}
