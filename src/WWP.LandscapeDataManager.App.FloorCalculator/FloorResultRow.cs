using System.Globalization;

namespace WWP.LandscapeDataManager.App.FloorCalculator;

/// <summary>
/// One floor's benefits from its match, for the Results step: what Calculate writes (the sheet's
/// coefficient times the area, or a value reviewed by hand). "—" while the floor has no match.
/// </summary>
public sealed class FloorResultRow
{
    public FloorResultRow(FloorAssignmentRow row, string landscapeType, FloorBenefits? benefits)
    {
        DisplayName = row.DisplayName;
        AreaText = row.AreaDisplay;
        LandscapeType = landscapeType;
        Status = row.MatchStatus;
        Co2Text = Format(benefits?.Co2Kg);
        RunoffText = Format(benefits?.RunoffM3);
        PollutantsText = Format(benefits?.PollutantsKg);
        OxygenText = Format(benefits?.OxygenKg);
        CostText = Format(benefits?.CostSaved);
        GwpText = Format(benefits?.GwpKg);
    }

    public string DisplayName { get; }
    public string AreaText { get; }
    public string LandscapeType { get; }
    public string Status { get; }
    public string Co2Text { get; }
    public string RunoffText { get; }
    public string PollutantsText { get; }
    public string OxygenText { get; }
    public string CostText { get; }
    public string GwpText { get; }

    /// <summary>Whole numbers once they're big enough to read that way; one decimal below 100.</summary>
    public static string Format(double? value) => value switch
    {
        null => "—",
        { } number when Math.Abs(number) >= 100 => number.ToString("N0", CultureInfo.CurrentCulture),
        { } number => number.ToString("N1", CultureInfo.CurrentCulture)
    };
}

/// <summary>A floor's yearly benefits and embodied carbon, in kg, m³ and the project currency.</summary>
public sealed record FloorBenefits(double Co2Kg, double RunoffM3, double PollutantsKg, double OxygenKg, double CostSaved, double GwpKg);
