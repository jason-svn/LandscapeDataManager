using System.Text.Json;
using WWP.LandscapeDataManager.Contracts;
using WWP.LandscapeDataManager.Shared.Models;
using WWP.LandscapeDataManager.Shared.Services;
using Xunit;

namespace WWP.LandscapeDataManager.Tests;

public class StableTypeMatcherTests
{
    private static readonly string[] KeyFields = ["Types"];

    private static ModelScanItem RevitType(string familyName, string typeName, long typeId = 1) =>
        new("Planting", typeName, typeId, 1, 0, null, false, familyName);

    private static AirtableRecord SourceRecord(string id, string typeValue) =>
        new(id, new Dictionary<string, JsonElement> { ["Types"] = JsonSerializer.SerializeToElement(typeValue) });

    [Fact]
    public void Matches_a_single_clean_pair()
    {
        var revitTypes = new[] { RevitType("Trees", "Oak") };
        var records = new[] { SourceRecord("rec1", "Oak") };

        var results = StableTypeMatcher.Build(revitTypes, records, KeyFields, []);

        var result = Assert.Single(results);
        Assert.Equal("Matched", result.Status);
        Assert.Equal("rec1", result.Record!.Id);
    }

    [Fact]
    public void Reports_missing_when_no_source_record_matches()
    {
        var revitTypes = new[] { RevitType("Trees", "Birch") };
        var records = new[] { SourceRecord("rec1", "Oak") };

        var results = StableTypeMatcher.Build(revitTypes, records, KeyFields, []);

        Assert.Equal("Missing", Assert.Single(results).Status);
    }

    [Fact]
    public void Reports_duplicate_when_two_source_records_share_a_key()
    {
        var revitTypes = new[] { RevitType("Trees", "Oak") };
        var records = new[] { SourceRecord("rec1", "Oak"), SourceRecord("rec2", "Oak") };

        var results = StableTypeMatcher.Build(revitTypes, records, KeyFields, []);

        Assert.Equal("Duplicate", Assert.Single(results).Status);
    }

    [Fact]
    public void Reports_ambiguous_when_two_families_share_a_type_name_and_no_alias_resolves_it()
    {
        var revitTypes = new[] { RevitType("FamilyA", "Oak"), RevitType("FamilyB", "Oak", 2) };
        var records = new[] { SourceRecord("rec1", "Oak") };

        var results = StableTypeMatcher.Build(revitTypes, records, KeyFields, []);

        Assert.All(results, result => Assert.Equal("Ambiguous", result.Status));
    }

    [Fact]
    public void An_alias_resolves_the_ambiguity_for_the_aliased_family_only()
    {
        var revitTypes = new[] { RevitType("FamilyA", "Oak"), RevitType("FamilyB", "Oak", 2) };
        var records = new[] { SourceRecord("rec1", "Oak") };
        var aliases = new[] { new TypeAlias("Oak", "FamilyA", "Oak") };

        var results = StableTypeMatcher.Build(revitTypes, records, KeyFields, aliases);

        var familyAResult = Assert.Single(results, r => r.RevitType.FamilyName == "FamilyA");
        var familyBResult = Assert.Single(results, r => r.RevitType.FamilyName == "FamilyB");
        Assert.Equal("Matched", familyAResult.Status);
        Assert.Equal("Ambiguous", familyBResult.Status);
    }

    [Fact]
    public void Never_matches_by_a_key_field_that_is_not_configured()
    {
        var revitTypes = new[] { RevitType("Trees", "Oak") };
        var records = new[]
        {
            new AirtableRecord("rec1", new Dictionary<string, JsonElement>
            {
                ["Some Other Column"] = JsonSerializer.SerializeToElement("Oak")
            })
        };

        var results = StableTypeMatcher.Build(revitTypes, records, KeyFields, []);

        Assert.Equal("Missing", Assert.Single(results).Status);
    }
}
