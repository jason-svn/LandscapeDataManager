using WWP.LandscapeDataManager.Shared.Services;
using WWP.LandscapeDataManager.Contracts;
using Xunit;

namespace WWP.LandscapeDataManager.Tests;

public sealed class ITreeApiClientTests
{
    [Fact]
    public async Task Species_catalog_download_does_not_require_a_Revit_species_code()
    {
        var apiKey = Environment.GetEnvironmentVariable("ITREE_API_KEY");
        if (string.IsNullOrWhiteSpace(apiKey))
        {
            return;
        }

        var result = await new ITreeApiClient().DownloadSpeciesCatalogAsync(apiKey);

        Assert.True(result.Records.Count > 8_000);
        Assert.Equal(5, result.FieldCount);
        Assert.All(result.Records.Take(20), record =>
        {
            Assert.False(string.IsNullOrWhiteSpace(record.SpeciesCode));
            Assert.True(record.Fields.ContainsKey("Scientific_Name"));
            Assert.True(record.Fields.ContainsKey("Common_Name"));
            Assert.True(record.Fields.ContainsKey("Species_Code"));
            Assert.True(record.Fields.ContainsKey("SpeciesType"));
            Assert.True(record.Fields.ContainsKey("ReplaceBy"));
        });
    }

    [Fact]
    public async Task Download_returns_default_summary_and_period_fields_when_key_is_available()
    {
        var apiKey = Environment.GetEnvironmentVariable("ITREE_API_KEY");
        if (string.IsNullOrWhiteSpace(apiKey))
        {
            return;
        }

        var input = new ITreeRevitInput(
            "QURO",
            "English oak",
            "Quercus robur",
            "Test Family",
            "Test Type",
            1,
            "excellent",
            10,
            51.4545,
            -2.5879,
            20,
            5);
        var profile = new ITreeExportProfile(
            Monetary: true,
            Carbon: true,
            Hydrology: true,
            AirQuality: true,
            Metadata: false,
            AnnualTimeline: false,
            CumulativeTimeline: false,
            FullResponse: false);

        var result = await new ITreeApiClient().DownloadAsync([input], apiKey, profile);

        var record = Assert.Single(result.Records);
        Assert.Equal("QURO", record.SpeciesCode);
        Assert.Equal("QURO", record.Fields["Species_Code"]);
        var expectedBenefitColumns = new[]
        {
            "Annual_Benefit_USD",
            "Benefit_20yr_USD",
            "Annual_CarbonSequestered_lb",
            "CarbonSequestered_20yr_lb",
            "Annual_CO2eq_lb",
            "CO2eq_20yr_lb",
            "Annual_RunoffAvoided_gal",
            "RunoffAvoided_20yr_gal",
            "Annual_RainfallIntercepted_gal",
            "RainfallIntercepted_20yr_gal",
            "Annual_O3_oz",
            "O3_20yr_oz",
            "CO_20yr_oz",
            "NO2_20yr_oz",
            "SO2_20yr_oz",
            "PM25_20yr_oz"
        };
        Assert.All(expectedBenefitColumns, column => Assert.True(record.Fields.ContainsKey(column), column));
        Assert.Equal(25, result.FieldCount);
        Assert.Empty(result.Errors);
    }
}
