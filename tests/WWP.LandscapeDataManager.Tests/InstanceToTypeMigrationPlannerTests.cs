using WWP.LandscapeDataManager.Revit.Services;
using Xunit;

namespace WWP.LandscapeDataManager.Tests;

public sealed class InstanceToTypeMigrationPlannerTests
{
    private static BindingMigrationSample Number(long element, long type, double? value) =>
        new(element, type, $"Type {type}", new BindingMigrationValue(Number: value));

    private static BindingMigrationSample Text(long element, long type, string? value) =>
        new(element, type, $"Type {type}", BindingMigrationValue.FromText(value));

    [Theory]
    [InlineData("!_S_PLT_LDS_MatchKey_Text", true)]
    [InlineData("!_S_PLT_LDS_AirTempReduction_Number", true)]
    [InlineData("!_S_PLT_LDS_SurfaceTempReduction_Number", true)]
    [InlineData("!_S_PLT_iTreeInput_Year_Number", false)]
    [InlineData("!_S_PLT_BNGResult_HabitatUnits_Number", false)]
    [InlineData("!_S_PLT_LDS_Type_Text", false)]
    public void Only_the_three_approved_fields_can_be_migrated(string name, bool supported) =>
        Assert.Equal(supported, InstanceToTypeMigrationPlanner.Supports(name));

    [Fact]
    public void Agreeing_instances_produce_one_value_per_type()
    {
        var plan = InstanceToTypeMigrationPlanner.Build([Number(1, 10, 2.5), Number(2, 10, 2.5), Number(3, 20, 7)]);

        Assert.True(plan.CanMigrate);
        Assert.Equal(3, plan.InstanceCount);
        Assert.Equal(2, plan.TypeValues.Count);
        Assert.Equal(2.5, Assert.Single(plan.TypeValues, value => value.TypeId == 10).Value.Number);
        Assert.Equal(7, Assert.Single(plan.TypeValues, value => value.TypeId == 20).Value.Number);
    }

    [Fact]
    public void One_disagreeing_type_blocks_the_entire_parameter()
    {
        var plan = InstanceToTypeMigrationPlanner.Build([Number(1, 10, 2), Number(2, 10, 3), Number(3, 20, 7)]);

        Assert.False(plan.CanMigrate);
        Assert.Empty(plan.TypeValues);
        var conflict = Assert.Single(plan.Conflicts);
        Assert.Equal(10, conflict.TypeId);
        Assert.Equal(new long[] { 1, 2 }, conflict.ElementIds);
    }

    [Fact]
    public void Small_numeric_differences_are_not_rounded_away()
    {
        var plan = InstanceToTypeMigrationPlanner.Build([Number(1, 10, 1), Number(2, 10, 1 + 1e-10)]);

        Assert.False(plan.CanMigrate);
    }

    [Fact]
    public void A_missing_number_is_distinct_from_a_stored_zero()
    {
        var plan = InstanceToTypeMigrationPlanner.Build([Number(1, 10, null), Number(2, 10, 0)]);

        Assert.False(plan.CanMigrate);
    }

    [Fact]
    public void An_empty_instance_is_not_filled_from_another_instance()
    {
        var plan = InstanceToTypeMigrationPlanner.Build([Text(1, 10, ""), Text(2, 10, "record-1")]);

        Assert.False(plan.CanMigrate);
    }

    [Fact]
    public void Matching_keys_are_not_case_folded_or_trimmed()
    {
        Assert.False(InstanceToTypeMigrationPlanner.Build([Text(1, 10, "Record"), Text(2, 10, "record")]).CanMigrate);
        Assert.False(InstanceToTypeMigrationPlanner.Build([Text(1, 10, "record"), Text(2, 10, "record ")]).CanMigrate);
        var plan = InstanceToTypeMigrationPlanner.Build([Text(1, 10, " record "), Text(2, 10, " record ")]);
        Assert.Equal(" record ", Assert.Single(plan.TypeValues).Value.Text);
    }

    [Fact]
    public void Empty_values_and_an_unused_binding_have_no_values_to_restore()
    {
        var plan = InstanceToTypeMigrationPlanner.Build([Text(1, 10, null), Text(2, 10, "")]);

        Assert.True(plan.CanMigrate);
        Assert.True(Assert.Single(plan.TypeValues).Value.IsEmpty);
        Assert.True(InstanceToTypeMigrationPlanner.Build([]).CanMigrate);
    }

    [Theory]
    [InlineData(double.NaN)]
    [InlineData(double.PositiveInfinity)]
    [InlineData(double.NegativeInfinity)]
    public void Non_finite_values_are_not_migrated(double value) =>
        Assert.False(InstanceToTypeMigrationPlanner.Build([Number(1, 10, value)]).CanMigrate);

    [Fact]
    public void A_missing_type_or_ambiguous_storage_blocks_migration()
    {
        Assert.False(InstanceToTypeMigrationPlanner.Build([Number(1, -1, 2)]).CanMigrate);
        var invalid = new BindingMigrationSample(1, 10, "Type 10", new BindingMigrationValue("record", 2));
        Assert.False(InstanceToTypeMigrationPlanner.Build([invalid]).CanMigrate);
    }

    [Fact]
    public void Preexisting_type_values_cannot_be_overwritten_by_instances()
    {
        var existing = new BindingMigrationTypeValue(10, "Type 10", new BindingMigrationValue(Number: 9));
        var plan = InstanceToTypeMigrationPlanner.Build([Number(1, 10, 2)], [existing]);

        Assert.False(plan.CanMigrate);
        Assert.Empty(plan.TypeValues);
    }

    [Fact]
    public void Preexisting_values_on_unplaced_types_are_preserved()
    {
        var existing = new BindingMigrationTypeValue(20, "Unplaced type", new BindingMigrationValue(Number: 9));
        var plan = InstanceToTypeMigrationPlanner.Build([Number(1, 10, 2)], [existing]);

        Assert.True(plan.CanMigrate);
        Assert.Equal(9, Assert.Single(plan.TypeValues, value => value.TypeId == 20).Value.Number);
    }

    [Fact]
    public void Agreeing_type_and_instance_values_are_preserved_once()
    {
        var existing = new BindingMigrationTypeValue(10, "Type 10", new BindingMigrationValue(Number: 2));
        var plan = InstanceToTypeMigrationPlanner.Build([Number(1, 10, 2)], [existing]);

        Assert.True(plan.CanMigrate);
        Assert.Single(plan.TypeValues);
    }
}
