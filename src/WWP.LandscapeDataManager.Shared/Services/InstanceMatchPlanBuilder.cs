using WWP.LandscapeDataManager.Contracts;
using WWP.LandscapeDataManager.Shared.Models;

namespace WWP.LandscapeDataManager.Shared.Services;

/// <summary>
/// Matches Planting instances to source records purely by stable ID — the
/// <c>!_S_PLANTING_DataSync_SourceRecordId_Text</c> parameter a Revit instance was previously
/// paired with, checked against the source record's own ID (Airtable's native record ID, or an
/// equivalent stable ID column for Excel). Never matches by display name; an instance with no
/// stable pairing yet is reported, not guessed, so the user can pair it explicitly in Revit.
/// </summary>
public static class InstanceMatchPlanBuilder
{
    public static InstanceMatchPlan Build(
        IReadOnlyList<PlantingInstanceScanItem> revitInstances,
        IReadOnlyList<AirtableRecord> sourceRecords)
    {
        var pairedCounts = revitInstances
            .Where(instance => !string.IsNullOrEmpty(instance.SourceRecordId))
            .GroupBy(instance => instance.SourceRecordId, StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.Count(), StringComparer.Ordinal);
        var sourceById = sourceRecords
            .GroupBy(record => record.Id, StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.First(), StringComparer.Ordinal);

        var matches = new List<InstanceMatch>(revitInstances.Count);
        var matchedSourceIds = new HashSet<string>(StringComparer.Ordinal);

        foreach (var instance in revitInstances)
        {
            if (string.IsNullOrEmpty(instance.SourceRecordId))
            {
                matches.Add(new InstanceMatch(
                    instance, null, "NotPaired",
                    "Not yet paired to a source record. Select it in Revit, then use \"Pair selected\"."));
                continue;
            }

            if (pairedCounts[instance.SourceRecordId] > 1)
            {
                matches.Add(new InstanceMatch(
                    instance, null, "Duplicate",
                    $"More than one Revit instance is paired to source record '{instance.SourceRecordId}'."));
                continue;
            }

            if (!sourceById.TryGetValue(instance.SourceRecordId, out var record))
            {
                matches.Add(new InstanceMatch(
                    instance, null, "Orphaned",
                    $"Paired source record '{instance.SourceRecordId}' was not found in the latest pull."));
                continue;
            }

            matchedSourceIds.Add(instance.SourceRecordId);
            matches.Add(new InstanceMatch(instance, record, "Matched", null));
        }

        var unpairedSourceRecords = sourceRecords
            .Where(record => !matchedSourceIds.Contains(record.Id))
            .ToList();

        return new InstanceMatchPlan(matches, unpairedSourceRecords);
    }
}

/// <summary>Status is one of: "Matched", "NotPaired", "Duplicate", "Orphaned".</summary>
public sealed record InstanceMatch(
    PlantingInstanceScanItem Instance,
    AirtableRecord? Record,
    string Status,
    string? Message);

public sealed record InstanceMatchPlan(
    IReadOnlyList<InstanceMatch> Instances,
    IReadOnlyList<AirtableRecord> UnpairedSourceRecords);
