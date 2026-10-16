namespace WWP.LandscapeDataManager.Shared.Services;

/// <summary>
/// Embodied carbon (GWP — kg CO2e emitted once, to make and deliver a material) from the WWP
/// landscape data sheet, kept apart from sequestration (CO2 taken up every year by planting).
/// The sheet's <c>WWP_Total_GWP</c> holds product + transport for hard surfaces, but for trees and
/// vegetation it's the negative of their yearly sequestration — a different quantity on a different
/// time basis, already counted as sequestration elsewhere — so it never counts as embodied carbon.
/// </summary>
public static class EmbodiedCarbon
{
    /// <summary>
    /// Embodied GWP rate (kg CO2e per m², or per tree): product + transport when the sheet gives
    /// either, else its total — and never below 0, since a negative "GWP" is sequestration.
    /// </summary>
    public static double Rate(double? productGwp, double? transportGwp, double? totalGwp)
    {
        var rate = productGwp is not null || transportGwp is not null
            ? (productGwp ?? 0) + (transportGwp ?? 0)
            : totalGwp ?? 0;
        return Math.Max(0, rate);
    }

    /// <summary>
    /// Years of the scheme's yearly CO2e sequestration needed to offset its embodied carbon (both in
    /// the same mass unit); null when nothing is sequestered. 0 when there's no embodied carbon.
    /// </summary>
    public static double? PaybackYears(double embodied, double sequesteredPerYear)
    {
        if (embodied <= 0)
        {
            return 0;
        }

        return sequesteredPerYear > 0 ? embodied / sequesteredPerYear : null;
    }
}
