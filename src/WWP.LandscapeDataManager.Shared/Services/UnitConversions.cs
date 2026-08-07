namespace WWP.LandscapeDataManager.Shared.Services;

/// <summary>Conversion constants shared by every place that turns i-Tree's US-unit API results into the project's preferred unit system — kept in one place so the mapper and the dashboard's cross-instance aggregation can never drift apart.</summary>
public static class UnitConversions
{
    public const double KilogramsPerPound = 0.45359237d;
    public const double KilogramsPerOunce = 0.028349523125d;
    public const double CubicMetresPerGallon = 0.00378541d;
}
