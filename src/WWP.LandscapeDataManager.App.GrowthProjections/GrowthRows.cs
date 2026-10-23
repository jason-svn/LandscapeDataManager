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

/// <summary>
/// An existing growth-year workset, for the status table. The originals are wherever they're drawn
/// (existing trees at year 0, new ones at the design year): every tree on a workset that isn't a
/// linked copy.
/// </summary>
public sealed class WorksetRow(GrowthYearWorkset workset, int designYear)
{
    public string YearText { get; } = GrowthYearLabel.Of(workset.Year, designYear);
    public string WorksetName { get; } = workset.WorksetName;
    public string TreeCountText { get; } = workset.TreeCount.ToString("N0");
    public string LinkedText { get; } = Describe(workset.LinkedCopyCount, Math.Max(0, workset.TreeCount - workset.LinkedCopyCount));

    private static string Describe(int linked, int originals) => (linked, originals) switch
    {
        ( > 0, > 0) => $"{linked:N0} linked, {originals:N0} originals",
        ( > 0, _) => $"{linked:N0} linked",
        (_, > 0) => $"{originals:N0} originals",
        _ => "—"
    };
}

/// <summary>One design option's growth projection, for the by-design-option table.</summary>
public sealed class OptionProgressRow(GrowthOptionProgressRow row)
{
    public string OptionText { get; } = row.OptionLabel + (row.IsActive ? " (active)" : string.Empty);
    public string OriginalsText { get; } = row.Originals.ToString("N0");
    public string CopiesText { get; } = row.CopiesNeeded == 0 ? "—" : $"{row.CopiesMade:N0} of {row.CopiesNeeded:N0}";
    public string MissingYearsText { get; } = row.MissingYears.Count == 0 ? "—" : string.Join(", ", row.MissingYears);
    public string ToCalculateText { get; } = row.ToCalculate.ToString("N0");
    public string NeedsInputsText { get; } = row.NeedsInputs.ToString("N0");
    public string ToDo { get; } = row.ToDo;
}

/// <summary>A growth year as the tables show it: year 0 is the project start, and the design year is where new trees are drawn.</summary>
public static class GrowthYearLabel
{
    public static string Of(int year, int designYear) =>
        year == 0 ? "0 (project start)" : year == designYear ? $"{year} (design year)" : year.ToString();
}

/// <summary>
/// One planting type's growth rates from i-Tree, for the growth rates table, with whether its Revit
/// type and its sheet row already have them (— until checked).
/// </summary>
public sealed class GrowthRateRow(Shared.Services.TypeGrowthRates type, Shared.Services.RateStatus? typeStatus, Shared.Services.RateStatus? sheetStatus)
{
    public string TypeStatusText { get; } = typeStatus?.Status ?? "—";
    public string? TypeChanges { get; } = typeStatus?.Changes;
    public string SheetStatusText { get; } = sheetStatus?.Status ?? "—";
    public string? SheetChanges { get; } = sheetStatus?.Changes;

    public Shared.Services.TypeGrowthRates Type { get; } = type;
    public string TypeName { get; } = type.TypeName;
    public string FamilyName { get; } = type.FamilyName;
    public string SpeciesCode { get; } = type.SpeciesCode;
    public string TreesText { get; } = type.TreeCount.ToString("N0");
    public string InputsText { get; } = type.InputsText;
    public string TrunkText { get; } = Rate(type.TrunkCmPerYear);
    public string HeightText { get; } = Rate(type.HeightMPerYear);
    public string WidthText { get; } = Rate(type.WidthMPerYear);
    public string Note { get; } = type.Error ?? type.Note ?? (type.TreeCount == 0 ? "No trees placed: default inputs" : string.Empty);

    private static string Rate(double? value) => value is { } rate ? rate.ToString("0.00") : "—";
}

/// <summary>Trees on one workset that belong on another growth year's, for the sort table.</summary>
public sealed class SortMoveRow(string from, string to, int trees, bool isNewWorkset)
{
    public string FromText { get; } = string.IsNullOrEmpty(from) ? "(not workshared)" : from;
    public string ToText { get; } = to;
    public int Trees { get; } = trees;
    public string TreesText { get; } = trees.ToString("N0");
    public bool IsNewWorkset { get; } = isNewWorkset;
    public string Note { get; } = isNewWorkset ? "New workset" : string.Empty;
}

/// <summary>One place where trees overlap: the one kept and the ones offered for deletion.</summary>
public sealed class OverlapRow(TreeOverlapGroup group)
{
    public string WorksetName { get; } = string.IsNullOrEmpty(group.WorksetName) ? "—" : group.WorksetName;
    public string OptionLabel { get; } = group.OptionLabel;
    public string TypeLabel { get; } = string.Join(", ", group.Extras.Prepend(group.Keep).Select(tree => tree.TypeLabel).Distinct());
    public string TreesText { get; } = (group.Extras.Count + 1).ToString("N0");
    public string KeepText { get; } = group.Keep.ElementId + (group.Keep.IsSourceOfCopies ? " (original)" : group.Keep.LinkedYear is not null ? " (copy)" : string.Empty);
    public string DeleteText { get; } = string.Join(", ", group.Extras.Select(tree => tree.ElementId));
}
