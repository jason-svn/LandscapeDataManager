using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace WWP.LandscapeDataManager.Shared.Models;

public sealed class MappingRow : INotifyPropertyChanged
{
    private bool _enabled;
    private ParameterOption? _selectedTarget;
    private string _selectedConversion = "Auto (Revit spec)";

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

    public event PropertyChangedEventHandler? PropertyChanged;

    public bool Enabled
    {
        get => _enabled;
        set
        {
            if (_enabled == value)
            {
                return;
            }

            _enabled = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(MappingStatus));
        }
    }

    public string? SelectedAirtableField { get; set; }

    public ParameterOption? SelectedTarget
    {
        get => _selectedTarget;
        set
        {
            if (ReferenceEquals(_selectedTarget, value))
            {
                return;
            }

            _selectedTarget = value;
            if (value is not null)
            {
                _enabled = true;
                OnPropertyChanged(nameof(Enabled));
            }

            OnPropertyChanged();
            OnPropertyChanged(nameof(MappingStatus));
        }
    }

    public string SelectedConversion
    {
        get => _selectedConversion;
        set
        {
            if (_selectedConversion == value)
            {
                return;
            }

            _selectedConversion = value;
            OnPropertyChanged();
        }
    }

    public string MappingStatus => SelectedTarget is null
        ? "Needs mapping"
        : Enabled ? "Ready" : "Ignored";

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
}
