using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace WWP.LandscapeDataManager.App.FloorCalculator;

/// <summary>
/// One benefit metric for a Floor row: a computed default (the matched coefficient times area,
/// or as-is for the two intrinsic temperature metrics) that can be manually edited in the
/// "Review values" dialog before Calculate writes it. There's no separate Revit override
/// parameter — checking "Manual" here and writing a different number is the entire mechanism.
/// </summary>
public sealed class FloorMetricEntry(string label, string unit) : INotifyPropertyChanged
{
    private double _computedValue;
    private bool _isManual;
    private double _manualValue;

    public string Label { get; } = label;
    public string Unit { get; } = unit;

    public double ComputedValue
    {
        get => _computedValue;
        set
        {
            if (_computedValue != value)
            {
                _computedValue = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(FinalValue));
                OnPropertyChanged(nameof(ComputedDisplay));
            }
        }
    }

    public bool IsManual
    {
        get => _isManual;
        set
        {
            if (_isManual != value)
            {
                _isManual = value;
                if (value && ManualValue == 0)
                {
                    ManualValue = ComputedValue;
                }

                OnPropertyChanged();
                OnPropertyChanged(nameof(FinalValue));
            }
        }
    }

    public double ManualValue
    {
        get => _manualValue;
        set
        {
            if (_manualValue != value)
            {
                _manualValue = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(FinalValue));
            }
        }
    }

    public double FinalValue => IsManual ? ManualValue : ComputedValue;

    public string ComputedDisplay => $"{ComputedValue:N2} {Unit}";

    public event PropertyChangedEventHandler? PropertyChanged;

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
}
