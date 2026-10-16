using WWP.LandscapeDataManager.Revit.Services;
using Xunit;

namespace WWP.LandscapeDataManager.Tests;

public sealed class ResultParameterReusePolicyTests
{
    private const string BroadHabitat = "!_S_PLT_BNGResult_BroadHabitat_Text";
    private const string TextSpec = "text-spec";
    private static readonly Guid ProjectGuid = new("46567b5d-550d-4461-8382-ab4318f12b92");

    [Fact]
    public void Existing_BNG_identity_from_the_reported_project_is_retained()
    {
        var decision = ResultParameterReusePolicy.Evaluate(
            BroadHabitat, TextSpec, [new(ProjectGuid, TextSpec, true)], 1);

        Assert.True(decision.CanReuse);
        Assert.Equal(ProjectGuid, decision.ExistingGuid);
        Assert.Null(decision.Conflict);
        Assert.NotEqual(new Guid("1d4c20e5-38f4-4ef5-99ba-2eedc2aee531"), decision.ExistingGuid);
    }

    [Theory]
    [InlineData("!_S_PLT_ConnectivityResult_Band_Number", "number-spec")]
    [InlineData("!_S_PLT_ConnectivityResult_Class_Text", TextSpec)]
    [InlineData("!_S_PLT_BNGResult_HabitatUnits_Number", "number-spec")]
    [InlineData("!_S_PLT_BNGResult_BaselineUnits_Number", "number-spec")]
    public void Compatible_result_fields_reuse_the_actual_project_ID(string name, string spec)
    {
        var decision = ResultParameterReusePolicy.Evaluate(name, spec, [new(ProjectGuid, spec, true)], 1);

        Assert.True(decision.CanReuse);
        Assert.Equal(ProjectGuid, decision.ExistingGuid);
    }

    [Theory]
    [InlineData("!_S_PLT_BNGInput_ProposedHabitat_Text")]
    [InlineData("!_S_PLT_ConnectivityInput_Locked_YesNo")]
    [InlineData("!_S_PLT_LDS_MatchKey_Text")]
    [InlineData("!_S_PLT_iTreeResult_CO2SequesteredAnnual_Mass")]
    [InlineData("!_S_PLT_BNGResult_Unknown_Text")]
    [InlineData("!_S_PLT_BNGResult_BroadHabitat_Text_EXTRA")]
    [InlineData("!_S_PLT_BNGResult_BroadHabitat_text")]
    public void Other_parameters_still_require_their_catalog_ID(string name)
    {
        var decision = ResultParameterReusePolicy.Evaluate(name, TextSpec, [new(ProjectGuid, TextSpec, true)], 1);

        Assert.False(decision.CanReuse);
        Assert.Null(decision.ExistingGuid);
    }

    [Theory]
    [InlineData("number-spec")]
    [InlineData("mass-spec")]
    [InlineData("")]
    public void A_matching_name_cannot_hide_a_different_data_type(string actualSpec)
    {
        var decision = ResultParameterReusePolicy.Evaluate(
            BroadHabitat, TextSpec, [new(ProjectGuid, actualSpec, true)], 1);

        Assert.False(decision.CanReuse);
        Assert.Contains("different data type", decision.Conflict);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(null)]
    public void Type_bound_or_unbound_shared_definitions_are_not_reused(bool? isInstance)
    {
        var decision = ResultParameterReusePolicy.Evaluate(
            BroadHabitat, TextSpec, [new(ProjectGuid, TextSpec, isInstance)], 1);

        Assert.False(decision.CanReuse);
        Assert.Contains("Instance project binding", decision.Conflict);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public void Missing_or_ambiguous_same_name_project_bindings_are_blocked(int count)
    {
        var decision = ResultParameterReusePolicy.Evaluate(
            BroadHabitat, TextSpec, [new(ProjectGuid, TextSpec, true)], count);

        Assert.False(decision.CanReuse);
        Assert.Null(decision.ExistingGuid);
    }

    [Fact]
    public void Canonical_and_alternate_shared_definitions_with_one_name_are_ambiguous()
    {
        var decision = ResultParameterReusePolicy.Evaluate(BroadHabitat, TextSpec,
            [new(ProjectGuid, TextSpec, true), new(new Guid("1d4c20e5-38f4-4ef5-99ba-2eedc2aee531"), TextSpec, null)], 1);

        Assert.False(decision.CanReuse);
        Assert.Contains("duplicate", decision.Conflict);
    }

    [Fact]
    public void No_shared_definition_cannot_be_adopted_as_a_shared_parameter()
    {
        var decision = ResultParameterReusePolicy.Evaluate(BroadHabitat, TextSpec, [], 1);

        Assert.False(decision.CanReuse);
    }

    [Fact]
    public void Invalid_shared_ID_is_rejected()
    {
        var decision = ResultParameterReusePolicy.Evaluate(
            BroadHabitat, TextSpec, [new(Guid.Empty, TextSpec, true)], 1);

        Assert.False(decision.CanReuse);
    }
}
