using System.Globalization;
using Autodesk.Revit.DB;

namespace WWP.LandscapeDataManager.Revit.Services;

/// <summary>
/// Shared numeric/unit conversion and formatting logic for writing a source value into a Revit
/// parameter, used by both the type-scoped <see cref="RevitParameterWriter"/> and the
/// instance-scoped <see cref="InstanceParameterWriter"/> so the two write paths behave
/// identically for a shared parameter.
/// </summary>
internal static class ParameterValueConverter
{
    public static object ConvertValue(Parameter parameter, string source, string conversion)
    {
        return parameter.StorageType switch
        {
            StorageType.String => source,
            StorageType.Integer => ParseInteger(source),
            StorageType.Double => ConvertDouble(parameter, ParseDouble(source), conversion),
            StorageType.ElementId => throw new InvalidOperationException(
                "ElementId parameters cannot be populated from text source data."),
            _ => throw new InvalidOperationException("Unsupported parameter storage type.")
        };
    }

    private static int ParseInteger(string source)
    {
        if (bool.TryParse(source, out var boolean))
        {
            return boolean ? 1 : 0;
        }

        if (int.TryParse(source, NumberStyles.Integer, CultureInfo.InvariantCulture, out var integer) ||
            int.TryParse(source, NumberStyles.Integer, CultureInfo.CurrentCulture, out integer))
        {
            return integer;
        }

        throw new InvalidOperationException($"'{source}' is not a valid integer or Yes/No value.");
    }

    private static double ParseDouble(string source)
    {
        const NumberStyles styles = NumberStyles.Float | NumberStyles.AllowThousands |
                                    NumberStyles.AllowCurrencySymbol;
        if (double.TryParse(source, styles, CultureInfo.InvariantCulture, out var number) ||
            double.TryParse(source, styles, CultureInfo.CurrentCulture, out number))
        {
            return number;
        }

        throw new InvalidOperationException($"'{source}' is not a valid number.");
    }

    private static double ConvertDouble(Parameter parameter, double value, string conversion)
    {
        if (string.Equals(conversion, "Metres → Revit length", StringComparison.OrdinalIgnoreCase))
        {
            return UnitUtils.ConvertToInternalUnits(value, UnitTypeId.Meters);
        }

        if (string.Equals(conversion, "Square metres → Revit area", StringComparison.OrdinalIgnoreCase))
        {
            return UnitUtils.ConvertToInternalUnits(value, UnitTypeId.SquareMeters);
        }

        if (!string.Equals(conversion, "Auto (Revit spec)", StringComparison.OrdinalIgnoreCase))
        {
            return value;
        }

        var dataType = parameter.Definition.GetDataType();
        if (dataType == SpecTypeId.Length)
        {
            return UnitUtils.ConvertToInternalUnits(value, UnitTypeId.Meters);
        }

        if (dataType == SpecTypeId.Area)
        {
            return UnitUtils.ConvertToInternalUnits(value, UnitTypeId.SquareMeters);
        }

        if (dataType == SpecTypeId.Volume)
        {
            return UnitUtils.ConvertToInternalUnits(value, UnitTypeId.CubicMeters);
        }

        if (dataType == SpecTypeId.Mass)
        {
            return UnitUtils.ConvertToInternalUnits(value, UnitTypeId.Kilograms);
        }

        return value;
    }

    public static bool ValuesEqual(Parameter parameter, object? value) =>
        parameter.StorageType switch
        {
            StorageType.String => string.Equals(parameter.AsString() ?? string.Empty, (string?)value ?? string.Empty),
            StorageType.Integer => parameter.AsInteger() == (int)value!,
            StorageType.Double => Math.Abs(parameter.AsDouble() - (double)value!) < 1e-9,
            _ => false
        };

    public static string FormatCurrentValue(Parameter parameter) => parameter.StorageType switch
    {
        StorageType.String => parameter.AsString() ?? string.Empty,
        StorageType.Integer => parameter.AsInteger().ToString(CultureInfo.InvariantCulture),
        StorageType.Double => parameter.AsValueString() ?? parameter.AsDouble().ToString("G", CultureInfo.InvariantCulture),
        StorageType.ElementId => parameter.AsElementId().Value.ToString(CultureInfo.InvariantCulture),
        _ => string.Empty
    };

    public static string FormatProposedValue(Parameter parameter, object? value) => parameter.StorageType switch
    {
        StorageType.String => (string?)value ?? string.Empty,
        StorageType.Integer => ((int)value!).ToString(CultureInfo.InvariantCulture),
        StorageType.Double => FormatInternalDouble(parameter, (double)value!),
        _ => value?.ToString() ?? string.Empty
    };

    private static string FormatInternalDouble(Parameter parameter, double value)
    {
        try
        {
            return UnitFormatUtils.Format(
                parameter.Element.Document.GetUnits(),
                parameter.Definition.GetDataType(),
                value,
                false);
        }
        catch
        {
            return value.ToString("G", CultureInfo.InvariantCulture);
        }
    }

    public static void SetValue(Parameter parameter, object? value)
    {
        var succeeded = parameter.StorageType switch
        {
            StorageType.String => parameter.Set((string?)value ?? string.Empty),
            StorageType.Integer => parameter.Set((int)value!),
            StorageType.Double => parameter.Set((double)value!),
            _ => false
        };
        if (!succeeded)
        {
            throw new InvalidOperationException($"Revit rejected parameter '{parameter.Definition.Name}'.");
        }
    }
}
