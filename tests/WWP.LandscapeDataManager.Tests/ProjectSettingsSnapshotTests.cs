using WWP.LandscapeDataManager.Contracts;
using WWP.LandscapeDataManager.Shared.Services;
using Xunit;

namespace WWP.LandscapeDataManager.Tests;

public class ProjectSettingsSnapshotTests
{
    [Fact]
    public void Round_trips_every_field_through_serialize_and_deserialize()
    {
        var snapshot = new ProjectSettingsSnapshot(
            PreferredUnitSystem: "Imperial",
            PreferredCurrency: "GBP",
            Latitude: 51.4545,
            Longitude: -2.5879,
            DataSource: new DataSourceSettings(DataSourceKind.Airtable, "https://airtable.com/shr123", string.Empty),
            AirtableApi: new AirtableApiSettings("appABC", "tblXYZ", "Grid view"),
            ParameterMappings: [new ParameterMappingDefinition("Species", "!_S_PLT_iTreeSpecies_Code_Text", "Type", "Text")],
            TypeAliases: [new TypeAlias("Oak (mixed)", "Quercus", "English Oak")]);

        var json = ProjectSettingsJson.Serialize(snapshot);
        var roundTripped = ProjectSettingsJson.Deserialize(json);

        Assert.NotNull(roundTripped);
        Assert.Equal(snapshot.PreferredUnitSystem, roundTripped.PreferredUnitSystem);
        Assert.Equal(snapshot.PreferredCurrency, roundTripped.PreferredCurrency);
        Assert.Equal(snapshot.Latitude, roundTripped.Latitude);
        Assert.Equal(snapshot.Longitude, roundTripped.Longitude);
        Assert.Equal(snapshot.DataSource, roundTripped.DataSource);
        Assert.Equal(snapshot.AirtableApi, roundTripped.AirtableApi);
        Assert.Equal(snapshot.ParameterMappings, roundTripped.ParameterMappings);
        Assert.Equal(snapshot.TypeAliases, roundTripped.TypeAliases);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Deserialize_returns_null_for_empty_or_missing_input(string? json) =>
        Assert.Null(ProjectSettingsJson.Deserialize(json));

    [Fact]
    public void Deserialize_returns_null_for_malformed_json_rather_than_throwing() =>
        Assert.Null(ProjectSettingsJson.Deserialize("{not valid json"));

    [Fact]
    public void A_snapshot_with_only_some_fields_set_leaves_the_rest_null()
    {
        var snapshot = new ProjectSettingsSnapshot(PreferredCurrency: "AUD");

        var roundTripped = ProjectSettingsJson.Deserialize(ProjectSettingsJson.Serialize(snapshot));

        Assert.NotNull(roundTripped);
        Assert.Equal("AUD", roundTripped.PreferredCurrency);
        Assert.Null(roundTripped.PreferredUnitSystem);
        Assert.Null(roundTripped.DataSource);
        Assert.Null(roundTripped.ParameterMappings);
        Assert.Null(roundTripped.TypeAliases);
    }
}
