using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using WWP.LandscapeDataManager.Contracts;

namespace WWP.LandscapeDataManager.Revit.Services;

/// <summary>
/// Reads back whatever i-Tree/LDS benefit results are already stored on Planting and Floor
/// instances — no calculation happens here, this only reports what <see cref="FloorLdsCalculationService"/>
/// and the i-Tree Calculator tool (<c>ITreeInstanceResultMapper</c>) have already written. Every
/// instance is returned regardless of Design Option (unlike <see cref="RevitModelScanner"/>'s
/// primary-only scans) — the Dashboard tool filters by Design Option itself, since summarizing
/// benefits across design alternatives is the point of the report.
/// </summary>
internal static class DashboardReportService
{
    private static readonly BuiltInCategory[] SupportedCategories =
    [
        BuiltInCategory.OST_Planting,
        BuiltInCategory.OST_Floors,
        BuiltInCategory.OST_LightingFixtures
    ];

    public static DashboardReportResult GetReport(UIApplication application, DashboardReportRequest request)
    {
        var uiDocument = application.ActiveUIDocument
                         ?? throw new InvalidOperationException("Open a Revit project before running the dashboard.");
        var document = uiDocument.Document;

        IEnumerable<Element> elements;
        if (request.SelectedOnly)
        {
            var selectedIds = uiDocument.Selection.GetElementIds();
            if (selectedIds.Count == 0)
            {
                throw new InvalidOperationException("Select one or more Planting or Floor instances first, or switch the dashboard scope to the whole model.");
            }

            var categoryFilter = new ElementMulticategoryFilter(SupportedCategories);
            elements = selectedIds
                .Select(document.GetElement)
                .Where(element => element is not null && categoryFilter.PassesFilter(document, element.Id))
                .Cast<Element>();
        }
        else
        {
            var categoryFilter = new ElementMulticategoryFilter(SupportedCategories);
            elements = new FilteredElementCollector(document)
                .WherePasses(categoryFilter)
                .WhereElementIsNotElementType()
                .ToElements();
        }

        var trees = new List<DashboardTreeItem>();
        var floors = new List<DashboardFloorItem>();
        var lighting = new List<DashboardLightingItem>();
        var siteLocation = PlantingInstanceValidationScanner.ReadLocation(document);
        foreach (var element in elements)
        {
            if (element.Category?.BuiltInCategory == BuiltInCategory.OST_Planting)
            {
                trees.Add(CreateTreeItem(document, element, siteLocation));
            }
            else if (element.Category?.BuiltInCategory == BuiltInCategory.OST_Floors)
            {
                floors.Add(CreateFloorItem(document, element));
            }
            else if (element.Category?.BuiltInCategory == BuiltInCategory.OST_LightingFixtures)
            {
                lighting.Add(CreateLightingItem(document, element));
            }
        }

        var unitPreference = RevitModelScanner.GetPreferredUnitSystemCode(document);
        var currencyPreference = RevitModelScanner.GetPreferredCurrencyCode(document);
        var siteTotalArea = GetSiteTotalAreaSquareMeters(document);
        var habitatConnectivityScore = GetHabitatConnectivityScore(document);

        ViewDesignScenarioResult? viewScenario = null;
        if (document.ActiveView is { } activeView)
        {
            try
            {
                viewScenario = DesignOptionScenarioService.GetViewScenario(document, activeView);
            }
            catch (Autodesk.Revit.Exceptions.ArgumentException)
            {
                // A schedule or sheet can't be collected from; the Dashboard then offers no view scenario.
            }
        }

        return new DashboardReportResult(
            document.Title, unitPreference, currencyPreference, siteTotalArea, habitatConnectivityScore, trees, floors, lighting,
            GetPropertyLines(document),
            DesignOptionScenarioService.GetSets(document),
            viewScenario,
            TreeTimelineStore.Load(document),
            ModelSetupChecks.Warnings(document));
    }

    /// <summary>Every property line in the model, whatever the scope/selection — a site boundary isn't something you select.</summary>
    private static List<DashboardPropertyLine> GetPropertyLines(Document document) =>
        new FilteredElementCollector(document)
            .OfCategory(BuiltInCategory.OST_SiteProperty)
            .WhereElementIsNotElementType()
            .Select(line =>
            {
                var area = line.get_Parameter(BuiltInParameter.PROPERTY_AREA);
                var areaSquareMeters = area is { HasValue: true, StorageType: StorageType.Double }
                    ? UnitUtils.ConvertFromInternalUnits(area.AsDouble(), UnitTypeId.SquareMeters)
                    : 0d;
                var name = string.IsNullOrWhiteSpace(line.Name) ? $"Property line {line.Id.Value}" : line.Name;
                return new DashboardPropertyLine(line.UniqueId, name, areaSquareMeters, areaSquareMeters > 0);
            })
            .OrderBy(line => line.Name, StringComparer.CurrentCultureIgnoreCase)
            .ToList();

    private static double? GetSiteTotalAreaSquareMeters(Document document)
    {
        var parameter = document.ProjectInformation.LookupParameter("!_S_PLT_Site_TotalArea_Area");
        return parameter is { HasValue: true }
            ? UnitUtils.ConvertFromInternalUnits(parameter.AsDouble(), UnitTypeId.SquareMeters)
            : null;
    }

    private static double? GetHabitatConnectivityScore(Document document)
    {
        var parameter = document.ProjectInformation.LookupParameter("!_S_PLT_Site_HabitatConnectivityScore_Number");
        return parameter is { HasValue: true } ? parameter.AsDouble() : null;
    }

    private static DashboardTreeItem CreateTreeItem(Document document, Element element, (double? Latitude, double? Longitude) siteLocation)
    {
        var elementType = document.GetElement(element.GetTypeId()) as ElementType;
        var familyName = elementType is FamilySymbol symbol ? symbol.FamilyName : elementType?.FamilyName ?? string.Empty;

        double GetNumber(string name) => GetDoubleParameter(element, name);
        double GetVolumeCubicMeters(string name)
        {
            var parameter = element.LookupParameter(name);
            return parameter is { HasValue: true }
                ? UnitUtils.ConvertFromInternalUnits(parameter.AsDouble(), UnitTypeId.CubicMeters)
                : 0d;
        }

        double GetMassKilograms(string name)
        {
            var parameter = element.LookupParameter(name);
            return parameter is { HasValue: true }
                ? UnitUtils.ConvertFromInternalUnits(parameter.AsDouble(), UnitTypeId.Kilograms)
                : 0d;
        }

        var canopyWidthParameter = element.LookupParameter("!_S_PLT_TreeFoliage_Width");
        var canopyAreaSquareMeters = 0d;
        if (canopyWidthParameter is { HasValue: true })
        {
            var radiusInternal = canopyWidthParameter.AsDouble() / 2d;
            canopyAreaSquareMeters = UnitUtils.ConvertFromInternalUnits(Math.PI * radiusInternal * radiusInternal, UnitTypeId.SquareMeters);
        }

        // Where the canopy sits, for canopy cover with overlaps counted once: the crown width, else the
        // family's plan size (the same fallback as the Connectivity Calculator).
        double? centerX = null, centerY = null, radiusMeters = null;
        if (element.Location is LocationPoint location)
        {
            var radiusFeet = canopyWidthParameter is { HasValue: true } ? canopyWidthParameter.AsDouble() / 2d : 0d;
            if (radiusFeet <= 0 && element.get_BoundingBox(null) is { } box)
            {
                radiusFeet = ((box.Max.X - box.Min.X) + (box.Max.Y - box.Min.Y)) / 4d;
            }

            if (radiusFeet > 0)
            {
                centerX = UnitUtils.ConvertFromInternalUnits(location.Point.X, UnitTypeId.Meters);
                centerY = UnitUtils.ConvertFromInternalUnits(location.Point.Y, UnitTypeId.Meters);
                radiusMeters = UnitUtils.ConvertFromInternalUnits(radiusFeet, UnitTypeId.Meters);
                if (canopyAreaSquareMeters <= 0)
                {
                    canopyAreaSquareMeters = Math.PI * radiusMeters.Value * radiusMeters.Value;
                }
            }
        }

        return new DashboardTreeItem(
            element.UniqueId,
            element.Id.Value,
            familyName,
            elementType?.Name ?? element.Name,
            elementType is null ? null : GetNullableText(elementType, "!_S_PLT_iTreeSpecies_Code_Text"),
            elementType is null ? null : GetNullableText(elementType, "!_S_PLT_iTreeSpecies_CommonName_Text"),
            elementType is null ? null : GetNullableText(elementType, "!_S_PLT_iTreeSpecies_ScientificName_Text"),
            elementType is null ? null : GetNullableText(elementType, "!_S_PLT_iTreeSpecies_Type_Text"),
            elementType is null ? null : GetNullableText(elementType, "!_S_PLT_iTreeSpecies_NativeStatus_Text"),
            elementType is null ? null : GetNullableText(elementType, "!_S_PLT_iTreeSpecies_BloomMonths_Text"),
            elementType is null ? null : GetNullableText(elementType, "!_S_PLT_iTreeSpecies_EcologicalFunctions_Text"),
            canopyAreaSquareMeters,
            GetLevelName(document, element),
            GetDesignOptionInfo(document, element),
            GetNullableText(element, "!_S_PLT_iTreeResult_Status_Text") ?? "MissingInput",
            GetNullableText(element, "!_S_PLT_iTreeResult_UnitSystem_Text") ?? "Metric",
            GetNullableText(element, "!_S_PLT_iTreeResult_CurrencyUsed_Text") ?? "USD",
            element.LookupParameter("!_S_PLT_iTreeResult_ExchangeRateUsed_Number") is { HasValue: true } rateParam ? rateParam.AsDouble() : 1d,
            GetMassKilograms("!_S_PLT_iTreeResult_CarbonSequesteredAnnual_Mass"),
            GetMassKilograms("!_S_PLT_iTreeResult_CarbonSequesteredLifetimeTotal_Mass"),
            GetMassKilograms("!_S_PLT_iTreeResult_CORemovedAnnual_Mass"),
            GetMassKilograms("!_S_PLT_iTreeResult_CORemovedLifetimeTotal_Mass"),
            GetMassKilograms("!_S_PLT_iTreeResult_NO2RemovedAnnual_Mass"),
            GetMassKilograms("!_S_PLT_iTreeResult_NO2RemovedLifetimeTotal_Mass"),
            GetMassKilograms("!_S_PLT_iTreeResult_O3RemovedAnnual_Mass"),
            GetMassKilograms("!_S_PLT_iTreeResult_O3RemovedLifetimeTotal_Mass"),
            GetMassKilograms("!_S_PLT_iTreeResult_PM25RemovedAnnual_Mass"),
            GetMassKilograms("!_S_PLT_iTreeResult_PM25RemovedLifetimeTotal_Mass"),
            GetMassKilograms("!_S_PLT_iTreeResult_SO2RemovedAnnual_Mass"),
            GetMassKilograms("!_S_PLT_iTreeResult_SO2RemovedLifetimeTotal_Mass"),
            GetNumber("!_S_PLT_iTreeResult_CostSavedAnnual_Currency"),
            GetNumber("!_S_PLT_iTreeResult_CostSavedLifetimeTotal_Currency"),
            GetNumber("!_S_PLT_iTreeResult_CarbonCostSavedAnnual_Currency"),
            GetNumber("!_S_PLT_iTreeResult_CarbonCostSavedLifetimeTotal_Currency"),
            GetNumber("!_S_PLT_iTreeResult_StormWaterCostSavedAnnual_Currency"),
            GetNumber("!_S_PLT_iTreeResult_StormWaterCostSavedLifetimeTotal_Currency"),
            GetNumber("!_S_PLT_iTreeResult_AirPollutionCostSavedAnnual_Currency"),
            GetNumber("!_S_PLT_iTreeResult_AirPollutionCostSavedLifetimeTotal_Currency"),
            GetVolumeCubicMeters("!_S_PLT_iTreeResult_RainfallInterceptedAnnual_Volume"),
            GetVolumeCubicMeters("!_S_PLT_iTreeResult_RainfallInterceptedLifetimeTotal_Volume"),
            GetVolumeCubicMeters("!_S_PLT_iTreeResult_RunoffAvoidedAnnual_Volume"),
            GetVolumeCubicMeters("!_S_PLT_iTreeResult_RunoffAvoidedLifetimeTotal_Volume"),
            GetMassKilograms("!_S_PLT_iTreeResult_CO2EquivalentAnnual_Mass"),
            GetMassKilograms("!_S_PLT_iTreeResult_CO2EquivalentLifetimeTotal_Mass"),
            GetWorksetName(document, element),
            centerX,
            centerY,
            radiusMeters,
            GetMassKilograms("!_S_PLT_Result_OxygenProducedAnnual_Mass"),
            GetMassKilograms("!_S_PLT_Result_OxygenProducedLifetimeTotal_Mass"),
            MaintenanceCost.AnnualFor(element),
            ConnectivityService.GetStoredBand(element),
            GetNullableText(element, TreeTimelineStore.KeyParameter),
            // WWP-sheet values: the pollutant total a sheet estimate stores (i-Tree gives each
            // pollutant instead), the tree's pollen, and its type's irrigation factor and allergenicity.
            GetMassKilograms("!_S_PLT_Result_PollutantsRemovedAnnual_Mass"),
            GetMassKilograms("!_S_PLT_Result_PollenAnnual_Mass"),
            GetNullableDoubleOnInstanceOrType(element, "!_S_PLT_LDS_IrrigationDemandFactor_Number"),
            GetNullableTextOnInstanceOrType(element, "!_S_PLT_iTreeSpecies_Allergenic_Text"),
            // Its i-Tree inputs, so the Dashboard can flag a result whose inputs changed since (Stale).
            PlantingInstanceValidationScanner.ReadItem(document, element, siteLocation.Latitude, siteLocation.Longitude),
            // Retained, lost (demolished), created or excluded, from its phases (see BngPhaseRoles).
            BngFloorService.GetPhaseRole(document, element).Role,
            GrowthProjectionService.LinkOf(element) is not null,
            elementType is null ? null : ReadAppearance(elementType));
    }

    /// <summary>The type's year in colour (flowers, fruit, seasons); null when it has none of it.</summary>
    private static PlantAppearance? ReadAppearance(Element elementType)
    {
        string? Text(string name) => GetNullableText(elementType, name) is { } value && !string.IsNullOrWhiteSpace(value) ? value.Trim() : null;

        var appearance = new PlantAppearance(
            Text(PlantAppearance.FlowerColourParameter),
            Text(PlantAppearance.FruitMonthsParameter),
            Text(PlantAppearance.FruitColourParameter),
            Text(PlantAppearance.FruitTypeParameter),
            Text(PlantAppearance.SpringColourParameter),
            Text(PlantAppearance.SummerColourParameter),
            Text(PlantAppearance.AutumnColourParameter),
            Text(PlantAppearance.WinterColourParameter));
        return appearance.IsEmpty ? null : appearance;
    }

    private static DashboardFloorItem CreateFloorItem(Document document, Element element)
    {
        var elementType = document.GetElement(element.GetTypeId()) as ElementType;
        var familyName = elementType is FamilySymbol symbol ? symbol.FamilyName : elementType?.FamilyName ?? string.Empty;

        var areaParameter = element.LookupParameter("Area");
        var areaSquareMeters = areaParameter is { HasValue: true }
            ? UnitUtils.ConvertFromInternalUnits(areaParameter.AsDouble(), UnitTypeId.SquareMeters)
            : 0d;

        double GetMassKilograms(string name)
        {
            var parameter = element.LookupParameter(name);
            return parameter is { HasValue: true }
                ? UnitUtils.ConvertFromInternalUnits(parameter.AsDouble(), UnitTypeId.Kilograms)
                : 0d;
        }

        double GetVolumeCubicMeters(string name)
        {
            var parameter = element.LookupParameter(name);
            return parameter is { HasValue: true }
                ? UnitUtils.ConvertFromInternalUnits(parameter.AsDouble(), UnitTypeId.CubicMeters)
                : 0d;
        }

        return new DashboardFloorItem(
            element.UniqueId,
            element.Id.Value,
            familyName,
            elementType?.Name ?? element.Name,
            // Type-bound parameters (see ElementParameters.LookupOnInstanceOrType).
            GetNullableTextOnInstanceOrType(element, "!_S_PLT_LDS_Type_Text"),
            GetNullableTextOnInstanceOrType(element, "!_S_PLT_LDS_SurfaceClass_Text"),
            GetLevelName(document, element),
            GetDesignOptionInfo(document, element),
            areaSquareMeters,
            GetMassKilograms("!_S_PLT_iTreeResult_CarbonSequesteredAnnual_Mass"),
            GetVolumeCubicMeters("!_S_PLT_iTreeResult_RunoffAvoidedAnnual_Volume"),
            GetMassKilograms("!_S_PLT_Result_PollutantsRemovedAnnual_Mass"),
            GetDoubleParameter(element, "!_S_PLT_iTreeResult_CostSavedAnnual_Currency"),
            GetMassKilograms("!_S_PLT_Result_OxygenProducedAnnual_Mass"),
            GetMassKilograms("!_S_PLT_Result_EmbodiedGWP_Mass"),
            GetDoubleParameterOnInstanceOrType(element, "!_S_PLT_LDS_SurfaceTempReduction_Number"),
            GetDoubleParameterOnInstanceOrType(element, "!_S_PLT_LDS_AirTempReduction_Number"),
            CreateFloorBng(document, element),
            MaintenanceCost.AnnualFor(element),
            element.get_Parameter(BuiltInParameter.FLOOR_PARAM_IS_STRUCTURAL) is { HasValue: true } structural && structural.AsInteger() == 1,
            ConnectivityService.GetStoredBand(element),
            GetMassKilograms("!_S_PLT_Result_PollenAnnual_Mass"),
            BngFloorService.GetPhaseRole(document, element).Role,
            // Floor Calculator stamps its source (sheet, or reviewed by hand) on every floor it calculates.
            GetNullableTextOnInstanceOrType(element, "!_S_PLT_Result_LDS_Source_Text") is not null);
    }

    /// <summary>Reads back what <see cref="BngFloorService"/> wrote; null when the BNG parameters aren't bound in this project.</summary>
    private static DashboardFloorBng? CreateFloorBng(Document document, Element element)
    {
        if (element.LookupParameter("!_S_PLT_BNGInput_ProposedHabitat_Text") is null)
        {
            return null;
        }

        var yearOffset = element.LookupParameter("!_S_PLT_BNGInput_CreationYearOffset_Number") is { HasValue: true, StorageType: StorageType.Integer } offset
            ? offset.AsInteger()
            : 0;

        return new DashboardFloorBng(
            GetNullableText(element, "!_S_PLT_BNGInput_ProposedHabitat_Text"),
            GetNullableText(element, "!_S_PLT_iTreeInput_Condition_Text"),
            GetNullableText(element, "!_S_PLT_BNGInput_StrategicSignificance_Text"),
            yearOffset,
            GetNullableText(element, "!_S_PLT_BNGResult_BroadHabitat_Text"),
            GetNullableText(element, "!_S_PLT_BNGResult_Distinctiveness_Text"),
            GetDoubleParameter(element, "!_S_PLT_BNGResult_HabitatUnits_Number"),
            GetNullableText(element, "!_S_PLT_BNGResult_Status_Text"),
            GetNullableText(element, "!_S_PLT_BNGResult_InputSignature_Text"),
            GetNullableText(element, "!_S_PLT_BNGResult_MetricVersion_Text"),
            BngFloorService.GetPhaseRole(document, element).Role,
            GetNullableText(element, BngFloorService.BaselineHabitatParameter),
            GetNullableText(element, BngFloorService.BaselineConditionParameter),
            GetNullableText(element, BngFloorService.IrreplaceableParameter),
            BngFloorService.IsEnhanced(element),
            GetDoubleParameter(element, BngFloorService.BaselineUnitsParameter));
    }

    private static DashboardLightingItem CreateLightingItem(Document document, Element element)
    {
        var elementType = document.GetElement(element.GetTypeId()) as ElementType;
        var familyName = elementType is FamilySymbol symbol ? symbol.FamilyName : elementType?.FamilyName ?? string.Empty;

        return new DashboardLightingItem(
            element.UniqueId,
            element.Id.Value,
            familyName,
            elementType?.Name ?? element.Name,
            GetLevelName(document, element),
            GetDesignOptionInfo(document, element),
            elementType is null ? null : GetNullableText(elementType, "!_S_PLT_Lighting_DarkSkyCompliant_Text"));
    }

    /// <summary>
    /// Resolves the option's own name and its parent set's name via the option's <see cref="BuiltInParameter.OPTION_SET_ID"/>
    /// parameter — no code elsewhere in this repo enumerates actual Design Options (only the primary/
    /// non-primary boolean), so this idiom should be re-checked against a live model with non-primary
    /// options while testing.
    /// </summary>
    internal static DesignOptionInfo GetDesignOptionInfo(Document document, Element element)
    {
        var designOption = element.DesignOption;
        if (designOption is null)
        {
            return new DesignOptionInfo(null, null, true);
        }

        // DesignOptionSet itself is not a public type in the Revit API, so the set's own Name is
        // read through the base Element rather than a cast to DesignOptionSet.
        var setParameter = designOption.get_Parameter(BuiltInParameter.OPTION_SET_ID);
        var setName = setParameter is { HasValue: true }
            ? document.GetElement(setParameter.AsElementId())?.Name
            : null;

        return new DesignOptionInfo(setName, designOption.Name, designOption.IsPrimary);
    }

    private static string? GetLevelName(Document document, Element element)
    {
        if (element.LevelId != ElementId.InvalidElementId &&
            document.GetElement(element.LevelId) is Level level)
        {
            return level.Name;
        }

        var scheduleLevel = element.get_Parameter(BuiltInParameter.INSTANCE_SCHEDULE_ONLY_LEVEL_PARAM);
        if (scheduleLevel is { HasValue: true } &&
            document.GetElement(scheduleLevel.AsElementId()) is Level scheduledLevel)
        {
            return scheduledLevel.Name;
        }

        var familyLevel = element.get_Parameter(BuiltInParameter.FAMILY_LEVEL_PARAM);
        if (familyLevel is { HasValue: true } &&
            document.GetElement(familyLevel.AsElementId()) is Level familyPlacedLevel)
        {
            return familyPlacedLevel.Name;
        }

        return null;
    }

    /// <summary>The element's workset name — growth years are modelled as worksets (e.g. "Trees - 15 Years"); null in a non-workshared model.</summary>
    private static string? GetWorksetName(Document document, Element element) =>
        document.IsWorkshared ? document.GetWorksetTable().GetWorkset(element.WorksetId)?.Name : null;

    private static string? GetNullableTextOnInstanceOrType(Element element, string name) =>
        ElementParameters.LookupOnInstanceOrType(element, name) is { HasValue: true, StorageType: StorageType.String } parameter
            ? NullIfBlank(parameter.AsString())
            : null;

    private static string? NullIfBlank(string? text) => string.IsNullOrWhiteSpace(text) ? null : text;

    private static string? GetNullableText(Element element, string name)
    {
        var parameter = element.LookupParameter(name);
        return parameter is { HasValue: true } ? parameter.AsString() : null;
    }

    private static double GetDoubleParameter(Element element, string name)
    {
        var parameter = element.LookupParameter(name);
        return parameter is { HasValue: true } ? parameter.AsDouble() : 0d;
    }

    private static double? GetNullableDoubleOnInstanceOrType(Element element, string name) =>
        ElementParameters.LookupOnInstanceOrType(element, name) is { HasValue: true, StorageType: StorageType.Double } parameter
            ? parameter.AsDouble()
            : null;

    private static double GetDoubleParameterOnInstanceOrType(Element element, string name)
    {
        var parameter = ElementParameters.LookupOnInstanceOrType(element, name);
        return parameter is { HasValue: true } ? parameter.AsDouble() : 0d;
    }
}
