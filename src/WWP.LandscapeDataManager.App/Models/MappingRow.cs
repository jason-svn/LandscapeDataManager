namespace WWP.LandscapeDataManager.App.Models;

public sealed class MappingRow
{
    public MappingRow(
        IReadOnlyList<string> sourceOptions,
        IReadOnlyList<ParameterOption> targetOptions)
    {
        SourceOptions = sourceOptions;
        TargetOptions = targetOptions;
    }

    public IReadOnlyList<string> SourceOptions { get; }
    public IReadOnlyList<ParameterOption> TargetOptions { get; }
    public IReadOnlyList<string> ConversionOptions { get; } =
    [
        "Auto (Revit spec)",
        "Text",
        "Number (no conversion)",
        "Metres → Revit length",
        "Square metres → Revit area"
    ];

    public bool Enabled { get; set; } = true;
    public string? SelectedAirtableField { get; set; }
    public ParameterOption? SelectedTarget { get; set; }
    public string SelectedConversion { get; set; } = "Auto (Revit spec)";
}
