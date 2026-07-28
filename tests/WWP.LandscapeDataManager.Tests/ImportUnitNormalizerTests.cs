using System.Globalization;
using System.Text.Json;
using WWP.LandscapeDataManager.App.Services;
using WWP.LandscapeDataManager.Contracts;
using Xunit;

namespace WWP.LandscapeDataManager.Tests;

public sealed class ImportUnitNormalizerTests
{
    [Fact]
    public void NormalizesHeaderFeetToMetresForRevitLength()
    {
        var result = ImportUnitNormalizer.Normalize(
            "10",
            "Tree Height (ft)",
            Descriptor("Height", "autodesk.spec.aec:length-2.0.0"),
            "Auto (Revit spec)",
            "Metric",
            null);

        Assert.True(result.Success);
        Assert.Equal(3.048d, ReadNumber(result.Value), 10);
        Assert.Contains("column header", result.Message);
    }

    [Fact]
    public void CellUnitOverridesConflictingHeaderUnit()
    {
        var result = ImportUnitNormalizer.Normalize(
            "1200 mm",
            "Tree Height (m)",
            Descriptor("Height", "autodesk.spec.aec:length-2.0.0"),
            "Auto (Revit spec)",
            "Metric",
            null);

        Assert.True(result.Success);
        Assert.Equal(1.2d, ReadNumber(result.Value), 10);
        Assert.Contains("overrides the conflicting header unit m", result.Message);
    }

    [Fact]
    public void UsesProjectPreferenceWhenPhysicalUnitIsMissing()
    {
        var result = ImportUnitNormalizer.Normalize(
            "10",
            "Tree Height",
            Descriptor("Height", "autodesk.spec.aec:length-2.0.0"),
            "Auto (Revit spec)",
            "Imperial",
            null);

        Assert.True(result.Success);
        Assert.Equal(3.048d, ReadNumber(result.Value), 10);
        Assert.Contains("Project Information preference (Imperial)", result.Message);
    }

    [Fact]
    public void RecordUnitSystemOverridesProjectFallback()
    {
        var result = ImportUnitNormalizer.Normalize(
            "10",
            "Tree Height",
            Descriptor("Height", "autodesk.spec.aec:length-2.0.0"),
            "Auto (Revit spec)",
            "Imperial",
            "Metric");

        Assert.True(result.Success);
        Assert.Equal(10d, ReadNumber(result.Value), 10);
        Assert.Contains("record unit system (Metric)", result.Message);
    }

    [Fact]
    public void AlignsMassNumberToPreferredImperialSystem()
    {
        var result = ImportUnitNormalizer.Normalize(
            "10",
            "Carbon (kg)",
            Descriptor("Carbon", "autodesk.spec.aec:number-2.0.0"),
            "Auto (Revit spec)",
            "Imperial",
            null);

        Assert.True(result.Success);
        Assert.Equal(22.0462262185d, ReadNumber(result.Value), 9);
        Assert.Contains("lb", result.Message);
    }

    [Fact]
    public void NormalizesUsGallonsToCubicMetresForRevitVolume()
    {
        var result = ImportUnitNormalizer.Normalize(
            "100 gal",
            "Water",
            Descriptor("Water", "autodesk.spec.aec:volume-2.0.0"),
            "Auto (Revit spec)",
            "Imperial",
            null);

        Assert.True(result.Success);
        Assert.Equal(0.3785411784d, ReadNumber(result.Value), 10);
    }

    [Fact]
    public void RejectsUnitWithWrongPhysicalDimension()
    {
        var result = ImportUnitNormalizer.Normalize(
            "10 kg",
            "Tree Height",
            Descriptor("Height", "autodesk.spec.aec:length-2.0.0"),
            "Auto (Revit spec)",
            "Metric",
            null);

        Assert.False(result.Success);
        Assert.Contains("but Revit parameter 'Height' is length", result.Message);
    }

    [Fact]
    public void RejectsHeaderUnitWithWrongPhysicalDimension()
    {
        var result = ImportUnitNormalizer.Normalize(
            "10",
            "Tree Height (kg)",
            Descriptor("Height", "autodesk.spec.aec:length-2.0.0"),
            "Auto (Revit spec)",
            "Metric",
            null);

        Assert.False(result.Success);
        Assert.Contains("but Revit parameter 'Height' is length", result.Message);
    }

    [Fact]
    public void PlanBuilderUsesSharedDetectionForImportedRecords()
    {
        var fields = new Dictionary<string, JsonElement>(StringComparer.OrdinalIgnoreCase)
        {
            ["Types"] = JsonSerializer.SerializeToElement("Oak"),
            ["Tree Height"] = JsonSerializer.SerializeToElement("10"),
            ["Unit System"] = JsonSerializer.SerializeToElement("Metric")
        };
        var records = new[] { new AirtableRecord("row-1", fields) };
        var scan = new ModelScanResult(
            "Test",
            [new ModelScanItem("Planting", "Oak", 42, 1, 0, null)]);
        var mappings = new[]
        {
            new ParameterMappingDefinition(
                "Tree Height",
                "Height",
                "Type",
                "Auto (Revit spec)")
        };

        var plan = ParameterSyncPlanBuilder.Build(
            records,
            scan,
            mappings,
            new ModelScanOptions(),
            [Descriptor("Height", "autodesk.spec.aec:length-2.0.0")],
            "Imperial");

        var write = Assert.Single(plan.Batch.Items);
        Assert.Equal(10d, ReadNumber(write.SourceValue), 10);
        Assert.Contains("record unit system (Metric)", write.UnitMessage);
        Assert.Single(plan.UnitAdjustments);
        Assert.Empty(plan.Issues);
    }

    private static RevitParameterDescriptor Descriptor(string name, string dataTypeId) =>
        new(name, "Type", "Double", dataTypeId, true, ["Planting"]);

    private static double ReadNumber(string value) =>
        double.Parse(value, NumberStyles.Float, CultureInfo.InvariantCulture);
}
