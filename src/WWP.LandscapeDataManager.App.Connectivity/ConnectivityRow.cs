using Microsoft.UI;
using Microsoft.UI.Xaml.Media;
using Windows.UI;
using WWP.LandscapeDataManager.Contracts;
using WWP.LandscapeDataManager.Shared.Services;

namespace WWP.LandscapeDataManager.App.Connectivity;

/// <summary>One scanned tree or floor: its connectivity result, or why it isn't a patch.</summary>
public sealed class ConnectivityRow
{
    public ConnectivityRow(ConnectivityPatchItem item, ConnectivityPatchResult? result, string? exclusionReason)
    {
        Item = item;
        Result = result;
        ExclusionReason = exclusionReason;
    }

    public ConnectivityPatchItem Item { get; }
    public ConnectivityPatchResult? Result { get; }
    public string? ExclusionReason { get; }

    public string UniqueId => Item.UniqueId;
    public string Category => Item.Category == "Floor" ? "Planting area" : "Tree";
    public string DisplayName => $"{Item.FamilyName} : {Item.TypeName}";
    public string AreaText => $"{Item.AreaSquareMeters:N1}";
    public string DpcText => Result is null ? "—" : $"{Result.MeanDpcPercent:N2}%";
    public string ScoreText => Result is null ? "—" : $"{Result.Score:N2}";

    public string BandText => Result is null
        ? "Excluded"
        : $"{Result.Band} · {ConnectivityCalculator.BandName(Result.Band)}";

    public SolidColorBrush BandBrush => new(Result?.Band switch
    {
        ConnectivityCalculator.CriticalBand => Color.FromArgb(255, 197, 27, 125),
        ConnectivityCalculator.StrongBand => Color.FromArgb(255, 49, 140, 74),
        ConnectivityCalculator.ModerateBand => Color.FromArgb(255, 110, 160, 60),
        ConnectivityCalculator.LowBand => Colors.Gray,
        _ => Colors.DarkGray
    });

    public string Notes
    {
        get
        {
            var notes = new List<string>();
            if (ExclusionReason is not null)
            {
                notes.Add(ExclusionReason);
            }

            if (Result?.Uncertain == true)
            {
                notes.Add("Uncertain — band changes by two or more between distances.");
            }

            if (Item.Locked)
            {
                notes.Add(Item.StoredBand is { } band
                    ? $"Locked — keeps its hand-set band {band}."
                    : "Locked — results won't be written.");
            }

            if (!Item.ParametersBound)
            {
                notes.Add("Connectivity parameters not set up.");
            }

            if (Item.Note is not null)
            {
                notes.Add(Item.Note);
            }

            return string.Join(" ", notes);
        }
    }

    /// <summary>The view-colour status key the Revit connector maps to a band colour.</summary>
    public string? ColourStatus => Result?.Band switch
    {
        ConnectivityCalculator.CriticalBand => "ConnectivityCritical",
        ConnectivityCalculator.StrongBand => "ConnectivityStrong",
        ConnectivityCalculator.ModerateBand => "ConnectivityModerate",
        ConnectivityCalculator.LowBand => "ConnectivityLow",
        _ => null
    };

    /// <summary>Sort key: patches by score high to low, excluded elements last.</summary>
    public double SortKey => Result?.Score ?? -1;
}
