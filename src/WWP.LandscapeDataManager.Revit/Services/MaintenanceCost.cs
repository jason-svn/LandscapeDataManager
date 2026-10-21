using Autodesk.Revit.DB;

namespace WWP.LandscapeDataManager.Revit.Services;

/// <summary>
/// Yearly upkeep of planting and surfaces from the WWP sheet's maintenance rate
/// (<c>!_S_PLT_LDS_MaintenanceCostAnnual_Currency</c>, imported onto the type): per tree for a
/// Planting instance, per m² × area for a Floor. The result is stored on the instance in
/// <c>!_S_PLT_Result_MaintenanceCostAnnual_Currency</c> so schedules can total it; the Dashboard
/// works it out live from the rate, so it's never stale there.
/// </summary>
internal static class MaintenanceCost
{
    public const string RateParameter = "!_S_PLT_LDS_MaintenanceCostAnnual_Currency";
    public const string ResultParameter = "!_S_PLT_Result_MaintenanceCostAnnual_Currency";

    /// <summary>The element's yearly maintenance cost in the project currency; 0 without a rate.</summary>
    public static double AnnualFor(Element element)
    {
        if (ElementParameters.LookupOnInstanceOrType(element, RateParameter) is not { HasValue: true, StorageType: StorageType.Double } rate)
        {
            return 0d;
        }

        if (element.Category?.BuiltInCategory != BuiltInCategory.OST_Floors)
        {
            return rate.AsDouble();
        }

        var area = element.get_Parameter(BuiltInParameter.HOST_AREA_COMPUTED);
        return area is { HasValue: true }
            ? rate.AsDouble() * UnitUtils.ConvertFromInternalUnits(area.AsDouble(), UnitTypeId.SquareMeters)
            : 0d;
    }

    /// <summary>Writes the result parameter inside the caller's open transaction; false when it isn't bound or can't change.</summary>
    public static bool WriteResult(Element element)
    {
        if (element.LookupParameter(ResultParameter) is not { IsReadOnly: false, StorageType: StorageType.Double } result)
        {
            return false;
        }

        var value = AnnualFor(element);
        return Math.Abs(result.AsDouble() - value) < 1e-9 || result.Set(value);
    }
}
