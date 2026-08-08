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

        var staleRenameNote = RemoveStaleBindings(document, definition, ownership, apply);
        var row = EvaluateBindingState(document, application, definition, ownership, apply);
        return staleRenameNote is null
            ? row
            : row with { Message = staleRenameNote + (string.IsNullOrEmpty(row.Message) ? string.Empty : " " + row.Message) };
    }

    /// <summary>
    /// Finds and removes (when <paramref name="apply"/> is true) two kinds of stale project binding
    /// so re-running this tool actually retires an old name instead of leaving it bound alongside
    /// the current one:
    /// (1) A rename in the shared parameter file (same GUID, different Name — e.g. the
    ///     !_S_PLANTING_* to !_S_PLT_* rename, or a Number-to-Mass/Currency retype that minted a new
    ///     GUID) is invisible to a project that already had the old name bound: <see cref="FindExistingBinding"/>
    ///     matches by name, so the stale binding lingers forever under its old name unless explicitly
    ///     unbound.
    /// (2) <see cref="ParameterOwnership.LegacyAliases"/> — genuinely different, pre-shared-parameter
    ///     names (like "WWP_Cost_Saved") that were never part of this shared parameter file at all, so
    ///     there's no GUID to match against; these are removed purely by name.
    /// Either way, <see cref="InternalDefinition"/> itself has no settable Name, so removing the old
    /// binding and letting the normal create/update path below bind cleanly under the current name is
    /// what actually "renames" it. Returns a note describing what was (or will be) removed, or null if
    /// nothing stale was found.
    /// </summary>
    private static string? RemoveStaleBindings(
        Document document, ExternalDefinition definition, ParameterOwnership ownership, bool apply)
    {
        var sameGuidStaleNames = new FilteredElementCollector(document)
            .OfClass(typeof(SharedParameterElement))
            .Cast<SharedParameterElement>()
            .Where(element => element.GuidValue == definition.GUID &&
                               !string.Equals(element.Name, ownership.Name, StringComparison.Ordinal))
            .Select(element => element.Name);

        var staleNames = sameGuidStaleNames
            .Concat(ownership.LegacyAliases)
            .Distinct(StringComparer.Ordinal)
            .ToList();

        if (staleNames.Count == 0)
        {
            return null;
        }

        if (!apply)
        {
            return $"Also bound in this project under the legacy name(s) " +
                   $"{string.Join(", ", staleNames.Select(name => $"'{name}'"))} — " +
                   "will remove the stale binding(s) and keep only the current name.";
        }

        var removedNames = new List<string>();
        foreach (var staleName in staleNames)
        {
            if (FindExistingBinding(document, staleName) is { Definition: var staleDefinition } &&
                document.ParameterBindings.Remove(staleDefinition))
            {
                removedNames.Add(staleName);
            }
        }

        return removedNames.Count == 0
            ? null
            : $"Removed the stale binding(s) previously named {string.Join(", ", removedNames.Select(name => $"'{name}'"))}.";
    }

    private static SharedParameterSetupRow EvaluateBindingState(
        Document document,
        UIApplication application,
        ExternalDefinition definition,
        ParameterOwnership ownership,
        bool apply)
    {
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

        if (isInstance != expectedIsInstance)
        {
            return new SharedParameterSetupRow(
                ownership.Name, guidText, ownership.Scope, string.Join(", ", ownership.Categories),
                "Conflict",
                $"Already bound, but not as expected (bound as {(isInstance ? "Instance" : "Type")}, expected {ownership.Scope}). Left unchanged.",
                ownership.Description);
        }

        // Adding a category to an existing binding is a safe, additive change — existing values on
        // already-bound categories are untouched. Only the scope mismatch above is a real conflict,
        // since Instance/Type bindings aren't interchangeable without risking data loss.
        var needsCategoryExpansion = missingCategories.Count > 0;
        var needsRegroup = !existingGroup.Equals(ownership.Group);

        if (!needsCategoryExpansion && !needsRegroup)
        {
            return new SharedParameterSetupRow(
                ownership.Name, guidText, ownership.Scope, string.Join(", ", ownership.Categories),
                "Already valid", null, ownership.Description);
        }

        var changeNotes = new List<string>();
        if (needsCategoryExpansion)
        {
            changeNotes.Add($"add categor{(missingCategories.Count == 1 ? "y" : "ies")}: {string.Join(", ", missingCategories)}");
        }

        if (needsRegroup)
        {
            changeNotes.Add($"move from '{LabelUtils.GetLabelForGroup(existingGroup)}' to '{LabelUtils.GetLabelForGroup(ownership.Group)}'");
        }

        var changeSummary = string.Join("; ", changeNotes) + ".";

        if (!apply)
        {
            return new SharedParameterSetupRow(
                ownership.Name, guidText, ownership.Scope, string.Join(", ", ownership.Categories),
                "Will update", changeSummary, ownership.Description);
        }

        if (needsCategoryExpansion)
        {
            foreach (var category in missingCategories)
            {
                boundCategories.Insert(document.Settings.Categories.get_Item(category));
            }

            Binding expandedBinding = expectedIsInstance
                ? application.Application.Create.NewInstanceBinding(boundCategories)
                : application.Application.Create.NewTypeBinding(boundCategories);

            if (!document.ParameterBindings.ReInsert(internalDefinition, expandedBinding, ownership.Group))
            {
                return new SharedParameterSetupRow(
                    ownership.Name, guidText, ownership.Scope, string.Join(", ", ownership.Categories),
                    "Error", "Revit rejected the updated parameter binding.", ownership.Description);
            }
        }
        else if (needsRegroup)
        {
            internalDefinition.SetGroupTypeId(ownership.Group);
        }

        return new SharedParameterSetupRow(
            ownership.Name, guidText, ownership.Scope, string.Join(", ", ownership.Categories),
            "Updated", changeSummary, ownership.Description);
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

    /// <summary>
    /// <paramref name="LegacyAliases"/> lists every prior name this exact parameter concept has been
    /// known by — either a same-GUID rename (like the !_S_PLANTING_ to !_S_PLT_ migration) or a
    /// genuinely different, pre-shared-parameter name (like the "WWP_" prefixed ones some projects
    /// still have bound from before the LIM parameter family existed). <see cref="RemoveStaleBindings"/>
    /// removes any project binding under one of these names so re-running this tool actually retires
    /// the old one instead of leaving it alongside the current name.
    /// </summary>
    private sealed record ParameterOwnership(
        string Name,
        string Scope,
        IReadOnlyList<BuiltInCategory> Categories,
        ForgeTypeId Group,
        string Description,
        IReadOnlyList<string> LegacyAliases)
    {
        private static ParameterOwnership PlantingType(string name, ForgeTypeId group, string description, IReadOnlyList<string>? legacyAliases = null) =>
            new(name, "Type", [BuiltInCategory.OST_Planting], group, description, legacyAliases ?? []);

        private static ParameterOwnership PlantingInstance(string name, ForgeTypeId group, string description, IReadOnlyList<string>? legacyAliases = null) =>
            new(name, "Instance", [BuiltInCategory.OST_Planting], group, description, legacyAliases ?? []);

        private static ParameterOwnership ProjectInfo(string name, ForgeTypeId group, string description, IReadOnlyList<string>? legacyAliases = null) =>
            new(name, "Instance", [BuiltInCategory.OST_ProjectInformation], group, description, legacyAliases ?? []);

        private static ParameterOwnership PlantingAndFloorInstance(string name, ForgeTypeId group, string description, IReadOnlyList<string>? legacyAliases = null) =>
            new(name, "Instance", [BuiltInCategory.OST_Planting, BuiltInCategory.OST_Floors], group, description, legacyAliases ?? []);

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
            PlantingType("!_S_PLT_GrowthRatio_HeightbyYear_Number", GroupTypeId.Geometry,
                "Type-specific dimensionless annual height-growth ratio used by family formulas."),
            PlantingType("!_S_PLT_GrowthRatio_TrunkDiameterbyYear_Number", GroupTypeId.Geometry,
                "Type-specific dimensionless annual trunk-diameter growth ratio used by family formulas."),
            PlantingType("!_S_PLT_GrowthRatio_WidthbyYear_Number", GroupTypeId.Geometry,
                "Type-specific dimensionless annual crown-width growth ratio used by family formulas."),
            // DataSync_InputSignature/LastUpdated/Status/SourceRecordId are bound Instance (not the
            // legacy workbook's Type scope): Planting Data Sync matches and tracks each Revit
            // instance individually by stable ID, so two instances of the same type can be synced
            // from two different source rows. Only SourceName (which dataset, not which record)
            // stays Type-level. All four are optional sync bookkeeping, not required for i-Tree itself.
            PlantingInstance("!_S_PLT_DataSync_InputSignature_Text", GroupTypeId.General,
                "Application-managed signature used to detect source or Revit data changes."),
            PlantingInstance("!_S_PLT_DataSync_LastUpdated_Text", GroupTypeId.General,
                "ISO 8601 date and time of the latest successful external-data synchronization."),
            PlantingInstance("!_S_PLT_DataSync_Status_Text", GroupTypeId.General,
                "Latest external-data synchronization state for the planting record."),
            PlantingType("!_S_PLT_DataSync_SourceName_Text", GroupTypeId.General,
                "Name of the Excel, Airtable, or configured external data source."),
            PlantingInstance("!_S_PLT_DataSync_SourceRecordId_Text", GroupTypeId.General,
                "Stable external record identifier used to match synchronized planting data."),
            // Required manual input — a person must assign the species (via Tree Searcher or the
            // i-Tree Downloader) before i-Tree can calculate anything for that type.
            PlantingType("!_S_PLT_iTreeSpecies_Code_Text", GroupTypeId.Constraints,
                "i-Tree species code assigned via Tree Searcher or the i-Tree Downloader."),
            // Optional — descriptive species reference data, not itself consumed as calculation input.
            PlantingType("!_S_PLT_iTreeSpecies_CommonName_Text", GroupTypeId.General,
                "Common species name populated from the i-Tree species catalogue (Tree Searcher or the i-Tree Downloader)."),
            PlantingType("!_S_PLT_iTreeSpecies_ReplaceBy_Text", GroupTypeId.General,
                "Replacement species code returned for a deprecated i-Tree catalogue record."),
            PlantingType("!_S_PLT_iTreeSpecies_ScientificName_Text", GroupTypeId.General,
                "Scientific species name populated from the i-Tree species catalogue (Tree Searcher or the i-Tree Downloader)."),
            PlantingType("!_S_PLT_iTreeSpecies_Type_Text", GroupTypeId.General,
                "Taxonomic type returned by the i-Tree species catalogue."),

            // Required manual input — typed per instance, and fed straight to the i-Tree API.
            PlantingInstance("!_S_PLT_TreeGrowth_Years_Number", GroupTypeId.Constraints,
                "Instance-specific modeled tree age used by family growth formulas."),
            PlantingInstance("!_S_PLT_iTreeInput_Condition_Text", GroupTypeId.Constraints,
                "Tree condition: Excellent, Good, Fair, Poor, Critical, Dying, or Dead."),
            PlantingInstance("!_S_PLT_iTreeInput_CrownExposure_Number", GroupTypeId.Constraints,
                "Crown light exposure from 0 fully shaded to 5 fully exposed."),
            // Output — the calculation's own status/tracking fields, written by i-Tree Calculator.
            PlantingInstance("!_S_PLT_iTreeResult_Details_Text", GroupTypeId.AnalysisResults,
                "Missing-input, validation, warning, or API-error details for the tree instance."),
            PlantingInstance("!_S_PLT_iTreeResult_EngineVersion_Text", GroupTypeId.AnalysisResults,
                "i-Tree engine or database version used for the latest result."),
            PlantingInstance("!_S_PLT_iTreeResult_InputSignature_Text", GroupTypeId.AnalysisResults,
                "Application-managed signature used to detect stale calculated results."),
            PlantingInstance("!_S_PLT_iTreeResult_LastUpdated_Text", GroupTypeId.AnalysisResults,
                "ISO 8601 date and time of the last successful i-Tree calculation."),
            PlantingInstance("!_S_PLT_iTreeResult_Status_Text", GroupTypeId.AnalysisResults,
                "Calculation state such as Ready, MissingInput, InvalidInput, Calculated, APIWarning, APIError, or Stale."),
            PlantingInstance("!_S_PLT_iTreeResult_UnitSystem_Text", GroupTypeId.AnalysisResults,
                "Unit system currently applied to the numeric i-Tree results: Metric or Imperial."),
            PlantingInstance("!_S_PLT_iTreeResult_CurrencyUsed_Text", GroupTypeId.AnalysisResults,
                "Currency the numeric i-Tree monetary results (CostSaved and its three category components, annual and lifetime) were converted into for this instance's latest calculation: USD, GBP, EUR, CAD, AUD, or NZD."),
            PlantingInstance("!_S_PLT_iTreeResult_ExchangeRateUsed_Number", GroupTypeId.AnalysisResults,
                "USD exchange rate applied to this instance's latest monetary results (1.0 when CurrencyUsed is USD). Recorded so a historical calculation's dollar figures can be reconstructed even as rates change."),
            // Calculated — computed by the planting family's formulas (driven by the growth ratios
            // above and the modeled tree age), not typed directly onto the instance.
            PlantingInstance("!_S_PLT_TreeFoliage_Height", GroupTypeId.Geometry,
                "Live instance vertical foliage or crown depth calculated by the planting family. Metric: m. Imperial: ft (per the project's Length unit settings)."),
            PlantingInstance("!_S_PLT_TreeFoliage_Width", GroupTypeId.Geometry,
                "Live instance crown width calculated by the planting family. Metric: m. Imperial: ft (per the project's Length unit settings)."),
            PlantingInstance("!_S_PLT_TreeOverall_Height", GroupTypeId.Geometry,
                "Live instance total tree height supplied to i-Tree. Metric: m. Imperial: ft (per the project's Length unit settings)."),
            PlantingInstance("!_S_PLT_TreeTrunk_DBH_Diameter", GroupTypeId.Geometry,
                "Live instance trunk diameter at breast height measured 1.37 metres above ground. Metric: cm. Imperial: in (per the project's Length unit settings)."),
            PlantingInstance("!_S_PLT_TreeTrunk_Diameter", GroupTypeId.Geometry,
                "Live instance modeled trunk diameter. Metric: cm. Imperial: in (per the project's Length unit settings)."),
            PlantingInstance("!_S_PLT_TreeTrunk_Height", GroupTypeId.Geometry,
                "Live instance clear trunk height calculated by the planting family. Metric: m. Imperial: ft (per the project's Length unit settings)."),
            // Output — the i-Tree API's own benefit results. Unified under the iTreeResult_
            // prefix (previously split across iTreeAir_/iTreeWater_/iTreeCarbon_) so every
            // i-Tree-sourced benefit reads as one consistent family in the Properties palette.
            PlantingInstance("!_S_PLT_iTreeResult_CORemovedAnnual_Mass", GroupTypeId.AnalysisResults,
                "Annual carbon-monoxide removal (Revit Mass parameter — displays per the project's Mass unit settings).",
                legacyAliases: ["!_S_PLT_iTreeResult_CORemovedAnnual_Number"]),
            PlantingInstance("!_S_PLT_iTreeResult_NO2RemovedAnnual_Mass", GroupTypeId.AnalysisResults,
                "Annual nitrogen-dioxide removal (Revit Mass parameter — displays per the project's Mass unit settings).",
                legacyAliases: ["!_S_PLT_iTreeResult_NO2RemovedAnnual_Number"]),
            PlantingInstance("!_S_PLT_iTreeResult_O3RemovedAnnual_Mass", GroupTypeId.AnalysisResults,
                "Annual ozone removal (Revit Mass parameter — displays per the project's Mass unit settings).",
                legacyAliases: ["!_S_PLT_iTreeResult_O3RemovedAnnual_Number"]),
            PlantingInstance("!_S_PLT_iTreeResult_PM25RemovedAnnual_Mass", GroupTypeId.AnalysisResults,
                "Annual PM2.5 removal (Revit Mass parameter — displays per the project's Mass unit settings).",
                legacyAliases: ["!_S_PLT_iTreeResult_PM25RemovedAnnual_Number"]),
            PlantingInstance("!_S_PLT_iTreeResult_SO2RemovedAnnual_Mass", GroupTypeId.AnalysisResults,
                "Annual sulfur-dioxide removal (Revit Mass parameter — displays per the project's Mass unit settings).",
                legacyAliases: ["!_S_PLT_iTreeResult_SO2RemovedAnnual_Number"]),
            // Bound to both Planting and Floors: i-Tree Calculator writes these for trees; the
            // Floor Calculator writes them for floors (from the WWP LDS coefficient table times
            // area) — same two parameters either way, so a schedule mixing both element types
            // still shows one consistent CO2/runoff column instead of two parallel ones.
            PlantingAndFloorInstance("!_S_PLT_iTreeResult_CO2SequesteredAnnual_Mass", GroupTypeId.AnalysisResults,
                "Annual carbon-dioxide sequestration (Revit Mass parameter — displays per the project's Mass unit settings).",
                legacyAliases: ["!_S_PLT_iTreeResult_CO2SequesteredAnnual_Number"]),
            PlantingInstance("!_S_PLT_iTreeResult_RainfallInterceptedAnnual_Volume", GroupTypeId.AnalysisResults,
                "Annual rainfall interception for the modeled tree instance. Metric: m³. Imperial: ft³ (Revit Volume parameter — displays per the project's Volume unit settings)."),
            PlantingAndFloorInstance("!_S_PLT_iTreeResult_RunoffAvoidedAnnual_Volume", GroupTypeId.AnalysisResults,
                "Annual avoided runoff. Metric: m³. Imperial: ft³ (Revit Volume parameter — displays per the project's Volume unit settings). For floors, Floor Calculator always writes the landscape data sheet's metric m³ value as-is."),
            // Bound to both Planting and Floors, same reuse pattern as CO2/runoff above: i-Tree
            // Calculator writes this for trees (from the API's own monetary benefit figure);
            // Floor Calculator writes it for floors (from the WWP LDS coefficient table times area).
            PlantingAndFloorInstance("!_S_PLT_iTreeResult_CostSavedAnnual_Currency", GroupTypeId.AnalysisResults,
                "Estimated annual cost saved — for trees, i-Tree Calculator's calculated annual monetary benefit; for floors, the WWP landscape data sheet's per-square-metre coefficient times area (Revit Currency parameter — displays per the project's Currency unit settings).",
                legacyAliases: ["!_S_PLT_iTreeResult_CostSavedAnnual_Number", "WWP_Cost_Saved"]),
            // Output — CO2 Equivalent and the per-category dollar breakdown behind CostSavedAnnual.
            // Trees only: neither has a Floor/LDS equivalent, so unlike the results above these
            // aren't PlantingAndFloorInstance.
            PlantingInstance("!_S_PLT_iTreeResult_CO2EquivalentAnnual_Mass", GroupTypeId.AnalysisResults,
                "Annual CO2 equivalent of sequestered carbon (sequestered carbon × 3.67) (Revit Mass parameter — displays per the project's Mass unit settings).",
                legacyAliases: ["!_S_PLT_iTreeResult_CO2EquivalentAnnual_Number"]),
            PlantingInstance("!_S_PLT_iTreeResult_CarbonCostSavedAnnual_Currency", GroupTypeId.AnalysisResults,
                "Annual carbon-benefit dollar value — one of three components summed into CostSavedAnnual for trees (Revit Currency parameter).",
                legacyAliases: ["!_S_PLT_iTreeResult_CarbonCostSavedAnnual_Number"]),
            PlantingInstance("!_S_PLT_iTreeResult_StormWaterCostSavedAnnual_Currency", GroupTypeId.AnalysisResults,
                "Annual storm-water-benefit dollar value — one of three components summed into CostSavedAnnual for trees (Revit Currency parameter).",
                legacyAliases: ["!_S_PLT_iTreeResult_StormWaterCostSavedAnnual_Number"]),
            PlantingInstance("!_S_PLT_iTreeResult_AirPollutionCostSavedAnnual_Currency", GroupTypeId.AnalysisResults,
                "Annual air-quality-benefit dollar value — one of three components summed into CostSavedAnnual for trees (Revit Currency parameter).",
                legacyAliases: ["!_S_PLT_iTreeResult_AirPollutionCostSavedAnnual_Number"]),
            // Output — lifetime cumulative i-Tree results, parallel to the Annual set above. "Lifetime"
            // here means summed over however many years the instance's own TreeGrowth_Years input is
            // set to — the API returns one entry per requested year and these sum all of them, so a
            // tree modeled at Years=25 gets a 25-year total, not a fixed 20-year one. Trees only:
            // Floor Calculator has no equivalent multi-year projection, so none of these are
            // PlantingAndFloorInstance even where their Annual counterpart is.
            PlantingInstance("!_S_PLT_iTreeResult_CO2SequesteredLifetimeTotal_Mass", GroupTypeId.AnalysisResults,
                "Lifetime cumulative carbon-dioxide sequestration, summed over the instance's modeled TreeGrowth_Years (Revit Mass parameter).",
                legacyAliases: ["!_S_PLT_iTreeResult_CO2SequesteredLifetimeTotal_Number"]),
            PlantingInstance("!_S_PLT_iTreeResult_CORemovedLifetimeTotal_Mass", GroupTypeId.AnalysisResults,
                "Lifetime cumulative carbon-monoxide removal, summed over the instance's modeled TreeGrowth_Years (Revit Mass parameter).",
                legacyAliases: ["!_S_PLT_iTreeResult_CORemovedLifetimeTotal_Number"]),
            PlantingInstance("!_S_PLT_iTreeResult_NO2RemovedLifetimeTotal_Mass", GroupTypeId.AnalysisResults,
                "Lifetime cumulative nitrogen-dioxide removal, summed over the instance's modeled TreeGrowth_Years (Revit Mass parameter).",
                legacyAliases: ["!_S_PLT_iTreeResult_NO2RemovedLifetimeTotal_Number"]),
            PlantingInstance("!_S_PLT_iTreeResult_O3RemovedLifetimeTotal_Mass", GroupTypeId.AnalysisResults,
                "Lifetime cumulative ozone removal, summed over the instance's modeled TreeGrowth_Years (Revit Mass parameter).",
                legacyAliases: ["!_S_PLT_iTreeResult_O3RemovedLifetimeTotal_Number"]),
            PlantingInstance("!_S_PLT_iTreeResult_SO2RemovedLifetimeTotal_Mass", GroupTypeId.AnalysisResults,
                "Lifetime cumulative sulfur-dioxide removal, summed over the instance's modeled TreeGrowth_Years (Revit Mass parameter).",
                legacyAliases: ["!_S_PLT_iTreeResult_SO2RemovedLifetimeTotal_Number"]),
            PlantingInstance("!_S_PLT_iTreeResult_PM25RemovedLifetimeTotal_Mass", GroupTypeId.AnalysisResults,
                "Lifetime cumulative PM2.5 removal, summed over the instance's modeled TreeGrowth_Years (Revit Mass parameter).",
                legacyAliases: ["!_S_PLT_iTreeResult_PM25RemovedLifetimeTotal_Number"]),
            PlantingInstance("!_S_PLT_iTreeResult_RainfallInterceptedLifetimeTotal_Volume", GroupTypeId.AnalysisResults,
                "Lifetime cumulative rainfall interception for the modeled tree instance, summed over its modeled TreeGrowth_Years. Metric: m³. Imperial: ft³ (Revit Volume parameter — displays per the project's Volume unit settings)."),
            PlantingInstance("!_S_PLT_iTreeResult_RunoffAvoidedLifetimeTotal_Volume", GroupTypeId.AnalysisResults,
                "Lifetime cumulative avoided runoff for the modeled tree instance, summed over its modeled TreeGrowth_Years. Metric: m³. Imperial: ft³ (Revit Volume parameter — displays per the project's Volume unit settings)."),
            PlantingInstance("!_S_PLT_iTreeResult_CostSavedLifetimeTotal_Currency", GroupTypeId.AnalysisResults,
                "Lifetime cumulative estimated cost saved, from i-Tree Calculator's calculated monetary benefit summed over the instance's modeled TreeGrowth_Years (Revit Currency parameter).",
                legacyAliases: ["!_S_PLT_iTreeResult_CostSavedLifetimeTotal_Number"]),
            PlantingInstance("!_S_PLT_iTreeResult_CO2EquivalentLifetimeTotal_Mass", GroupTypeId.AnalysisResults,
                "Lifetime cumulative CO2 equivalent of sequestered carbon (sequestered carbon × 3.67), summed over the instance's modeled TreeGrowth_Years (Revit Mass parameter).",
                legacyAliases: ["!_S_PLT_iTreeResult_CO2EquivalentLifetimeTotal_Number"]),
            PlantingInstance("!_S_PLT_iTreeResult_CarbonCostSavedLifetimeTotal_Currency", GroupTypeId.AnalysisResults,
                "Lifetime cumulative carbon-benefit dollar value — one of three components summed into CostSavedLifetimeTotal (Revit Currency parameter).",
                legacyAliases: ["!_S_PLT_iTreeResult_CarbonCostSavedLifetimeTotal_Number"]),
            PlantingInstance("!_S_PLT_iTreeResult_StormWaterCostSavedLifetimeTotal_Currency", GroupTypeId.AnalysisResults,
                "Lifetime cumulative storm-water-benefit dollar value — one of three components summed into CostSavedLifetimeTotal (Revit Currency parameter).",
                legacyAliases: ["!_S_PLT_iTreeResult_StormWaterCostSavedLifetimeTotal_Number"]),
            PlantingInstance("!_S_PLT_iTreeResult_AirPollutionCostSavedLifetimeTotal_Currency", GroupTypeId.AnalysisResults,
                "Lifetime cumulative air-quality-benefit dollar value — one of three components summed into CostSavedLifetimeTotal (Revit Currency parameter).",
                legacyAliases: ["!_S_PLT_iTreeResult_AirPollutionCostSavedLifetimeTotal_Number"]),

            // Required manual input — location must be provided (typed, or via Location Finder)
            // before i-Tree can calculate anything for the project.
            ProjectInfo("!_S_PLT_iTreeLocation_Latitude_Number", GroupTypeId.Constraints,
                "Decimal latitude used by i-Tree; bind as an instance parameter to Project Information."),
            ProjectInfo("!_S_PLT_iTreeLocation_Longitude_Number", GroupTypeId.Constraints,
                "Decimal longitude used by i-Tree; bind as an instance parameter to Project Information."),
            // Optional — a display/reporting preference, not consumed by the i-Tree API itself.
            ProjectInfo("!_S_PLT_iTreeUnits_PreferredSystem_Text", GroupTypeId.General,
                "Project Information preference for reporting i-Tree data as Metric or Imperial."),
            // Set via the i-Tree Calculator currency dropdown (which writes it back through
            // PublishPreferredCurrency), not typed free-form — RevitModelScanner.SupportedCurrencyCodes
            // is the source of truth for the offered list, mirrored in this description for anyone
            // reading Properties without the tool open.
            ProjectInfo("!_S_PLT_iTreeUnits_PreferredCurrency_Text", GroupTypeId.General,
                "Project Information preference for reporting i-Tree monetary benefits in USD, GBP, EUR, CAD, AUD, or NZD."),
            // Multiline text — a JSON blob, not a human-typed value. Apps read/write this through
            // GetProjectSettingsJson/PublishProjectSettingsJson; see Shared.Services.ProjectSettingsSnapshot
            // for the shape. Project-wins by design: apps overwrite their own local machine settings
            // from this on every load, so it's this parameter — not any one machine — that's the
            // durable record once a project has been saved with settings at least once.
            ProjectInfo("!_S_PLT_Settings_Json_Text", GroupTypeId.General,
                "Consolidated JSON snapshot of non-secret tool settings (unit system, currency, location, data source, parameter mappings, type aliases) so they travel with the project file. Never contains API keys or tokens."),

            // --- Phase 2: Floor Calculator (WWP landscape data sheet coefficients) ---
            // Bound to both Planting and Floors, not Floors alone: these Revit Floor elements
            // represent planted/paved landscape areas (lawn, meadow, hardscape), not building
            // floors, so they share the PLANTING parameter family — and it means these fields
            // are already available if a future update lets Planting instances fall back to the
            // coefficient table too, not just i-Tree. There's no manual-override parameter here:
            // Floor Calculator lets you edit a value before writing it, so "manual" only ever
            // shows up as a different number in ResultSource — never a second parameter to check.
            //
            // Required manual input — a person must assign the landscape type (via Floor
            // Calculator's search/match) before a coefficient row can be looked up for it.
            PlantingAndFloorInstance("!_S_PLT_LDS_Type_Text", GroupTypeId.Constraints,
                "WWP landscape data sheet type assigned via Floor Calculator (e.g. Lawn, Meadow, Granite - Blanco Cristal)."),
            // Optional — internal bookkeeping, not something a person reads directly.
            PlantingAndFloorInstance("!_S_PLT_LDS_MatchKey_Text", GroupTypeId.General,
                "Stable lookup key into the cached WWP landscape data sheet, used to re-find the exact coefficient row without re-matching."),
            // Output — Floor Calculator's own status/tracking fields, mirroring i-Tree Calculator's pattern.
            PlantingAndFloorInstance("!_S_PLT_LDS_ResultSource_Text", GroupTypeId.AnalysisResults,
                "Whether the currently written values came from the coefficient table as calculated, or were edited manually in Floor Calculator before writing."),
            PlantingAndFloorInstance("!_S_PLT_LDS_LastCalculated_Text", GroupTypeId.AnalysisResults,
                "ISO 8601 date and time of the last Floor Calculator run for this element."),
            // Output — the WWP landscape data sheet's per-square-metre coefficients times area,
            // for metrics with no i-Tree equivalent to reuse. (CostSavedAnnual moved up to the
            // iTreeResult_ group above — it's now shared with the i-Tree tree-benefit result.)
            PlantingAndFloorInstance("!_S_PLT_LDS_OxygenProducedAnnual_Mass", GroupTypeId.AnalysisResults,
                "Estimated annual oxygen produced, from the WWP landscape data sheet's per-square-metre coefficient times area (Revit Mass parameter).",
                legacyAliases: ["!_S_PLT_LDS_OxygenProducedAnnual_Number"]),
            PlantingAndFloorInstance("!_S_PLT_LDS_TotalGWP_Mass", GroupTypeId.AnalysisResults,
                "Total global warming potential (product plus transport) from the WWP landscape data sheet's per-square-metre coefficient times area — an embodied-impact figure, not a benefit (Revit Mass parameter).",
                legacyAliases: ["!_S_PLT_LDS_TotalGWP_Number"]),
            PlantingAndFloorInstance("!_S_PLT_LDS_SurfaceTempReduction_Number", GroupTypeId.AnalysisResults,
                "Surface temperature reduction for this landscape type, from the WWP landscape data sheet — an intrinsic material property, not scaled by area. Metric: °C. (Metric-only — no Imperial conversion.)"),
            PlantingAndFloorInstance("!_S_PLT_LDS_AirTempReduction_Number", GroupTypeId.AnalysisResults,
                "Air temperature reduction at 1.5m height for this landscape type, from the WWP landscape data sheet — an intrinsic material property, not scaled by area. Metric: °C. (Metric-only — no Imperial conversion.)"),

            // Optional — descriptive landscape-type reference data from the WWP landscape data
            // sheet, same bucket as the i-Tree species reference fields above.
            PlantingAndFloorInstance("!_S_PLT_LDS_Origin_Text", GroupTypeId.General,
                "Geographic origin of the landscape type, from the WWP landscape data sheet."),
            // Recycled from the pre-existing "Données d'indentification" group (same GUIDs, moved
            // into this group and given a description) rather than minting new duplicates — they
            // predate this parameter family but were never wired into EnsureParameters. Renamed to
            // the !_S_PLT_LDS_ prefix for consistency with their siblings above; same GUIDs, so
            // existing bindings still resolve correctly under the new name.
            PlantingAndFloorInstance("!_S_PLT_LDS_CalculationType_Text", GroupTypeId.General,
                "Landscape type classification (e.g. Tree, Shrub species, Surfaces), from the WWP landscape data sheet."),
            PlantingAndFloorInstance("!_S_PLT_LDS_Category_Text", GroupTypeId.General,
                "Landscape data sheet category for this type, from the WWP landscape data sheet."),
            PlantingAndFloorInstance("!_S_PLT_LDS_SubCategory_Text", GroupTypeId.General,
                "Landscape data sheet sub-category for this type, from the WWP landscape data sheet."),
            // Output — additional WWP landscape data sheet coefficients times area, same pattern as
            // CostSavedAnnual/OxygenProducedAnnual above.
            PlantingAndFloorInstance("!_S_PLT_LDS_MaintenanceCostAnnual_Currency", GroupTypeId.AnalysisResults,
                "Estimated annual maintenance cost, from the WWP landscape data sheet's per-square-metre coefficient times area (Revit Currency parameter).",
                legacyAliases: ["!_S_PLT_LDS_MaintenanceCostAnnual_Number"]),
            PlantingAndFloorInstance("!_S_PLT_LDS_PollutantsRemovedAnnual_Mass", GroupTypeId.AnalysisResults,
                "Estimated annual pollutants removed, from the WWP landscape data sheet's per-square-metre coefficient times area (Revit Mass parameter).",
                legacyAliases: ["!_S_PLT_LDS_PollutantsRemovedAnnual_Number"]),
            // Defined in the shared parameter file and referenced by the main App's
            // LandscapeCalculationEngine/mapping-suggestion feature, but never actually bound here
            // until now — closing that gap so "Ensure Shared Parameters" actually guarantees these
            // exist, same as their MaintenanceCost/PollutantsRemoved siblings above.
            PlantingAndFloorInstance("!_S_PLT_LDS_AvoidedWaterRunoffAnnual_Number", GroupTypeId.AnalysisResults,
                "Estimated annual avoided water runoff, from the WWP landscape data sheet's per-square-metre coefficient times area.",
                legacyAliases: ["WWP_Avoided_Water_Runoff"]),
            PlantingAndFloorInstance("!_S_PLT_LDS_OxygenLevelsAnnual_Number", GroupTypeId.AnalysisResults,
                "Estimated annual oxygen levels, from the WWP landscape data sheet's per-square-metre coefficient times area.",
                legacyAliases: ["WWP_Oxygen_Levels"]),
            PlantingAndFloorInstance("!_S_PLT_LDS_CarbonDioxideSequestrationAnnual_Number", GroupTypeId.AnalysisResults,
                "Estimated annual carbon dioxide sequestration, from the WWP landscape data sheet's per-square-metre coefficient times area.",
                legacyAliases: ["WWP_Carbon_Dioxide_Sequestration"]),
            PlantingAndFloorInstance("!_S_PLT_LDS_ProductGWP_Number", GroupTypeId.AnalysisResults,
                "Embodied product global warming potential, from the WWP landscape data sheet — a component of total GWP, not scaled by area. Metric: kg CO2e/m². (Metric-only — no Imperial conversion.)"),
            PlantingAndFloorInstance("!_S_PLT_LDS_TransportGWP_Number", GroupTypeId.AnalysisResults,
                "Embodied transport global warming potential, from the WWP landscape data sheet — a component of total GWP, not scaled by area. Metric: kg CO2e/m². (Metric-only — no Imperial conversion.)"),
            PlantingAndFloorInstance("!_S_PLT_LDS_PollenAnnual_Number", GroupTypeId.AnalysisResults,
                "Estimated annual pollen production, from the WWP landscape data sheet. Metric: kg/m²/yr. (Metric-only — no Imperial conversion.)"),
            // Output — intrinsic reference values from the WWP landscape data sheet, same bucket as
            // SurfaceTempReduction/AirTempReduction above (not scaled by area).
            PlantingAndFloorInstance("!_S_PLT_LDS_MaxHeight_Number", GroupTypeId.AnalysisResults,
                "Maximum mature height for this landscape type, from the WWP landscape data sheet — an intrinsic reference value, not scaled by area. Metric: m. (Metric-only — no Imperial conversion.)"),
            PlantingAndFloorInstance("!_S_PLT_LDS_MaxWidth_Number", GroupTypeId.AnalysisResults,
                "Maximum mature width/spread for this landscape type, from the WWP landscape data sheet — an intrinsic reference value, not scaled by area. Metric: m. (Metric-only — no Imperial conversion.)"),
            PlantingAndFloorInstance("!_S_PLT_LDS_IrrigationDemandFactor_Number", GroupTypeId.AnalysisResults,
                "Irrigation demand plant factor for this landscape type, from the WWP landscape data sheet — an intrinsic reference value, not scaled by area.")
        ];
    }
}
