using System.Globalization;
using System.Text.RegularExpressions;
using WWP.LandscapeDataManager.Contracts;

namespace WWP.LandscapeDataManager.Shared.Services;

public static class ImportUnitNormalizer
{
    private const string AutoConversion = "Auto (Revit spec)";

    private static readonly Regex NumberWithOptionalUnit = new(
        @"^\s*(?<number>[+-]?(?:(?:\d{1,3}(?:,\d{3})+)|\d+)(?:\.\d+)?(?:[eE][+-]?\d+)?)\s*(?<unit>.*?)\s*$",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private static readonly IReadOnlyList<UnitDefinition> Units =
    [
        new("mm", UnitDimension.Length, 0.001d, ["mm", "millimeter", "millimeters", "millimetre", "millimetres"]),
        new("cm", UnitDimension.Length, 0.01d, ["cm", "centimeter", "centimeters", "centimetre", "centimetres"]),
        new("m", UnitDimension.Length, 1d, ["m", "meter", "meters", "metre", "metres"]),
        new("km", UnitDimension.Length, 1000d, ["km", "kilometer", "kilometers", "kilometre", "kilometres"]),
        new("in", UnitDimension.Length, 0.0254d, ["in", "inch", "inches", "\""]),
        new("ft", UnitDimension.Length, 0.3048d, ["ft", "foot", "feet", "'"]),

        new("mm2", UnitDimension.Area, 0.000001d, ["mm2", "mm²", "sq mm", "square millimeters", "square millimetres"]),
        new("cm2", UnitDimension.Area, 0.0001d, ["cm2", "cm²", "sq cm", "square centimeters", "square centimetres"]),
        new("m2", UnitDimension.Area, 1d, ["m2", "m²", "sqm", "sq m", "square meters", "square metres"]),
        new("in2", UnitDimension.Area, 0.00064516d, ["in2", "in²", "sq in", "square inches"]),
        new("ft2", UnitDimension.Area, 0.09290304d, ["ft2", "ft²", "sqft", "sq ft", "square feet"]),

        new("mm3", UnitDimension.Volume, 0.000000001d, ["mm3", "mm³", "cubic millimeters", "cubic millimetres"]),
        new("cm3", UnitDimension.Volume, 0.000001d, ["cm3", "cm³", "cc", "cubic centimeters", "cubic centimetres"]),
        new("m3", UnitDimension.Volume, 1d, ["m3", "m³", "cum", "cu m", "cubic meters", "cubic metres"]),
        new("L", UnitDimension.Volume, 0.001d, ["l", "liter", "liters", "litre", "litres"]),
        new("mL", UnitDimension.Volume, 0.000001d, ["ml", "milliliter", "milliliters", "millilitre", "millilitres"]),
        new("in3", UnitDimension.Volume, 0.000016387064d, ["in3", "in³", "cu in", "cubic inches"]),
        new("ft3", UnitDimension.Volume, 0.028316846592d, ["ft3", "ft³", "cuft", "cu ft", "cubic feet"]),
        new("US gal", UnitDimension.Volume, 0.003785411784d, ["gal", "gallon", "gallons", "us gal", "us gallon", "us gallons"]),
        new("Imp gal", UnitDimension.Volume, 0.00454609d, ["imp gal", "imperial gallon", "imperial gallons"]),

        new("mg", UnitDimension.Mass, 0.000001d, ["mg", "milligram", "milligrams"]),
        new("g", UnitDimension.Mass, 0.001d, ["g", "gram", "grams"]),
        new("kg", UnitDimension.Mass, 1d, ["kg", "kilogram", "kilograms"]),
        new("t", UnitDimension.Mass, 1000d, ["t", "tonne", "tonnes", "metric ton", "metric tons"]),
        new("oz", UnitDimension.Mass, 0.028349523125d, ["oz", "ounce", "ounces"]),
        new("lb", UnitDimension.Mass, 0.45359237d, ["lb", "lbs", "pound", "pounds"])
    ];

    private static readonly IReadOnlyDictionary<string, UnitDefinition> ExplicitAliases =
        Units.SelectMany(unit => unit.Aliases.Select(alias => new KeyValuePair<string, UnitDefinition>(
                NormalizeUnitText(alias),
                unit)))
            .GroupBy(pair => pair.Key, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(
                group => group.Key,
                group => group.First().Value,
                StringComparer.OrdinalIgnoreCase);

    public static ImportUnitNormalizationResult Normalize(
        string sourceValue,
        string sourceField,
        RevitParameterDescriptor target,
        string conversion,
        string preferredUnitSystem,
        string? recordUnitSystem)
    {
        if (!string.Equals(conversion, AutoConversion, StringComparison.OrdinalIgnoreCase) ||
            !string.Equals(target.StorageType, "Double", StringComparison.OrdinalIgnoreCase))
        {
            return ImportUnitNormalizationResult.Unchanged(sourceValue);
        }

        var targetDimension = GetTargetDimension(target.DataTypeId);
        if (!TryParseNumber(sourceValue, out var number, out var explicitUnitText))
        {
            return ImportUnitNormalizationResult.Unchanged(sourceValue);
        }

        UnitDefinition? explicitUnit = null;
        if (!string.IsNullOrWhiteSpace(explicitUnitText))
        {
            explicitUnit = ResolveExplicitUnit(explicitUnitText);
            if (explicitUnit is null && targetDimension is not null)
            {
                return ImportUnitNormalizationResult.Invalid(
                    $"Unit '{explicitUnitText}' in source value '{sourceValue}' is not supported for " +
                    $"{targetDimension.Value.ToString().ToLowerInvariant()} parameter '{target.Name}'.");
            }
        }

        var headerUnits = DetectHeaderUnits(
            sourceField,
            ignoreCompoundHeader: targetDimension is null);
        if (headerUnits.Count > 1 && explicitUnit is null)
        {
            return ImportUnitNormalizationResult.Invalid(
                $"Source column '{sourceField}' contains more than one possible unit " +
                $"({string.Join(", ", headerUnits.Select(unit => unit.Symbol))}). " +
                "Put a single unit in the header or include the unit in each cell.");
        }

        var headerUnit = headerUnits.Count == 1 ? headerUnits[0] : null;
        var detectedUnit = explicitUnit ?? headerUnit;
        var detectionSource = explicitUnit is not null
            ? "cell value"
            : headerUnit is not null ? "column header" : null;

        if (detectedUnit is not null &&
            targetDimension is not null &&
            detectedUnit.Dimension != targetDimension)
        {
            return ImportUnitNormalizationResult.Invalid(
                $"Detected {detectedUnit.Symbol} ({detectedUnit.Dimension.ToString().ToLowerInvariant()}) " +
                $"in {detectionSource}, but Revit parameter '{target.Name}' is {targetDimension.Value.ToString().ToLowerInvariant()}.");
        }

        if (detectedUnit is null && targetDimension is not null)
        {
            var fallbackSystem = NormalizeSystem(recordUnitSystem) ?? NormalizeSystem(preferredUnitSystem) ?? "Metric";
            detectedUnit = GetFallbackUnit(targetDimension.Value, fallbackSystem, nativeRevitSpec: true);
            detectionSource = recordUnitSystem is null
                ? $"Project Information preference ({fallbackSystem})"
                : $"record unit system ({fallbackSystem})";
        }

        if (detectedUnit is null)
        {
            return ImportUnitNormalizationResult.Unchanged(sourceValue);
        }

        var outputUnit = targetDimension is not null
            ? GetSiUnit(targetDimension.Value)
            : GetFallbackUnit(
                detectedUnit.Dimension,
                NormalizeSystem(preferredUnitSystem) ?? "Metric",
                nativeRevitSpec: false);
        var normalizedValue = number * detectedUnit.ToSiFactor / outputUnit.ToSiFactor;
        var conflict = explicitUnit is not null &&
                       headerUnit is not null &&
                       !string.Equals(explicitUnit.Symbol, headerUnit.Symbol, StringComparison.Ordinal);
        var message =
            $"Detected {detectedUnit.Symbol} from {detectionSource}; normalized {Format(number)} {detectedUnit.Symbol} " +
            $"to {Format(normalizedValue)} {outputUnit.Symbol} for {target.Name}." +
            (conflict
                ? $" The cell unit overrides the conflicting header unit {headerUnit!.Symbol}."
                : string.Empty);

        return ImportUnitNormalizationResult.Normalized(
            normalizedValue.ToString("G17", CultureInfo.InvariantCulture),
            message);
    }

    private static bool TryParseNumber(
        string sourceValue,
        out double number,
        out string explicitUnitText)
    {
        var match = NumberWithOptionalUnit.Match(sourceValue);
        if (!match.Success)
        {
            number = 0;
            explicitUnitText = string.Empty;
            return false;
        }

        explicitUnitText = match.Groups["unit"].Value.Trim();
        var numberText = match.Groups["number"].Value;
        return double.TryParse(
            numberText,
            NumberStyles.Float | NumberStyles.AllowThousands,
            CultureInfo.InvariantCulture,
            out number);
    }

    private static UnitDefinition? ResolveExplicitUnit(string value)
    {
        var normalized = NormalizeUnitText(value);
        return ExplicitAliases.TryGetValue(normalized, out var unit) ? unit : null;
    }

    private static IReadOnlyList<UnitDefinition> DetectHeaderUnits(
        string sourceField,
        bool ignoreCompoundHeader)
    {
        if (sourceField.Contains('/') && ignoreCompoundHeader)
        {
            return [];
        }

        var found = new List<UnitDefinition>();
        foreach (var unit in Units)
        {
            if (unit.Aliases.Any(alias => HeaderContainsAlias(sourceField, alias)))
            {
                found.Add(unit);
            }
        }

        return found
            .GroupBy(unit => unit.Symbol, StringComparer.Ordinal)
            .Select(group => group.First())
            .ToList();
    }

    private static bool HeaderContainsAlias(string header, string alias)
    {
        var normalizedHeader = header
            .Replace('²', '2')
            .Replace('³', '3');
        var normalizedAlias = alias
            .Replace('²', '2')
            .Replace('³', '3');
        if (normalizedAlias is "\"" or "'")
        {
            return normalizedHeader.Contains(normalizedAlias, StringComparison.Ordinal);
        }

        var pattern =
            $@"(?<![A-Za-z0-9]){Regex.Escape(normalizedAlias).Replace(@"\ ", @"[\s_-]+")}(?![A-Za-z0-9])";
        return Regex.IsMatch(
            normalizedHeader,
            pattern,
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
    }

    private static UnitDimension? GetTargetDimension(string dataTypeId)
    {
        if (dataTypeId.Contains(":length-", StringComparison.OrdinalIgnoreCase))
        {
            return UnitDimension.Length;
        }

        if (dataTypeId.Contains(":area-", StringComparison.OrdinalIgnoreCase))
        {
            return UnitDimension.Area;
        }

        if (dataTypeId.Contains(":volume-", StringComparison.OrdinalIgnoreCase))
        {
            return UnitDimension.Volume;
        }

        return null;
    }

    private static UnitDefinition GetSiUnit(UnitDimension dimension) => dimension switch
    {
        UnitDimension.Length => FindUnit("m"),
        UnitDimension.Area => FindUnit("m2"),
        UnitDimension.Volume => FindUnit("m3"),
        UnitDimension.Mass => FindUnit("kg"),
        _ => throw new ArgumentOutOfRangeException(nameof(dimension), dimension, null)
    };

    private static UnitDefinition GetFallbackUnit(
        UnitDimension dimension,
        string system,
        bool nativeRevitSpec)
    {
        if (string.Equals(system, "Imperial", StringComparison.OrdinalIgnoreCase))
        {
            return dimension switch
            {
                UnitDimension.Length => FindUnit("ft"),
                UnitDimension.Area => FindUnit("ft2"),
                UnitDimension.Volume => nativeRevitSpec ? FindUnit("ft3") : FindUnit("US gal"),
                UnitDimension.Mass => FindUnit("lb"),
                _ => throw new ArgumentOutOfRangeException(nameof(dimension), dimension, null)
            };
        }

        return GetSiUnit(dimension);
    }

    private static UnitDefinition FindUnit(string symbol) =>
        Units.First(unit => string.Equals(unit.Symbol, symbol, StringComparison.Ordinal));

    private static string? NormalizeSystem(string? value)
    {
        if (string.Equals(value, "Metric", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(value, "SI", StringComparison.OrdinalIgnoreCase))
        {
            return "Metric";
        }

        if (string.Equals(value, "Imperial", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(value, "US", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(value, "US customary", StringComparison.OrdinalIgnoreCase))
        {
            return "Imperial";
        }

        return null;
    }

    private static string NormalizeUnitText(string value) =>
        value.Trim()
            .Trim('(', ')', '[', ']', '{', '}', '.', ',')
            .Replace('²', '2')
            .Replace('³', '3')
            .Replace("_", " ", StringComparison.Ordinal)
            .Replace("-", " ", StringComparison.Ordinal)
            .Replace("  ", " ", StringComparison.Ordinal)
            .ToLowerInvariant();

    private static string Format(double value) =>
        value.ToString("G8", CultureInfo.InvariantCulture);

    private enum UnitDimension
    {
        Length,
        Area,
        Volume,
        Mass
    }

    private sealed record UnitDefinition(
        string Symbol,
        UnitDimension Dimension,
        double ToSiFactor,
        IReadOnlyList<string> Aliases);
}

public sealed record ImportUnitNormalizationResult(
    bool Success,
    string Value,
    string? Message)
{
    public static ImportUnitNormalizationResult Unchanged(string value) =>
        new(true, value, null);

    public static ImportUnitNormalizationResult Normalized(string value, string message) =>
        new(true, value, message);

    public static ImportUnitNormalizationResult Invalid(string message) =>
        new(false, string.Empty, message);
}
