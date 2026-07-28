using System.Text.Json;
using WWP.LandscapeDataManager.Contracts;
using WWP.LandscapeDataManager.Shared.Models;
using WWP.LandscapeDataManager.Shared.Services;
using Xunit;

namespace WWP.LandscapeDataManager.Tests;

public class InstanceMatchPlanBuilderTests
{
    private static PlantingInstanceScanItem Instance(string uniqueId, string sourceRecordId, long elementId = 1) =>
        new(uniqueId, elementId, "Trees", "Oak", 1, sourceRecordId);

    private static AirtableRecord SourceRecord(string id) =>
        new(id, new Dictionary<string, JsonElement>());

    [Fact]
    public void Matches_an_instance_paired_to_an_existing_source_record()
    {
        var instances = new[] { Instance("uid-1", "rec1") };
        var records = new[] { SourceRecord("rec1") };

        var plan = InstanceMatchPlanBuilder.Build(instances, records);

        var match = Assert.Single(plan.Instances);
        Assert.Equal("Matched", match.Status);
        Assert.Equal("rec1", match.Record!.Id);
        Assert.Empty(plan.UnpairedSourceRecords);
    }

    [Fact]
    public void Reports_not_paired_for_an_instance_with_no_source_record_id()
    {
        var instances = new[] { Instance("uid-1", string.Empty) };
        var records = Array.Empty<AirtableRecord>();

        var plan = InstanceMatchPlanBuilder.Build(instances, records);

        Assert.Equal("NotPaired", Assert.Single(plan.Instances).Status);
    }

    [Fact]
    public void Reports_duplicate_when_two_instances_share_the_same_pairing()
    {
        var instances = new[] { Instance("uid-1", "rec1"), Instance("uid-2", "rec1", 2) };
        var records = new[] { SourceRecord("rec1") };

        var plan = InstanceMatchPlanBuilder.Build(instances, records);

        Assert.All(plan.Instances, match => Assert.Equal("Duplicate", match.Status));
    }

    [Fact]
    public void Reports_orphaned_when_the_paired_source_record_no_longer_exists()
    {
        var instances = new[] { Instance("uid-1", "rec-gone") };
        var records = Array.Empty<AirtableRecord>();

        var plan = InstanceMatchPlanBuilder.Build(instances, records);

        Assert.Equal("Orphaned", Assert.Single(plan.Instances).Status);
    }

    [Fact]
    public void Reports_unpaired_source_records_that_no_instance_claims()
    {
        var instances = new[] { Instance("uid-1", "rec1") };
        var records = new[] { SourceRecord("rec1"), SourceRecord("rec2") };

        var plan = InstanceMatchPlanBuilder.Build(instances, records);

        var unpaired = Assert.Single(plan.UnpairedSourceRecords);
        Assert.Equal("rec2", unpaired.Id);
    }

    [Fact]
    public void Never_matches_an_unpaired_instance_to_an_unpaired_record_by_any_implicit_rule()
    {
        var instances = new[] { Instance("uid-1", string.Empty) };
        var records = new[] { SourceRecord("rec1") };

        var plan = InstanceMatchPlanBuilder.Build(instances, records);

        Assert.Equal("NotPaired", Assert.Single(plan.Instances).Status);
        Assert.Single(plan.UnpairedSourceRecords);
    }
}
