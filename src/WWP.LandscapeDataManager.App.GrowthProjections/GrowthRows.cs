using System.ComponentModel;
using System.Runtime.CompilerServices;
using WWP.LandscapeDataManager.Contracts;

namespace WWP.LandscapeDataManager.App.GrowthProjections;

/// <summary>One "Growth year N" row the user can edit or remove.</summary>
public sealed class GrowthYearRow : INotifyPropertyChanged
{
    private double _year;

    public GrowthYearRow(int year) => _year = year;

    public event PropertyChangedEventHandler? PropertyChanged;

    /// <summary>NumberBox works in doubles; <see cref="Year"/> is the whole-number value used.</summary>
    public double Value
    {
        get => _year;
        set
        {
            if (!double.IsNaN(value) && value != _year)
            {
                _year = Math.Clamp(Math.Round(value), 1, GrowthYearNames.MaximumYear);
                OnPropertyChanged();
                OnPropertyChanged(nameof(WorksetName));
            }
        }
    }

    public int Year => (int)_year;

    public string WorksetName => GrowthYearNames.WorksetName(Year);

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
}

/// <summary>A tickable source of original trees: the main model or one design option.</summary>
public sealed class DesignOptionRow(GrowthDesignOptionChoice choice)
{
    public string Key { get; } = choice.Key;

    public bool IsChecked { get; set; }

    public string Label { get; } =
        $"{choice.Label} — {choice.OriginalTreeCount:N0} tree(s){(choice.IsActive ? " · active option" : string.Empty)}";
}

/// <summary>One growth year's i-Tree state: how many of its trees are calculated, waiting, or need inputs.</summary>
public sealed class YearCalculationRow(int year, int trees, int calculated, int toCalculate, int needsAttention)
{
    public int Year { get; } = year;
    public string YearText { get; } = year == 0 ? "0 (Baseline)" : year.ToString();
    public string TreesText { get; } = trees.ToString("N0");
    public string CalculatedText { get; } = calculated.ToString("N0");
    public string ToCalculateText { get; } = toCalculate.ToString("N0");
    public string NeedsAttentionText { get; } = needsAttention.ToString("N0");
}

/// <summary>An existing growth-year workset, for the status table.</summary>
public sealed class WorksetRow(GrowthYearWorkset workset)
{
    public string YearText { get; } = workset.Year == 0 ? "0 (Baseline)" : workset.Year.ToString();
    public string WorksetName { get; } = workset.WorksetName;
    public string TreeCountText { get; } = workset.TreeCount.ToString("N0");
    public string LinkedText { get; } = workset.Year == 0 ? "originals" : $"{workset.LinkedCopyCount:N0} linked";
}

/// <summary>One planting type's trunk growth rate from i-Tree, for the trunk growth table.</summary>
public sealed class TrunkGrowthRow(Shared.Services.TypeTrunkGrowth type)
{
    public Shared.Services.TypeTrunkGrowth Type { get; } = type;
    public string TypeName { get; } = type.TypeName;
    public string FamilyName { get; } = type.FamilyName;
    public string SpeciesCode { get; } = type.SpeciesCode;
    public string TreesText { get; } = type.TreeCount.ToString("N0");
    public string InputsText { get; } = type.InputsText;
    public string RateText { get; } = type.RateCmPerYear is { } rate ? rate.ToString("0.00") : "—";
    public string Note { get; } = type.Error ?? (type.TreeCount == 0 ? "No trees placed: default inputs" : string.Empty);
}
