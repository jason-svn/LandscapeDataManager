using System.Globalization;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using WWP.LandscapeDataManager.Contracts;

namespace WWP.LandscapeDataManager.Revit.Services;

/// <summary>
/// Phase 2: reports selected Floor instances for Floor Calculator to match against the WWP
/// landscape data sheet, and writes whatever final values it computes. All calculation (coefficient
/// times area, or as-is for the two intrinsic temperature metrics, with any manual entry from the
/// tool's own UI already substituted) happens client-side — this service only reads each floor's
/// area for the client to compute with, and writes back the finished numbers. Bound to both
/// Planting and Floors (see <c>!_S_PLT_LDS_*</c> in <see cref="SharedParameterSetupService"/>):
/// these Revit Floor elements represent planted/paved landscape areas, not building floors, so the
/// same parameter family as trees applies.
/// </summary>
internal static class FloorLdsCalculationService
{
    private const string LdsTypeParameter = "!_S_PLT_LDS_Type_Text";
    private const string LdsMatchKeyParameter = "!_S_PLT_LDS_MatchKey_Text";
    private const string ResultSourceParameter = "!_S_PLT_LDS_ResultSource_Text";
    private const string LastCalculatedParameter = "!_S_PLT_LDS_LastCalculated_Text";
    private const string CostSavedParameter = "!_S_PLT_iTreeResult_CostSavedAnnual_Number";
    private const string OxygenProducedParameter = "!_S_PLT_LDS_OxygenProducedAnnual_Number";
    private const string TotalGwpParameter = "!_S_PLT_LDS_TotalGWP_Number";
    private const string SurfaceTempReductionParameter = "!_S_PLT_LDS_SurfaceTempReduction_Number";
    private const string AirTempReductionParameter = "!_S_PLT_LDS_AirTempReduction_Number";
    private const string Co2SequesteredParameter = "!_S_PLT_iTreeResult_CO2SequesteredAnnual_Number";
    private const string RunoffAvoidedParameter = "!_S_PLT_iTreeResult_RunoffAvoidedAnnual_Volume";

    public static SelectedFloorsResult GetSelectedFloors(UIApplication application)
    {
        var (document, floors) = ResolveSelectedFloors(application);
        var items = floors
            .Select(floor =>
            {
                var typeName = GetTypeName(document, floor, out var familyName);
                return new SelectedFloorItem(
                    floor.UniqueId,
                    familyName,
                    typeName,
                    GetAreaSquareMeters(floor),
                    GetNullableText(floor, LdsTypeParameter),
                    GetNullableText(floor, LdsMatchKeyParameter));
            })
            .ToList();

        return new SelectedFloorsResult(document.Title, items);
    }

    public static CalculateFloorsBatchResult CalculateBatch(UIApplication application, CalculateFloorsBatchRequest request)
    {
        var document = application.ActiveUIDocument?.Document
                       ?? throw new InvalidOperationException("Open a Revit project before calculating floors.");

        var rows = new List<FloorCalculationRow>();
        using var transaction = new Transaction(document, "LIM Calculate Floor Landscape Data");
        transaction.Start();
        try
        {
            foreach (var assignment in request.Assignments)
            {
                if (document.GetElement(assignment.FloorUniqueId) is not { } floor ||
                    floor.Category?.BuiltInCategory != BuiltInCategory.OST_Floors)
                {
                    continue;
                }

                WriteValues(floor, assignment.Values);
                rows.Add(new FloorCalculationRow(floor.UniqueId, assignment.Values.ResultSource));
            }

            transaction.Commit();
        }
        catch
        {
            if (transaction.GetStatus() == TransactionStatus.Started)
            {
                transaction.RollBack();
            }

            throw;
        }

        return new CalculateFloorsBatchResult(document.Title, rows);
    }

    private static void WriteValues(Element floor, FloorLdsValues values)
    {
        SetIfWritable(floor.LookupParameter(LdsTypeParameter), values.LdsType);
        SetIfWritable(floor.LookupParameter(LdsMatchKeyParameter), values.MatchKey);
        SetIfWritable(floor.LookupParameter(ResultSourceParameter), values.ResultSource);
        SetIfWritable(floor.LookupParameter(LastCalculatedParameter), FormatLocalTimestamp());

        SetIfWritable(floor.LookupParameter(Co2SequesteredParameter), values.Co2SequesteredAnnual);
        SetIfWritable(floor.LookupParameter(RunoffAvoidedParameter), UnitUtils.ConvertToInternalUnits(values.RunoffAvoidedAnnual, UnitTypeId.CubicMeters));
        SetIfWritable(floor.LookupParameter(CostSavedParameter), values.CostSavedAnnual);
        SetIfWritable(floor.LookupParameter(OxygenProducedParameter), values.OxygenProducedAnnual);
        SetIfWritable(floor.LookupParameter(TotalGwpParameter), values.TotalGwp);
        SetIfWritable(floor.LookupParameter(SurfaceTempReductionParameter), values.SurfaceTempReduction);
        SetIfWritable(floor.LookupParameter(AirTempReductionParameter), values.AirTempReduction);
    }

    private static double GetAreaSquareMeters(Element floor)
    {
        var areaParameter = floor.LookupParameter("Area");
        var internalArea = areaParameter is { HasValue: true } ? areaParameter.AsDouble() : 0.0;
        return UnitUtils.ConvertFromInternalUnits(internalArea, UnitTypeId.SquareMeters);
    }

    private static (Document Document, IReadOnlyList<Element> Floors) ResolveSelectedFloors(UIApplication application)
    {
        var uiDocument = application.ActiveUIDocument
                        ?? throw new InvalidOperationException("Open a Revit project before working with a selection.");
        var document = uiDocument.Document;

        var selectedIds = uiDocument.Selection.GetElementIds();
        if (selectedIds.Count == 0)
        {
            throw new InvalidOperationException("Select one or more Floor instances in Revit first.");
        }

        var floors = selectedIds
            .Select(document.GetElement)
            .Where(element => element is not null and not ElementType &&
                              element.Category?.BuiltInCategory == BuiltInCategory.OST_Floors)
            .OrderBy(element => GetTypeName(document, element, out _))
            .ToList();

        if (floors.Count == 0)
        {
            throw new InvalidOperationException("None of the selected elements are Floor instances.");
        }

        return (document, floors);
    }

    private static string GetTypeName(Document document, Element floor, out string familyName)
    {
        var elementType = document.GetElement(floor.GetTypeId()) as ElementType;
        familyName = elementType is FamilySymbol symbol ? symbol.FamilyName : elementType?.FamilyName ?? string.Empty;
        return elementType?.Name ?? floor.Name;
    }

    private static void SetIfWritable(Parameter? parameter, string value)
    {
        if (parameter is { IsReadOnly: false })
        {
            parameter.Set(value);
        }
    }

    private static void SetIfWritable(Parameter? parameter, double value)
    {
        if (parameter is { IsReadOnly: false })
        {
            parameter.Set(value);
        }
    }

    private static string? GetNullableText(Element element, string name)
    {
        var parameter = element.LookupParameter(name);
        return parameter is { HasValue: true } ? parameter.AsString() : null;
    }

    /// <summary>Local wall-clock time in the machine's own time zone, e.g. "2026-08-04, 14:23:07, Eastern Standard Time" — readable at a glance instead of a UTC ISO 8601 stamp.</summary>
    private static string FormatLocalTimestamp()
    {
        var now = DateTimeOffset.Now;
        var zone = TimeZoneInfo.Local;
        var zoneName = zone.IsDaylightSavingTime(now) ? zone.DaylightName : zone.StandardName;
        return $"{now.ToString("yyyy-MM-dd, HH:mm:ss", CultureInfo.InvariantCulture)}, {zoneName}";
    }
}
