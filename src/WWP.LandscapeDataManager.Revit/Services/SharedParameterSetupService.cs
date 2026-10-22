using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using System.IO;
using WWP.LandscapeDataManager.Contracts;

namespace WWP.LandscapeDataManager.Revit.Services;

/// <summary>
/// Creates/validates the project-parameter bindings the LIM planting and i-Tree workflow needs,
/// from the "PLANTING - iTree" group of the WWP shared-parameter file. Every definition is
/// checked before it is touched. Compatible BNG/Connectivity results retain their project's shared
/// IDs; ambiguous identities, data-type mismatches and unsupported scope changes remain conflicts.
/// The three LDS Instance-to-Type migrations are
/// previewed and applied only after all instances of each type are confirmed to agree.
/// </summary>
internal static class SharedParameterSetupService
{
    private const string SharedGroupName = "PLANTING - iTree";
    private const string SharedParameterResourceName = "LIM.LandscapeData.SharedParameters.txt";

    /// <summary>
    /// Read-only dry run: reports what <see cref="EnsureParameters"/> would do for every known
    /// LIM parameter, without starting a transaction or touching the document. Missing bindings
    /// are reported as "Will create" instead of being created.
    /// </summary>
    public static SharedParameterSetupResult PreviewParameters(UIApplication application)
    {
        using var catalog = OpenBundledCatalog(application);
        var (document, definitionsByName) = catalog;
        var rows = ParameterOwnership.All
            .Select(ownership => EvaluateOne(document, application, definitionsByName, ownership, apply: false))
            .ToList();
        return new SharedParameterSetupResult(document.Title, rows);
    }

    public static SharedParameterSetupResult EnsureParameters(
        UIApplication application,
        EnsureSharedParametersRequest request)
    {
        using var catalog = OpenBundledCatalog(application);
        var (document, definitionsByName) = catalog;
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

            if (transaction.Commit() != TransactionStatus.Committed)
            {
                throw new InvalidOperationException("Revit did not commit parameter setup. Reload the parameters before retrying.");
            }
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

    private static BundledCatalog OpenBundledCatalog(UIApplication application)
    {
        var document = application.ActiveUIDocument?.Document
                       ?? throw new InvalidOperationException("Open a Revit project before setting up parameters.");

        var assembly = typeof(SharedParameterSetupService).Assembly;
        using var resource = assembly.GetManifestResourceStream(SharedParameterResourceName)
                             ?? throw new InvalidOperationException("The LIM shared-parameter catalog is missing from this installation.");
        var temporaryPath = Path.Combine(Path.GetTempPath(), $"LIM-SharedParameters-{Guid.NewGuid():N}.txt");
        using (var file = File.Create(temporaryPath))
        {
            resource.CopyTo(file);
        }

        var previousPath = application.Application.SharedParametersFilename;
        try
        {
            application.Application.SharedParametersFilename = temporaryPath;
            var definitionFile = application.Application.OpenSharedParameterFile()
                                  ?? throw new InvalidOperationException("The built-in LIM shared-parameter catalog could not be opened.");
            var group = definitionFile.Groups.get_Item(SharedGroupName)
                        ?? throw new InvalidOperationException(
                            $"The built-in LIM catalog does not contain a group named '{SharedGroupName}'.");
            var definitionsByName = group.Definitions
                .OfType<ExternalDefinition>()
                .ToDictionary(definition => definition.Name, StringComparer.Ordinal);

            return new BundledCatalog(application, previousPath, temporaryPath, document, definitionsByName);
        }
        catch
        {
            application.Application.SharedParametersFilename = previousPath;
            File.Delete(temporaryPath);
            throw;
        }
    }

    private sealed class BundledCatalog(
        UIApplication application,
        string previousPath,
        string temporaryPath,
        Document document,
        IReadOnlyDictionary<string, ExternalDefinition> definitionsByName) : IDisposable
    {
        public void Deconstruct(out Document openedDocument, out IReadOnlyDictionary<string, ExternalDefinition> openedDefinitions)
        {
            openedDocument = document;
            openedDefinitions = definitionsByName;
        }

        public void Dispose()
        {
            application.Application.SharedParametersFilename = previousPath;
            try { File.Delete(temporaryPath); }
            catch (System.IO.IOException) { }
            catch (UnauthorizedAccessException) { }
        }
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

        // A restored result name may already be bound with an older catalog's shared ID.
        // Validate and retain that binding before any legacy-alias cleanup can remove data.
        if (ResultParameterReusePolicy.Supports(ownership.Name) &&
            (FindExistingBinding(document, ownership.Name) is not null ||
             new FilteredElementCollector(document).OfClass(typeof(SharedParameterElement))
                 .Cast<SharedParameterElement>().Any(element =>
                     string.Equals(element.Name, ownership.Name, StringComparison.Ordinal))))
        {
            return EvaluateBindingState(document, application, definition, ownership, apply);
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
            // LegacyAliases is a static list of names that MIGHT be stale in some project — a project
            // that never had the old name bound (e.g. a brand-new file) has nothing to report. Verify
            // an actual binding exists before claiming one does, same check the apply branch below
            // already relies on to decide what to remove.
            .Where(name => FindExistingBinding(document, name) is not null)
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
        var namedDefinitions = new FilteredElementCollector(document)
            .OfClass(typeof(SharedParameterElement))
            .Cast<SharedParameterElement>()
            .Where(element => string.Equals(element.Name, ownership.Name, StringComparison.Ordinal))
            .ToList();
        var conflictingElement = namedDefinitions.FirstOrDefault(element => element.GuidValue != definition.GUID);
        var existingBinding = FindExistingBinding(document, ownership.Name);
        string? reuseNote = null;
        if (ResultParameterReusePolicy.Supports(ownership.Name) && ownership.Scope == "Instance" &&
            (namedDefinitions.Count > 0 || existingBinding is not null))
        {
            var namedBindings = FindExistingBindings(document, ownership.Name);
            var candidates = namedDefinitions.Select(element =>
            {
                var scopes = namedBindings.Where(binding => binding.Definition.Id == element.Id)
                    .Select(binding => binding.IsInstance).Distinct().ToList();
                return new ExistingResultParameter(element.GuidValue, element.GetDefinition().GetDataType().TypeId,
                    scopes.Count == 1 ? scopes[0] : null);
            }).ToList();
            var decision = ResultParameterReusePolicy.Evaluate(
                ownership.Name, definition.GetDataType().TypeId, candidates,
                namedBindings.Select(binding => binding.Definition.Id.Value).Distinct().Count());
            if (!decision.CanReuse)
            {
                return new SharedParameterSetupRow(
                    ownership.Name, guidText, ownership.Scope, string.Join(", ", ownership.Categories),
                    "Conflict", decision.Conflict, ownership.Description);
            }

            guidText = decision.ExistingGuid!.Value.ToString();
            if (decision.ExistingGuid.Value != definition.GUID)
            {
                reuseNote = $"Using this project's existing shared ID ({guidText}); " +
                           "the result name, data type and Instance binding match. Existing parameter and stored values retained.";
            }
        }
        else if (conflictingElement is not null)
        {
            return new SharedParameterSetupRow(
                ownership.Name, guidText, ownership.Scope, string.Join(", ", ownership.Categories),
                "Conflict", $"A different parameter named '{ownership.Name}' already exists in this project " +
                $"with a different ID ({conflictingElement.GuidValue}). It was left unchanged.",
                ownership.Description);
        }

        var row = EvaluateResolvedBindingState(
            document, application, definition, ownership, apply, guidText, existingBinding);
        return reuseNote is null ? row : row with
        {
            Message = reuseNote + (string.IsNullOrEmpty(row.Message) ? string.Empty : " " + row.Message)
        };
    }

    private static SharedParameterSetupRow EvaluateResolvedBindingState(
        Document document,
        UIApplication application,
        ExternalDefinition definition,
        ParameterOwnership ownership,
        bool apply,
        string guidText,
        ParameterBindingState? existingBinding)
    {
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

        var (isInstance, boundCategories, existingGroup, internalDefinition) = existingBinding;
        var expectedIsInstance = ownership.Scope == "Instance";
        var boundCategoryNames = boundCategories.Cast<Category>().Select(category => category.Name).ToHashSet();
        var missingCategories = ownership.Categories.Where(category =>
            !boundCategoryNames.Contains(document.Settings.Categories.get_Item(category).Name)).ToList();

        if (isInstance != expectedIsInstance)
        {
            if (isInstance && !expectedIsInstance && InstanceToTypeMigrationPlanner.Supports(ownership.Name))
            {
                return ParameterBindingScopeMigrationService.MigrateToType(
                    document, application, definition, internalDefinition, boundCategories,
                    ownership.Categories, ownership.Group, ownership.Description, apply);
            }

            if (!isInstance && expectedIsInstance && ParameterBindingScopeMigrationService.SupportsTypeToInstance(ownership.Name))
            {
                return ParameterBindingScopeMigrationService.MigrateToInstance(
                    document, application, definition, internalDefinition, boundCategories,
                    ownership.Categories, ownership.Group, ownership.Description, apply);
            }

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
        if (needsRegroup)
        {
            internalDefinition.SetGroupTypeId(ownership.Group);
        }

        // Trees are never grouped, so the growth parameters don't need "values can vary by group
        // instance" — Revit refuses it anyway for parameters a loaded family defines.
        return new SharedParameterSetupRow(
            ownership.Name, guidText, ownership.Scope, string.Join(", ", ownership.Categories),
            "Updated", changeSummary, ownership.Description);
    }

    private sealed record ParameterBindingState(
        bool IsInstance, CategorySet Categories, ForgeTypeId Group, InternalDefinition Definition);

    private static ParameterBindingState? FindExistingBinding(Document document, string name) =>
        FindExistingBindings(document, name).FirstOrDefault();

    private static List<ParameterBindingState> FindExistingBindings(Document document, string name)
    {
        var bindings = new List<ParameterBindingState>();
        var iterator = document.ParameterBindings.ForwardIterator();
        iterator.Reset();
        while (iterator.MoveNext())
        {
            if (iterator.Key is not InternalDefinition definition || !string.Equals(definition.Name, name, StringComparison.Ordinal))
            {
                continue;
            }

            var binding = iterator.Current switch
            {
                InstanceBinding instanceBinding => new ParameterBindingState(true, instanceBinding.Categories, definition.GetGroupTypeId(), definition),
                TypeBinding typeBinding => new ParameterBindingState(false, typeBinding.Categories, definition.GetGroupTypeId(), definition),
                _ => null
            };
            if (binding is not null)
            {
                bindings.Add(binding);
            }
        }

        return bindings;
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

        // Same two categories as PlantingAndFloorInstance above, but Type scope — for landscape-type
        // reference values that are the same for every instance of a species/type (e.g. a WWP sheet
        // reference figure for a standard starter tree), rather than something that varies per
        // individual placed element or needs area-scaling.
        private static ParameterOwnership PlantingAndFloorType(string name, ForgeTypeId group, string description, IReadOnlyList<string>? legacyAliases = null) =>
            new(name, "Type", [BuiltInCategory.OST_Planting, BuiltInCategory.OST_Floors], group, description, legacyAliases ?? []);

        private static ParameterOwnership FloorInstance(string name, ForgeTypeId group, string description, IReadOnlyList<string>? legacyAliases = null) =>
            new(name, "Instance", [BuiltInCategory.OST_Floors], group, description, legacyAliases ?? []);

        private static ParameterOwnership FloorType(string name, ForgeTypeId group, string description, IReadOnlyList<string>? legacyAliases = null) =>
            new(name, "Type", [BuiltInCategory.OST_Floors], group, description, legacyAliases ?? []);

        private static ParameterOwnership LightingFixtureType(string name, ForgeTypeId group, string description, IReadOnlyList<string>? legacyAliases = null) =>
            new(name, "Type", [BuiltInCategory.OST_LightingFixtures], group, description, legacyAliases ?? []);

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
            // Nursery stock is often already several years old when planted. On a growth-year
            // workset, Tree Calculator sets the instance's Years to this + the workset's year.
            // Growth Projections writes the tree's growth year here (from its growth-year workset);
            // the planting family computes its modelled age as AgeAtPlanting + ProjectedYears.
            PlantingInstance("!_S_PLT_TreeGrowth_ProjectedYears_Number", GroupTypeId.Constraints,
                "Growth years after planting this tree is projected to (0 = Baseline, as planted), written by Growth Projections from the tree's growth-year workset. The planting family computes the modelled age (!_S_PLT_TreeGrowth_Years_Number) as AgeAtPlanting + this."),
            // Growth Projections labels its copies so nobody edits them by hand (it also pins them);
            // the copy's link to its original stays in extensible storage.
            PlantingInstance("!_S_PLT_TreeGrowth_GeneratedBy_Text", GroupTypeId.IdentityData,
                "Set by LIM Growth Projections on each growth-year copy it makes: its year and the original tree it copies. Don't edit, move or delete these copies: edit the original tree and run Growth Projections again. Empty on every other tree."),
            // Instance: trees of one type can go in at different nursery ages (and retained trees
            // are older again). Was Type — Import Shared Parameter migrates the binding in place
            // (same GUID) and gives each tree its type's old value.
            PlantingInstance("!_S_PLT_TreeGrowth_AgeAtPlanting_Number", GroupTypeId.Constraints,
                "Years this tree had already grown when planted (nursery stock age; the tree's own age for a retained tree). The planting family computes the modelled age (!_S_PLT_TreeGrowth_Years_Number) as this plus !_S_PLT_TreeGrowth_ProjectedYears_Number."),
            // Also bound to Floors: the same "condition" input feeds Floor Calculator's BNG tab, where
            // it holds the Statutory Biodiversity Metric's condition instead (each category is
            // validated against its own vocabulary).
            PlantingAndFloorInstance("!_S_PLT_iTreeInput_Condition_Text", GroupTypeId.Constraints,
                "Tree condition: Excellent, Good, Fair, Poor, Critical, Dying, or Dead. On floors, the BNG habitat condition: Good, Fairly Good, Moderate, Fairly Poor, Poor, Condition Assessment N/A, or N/A - Other."),
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
            // Bound to both Planting and Floors, same reuse pattern as CostSavedAnnual_Currency
            // below: needed on Floors too so a currency switch can rescale a floor's own cost-saved
            // figure from its own last-applied rate, the same way it already does for trees.
            PlantingAndFloorInstance("!_S_PLT_iTreeResult_CurrencyUsed_Text", GroupTypeId.AnalysisResults,
                "Currency the numeric monetary results (CostSaved and, for trees, its three category components, annual and lifetime) were converted into for this element's latest calculation: USD, GBP, EUR, CAD, AUD, or NZD."),
            PlantingAndFloorInstance("!_S_PLT_iTreeResult_ExchangeRateUsed_Number", GroupTypeId.AnalysisResults,
                "USD exchange rate applied to this element's latest monetary results (1.0 when CurrencyUsed is USD). Recorded so a historical calculation's dollar figures can be reconstructed, or rescaled to a new currency, even as rates change."),
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
            // First the tree's size for its DBH, which the planting family draws the tree at.
            PlantingInstance("!_S_PLT_iTreeResult_TreeHeight_Length", GroupTypeId.AnalysisResults,
                "Total tree height i-Tree forecasts for this tree in its growth year (from its species, DBH, condition and crown exposure). Written by LIM when it calculates i-Tree; the planting family draws the tree at this height, and from its type's height growth rate while it's empty. Metric: m."),
            PlantingInstance("!_S_PLT_iTreeResult_CrownWidth_Length", GroupTypeId.AnalysisResults,
                "Crown width i-Tree forecasts for this tree in its growth year (the width of a circle of i-Tree's crown area). Written by LIM when it calculates i-Tree; the planting family draws the crown at this width, and from its type's width growth rate while it's empty. Metric: m."),
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
            // Deliberately a new parameter, not a rename of the one above (see its own comment
            // just below — it holds a genuinely different quantity: raw elemental carbon, not
            // CO2). No legacyAliases here — nothing migrates automatically; the project owner is
            // retiring the CO2SequesteredAnnual_Mass binding by hand.
            PlantingAndFloorInstance("!_S_PLT_iTreeResult_CarbonSequesteredAnnual_Mass", GroupTypeId.AnalysisResults,
                "Annual elemental carbon sequestered — i-Tree's raw measured figure. Distinct from CO2SequesteredAnnual_Mass/CO2EquivalentAnnual_Mass, which are that same figure × 3.67 (Revit Mass parameter — displays per the project's Mass unit settings)."),
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
            // CO2 Equivalent is on Floors too: Floor Calculator writes the WWP sheet's own kgCO2e
            // figure (coefficient × area) here, the same number its table shows. The dollar
            // breakdown has no Floor/LDS equivalent, so it stays trees only.
            PlantingAndFloorInstance("!_S_PLT_iTreeResult_CO2EquivalentAnnual_Mass", GroupTypeId.AnalysisResults,
                "Annual CO2 equivalent sequestered: sequestered carbon × 3.67 for trees, the WWP landscape data sheet's kgCO2e coefficient × area for planting areas (Revit Mass parameter — displays per the project's Mass unit settings).",
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
            // New parameter, same reasoning as CarbonSequesteredAnnual_Mass above — raw carbon,
            // not CO2, and no automatic migration from the old parameter.
            PlantingInstance("!_S_PLT_iTreeResult_CarbonSequesteredLifetimeTotal_Mass", GroupTypeId.AnalysisResults,
                "Lifetime cumulative elemental carbon sequestered, summed over the instance's modeled TreeGrowth_Years — i-Tree's raw measured figure, distinct from CO2SequesteredLifetimeTotal_Mass/CO2EquivalentLifetimeTotal_Mass (Revit Mass parameter)."),
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
            // Written alongside PreferredCurrency by the same PublishPreferredCurrency call — the
            // resolved USD-to-preferred-currency rate, refreshed each time the currency preference
            // is set. A project-wide "current rate" any tool or hand-built schedule formula can
            // multiply a USD figure by directly, without needing its own exchange-rate lookup.
            // Distinct from the per-instance iTreeResult_ExchangeRateUsed_Number above, which is a
            // historical record of the rate baked into one specific tree's already-calculated
            // results, not necessarily this project-wide preference's current value.
            ProjectInfo("!_S_PLT_iTreeUnits_PreferredCurrencyFactor_Number", GroupTypeId.General,
                "USD-to-preferred-currency exchange rate (e.g. 0.78 for GBP), refreshed whenever the preferred currency is set. Multiply a USD figure by this to report it in the preferred currency; 1.0 when the preference is USD."),
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
            // Match key/status fields are a function of which Type got assigned via Floor
            // Calculator, not of any per-instance measurement — every instance of the same Type
            // resolves to the same coefficient row, so Type scope (same reasoning as the rest of
            // this file's LDS reference data).
            PlantingAndFloorType("!_S_PLT_LDS_Type_Text", GroupTypeId.Constraints,
                "WWP landscape data sheet type assigned via Floor Calculator (e.g. Lawn, Meadow, Granite - Blanco Cristal)."),
            // Optional — internal bookkeeping, not something a person reads directly.
            PlantingAndFloorType("!_S_PLT_LDS_MatchKey_Text", GroupTypeId.General,
                "Stable lookup key into the cached WWP landscape data sheet, used to re-find the exact coefficient row without re-matching."),
            // Output — Floor Calculator's own status/tracking fields, mirroring i-Tree Calculator's pattern.
            PlantingAndFloorType("!_S_PLT_Result_LDS_Source_Text", GroupTypeId.AnalysisResults,
                "Whether the currently written values came from the coefficient table as calculated, or were edited manually in Floor Calculator before writing.",
                legacyAliases: ["!_S_PLT_LDS_ResultSource_Text"]),
            PlantingAndFloorType("!_S_PLT_Result_LDS_LastCalculated_Text", GroupTypeId.AnalysisResults,
                "ISO 8601 date and time of the last Floor Calculator run for this element.",
                legacyAliases: ["!_S_PLT_LDS_LastCalculated_Text"]),
            // Output — the WWP landscape data sheet's per-square-metre coefficients times area,
            // for metrics with no i-Tree equivalent to reuse. (CostSavedAnnual moved up to the
            // iTreeResult_ group above — it's now shared with the i-Tree tree-benefit result.)
            // These three are genuinely instance-varying (App.FloorCalculator/MainPage.xaml.cs
            // multiplies each by that instance's own area) — stay Instance, unlike the rest of
            // this group, so two instances of the same Type with different areas still get their
            // own totals.
            PlantingAndFloorInstance("!_S_PLT_Result_OxygenProducedAnnual_Mass", GroupTypeId.AnalysisResults,
                "Estimated annual oxygen produced, from the WWP landscape data sheet's per-square-metre coefficient times area (Revit Mass parameter).",
                legacyAliases: ["!_S_PLT_LDS_OxygenProducedAnnual_Mass", "!_S_PLT_LDS_OxygenProducedAnnual_Number"]),
            PlantingAndFloorInstance("!_S_PLT_Result_EmbodiedGWP_Mass", GroupTypeId.AnalysisResults,
                "Embodied carbon of this floor: (product + transport GWP rate) × area, kg CO2e, emitted once — a cost, not a benefit. Planting's sequestration is not netted off here (Revit Mass parameter).",
                legacyAliases: ["!_S_PLT_LDS_TotalGWP_Mass", "!_S_PLT_LDS_TotalGWP_Number"]),
            // Not multiplied by area (App.FloorCalculator/MainPage.xaml.cs passes these straight
            // through) — an intrinsic material property, so Type scope like MaxHeight/MaxWidth above.
            PlantingAndFloorType("!_S_PLT_LDS_SurfaceTempReduction_Number", GroupTypeId.AnalysisResults,
                "Surface temperature reduction for this landscape type, from the WWP landscape data sheet — an intrinsic material property, not scaled by area. Metric: °C. (Metric-only — no Imperial conversion.)"),
            PlantingAndFloorType("!_S_PLT_LDS_AirTempReduction_Number", GroupTypeId.AnalysisResults,
                "Air temperature reduction at 1.5m height for this landscape type, from the WWP landscape data sheet — an intrinsic material property, not scaled by area. Metric: °C. (Metric-only — no Imperial conversion.)"),

            // Optional — descriptive landscape-type reference data from the WWP landscape data
            // sheet, same bucket as the i-Tree species reference fields above. Type scope: a
            // classification tag or reference figure for the type, not something that varies per
            // placed instance — and Instance scope would otherwise require per-instance pairing
            // before the Importer could ever write it (see AvoidedWaterRunoff etc. below).
            PlantingAndFloorType("!_S_PLT_LDS_Origin_Text", GroupTypeId.General,
                "Geographic origin of the landscape type, from the WWP landscape data sheet."),
            // Originally recycled from the pre-existing "Données d'indentification" group under the
            // same GUIDs on the theory that renaming the .txt entry would resolve existing bindings
            // under the new name — confirmed false: Revit caches a bound SharedParameterElement's
            // display name per GUID at the project level, so a project that already had one of these
            // GUIDs bound under its old "WWP_LDS_*" name kept showing that old name forever, no
            // matter what the .txt file's text said. Reissued with fresh GUIDs and legacyAliases
            // (matching AvoidedWaterRunoff/CarbonDioxideSequestration/OxygenLevels below) so
            // RemoveStaleBindings can actually retire the old names instead of leaving them as
            // orphaned duplicates. Also converted Instance -> Type for the same reason as Origin_Text
            // above — RevitModelScanner already falls back to a Type-level lookup for
            // CalculationType_Text, which only made sense if this was meant to be Type-bound.
            PlantingAndFloorType("!_S_PLT_LDS_CalculationType_Text", GroupTypeId.General,
                "Landscape type classification (e.g. Tree, Shrub species, Surfaces), from the WWP landscape data sheet.",
                legacyAliases: ["WWP_LDS_CalculationType"]),
            PlantingAndFloorType("!_S_PLT_LDS_Category_Text", GroupTypeId.General,
                "Landscape data sheet category for this type, from the WWP landscape data sheet.",
                legacyAliases: ["WWP_LDS_Category"]),
            PlantingAndFloorType("!_S_PLT_LDS_SubCategory_Text", GroupTypeId.General,
                "Landscape data sheet sub-category for this type, from the WWP landscape data sheet.",
                legacyAliases: ["WWP_LDS_SubCategory"]),
            // Despite the "coefficient times area" wording, this is NOT actually computed anywhere —
            // grepped FloorLdsCalculationService's full write list and it's absent (unlike its
            // OxygenProducedAnnual/PollutantsRemovedAnnual siblings, which really are computed
            // there). It's a pure Importer-only reference value, so Type scope for the same reason as
            // AvoidedWaterRunoff/CarbonDioxideSequestration/OxygenLevels below.
            PlantingAndFloorType("!_S_PLT_LDS_MaintenanceCostAnnual_Currency", GroupTypeId.AnalysisResults,
                "Estimated annual maintenance cost for this landscape type, from the WWP landscape data sheet — an intrinsic reference value, not scaled by area (Revit Currency parameter).",
                legacyAliases: ["!_S_PLT_LDS_MaintenanceCostAnnual_Number"]),
            // Calculated from the rate above: per tree for Planting, per m² × area for a Floor (see
            // MaintenanceCost). Refreshed whenever Tree Calculator or Floor Calculator writes results.
            PlantingAndFloorInstance("!_S_PLT_Result_MaintenanceCostAnnual_Currency", GroupTypeId.AnalysisResults,
                "Yearly maintenance cost: the WWP sheet rate per tree, or per m² × area for a floor (Revit Currency parameter)."),
            // Trees: CO2e × 32/44 over the i-Tree forecast; the annual figure shares
            // Result_OxygenProducedAnnual_Mass with planting areas.
            PlantingInstance("!_S_PLT_Result_OxygenProducedLifetimeTotal_Mass", GroupTypeId.AnalysisResults,
                "Oxygen this tree releases over its forecast: CO2 equivalent × 32/44 (Revit Mass parameter)."),
            // i-Tree timeline (one stored forecast per unique planting input, see TreeTimelineStore):
            // the key this tree uses, and its totals over the standard outlooks so schedules can add them up.
            PlantingInstance("!_S_PLT_Result_iTreeTimelineKey_Text", GroupTypeId.AnalysisResults,
                "Which stored i-Tree year-by-year forecast this tree uses. Application-managed."),
            .. new[] { 5, 10, 20, 25 }.SelectMany(years => new[]
            {
                PlantingInstance($"!_S_PLT_Result_CO2Equivalent{years}yr_Mass", GroupTypeId.AnalysisResults,
                    $"CO2 equivalent taken up over the first {years} years after planting, from the i-Tree forecast."),
                PlantingInstance($"!_S_PLT_Result_RunoffAvoided{years}yr_Volume", GroupTypeId.AnalysisResults,
                    $"Runoff avoided over the first {years} years after planting, from the i-Tree forecast."),
                PlantingInstance($"!_S_PLT_Result_PollutantsRemoved{years}yr_Mass", GroupTypeId.AnalysisResults,
                    $"Air pollution removed over the first {years} years after planting, from the i-Tree forecast."),
                PlantingInstance($"!_S_PLT_Result_CostSaved{years}yr_Currency", GroupTypeId.AnalysisResults,
                    $"Value of the tree's benefits over the first {years} years after planting, from the i-Tree forecast."),
            }),
            // Pollen from the WWP sheet, which i-Tree doesn't model: per tree for a tree (Tree Calculator or
            // Floor Calculator, from the tree's sheet row), the per-m² rate × area for a planting area.
            PlantingAndFloorInstance("!_S_PLT_Result_PollenAnnual_Mass", GroupTypeId.AnalysisResults,
                "Pollen this tree or planting area releases each year, from the WWP landscape data sheet: per tree for a tree, the per-m² rate × area for a planting area (Revit Mass parameter)."),
            PlantingAndFloorInstance("!_S_PLT_Result_PollutantsRemovedAnnual_Mass", GroupTypeId.AnalysisResults,
                "Estimated annual pollutants removed, from the WWP landscape data sheet's per-square-metre coefficient times area (Revit Mass parameter).",
                legacyAliases: ["!_S_PLT_LDS_PollutantsRemovedAnnual_Mass", "!_S_PLT_LDS_PollutantsRemovedAnnual_Number"]),
            // Defined in the shared parameter file and referenced by the main App's
            // LandscapeCalculationEngine/mapping-suggestion feature, but never actually bound here
            // until now — closing that gap so "Ensure Shared Parameters" actually guarantees these
            // exist, same as their MaintenanceCost/PollutantsRemoved siblings above.
            // Instance scope was the original design (per the "times area" wording below), but
            // nothing in this codebase ever actually multiplies these by area — the Importer just
            // copies the raw sheet number across — and the WWP sheet's own column names carry a
            // "(16/18 girth)" qualifier: a standard nursery starter-tree trunk-girth spec, meaning
            // these are per-species reference figures for a typical young tree, not a true
            // per-square-metre rate. Type scope matches what they actually are (and lets them
            // populate via the Importer's automatic Family+Type name matching, with no per-instance
            // pairing required — same as MaxHeight/MaxWidth below).
            // Volume/Mass now that a real Revit spec exists for them — was "_Number" (dimensionless),
            // so it never responded to the project's Metric/Imperial display preference the way
            // !_S_PLT_iTreeResult_*_Mass/_Volume result parameters already do. New GUID/name since a
            // shared parameter's data type can't change in place; the old "_Number" binding is
            // retired automatically via legacyAliases + RemoveStaleBindings above.
            // Reference *rates*, not totals: per 16/18-girth starter tree for trees, per m² for planting
            // areas. Floor Calculator multiplies them by area into the calculated instance values
            // (iTreeResult_RunoffAvoidedAnnual_Volume, Result_OxygenProducedAnnual_Mass,
            // iTreeResult_CO2EquivalentAnnual_Mass). Plain numbers on purpose: as Mass/Volume they
            // read like totals, and Imperial display converted the kg but not the "per m²".
            // Renamed from the *_Annual_Volume/_Mass names (new GUIDs; old bindings retired via legacyAliases).
            PlantingAndFloorType("!_S_PLT_LDS_RunoffAvoidedRate_Number", GroupTypeId.AnalysisResults,
                "Avoided water runoff RATE from the WWP landscape data sheet: m³/yr per 16/18-girth starter tree for trees, m³/m²/yr for planting areas. Floor Calculator multiplies it by area into !_S_PLT_iTreeResult_RunoffAvoidedAnnual_Volume.",
                legacyAliases: ["WWP_Avoided_Water_Runoff", "!_S_PLT_LDS_AvoidedWaterRunoffAnnual_Number", "!_S_PLT_LDS_AvoidedWaterRunoffAnnual_Volume"]),
            PlantingAndFloorType("!_S_PLT_LDS_OxygenProductionRate_Number", GroupTypeId.AnalysisResults,
                "Oxygen production RATE from the WWP landscape data sheet: kg O2/yr per 16/18-girth starter tree for trees, kg O2/m²/yr for planting areas. Floor Calculator multiplies it by area into !_S_PLT_Result_OxygenProducedAnnual_Mass.",
                legacyAliases: ["WWP_Oxygen_Levels", "!_S_PLT_LDS_OxygenLevelsAnnual_Number", "!_S_PLT_LDS_OxygenLevelsAnnual_Mass"]),
            PlantingAndFloorType("!_S_PLT_LDS_CO2SequestrationRate_Number", GroupTypeId.AnalysisResults,
                "CO2 sequestration RATE from the WWP landscape data sheet: kg CO2e/yr per 16/18-girth starter tree for trees, kg CO2e/m²/yr for planting areas. Floor Calculator multiplies it by area into !_S_PLT_iTreeResult_CO2EquivalentAnnual_Mass.",
                legacyAliases: ["WWP_Carbon_Dioxide_Sequestration", "!_S_PLT_LDS_CarbonDioxideSequestrationAnnual_Number", "!_S_PLT_LDS_CarbonDioxideSequestrationAnnual_Mass"]),
            // Same "declared times-area, never actually computed" pattern as MaintenanceCostAnnual
            // above — neither is in FloorLdsCalculationService's write list. Type scope.
            // Embodied carbon (GWP, kg CO2e emitted once to make/deliver the material) — reference
            // rates per m², not totals. Renamed from ProductGWP_Number/TransportGWP_Number (new GUIDs).
            // The sheet's WWP_Total_GWP is a formula (product + transport, or −sequestration for
            // planting), so it has no parameter: Floor Calculator derives the total itself.
            PlantingAndFloorType("!_S_PLT_LDS_ProductGWPRate_Number", GroupTypeId.AnalysisResults,
                "Embodied product GWP RATE (manufacture, stages A1-A3) from the WWP landscape data sheet, kg CO2e per m².",
                legacyAliases: ["!_S_PLT_LDS_ProductGWP_Number"]),
            PlantingAndFloorType("!_S_PLT_LDS_TransportGWPRate_Number", GroupTypeId.AnalysisResults,
                "Embodied transport GWP RATE (delivery to site, stage A4) from the WWP landscape data sheet, kg CO2e per m².",
                legacyAliases: ["!_S_PLT_LDS_TransportGWP_Number"]),
            // The sheet's WWP_Pollutants_Removed column, the one rate that had no parameter of its own:
            // imported here like its siblings, never onto the area-scaled Result_PollutantsRemovedAnnual_Mass.
            PlantingAndFloorType("!_S_PLT_LDS_PollutantsRemovedRate_Number", GroupTypeId.AnalysisResults,
                "Air pollution removal RATE from the WWP landscape data sheet: kg/yr per 16/18-girth starter tree for trees, kg/m²/yr for planting areas. Floor Calculator multiplies it by area into !_S_PLT_Result_PollutantsRemovedAnnual_Mass.",
                legacyAliases: ["WWP_Pollutants_Removed"]),
            PlantingAndFloorType("!_S_PLT_LDS_PollenAnnual_Number", GroupTypeId.AnalysisResults,
                "Estimated annual pollen production for this landscape type, from the WWP landscape data sheet — an intrinsic reference value, not scaled by area. Metric: kg/m²/yr. (Metric-only — no Imperial conversion.)"),
            // Reference values from the WWP landscape data sheet — every instance of a type shares
            // the same species-level max height/width, so Type scope (same fix as
            // AvoidedWaterRunoff/OxygenLevels/CarbonDioxideSequestration above).
            // Length now that a real Revit spec exists — was "_Number" (Metric-only, no Imperial
            // conversion by design); same new-GUID/legacyAliases retirement as the Mass/Volume
            // fields above.
            PlantingAndFloorType("!_S_PLT_LDS_MaxHeight_Length", GroupTypeId.AnalysisResults,
                "Maximum mature height for this landscape type, from the WWP landscape data sheet — an intrinsic reference value, not scaled by area.",
                legacyAliases: ["!_S_PLT_LDS_MaxHeight_Number"]),
            PlantingAndFloorType("!_S_PLT_LDS_MaxWidth_Length", GroupTypeId.AnalysisResults,
                "Maximum mature width/spread for this landscape type, from the WWP landscape data sheet — an intrinsic reference value, not scaled by area.",
                legacyAliases: ["!_S_PLT_LDS_MaxWidth_Number"]),
            // Was Instance scope despite its own description already saying "not scaled by area" —
            // same fix as AvoidedWaterRunoff/OxygenLevels/CarbonDioxideSequestration above.
            PlantingAndFloorType("!_S_PLT_LDS_IrrigationDemandFactor_Number", GroupTypeId.AnalysisResults,
                "Irrigation demand plant factor for this landscape type, from the WWP landscape data sheet — an intrinsic reference value, not scaled by area."),

            // Site & Biodiversity KPI dashboard — new reference data, person-entered directly in
            // Revit rather than synced from Airtable (no external source exists for any of this).
            ProjectInfo("!_S_PLT_Site_TotalArea_Area", GroupTypeId.General,
                "Total site boundary area entered by the project team, used to compute canopy-cover and softscape-surface-ratio percentages."),
            ProjectInfo("!_S_PLT_Site_HabitatConnectivityScore_Number", GroupTypeId.General,
                "Habitat continuity score (0-10) from external GIS connectivity modelling against neighbouring ecological assets, recorded here for reporting."),
            PlantingAndFloorType("!_S_PLT_LDS_SurfaceClass_Text", GroupTypeId.General,
                "Pervious or Impervious surface classification for this landscape type, used to compute the softscape surface ratio."),
            PlantingType("!_S_PLT_iTreeSpecies_NativeStatus_Text", GroupTypeId.General,
                "Native, Adaptive, or Non-native status for this species, used to compute the native species ratio."),
            PlantingType("!_S_PLT_iTreeSpecies_BloomMonths_Text", GroupTypeId.General,
                "Comma-separated calendar months (1-12) this species blooms or seeds in, used to compute phenological resilience."),
            PlantingType("!_S_PLT_iTreeSpecies_EcologicalFunctions_Text", GroupTypeId.General,
                "Comma-separated ecological function tags for this species (e.g. Pollinator, BirdFood, ErosionControl), used to compute the functional diversity index."),
            LightingFixtureType("!_S_PLT_Lighting_DarkSkyCompliant_Text", GroupTypeId.General,
                "Yes or No: whether this lighting fixture type is shielded/full-cutoff and compliant with dark-sky standards, used to compute lighting impact reduction."),

            // Create KPI Schedules — Revit has no native Design Option schedule field/filter, so this
            // is stamped onto every instance to make per-option schedules possible at all.
            PlantingAndFloorInstance("!_S_PLT_Schedule_DesignOptionLabel_Text", GroupTypeId.General,
                "Resolved Design Option label (set name : option name), stamped by Create KPI Schedules so schedules can filter by it since Revit has no native Design Option schedule field."),

            // WWP landscape data sheet columns with no prior parameter — same bucket as
            // MaintenanceCostAnnual above: imported reference values, never computed by a live API.
            // Type scope, same reasoning as the rest of this reference-value group.
            PlantingAndFloorType("!_S_PLT_LDS_OxygenCostSavedAnnual_Currency", GroupTypeId.AnalysisResults,
                "Estimated annual dollar value of the oxygen benefit for this landscape type, from the WWP landscape data sheet — i-Tree does not monetize oxygen production, so this is always an imported reference value, never a live-calculated result."),
            PlantingAndFloorType("!_S_PLT_LDS_RunoffCoefficient_Number", GroupTypeId.AnalysisResults,
                "Dimensionless rainfall-interception/runoff coefficient (f_int / Crg) for this landscape type, from the WWP landscape data sheet — an intrinsic reference value, not scaled by area."),
            PlantingType("!_S_PLT_iTreeSpecies_Allergenic_Text", GroupTypeId.General,
                "Yes or No: whether this species is classified as allergenic, from the WWP landscape data sheet."),

            // Biodiversity Net Gain — Floor Calculator's BNG tab, mirroring one row of the Statutory
            // Biodiversity Metric's "A-2 On-Site Habitat Creation" sheet per floor. Same grouping
            // as i-Tree: inputs (plus the reused iTreeInput_Condition_Text above) in Constraints,
            // everything the calculator writes back in Analysis Results.
            FloorInstance("!_S_PLT_BNGInput_ProposedHabitat_Text", GroupTypeId.Constraints,
                "BNG proposed habitat as the metric's habitat description, e.g. \"Grassland - Floodplain wetland mosaic and CFGM\" (A-2 column F)."),
            FloorInstance("!_S_PLT_BNGInput_StrategicSignificance_Text", GroupTypeId.Constraints,
                "BNG strategic significance (A-2 column L): Formally identified in local strategy, Location ecologically desirable but not in local strategy, or Area/compensation not in local strategy/ no local strategy."),
            FloorInstance("!_S_PLT_BNGInput_CreationYearOffset_Number", GroupTypeId.Constraints,
                "Years the habitat is created in advance (positive, A-2 column P) or delayed (negative, A-2 column Q); 0 for the standard time to target condition."),
            FloorType("!_S_PLT_BNGInput_HabitatMapping_Text", GroupTypeId.Constraints,
                "BNG habitat remembered for this floor type, used to pre-fill ProposedHabitat on new floors of the type."),
            FloorInstance("!_S_PLT_BNGResult_BroadHabitat_Text", GroupTypeId.AnalysisResults,
                "BNG broad habitat of the proposed habitat (A-2 column D).",
                legacyAliases: ["!_S_PLT_Result_BNG_BroadHabitat_Text"]),
            FloorInstance("!_S_PLT_BNGResult_AreaHectares_Text", GroupTypeId.AnalysisResults,
                "Floor area in hectares used for the BNG calculation (A-2 column G).",
                legacyAliases: ["!_S_PLT_Result_BNG_AreaHectares_Text"]),
            FloorInstance("!_S_PLT_BNGResult_Distinctiveness_Text", GroupTypeId.AnalysisResults,
                "BNG distinctiveness band of the proposed habitat (A-2 column H).",
                legacyAliases: ["!_S_PLT_Result_BNG_Distinctiveness_Text"]),
            FloorInstance("!_S_PLT_BNGResult_DistinctivenessScore_Text", GroupTypeId.AnalysisResults,
                "BNG distinctiveness score (A-2 column I).",
                legacyAliases: ["!_S_PLT_Result_BNG_DistinctivenessScore_Text"]),
            FloorInstance("!_S_PLT_BNGResult_ConditionScore_Text", GroupTypeId.AnalysisResults,
                "BNG condition score (A-2 column K).",
                legacyAliases: ["!_S_PLT_Result_BNG_ConditionScore_Text"]),
            FloorInstance("!_S_PLT_BNGResult_StrategicSignificanceCategory_Text", GroupTypeId.AnalysisResults,
                "BNG strategic significance category (A-2 column M).",
                legacyAliases: ["!_S_PLT_Result_BNG_StrategicSignificanceCategory_Text"]),
            FloorInstance("!_S_PLT_BNGResult_StrategicSignificanceMultiplier_Text", GroupTypeId.AnalysisResults,
                "BNG strategic significance multiplier (A-2 column N).",
                legacyAliases: ["!_S_PLT_Result_BNG_StrategicSignificanceMultiplier_Text"]),
            FloorInstance("!_S_PLT_BNGResult_StandardTimeToTarget_Text", GroupTypeId.AnalysisResults,
                "BNG standard time to target condition in years (A-2 column O).",
                legacyAliases: ["!_S_PLT_Result_BNG_StandardTimeToTarget_Text"]),
            FloorInstance("!_S_PLT_BNGResult_TimeToTargetStatus_Text", GroupTypeId.AnalysisResults,
                "BNG standard or adjusted time to target condition message (A-2 column R).",
                legacyAliases: ["!_S_PLT_Result_BNG_TimeToTargetStatus_Text"]),
            FloorInstance("!_S_PLT_BNGResult_FinalTimeToTarget_Text", GroupTypeId.AnalysisResults,
                "BNG final time to target condition in years (A-2 column S).",
                legacyAliases: ["!_S_PLT_Result_BNG_FinalTimeToTarget_Text"]),
            FloorInstance("!_S_PLT_BNGResult_FinalTimeToTargetMultiplier_Text", GroupTypeId.AnalysisResults,
                "BNG final time to target multiplier (A-2 column T).",
                legacyAliases: ["!_S_PLT_Result_BNG_FinalTimeToTargetMultiplier_Text"]),
            FloorInstance("!_S_PLT_BNGResult_StandardDifficulty_Text", GroupTypeId.AnalysisResults,
                "BNG standard difficulty of creation (A-2 column U).",
                legacyAliases: ["!_S_PLT_Result_BNG_StandardDifficulty_Text"]),
            FloorInstance("!_S_PLT_BNGResult_AppliedDifficulty_Text", GroupTypeId.AnalysisResults,
                "BNG applied difficulty multiplier message (A-2 column V).",
                legacyAliases: ["!_S_PLT_Result_BNG_AppliedDifficulty_Text"]),
            FloorInstance("!_S_PLT_BNGResult_FinalDifficulty_Text", GroupTypeId.AnalysisResults,
                "BNG final difficulty of creation (A-2 column W).",
                legacyAliases: ["!_S_PLT_Result_BNG_FinalDifficulty_Text"]),
            FloorInstance("!_S_PLT_BNGResult_DifficultyMultiplier_Text", GroupTypeId.AnalysisResults,
                "BNG difficulty multiplier applied (A-2 column X).",
                legacyAliases: ["!_S_PLT_Result_BNG_DifficultyMultiplier_Text"]),
            FloorInstance("!_S_PLT_BNGResult_HabitatUnits_Number", GroupTypeId.AnalysisResults,
                "BNG habitat units delivered by this floor (A-2 column Y) -- a number so schedules can total it.",
                legacyAliases: ["!_S_PLT_Result_BNG_HabitatUnits_Number"]),
            FloorInstance("!_S_PLT_BNGResult_Status_Text", GroupTypeId.AnalysisResults,
                "BNG calculation state: Calculated, CheckData, NeedsInfo, or Stale.",
                legacyAliases: ["!_S_PLT_Result_BNG_Status_Text"]),
            FloorInstance("!_S_PLT_BNGResult_Details_Text", GroupTypeId.AnalysisResults,
                "Missing-input or metric warning details for the latest BNG calculation.",
                legacyAliases: ["!_S_PLT_Result_BNG_Details_Text"]),
            FloorInstance("!_S_PLT_BNGResult_InputSignature_Text", GroupTypeId.AnalysisResults,
                "Application-managed signature used to detect stale BNG results.",
                legacyAliases: ["!_S_PLT_Result_BNG_InputSignature_Text"]),
            FloorInstance("!_S_PLT_BNGResult_LastUpdated_Text", GroupTypeId.AnalysisResults,
                "ISO 8601 date and time of the last BNG calculation.",
                legacyAliases: ["!_S_PLT_Result_BNG_LastUpdated_Text"]),
            FloorInstance("!_S_PLT_BNGResult_MetricVersion_Text", GroupTypeId.AnalysisResults,
                "Statutory Biodiversity Metric release used for the latest BNG calculation.",
                legacyAliases: ["!_S_PLT_Result_BNG_MetricVersion_Text"]),
            // Baseline (A-1) and enhancement (A-3): a floor's Revit phases decide its BNG role —
            // created in the project's first phase = baseline habitat (retained, or lost if
            // demolished), created later = new habitat (A-2). A retained floor flagged Enhanced is
            // calculated on A-3, with ProposedHabitat/Condition as its post-enhancement target.
            FloorInstance("!_S_PLT_BNGInput_BaselineHabitat_Text", GroupTypeId.Constraints,
                "BNG baseline (existing) habitat of an Existing-phase floor, as the metric's habitat description (A-1 column F)."),
            FloorInstance("!_S_PLT_BNGInput_BaselineCondition_Text", GroupTypeId.Constraints,
                "BNG baseline condition of an Existing-phase floor (A-1 column K): Good, Fairly Good, Moderate, Fairly Poor, Poor, Condition Assessment N/A, or N/A - Other."),
            FloorInstance("!_S_PLT_BNGInput_Irreplaceable_Text", GroupTypeId.Constraints,
                "BNG irreplaceable habitat status of the baseline habitat, Yes or No (A-1 column G) -- only needed where the metric allows either."),
            FloorInstance("!_S_PLT_BNGInput_Enhanced_YesNo", GroupTypeId.Constraints,
                "Yes when a retained Existing-phase floor's habitat is enhanced (metric sheet A-3); its ProposedHabitat and Condition are then the enhanced target."),
            // Floors of one type share their BNG inputs in Floor Calculator unless ticked Unique.
            FloorInstance("!_S_PLT_BNGInput_Unique_YesNo", GroupTypeId.Constraints,
                "Yes when this floor keeps its own BNG inputs in Floor Calculator instead of sharing them with other floors of the same type and role."),
            FloorInstance("!_S_PLT_BNGResult_Role_Text", GroupTypeId.AnalysisResults,
                "BNG role from the floor's phases: Retained, Enhanced, Lost, Created, or Excluded.",
                legacyAliases: ["!_S_PLT_Result_BNG_Role_Text"]),
            FloorInstance("!_S_PLT_BNGResult_BaselineUnits_Number", GroupTypeId.AnalysisResults,
                "BNG baseline habitat units of an Existing-phase floor (A-1 column Q); 0 for new floors.",
                legacyAliases: ["!_S_PLT_Result_BNG_BaselineUnits_Number"]),

            // Connectivity tool — structural connectivity of each tree and planting area by the
            // Probability of Connectivity index (dPC), banded 1-4. Locked is the hand override.
            PlantingAndFloorInstance("!_S_PLT_ConnectivityInput_Locked_YesNo", GroupTypeId.Constraints,
                "Yes to keep a hand-set connectivity band and class on this element: Connectivity Calculator then leaves its results alone."),
            PlantingAndFloorInstance("!_S_PLT_ConnectivityResult_Band_Number", GroupTypeId.AnalysisResults,
                "Connectivity band 1-4: 1 Low (contributes little), 2 Moderate (local value only), 3 Strong connector (important but substitutable), 4 Critical stepping-stone (removing it fragments the network).",
                legacyAliases: ["!_S_PLT_Result_Connectivity_Band_Number"]),
            PlantingAndFloorInstance("!_S_PLT_ConnectivityResult_Class_Text", GroupTypeId.AnalysisResults,
                "Connectivity band name: Low, Moderate, Strong connector, or Critical stepping-stone.",
                legacyAliases: ["!_S_PLT_Result_Connectivity_Class_Text"]),
            PlantingAndFloorInstance("!_S_PLT_ConnectivityResult_Score_Number", GroupTypeId.AnalysisResults,
                "Connectivity score 0-1: this element's percentile rank by dPC among every tree and planting area, averaged over the dispersal distances used.",
                legacyAliases: ["!_S_PLT_Result_Connectivity_Score_Number"]),
            PlantingAndFloorInstance("!_S_PLT_ConnectivityResult_dPC_Number", GroupTypeId.AnalysisResults,
                "dPC: percentage of the network's Probability of Connectivity lost if this element is removed, averaged over the dispersal distances used.",
                legacyAliases: ["!_S_PLT_Result_Connectivity_dPC_Number"]),
            PlantingAndFloorInstance("!_S_PLT_ConnectivityResult_Details_Text", GroupTypeId.AnalysisResults,
                "Dispersal distances used, dPC and band at each, and whether the band is uncertain (changes by two or more bands between distances).",
                legacyAliases: ["!_S_PLT_Result_Connectivity_Details_Text"]),
            PlantingAndFloorInstance("!_S_PLT_ConnectivityResult_LastUpdated_Text", GroupTypeId.AnalysisResults,
                "ISO 8601 date and time of the last connectivity calculation.",
                legacyAliases: ["!_S_PLT_Result_Connectivity_LastUpdated_Text"])
        ];
    }
}
