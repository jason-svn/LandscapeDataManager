using WWP.LandscapeDataManager.Contracts;
using WWP.LandscapeDataManager.Shared.Services;
using Xunit;

namespace WWP.LandscapeDataManager.Tests;

public class LimSettingsFileTests
{
    private static readonly DateTimeOffset ExportedAt = new(2026, 10, 6, 9, 30, 0, TimeSpan.Zero);

    private static ProjectSettingsSnapshot FullSnapshot() => new(
        PreferredUnitSystem: "Imperial",
        PreferredCurrency: "USD",
        Latitude: 51.5,
        Longitude: -0.1,
        DataSource: new DataSourceSettings(DataSourceKind.Excel, string.Empty, @"C:\data\planting.xlsx"),
        AirtableApi: new AirtableApiSettings("appPLANTING", "Trees", "Grid view"),
        TypeAliases: [new TypeAlias("Oak 3m", "Planting_Tree", "Quercus robur 3m")],
        WwpLdsSource: new WwpLdsAirtableSettings("appLDS", "Coefficients", null),
        SharedParameterFilePath: @"C:\LIM\Shared_Parameters_WWP.txt",
        InstanceMatchKey: new InstanceMatchKeySettings("Mark", "Tag"));

    [Fact]
    public void Round_trips_every_setting_except_the_site_location()
    {
        var file = LimSettingsFileFormat.Build(FullSnapshot(), "Metric", "GBP", "Site A", "1.2.1", null, ExportedAt);

        var parsed = LimSettingsFileFormat.Parse(LimSettingsFileFormat.Serialize(file));

        Assert.Equal("Site A", parsed.ExportedFromProject);
        Assert.Equal(ExportedAt, parsed.ExportedAt);
        Assert.Equal("Metric", parsed.Settings.PreferredUnitSystem); // the live parameter wins over the snapshot mirror
        Assert.Equal("GBP", parsed.Settings.PreferredCurrency);
        Assert.Null(parsed.Settings.Latitude);
        Assert.Null(parsed.Settings.Longitude);
        Assert.Equal(new AirtableApiSettings("appPLANTING", "Trees", "Grid view"), parsed.Settings.AirtableApi);
        Assert.Equal(new WwpLdsAirtableSettings("appLDS", "Coefficients", null), parsed.Settings.WwpLdsSource);
        Assert.Equal(DataSourceKind.Excel, parsed.Settings.DataSource!.Kind);
        Assert.Equal(new TypeAlias("Oak 3m", "Planting_Tree", "Quercus robur 3m"), Assert.Single(parsed.Settings.TypeAliases!));
        Assert.Equal(new InstanceMatchKeySettings("Mark", "Tag"), parsed.Settings.InstanceMatchKey);
        Assert.Equal(@"C:\LIM\Shared_Parameters_WWP.txt", parsed.Settings.SharedParameterFilePath);
        Assert.Null(parsed.Secrets);
    }

    [Fact]
    public void Keys_are_written_only_when_supplied()
    {
        var withKeys = LimSettingsFileFormat.Build(null, null, null, null, null, new LimSettingsSecrets("itree-key", "patABC"), ExportedAt);
        var blankKeys = LimSettingsFileFormat.Build(null, null, null, null, null, new LimSettingsSecrets(" ", null), ExportedAt);

        var parsed = LimSettingsFileFormat.Parse(LimSettingsFileFormat.Serialize(withKeys));
        Assert.Equal("itree-key", parsed.Secrets!.ITreeApiKey);
        Assert.Equal("patABC", parsed.Secrets.AirtableToken);
        Assert.Null(LimSettingsFileFormat.Parse(LimSettingsFileFormat.Serialize(blankKeys)).Secrets);
    }

    [Fact]
    public void A_hand_edited_location_is_still_dropped_on_import()
    {
        const string json = """
            { "format": "lim-settings", "formatVersion": 1, "exportedAt": "2026-10-06T09:30:00Z",
              "settings": { "latitude": 10, "longitude": 20, "preferredCurrency": "EUR" } }
            """;

        var parsed = LimSettingsFileFormat.Parse(json);

        Assert.Null(parsed.Settings.Latitude);
        Assert.Null(parsed.Settings.Longitude);
        Assert.Equal("EUR", parsed.Settings.PreferredCurrency);
    }

    [Theory]
    [InlineData("not json at all")]
    [InlineData("""{ "documentTitle": "a dashboard export" }""")]
    [InlineData("""{ "format": "something-else", "formatVersion": 1, "settings": {} }""")]
    [InlineData("""{ "format": "lim-settings", "formatVersion": 1 }""")]
    public void Rejects_files_that_are_not_lim_settings(string json) =>
        Assert.Throws<InvalidDataException>(() => LimSettingsFileFormat.Parse(json));

    [Fact]
    public void Rejects_a_newer_format_with_a_hint_to_update()
    {
        var exception = Assert.Throws<InvalidDataException>(() =>
            LimSettingsFileFormat.Parse("""{ "format": "lim-settings", "formatVersion": 99, "settings": {} }"""));
        Assert.Contains("Check for Updates", exception.Message);
    }
}
