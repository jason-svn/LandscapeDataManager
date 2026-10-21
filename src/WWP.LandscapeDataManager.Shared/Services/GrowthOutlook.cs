namespace WWP.LandscapeDataManager.Shared.Services;

/// <summary>
/// Accumulates a yearly benefit over an outlook from the growth years that were actually modelled.
/// Each growth-year workset's copies give the benefit in that year (i-Tree for trees); the years in
/// between are interpolated linearly, so the total is the area under that curve (trapezoid rule):
/// stops 0, 5, 10 → (v0 + v5) / 2 × 5 + (v5 + v10) / 2 × 5. Before the first stop the first value
/// holds, and after the last stop the last value holds (the tree has stopped growing).
/// A single stop gives value × years, the old annual × years shortcut.
/// </summary>
public static class GrowthOutlook
{
    public static double Accumulate(IEnumerable<(int Year, double Value)> stops, int years)
    {
        var points = stops.OrderBy(stop => stop.Year).ToList();
        if (points.Count == 0 || years <= 0)
        {
            return 0d;
        }

        var total = 0d;
        var previousYear = 0;
        var previousValue = points[0].Value;
        foreach (var (year, value) in points)
        {
            if (year <= previousYear)
            {
                previousValue = value;
                continue;
            }

            if (year >= years)
            {
                // The outlook ends inside this span: interpolate the value at the outlook year.
                var valueAtEnd = previousValue + (value - previousValue) * (years - previousYear) / (year - previousYear);
                return total + (previousValue + valueAtEnd) / 2d * (years - previousYear);
            }

            total += (previousValue + value) / 2d * (year - previousYear);
            previousYear = year;
            previousValue = value;
        }

        return total + previousValue * (years - previousYear);
    }
}
