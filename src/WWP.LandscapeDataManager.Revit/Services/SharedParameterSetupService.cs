using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using WWP.LandscapeDataManager.Contracts;

namespace WWP.LandscapeDataManager.Revit.Services;

/// <summary>
/// Creates/validates the project-parameter bindings the LIM planting and i-Tree workflow needs,
/// from the "PLANTING - iTree" group of the WWP shared-parameter file. Every definition is
/// checked before it is touched, so running this repeatedly never creates a duplicate parameter
/// and never silently changes an existing binding it disagrees with — mismatches are reported
/// as conflicts for the user to resolve instead.
/// </summary>
internal static class SharedParameterSetupService
{
    private const string SharedGroupName = "PLANTING - iTree";

    /// <summary>
    /// Read-only dry run: reports what <see cref="EnsureParameters"/> would do for every known
    /// LIM parameter, without starting a transaction or touching the document. Missing bindings
    /// are reported as "Will create" instead of being created.
    /// </summary>
    public static SharedParameterSetupResult PreviewParameters(UIApplication application, string sharedParameterFilePath)
    {
        var (document, definitionsByName) = OpenFileAndGroup(application, sharedParameterFilePath);
        var rows = ParameterOwnership.All
            .Select(ownership => EvaluateOne(document, application, definitionsByName, ownership, apply: false))
            .ToList();
        return new SharedParameterSetupResult(document.Title, rows);
    }

    public static SharedParameterSetupResult EnsureParameters(
        UIApplication application,
        EnsureSharedParametersRequest request)
    {
        var (document, definitionsByName) = OpenFileAndGroup(application, request.SharedParameterFilePath);
        var includedNames = new HashSet<string>(request.IncludedParameterNames, StringComparer.Ordinal);
        var ownerships = ParameterOwnership.All.Where(ownership => includedNames.Contains(ownership.Name)).ToList();

        var rows = new List<SharedParameterSetupRow>();
        using var transaction = new Transaction(document, "LIM Shared Parameter Setup");
        transaction.Start();
        try
        {
            foreach (var ownership in ownerships)
            {
                rows.Add(EvaluateOne(document, application, definitionsByName, ownership, apply: true));
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

        return new SharedParameterSetupResult(document.Title, rows);
    }

    private static (Document Document, IReadOnlyDictionary<string, ExternalDefinition> DefinitionsByName) OpenFileAndGroup(
        UIApplication application,
        string sharedParameterFilePath)
    {
        var document = application.ActiveUIDocument?.Document
                       ?? throw new InvalidOperationException("Open a Revit project before setting up parameters.");

        application.Application.SharedParametersFilename = sharedParameterFilePath;
        var definitionFile = application.Application.OpenSharedParameterFile()
                              ?? throw new InvalidOperationException(
                                  $"'{sharedParameterFilePath}' could not be opened as a Revit shared parameter file.");

        var group = definitionFile.Groups.get_Item(SharedGroupName)
                    ?? throw new InvalidOperationException(
                        $"The shared parameter file does not contain a group named '{SharedGroupName}'.");

        var definitionsByName = group.Definitions
            .OfType<ExternalDefinition>()
            .ToDictionary(definition => definition.Name, StringComparer.Ordinal);

        return (document, definitionsByName);
    }

    /// <summary>
    /// Checks one parameter's binding against expectations. When <paramref name="apply"/> is
    /// false, a missing binding is reported as "Will create" and left untouched; when true, it is
    /// actually created. Every other outcome (Conflict/Already valid/Error) is identical either way.
    /// </summary>
    private static SharedParameterSetupRow EvaluateOne(
        Document document,
        UIApplication application,
        IReadOnlyDictionary<string, ExternalDefinition> definitionsByName,
        ParameterOwnership ownership,
        bool apply)
    {
        if (!definitionsByName.TryGetValue(ownership.Name, out var definition))
        {
            return new SharedParameterSetupRow(
                ownership.Name,
                string.Empty,
                ownership.Scope,
                string.Join(", ", ownership.Categories),
                "Error",
                "Not found in the shared parameter file's 'PLANTING - iTree' group.",
                ownership.Description);
        }

        var guidText = definition.GUID.ToString();
        var conflictingElement = new FilteredElementCollector(document)
            .OfClass(typeof(SharedParameterElement))
            .Cast<SharedParameterElement>()
            .FirstOrDefault(element => string.Equals(element.Name, ownership.Name, StringComparison.Ordinal) &&
                                       element.GuidValue != definition.GUID);
        if (conflictingElement is not null)
        {
            return new SharedParameterSetupRow(
                ownership.Name,
                guidText,
                ownership.Scope,
                string.Join(", ", ownership.Categories),
                "Conflict",
                $"A different parameter named '{ownership.Name}' already exists in this project " +
                $"with a different ID ({conflictingElement.GuidValue}). It was left unchanged.",
                ownership.Description);
        }

        var existingBinding = FindExistingBinding(document, ownership.Name);
        if (existingBinding is null)
        {
            if (!apply)
            {
                return new SharedParameterSetupRow(
                    ownership.Name, guidText, ownership.Scope, string.Join(", ", ownership.Categories),
                    "Will create", null, ownership.Description);
            }

            var categorySet = application.Application.Create.NewCategorySet();
            foreach (var category in ownership.Categories)
            {
                categorySet.Insert(document.Settings.Categories.get_Item(category));
            }

            Binding binding = ownership.Scope == "Instance"
                ? application.Application.Create.NewInstanceBinding(categorySet)
                : application.Application.Create.NewTypeBinding(categorySet);

            var inserted = document.ParameterBindings.Insert(definition, binding, ownership.Group);
            return inserted
                ? new SharedParameterSetupRow(
                    ownership.Name, guidText, ownership.Scope, string.Join(", ", ownership.Categories),
                    "Created", null, ownership.Description)
                : new SharedParameterSetupRow(
                    ownership.Name, guidText, ownership.Scope, string.Join(", ", ownership.Categories),
                    "Error", "Revit rejected the new parameter binding.", ownership.Description);
        }

        var (isInstance, boundCategories, existingGroup, internalDefinition) = existingBinding.Value;
        var expectedIsInstance = ownership.Scope == "Instance";
        var boundCategoryNames = boundCategories.Cast<Category>().Select(category => category.Name).ToHashSet();
        var missingCategories = ownership.Categories.Where(category =>
            !boundCategoryNames.Contains(document.Settings.Categories.get_Item(category).Name)).ToList();

        if (isInstance != expectedIsInstance || missingCategories.Count > 0)
        {
            var reason = isInstance != expectedIsInstance
                ? $"bound as {(isInstance ? "Instance" : "Type")}, expected {ownership.Scope}"
                : $"missing categories: {string.Join(", ", missingCategories)}";
            return new SharedParameterSetupRow(
                ownership.Name, guidText, ownership.Scope, string.Join(", ", ownership.Categories),
                "Conflict", $"Already bound, but not as expected ({reason}). Left unchanged.", ownership.Description);
        }

        if (!existingGroup.Equals(ownership.Group))
        {
            var fromLabel = LabelUtils.GetLabelForGroup(existingGroup);
            var toLabel = LabelUtils.GetLabelForGroup(ownership.Group);

            if (!apply)
            {
                return new SharedParameterSetupRow(
                    ownership.Name, guidText, ownership.Scope, string.Join(", ", ownership.Categories),
                    "Will regroup", $"Currently under '{fromLabel}'; will move to '{toLabel}'.", ownership.Description);
            }

            internalDefinition.SetGroupTypeId(ownership.Group);
            return new SharedParameterSetupRow(
                ownership.Name, guidText, ownership.Scope, string.Join(", ", ownership.Categories),
                "Regrouped", $"Moved from '{fromLabel}' to '{toLabel}'.", ownership.Description);
        }

        return new SharedParameterSetupRow(
            ownership.Name, guidText, ownership.Scope, string.Join(", ", ownership.Categories),
            "Already valid", null, ownership.Description);
    }

    private static (bool IsInstance, CategorySet Categories, ForgeTypeId Group, InternalDefinition Definition)? FindExistingBinding(
        Document document, string name)
    {
        var iterator = document.ParameterBindings.ForwardIterator();
        iterator.Reset();
        while (iterator.MoveNext())
        {
            if (iterator.Key is not InternalDefinition definition || !string.Equals(definition.Name, name, StringComparison.Ordinal))
            {
                continue;
            }

            return iterator.Current switch
            {
                InstanceBinding instanceBinding => (true, instanceBinding.Categories, definition.GetGroupTypeId(), definition),
                TypeBinding typeBinding => (false, typeBinding.Categories, definition.GetGroupTypeId(), definition),
                _ => null
            };
        }

        return null;
    }

    private sealed record ParameterOwnership(
        string Name,
        string Scope,
        IReadOnlyList<BuiltInCategory> Categories,
        ForgeTypeId Group,
        string Description)
    {
        private static ParameterOwnership PlantingType(string name, ForgeTypeId group, string description) =>
            new(name, "Type", [BuiltInCategory.OST_Planting], group, description);

        private static ParameterOwnership PlantingInstance(string name, ForgeTypeId group, string description) =>
            new(name, "Instance", [BuiltInCategory.OST_Planting], group, description);

        private static ParameterOwnership ProjectInfo(string name, ForgeTypeId group, string description) =>
            new(name, "Instance", [BuiltInCategory.OST_ProjectInformation], group, description);

        // Descriptions sourced verbatim from "WWP Planting iTree Project Parameters.xlsx" (the
        // Description column), except where a description referenced the retired Planting Key
        // Schedule — those three now point at Tree Searcher / the i-Tree Downloader instead.
        //
        // Groups are assigned by functional role rather than by data type, so the Properties
        // palette clusters parameters the same way this tool's own preview list does:
        //   - Constraints      = required manual input: necessary for the i-Tree calculation
        //                        and provided directly by a person (species, condition, crown
        //                        exposure, tree age, project location).
        //   - AnalysisResults  = output: everything i-Tree Calculator writes back after calling
        //                        the API (the benefit values and the calculation's own status).
        //   - Geometry         = calculated: driven by the planting family's parametric formulas
        //                        (the growth ratios and the dimensions they produce), not typed
        //                        or API-sourced.
        //   - General          = optional: descriptive/tracking metadata not required for the
        //                        calculation itself (species reference data, sync bookkeeping,
        //                        display preferences).
        public static readonly IReadOnlyList<ParameterOwnership> All =
        [
            // Calculated — driven by the planting family's growth-ratio formulas.
            PlantingType("!_S_PLANTING_GrowthRatio_HeightbyYear_Number", GroupTypeId.Geometry,
                "Type-specific dimensionless annual height-growth ratio used by family formulas."),
            PlantingType("!_S_PLANTING_GrowthRatio_TrunkDiameterbyYear_Number", GroupTypeId.Geometry,
                "Type-specific dimensionless annual trunk-diameter growth ratio used by family formulas."),
            PlantingType("!_S_PLANTING_GrowthRatio_WidthbyYear_Number", GroupTypeId.Geometry,
                "Type-specific dimensionless annual crown-width growth ratio used by family formulas."),
            // DataSync_InputSignature/LastUpdated/Status/SourceRecordId are bound Instance (not the
            // legacy workbook's Type scope): Planting Data Sync matches and tracks each Revit
            // instance individually by stable ID, so two instances of the same type can be synced
            // from two different source rows. Only SourceName (which dataset, not which record)
            // stays Type-level. All four are optional sync bookkeeping, not required for i-Tree itself.
            PlantingInstance("!_S_PLANTING_DataSync_InputSignature_Text", GroupTypeId.General,
                "Application-managed signature used to detect source or Revit data changes."),
            PlantingInstance("!_S_PLANTING_DataSync_LastUpdated_Text", GroupTypeId.General,
                "ISO 8601 date and time of the latest successful external-data synchronization."),
            PlantingInstance("!_S_PLANTING_DataSync_Status_Text", GroupTypeId.General,
                "Latest external-data synchronization state for the planting record."),
            PlantingType("!_S_PLANTING_DataSync_SourceName_Text", GroupTypeId.General,
                "Name of the Excel, Airtable, or configured external data source."),
            PlantingInstance("!_S_PLANTING_DataSync_SourceRecordId_Text", GroupTypeId.General,
                "Stable external record identifier used to match synchronized planting data."),
            // Required manual input — a person must assign the species (via Tree Searcher or the
            // i-Tree Downloader) before i-Tree can calculate anything for that type.
            PlantingType("!_S_PLANTING_iTreeSpecies_Code_Text", GroupTypeId.Constraints,
                "i-Tree species code assigned via Tree Searcher or the i-Tree Downloader."),
            // Optional — descriptive species reference data, not itself consumed as calculation input.
            PlantingType("!_S_PLANTING_iTreeSpecies_CommonName_Text", GroupTypeId.General,
                "Common species name populated from the i-Tree species catalogue (Tree Searcher or the i-Tree Downloader)."),
            PlantingType("!_S_PLANTING_iTreeSpecies_ReplaceBy_Text", GroupTypeId.General,
                "Replacement species code returned for a deprecated i-Tree catalogue record."),
            PlantingType("!_S_PLANTING_iTreeSpecies_ScientificName_Text", GroupTypeId.General,
                "Scientific species name populated from the i-Tree species catalogue (Tree Searcher or the i-Tree Downloader)."),
            PlantingType("!_S_PLANTING_iTreeSpecies_Type_Text", GroupTypeId.General,
                "Taxonomic type returned by the i-Tree species catalogue."),

            // Required manual input — typed per instance, and fed straight to the i-Tree API.
            PlantingInstance("!_S_PLANTING_TreeGrowth_Years_Number", GroupTypeId.Constraints,
                "Instance-specific modeled tree age used by family growth formulas."),
            PlantingInstance("!_S_PLANTING_iTreeInput_Condition_Text", GroupTypeId.Constraints,
                "Tree condition: Excellent, Good, Fair, Poor, Critical, Dying, or Dead."),
            PlantingInstance("!_S_PLANTING_iTreeInput_CrownExposure_Number", GroupTypeId.Constraints,
                "Crown light exposure from 0 fully shaded to 5 fully exposed."),
            // Output — the calculation's own status/tracking fields, written by i-Tree Calculator.
            PlantingInstance("!_S_PLANTING_iTreeResult_Details_Text", GroupTypeId.AnalysisResults,
                "Missing-input, validation, warning, or API-error details for the tree instance."),
            PlantingInstance("!_S_PLANTING_iTreeResult_EngineVersion_Text", GroupTypeId.AnalysisResults,
                "i-Tree engine or database version used for the latest result."),
            PlantingInstance("!_S_PLANTING_iTreeResult_InputSignature_Text", GroupTypeId.AnalysisResults,
                "Application-managed signature used to detect stale calculated results."),
            PlantingInstance("!_S_PLANTING_iTreeResult_LastUpdated_Text", GroupTypeId.AnalysisResults,
                "ISO 8601 date and time of the last successful i-Tree calculation."),
            PlantingInstance("!_S_PLANTING_iTreeResult_Status_Text", GroupTypeId.AnalysisResults,
                "Calculation state such as Ready, MissingInput, InvalidInput, Calculated, APIWarning, APIError, or Stale."),
            PlantingInstance("!_S_PLANTING_iTreeResult_UnitSystem_Text", GroupTypeId.AnalysisResults,
                "Unit system currently applied to the numeric i-Tree results: Metric or Imperial."),
            // Calculated — computed by the planting family's formulas (driven by the growth ratios
            // above and the modeled tree age), not typed directly onto the instance.
            PlantingInstance("!_S_PLANTING_TreeFoliage_Height", GroupTypeId.Geometry,
                "Live instance vertical foliage or crown depth calculated by the planting family."),
            PlantingInstance("!_S_PLANTING_TreeFoliage_Width", GroupTypeId.Geometry,
                "Live instance crown width calculated by the planting family."),
            PlantingInstance("!_S_PLANTING_TreeOverall_Height", GroupTypeId.Geometry,
                "Live instance total tree height supplied to i-Tree."),
            PlantingInstance("!_S_PLANTING_TreeTrunk_DBH_Diameter", GroupTypeId.Geometry,
                "Live instance trunk diameter at breast height measured 1.37 metres above ground."),
            PlantingInstance("!_S_PLANTING_TreeTrunk_Diameter", GroupTypeId.Geometry,
                "Live instance modeled trunk diameter."),
            PlantingInstance("!_S_PLANTING_TreeTrunk_Height", GroupTypeId.Geometry,
                "Live instance clear trunk height calculated by the planting family."),
            // Output — the i-Tree API's own benefit results.
            PlantingInstance("!_S_PLANTING_iTreeAir_CORemovedAnnual_Number", GroupTypeId.AnalysisResults,
                "Annual carbon-monoxide removal; metric values use kilograms and imperial values use ounces."),
            PlantingInstance("!_S_PLANTING_iTreeAir_NO2RemovedAnnual_Number", GroupTypeId.AnalysisResults,
                "Annual nitrogen-dioxide removal; metric values use kilograms and imperial values use ounces."),
            PlantingInstance("!_S_PLANTING_iTreeAir_O3RemovedAnnual_Number", GroupTypeId.AnalysisResults,
                "Annual ozone removal; metric values use kilograms and imperial values use ounces."),
            PlantingInstance("!_S_PLANTING_iTreeAir_PM25RemovedAnnual_Number", GroupTypeId.AnalysisResults,
                "Annual PM2.5 removal; metric values use kilograms and imperial values use ounces."),
            PlantingInstance("!_S_PLANTING_iTreeAir_SO2RemovedAnnual_Number", GroupTypeId.AnalysisResults,
                "Annual sulfur-dioxide removal; metric values use kilograms and imperial values use ounces."),
            PlantingInstance("!_S_PLANTING_iTreeCarbon_CO2SequesteredAnnual_Number", GroupTypeId.AnalysisResults,
                "Annual carbon-dioxide sequestration; metric values use kilograms and imperial values use pounds."),
            PlantingInstance("!_S_PLANTING_iTreeWater_RainfallInterceptedAnnual_Volume", GroupTypeId.AnalysisResults,
                "Annual rainfall interception for the modeled tree instance; converted from API cubic metres to Revit volume units."),
            PlantingInstance("!_S_PLANTING_iTreeWater_RunoffAvoidedAnnual_Volume", GroupTypeId.AnalysisResults,
                "Annual avoided runoff for the modeled tree instance; converted from API cubic metres to Revit volume units."),

            // Required manual input — location must be provided (typed, or via Location Finder)
            // before i-Tree can calculate anything for the project.
            ProjectInfo("!_S_PLANTING_iTreeLocation_Latitude_Number", GroupTypeId.Constraints,
                "Decimal latitude used by i-Tree; bind as an instance parameter to Project Information."),
            ProjectInfo("!_S_PLANTING_iTreeLocation_Longitude_Number", GroupTypeId.Constraints,
                "Decimal longitude used by i-Tree; bind as an instance parameter to Project Information."),
            // Optional — a display/reporting preference, not consumed by the i-Tree API itself.
            ProjectInfo("!_S_PLANTING_iTreeUnits_PreferredSystem_Text", GroupTypeId.General,
                "Project Information preference for reporting i-Tree data as Metric or Imperial.")
        ];
    }
}
