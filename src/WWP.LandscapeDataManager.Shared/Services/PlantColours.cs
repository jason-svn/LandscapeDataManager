using System.Text.RegularExpressions;
using WWP.LandscapeDataManager.Contracts;

namespace WWP.LandscapeDataManager.Shared.Services;

/// <summary>A colour as the sheet writes it ("Creamy white #F3EFD9"): its name and "#RRGGBB".</summary>
public sealed record PlantColour(string Name, string Hex);

/// <summary>
/// Reads a species' year in colour (<see cref="PlantAppearance"/>): the "Name #RRGGBB" colours, its
/// fruit months, and which season a month is in — spring March to May, summer June to August,
/// autumn September to November, winter December to February, as the sheet's columns mean them.
/// </summary>
public static partial class PlantColours
{
    public static readonly IReadOnlyList<string> SeasonNames = ["Spring", "Summer", "Autumn", "Winter"];

    /// <summary>The colour in "Name #RRGGBB" (a bare "#RRGGBB" is named by its hex); null without a hex.</summary>
    public static PlantColour? Parse(string? text)
    {
        if (string.IsNullOrWhiteSpace(text) || HexPattern().Match(text) is not { Success: true } match)
        {
            return null;
        }

        var name = text[..match.Index].Trim().TrimEnd('(', ',', '-', '–').Trim();
        var hex = match.Value.ToUpperInvariant();
        return new PlantColour(name.Length > 0 ? name : hex, hex);
    }

    /// <summary>0 spring, 1 summer, 2 autumn, 3 winter, for a month 1-12.</summary>
    public static int SeasonOf(int month) => month switch
    {
        3 or 4 or 5 => 0,
        6 or 7 or 8 => 1,
        9 or 10 or 11 => 2,
        _ => 3
    };

    /// <summary>The species' colour in the season <paramref name="month"/> falls in.</summary>
    public static PlantColour? SeasonColour(PlantAppearance? appearance, int month) => appearance is null
        ? null
        : Parse(SeasonOf(month) switch
        {
            0 => appearance.SpringColour,
            1 => appearance.SummerColour,
            2 => appearance.AutumnColour,
            _ => appearance.WinterColour
        });

    /// <summary>The months it carries ripe fruit or seed.</summary>
    public static IReadOnlyList<int> FruitMonths(PlantAppearance? appearance) => Months(appearance?.FruitMonths);

    /// <summary>A sheet month list ("4,5,9"), months 1-12 only.</summary>
    public static IReadOnlyList<int> Months(string? text) => DashboardAggregationService.ParseMonthList(text);

    /// <summary>Its kind of fruit, or null when it has none ("None" or blank).</summary>
    public static string? FruitType(PlantAppearance? appearance) =>
        appearance?.FruitType is { } type && !string.IsNullOrWhiteSpace(type) && !type.Trim().Equals("None", StringComparison.OrdinalIgnoreCase)
            ? type.Trim()
            : null;

    [GeneratedRegex("#[0-9A-Fa-f]{6}\\b")]
    private static partial Regex HexPattern();
}
