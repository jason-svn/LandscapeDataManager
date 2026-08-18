using WWP.LandscapeDataManager.Contracts;
using WWP.LandscapeDataManager.Shared.Models;
using WWP.LandscapeDataManager.Shared.Services;
using Xunit;

namespace WWP.LandscapeDataManager.Tests;

public sealed class DefaultParameterMappingCatalogTests
{
    private static ParameterOption Option(string name, string scope = "Type") =>
        new(new RevitParameterDescriptor(name, scope, "String", "text", IsWritable: true, Categories: ["Planting"]));

    [Fact]
    public void A_known_wwp_header_matches_its_documented_parameter()
    {
        var options = new[] { Option("!_S_PLT_LDS_MaintenanceCostAnnual_Currency") };

        var match = DefaultParameterMappingCatalog.FindMatch("Maintenance Costs", options, new HashSet<string>());

        Assert.NotNull(match);
        Assert.Equal("!_S_PLT_LDS_MaintenanceCostAnnual_Currency", match!.Descriptor.Name);
    }

    [Fact]
    public void An_unknown_header_still_matches_by_normalized_equality_to_its_own_text()
    {
        var options = new[] { Option("WWP_Cost_Saved") };

        var match = DefaultParameterMappingCatalog.FindMatch("WWP Cost Saved", options, new HashSet<string>());

        Assert.NotNull(match);
        Assert.Equal("WWP_Cost_Saved", match!.Descriptor.Name);
    }

    [Fact]
    public void A_completely_unrecognized_header_with_no_matching_parameter_returns_null()
    {
        var options = new[] { Option("!_S_PLT_LDS_MaintenanceCostAnnual_Currency") };

        var match = DefaultParameterMappingCatalog.FindMatch("Some Totally Unrelated Column", options, new HashSet<string>());

        Assert.Null(match);
    }

    [Fact]
    public void Already_used_targets_are_skipped_so_two_headers_never_claim_the_same_parameter()
    {
        var options = new[] { Option("!_S_PLT_iTreeSpecies_Code_Text") };
        var used = new HashSet<string> { DefaultParameterMappingCatalog.TargetKey(options[0]) };

        var match = DefaultParameterMappingCatalog.FindMatch("Species Code", options, used);

        Assert.Null(match);
    }

    [Fact]
    public void A_type_scoped_option_is_preferred_over_an_instance_scoped_option_with_the_same_name()
    {
        var instanceOption = Option("!_S_PLT_iTreeSpecies_Code_Text", scope: "Instance");
        var typeOption = Option("!_S_PLT_iTreeSpecies_Code_Text", scope: "Type");

        var match = DefaultParameterMappingCatalog.FindMatch("Species Code", [instanceOption, typeOption], new HashSet<string>());

        Assert.NotNull(match);
        Assert.Equal("Type", match!.Descriptor.Scope);
    }

    [Theory]
    [InlineData("Species_Code", "!_S_PLT_iTreeSpecies_Code_Text")]
    [InlineData("Common Name", "!_S_PLT_iTreeSpecies_CommonName_Text")]
    [InlineData("Scientific Name", "!_S_PLT_iTreeSpecies_ScientificName_Text")]
    [InlineData("Origin", "!_S_PLT_LDS_Origin_Text")]
    [InlineData("WWP_LDS_Category", "!_S_PLT_LDS_Category_Text")]
    [InlineData("WWP_Pollutants_Removed", "!_S_PLT_LDS_PollutantsRemovedAnnual_Mass")]
    [InlineData("Max_Height", "!_S_PLT_LDS_MaxHeight_Number")]
    [InlineData("Max_Width", "!_S_PLT_LDS_MaxWidth_Number")]
    [InlineData("Height year annual growth rate (m/yr)", "!_S_PLT_GrowthRatio_HeightbyYear_Number")]
    [InlineData("Width annual growth rate (m/yr)", "!_S_PLT_GrowthRatio_WidthbyYear_Number")]
    [InlineData(
        "COST SAVED per year - Water run-off (based on UK stormwater treatment costs and infrastructure)",
        "!_S_PLT_iTreeResult_StormWaterCostSavedAnnual_Currency")]
    [InlineData(
        "COST SAVED per year - Carbon - calculated based on offsets saved (carbon price)",
        "!_S_PLT_iTreeResult_CarbonCostSavedAnnual_Currency")]
    [InlineData(
        "COST SAVED per year - Air pollutants based on health care costs, cleaning and ecosystem recovery costs",
        "!_S_PLT_iTreeResult_AirPollutionCostSavedAnnual_Currency")]
    [InlineData("COST SAVED per year - Oxygen", "!_S_PLT_LDS_OxygenCostSavedAnnual_Currency")]
    [InlineData(
        "f_int and Crg ( fraction of rainfall intercepted by the area(dimensionless, per planting type) (runoff coefficient",
        "!_S_PLT_LDS_RunoffCoefficient_Number")]
    [InlineData("Allergenic", "!_S_PLT_iTreeSpecies_Allergenic_Text")]
    public void Every_documented_default_header_resolves_to_its_current_parameter_name(string header, string expectedParameter)
    {
        var options = new[] { Option(expectedParameter) };

        var match = DefaultParameterMappingCatalog.FindMatch(header, options, new HashSet<string>());

        Assert.NotNull(match);
        Assert.Equal(expectedParameter, match!.Descriptor.Name);
    }
}
