using Autodesk.Revit.DB;
using Autodesk.Revit.DB.ExtensibleStorage;
using Autodesk.Revit.UI;
using WWP.LandscapeDataManager.Contracts;

namespace WWP.LandscapeDataManager.Revit.Services;

/// <summary>
/// Growth Projections: models the planting at several growth years by copying each original tree
/// onto a growth-year workset (<see cref="GrowthYearNames.WorksetName"/>) with its modelled age
/// (<c>!_S_PLT_TreeGrowth_Years_Number</c>) set to its age at planting plus that year. The originals
/// move to the year-0 ("Project start") workset, so every year is its own complete set. Each copy carries an
/// extensible-storage link to its original, so running again refreshes copies in place (location,
/// rotation, type, i-Tree inputs) instead of duplicating them — and keeps their calculated results.
/// Copies are labelled in <see cref="GeneratedByParameter"/> and pinned, so nobody edits them by hand.
/// Which copies to create/refresh/remove is planned by <see cref="GrowthProjectionPlanner"/>.
/// </summary>
internal static class GrowthProjectionService
{
    private const string InputParameterPrefix = "!_S_PLT_iTreeInput_";
    internal const string GeneratedByParameter = "!_S_PLT_TreeGrowth_GeneratedBy_Text";
    private static readonly Guid LinkSchemaGuid = new("5b0f2d8e-3c1a-4e7b-9a62-1d4c8f7e2a90");

    public static GrowthProjectionStatus GetStatus(UIApplication application)
    {
        var uiDocument = application.ActiveUIDocument
                         ?? throw new InvalidOperationException("Open a Revit project before setting up growth projections.");
        var document = uiDocument.Document;
        var model = ReadModel(document);
        var selected = uiDocument.Selection.GetElementIds().Select(id => id.Value).ToHashSet();

        var options = new List<GrowthDesignOptionChoice>
        {
            new(GrowthOptionKeys.MainModel, "Main model", model.Originals.Count(tree => OptionKey(tree) == GrowthOptionKeys.MainModel), ActiveOptionId(document) is null)
        };
        foreach (var option in new FilteredElementCollector(document).OfClass(typeof(DesignOption)).Cast<DesignOption>())
        {
            var key = option.Id.Value.ToString();
            options.Add(new GrowthDesignOptionChoice(
                key,
                OptionLabel(document, option),
                model.Originals.Count(tree => OptionKey(tree) == key),
                ActiveOptionId(document) == option.Id.Value));
        }

        var worksets = model.YearWorksets
            .OrderBy(entry => entry.Key)
            .Select(entry => new GrowthYearWorkset(
                entry.Key,
                entry.Value.Name,
                model.Trees.Count(tree => tree.WorksetId == entry.Value.Id),
                model.Copies.Count(copy => copy.Link.Year == entry.Key)))
            .ToList();

        var sampleTree = model.Trees.FirstOrDefault();
        var activeId = ActiveOptionId(document);
        return new GrowthProjectionStatus(
            document.Title,
            document.IsWorkshared,
            sampleTree?.LookupParameter(TreeAgeSyncService.ProjectedYearsParameter) is not null,
            sampleTree is not null && ElementParameters.LookupOnInstanceOrType(sampleTree, TreeAgeSyncService.AgeAtPlantingParameter) is not null,
            activeId is null ? "Main model" : OptionLabel(document, (DesignOption)document.GetElement(new ElementId(activeId.Value))),
            model.Originals.Count,
            model.Originals.Count(tree => selected.Contains(tree.Id.Value)),
            model.UnlinkedGrowthTrees,
            options,
            worksets,
            model.Trees.Count(tree => tree.GroupId != ElementId.InvalidElementId));
    }

    public static GrowthProjectionResult Run(UIApplication application, GrowthProjectionRequest request)
    {
        var uiDocument = application.ActiveUIDocument
                         ?? throw new InvalidOperationException("Open a Revit project before running growth projections.");
        var document = uiDocument.Document;
        if (!document.IsWorkshared)
        {
            throw new InvalidOperationException("Growth years are kept on worksets, so the model must be workshared — enable it under Collaborate > Worksharing first.");
        }

        var years = request.Years.Where(year => year is > 0 and <= GrowthYearNames.MaximumYear).Distinct().Order().ToList();
        if (years.Count == 0)
        {
            throw new InvalidOperationException($"Add at least one growth year between 1 and {GrowthYearNames.MaximumYear}.");
        }

        var model = ReadModel(document);
        var notes = new List<string>();
        var inScope = SelectScope(uiDocument, model, request, notes);
        var grouped = inScope.Where(tree => tree.GroupId != ElementId.InvalidElementId).ToList();
        if (grouped.Count > 0)
        {
            notes.Add($"{grouped.Count:N0} tree(s) inside groups were skipped — copies can't be placed into a group. Ungroup them to project them.");
            inScope = inScope.Except(grouped).ToList();
        }

        var plan = GrowthProjectionPlanner.Plan(
            inScope.Select(tree => tree.UniqueId).ToList(),
            model.Originals.Select(tree => tree.UniqueId).ToHashSet(StringComparer.Ordinal),
            model.Copies.Select(copy => copy.Link).ToList(),
            years);
        var toMove = inScope.Where(tree => GrowthYearOf(model, tree) != 0).ToList();

        if (model.UnlinkedGrowthTrees > 0)
        {
            notes.Add($"{model.UnlinkedGrowthTrees:N0} tree(s) on a growth year (placed by hand, or given a projected year) weren't made by Growth Projections and were left alone.");
        }

        if (request.PreviewOnly)
        {
            var missingWorksets = years.Prepend(0).Where(year => !model.YearWorksets.ContainsKey(year)).Select(GrowthYearNames.WorksetName).ToList();
            return new GrowthProjectionResult(
                document.Title, true, inScope.Count, toMove.Count, plan.ToCreate.Count, plan.ToUpdate.Count, plan.ToDelete.Count, missingWorksets, notes);
        }

        // Measured before the transaction: it probes sample trees in a rolled-back transaction of its own.
        var matureAges = TreeMaturityService.GetMatureAges(document, inScope);
        var cappedTypes = inScope
            .Where(tree => matureAges.TryGetValue(tree.GetTypeId().Value, out var matureAge) &&
                           years.Any(year => GrowthYearNames.ModelledAge(TreeAgeSyncService.AgeAtPlanting(tree), year, matureAge) <
                                             GrowthYearNames.ActualAge(TreeAgeSyncService.AgeAtPlanting(tree), year)))
            .Select(tree => tree.GetTypeId().Value)
            .Distinct()
            .Count();

        if (cappedTypes > 0)
        {
            notes.Add($"{cappedTypes:N0} planting type(s) reach mature size within these years — their later copies keep the mature size (age capped at maturity).");
        }

        var typesWithoutMatureSize = inScope.Select(tree => tree.GetTypeId().Value).Distinct().Count(typeId => !matureAges.ContainsKey(typeId));
        if (typesWithoutMatureSize > 0 && years.Any(year => year > GrowthYearNames.GrowthLimitWithoutMatureSize))
        {
            notes.Add($"{typesWithoutMatureSize:N0} planting type(s) have no mature size (no LDS max height/width), so they stop growing {GrowthYearNames.GrowthLimitWithoutMatureSize} years after planting.");
        }

        var worksetsCreated = new List<string>();
        int moved = 0, created = 0, updated = 0, deleted = 0, unlabelled = 0;
        using var transaction = new Transaction(document, "LIM Growth Projections");
        var failureOptions = transaction.GetFailureHandlingOptions();
        failureOptions.SetFailuresPreprocessor(new SuppressWarnings());
        transaction.SetFailureHandlingOptions(failureOptions);
        transaction.Start();
        try
        {
            var worksetIds = EnsureWorksets(document, model, years.Prepend(0), worksetsCreated);
            var schema = GetOrCreateSchema();
            var byUniqueId = model.Trees.ToDictionary(tree => tree.UniqueId, StringComparer.Ordinal);

            // Elements other people own can't be edited; check them out first and skip what we can't get.
            var editIds = toMove.Select(tree => tree.Id)
                .Concat(plan.ToUpdate.Concat(plan.ToDelete).Select(copy => byUniqueId[copy.CopyUniqueId].Id))
                .ToList();
            var editable = editIds.Count == 0
                ? new HashSet<long>()
                : WorksharingUtils.CheckoutElements(document, editIds).Select(id => id.Value).ToHashSet();
            var notEditable = editIds.Count(id => !editable.Contains(id.Value));
            if (notEditable > 0)
            {
                notes.Add($"{notEditable:N0} element(s) are borrowed by someone else and were skipped — sync with central and run again.");
            }

            foreach (var tree in toMove.Where(tree => editable.Contains(tree.Id.Value)))
            {
                SetWorkset(tree, worksetIds[0]);
                SetAge(document, tree, 0, matureAges);
                moved++;
            }

            foreach (var link in plan.ToUpdate)
            {
                var copy = byUniqueId[link.CopyUniqueId];
                if (!editable.Contains(copy.Id.Value) || !byUniqueId.TryGetValue(link.SourceUniqueId, out var source))
                {
                    continue;
                }

                SetPinned(copy, false);
                MatchOriginal(document, source, copy);
                SetWorkset(copy, worksetIds[link.Year]);
                SetAge(document, copy, link.Year, matureAges);
                unlabelled += MarkGenerated(copy, link.Year, source) ? 0 : 1;
                SetPinned(copy, true);
                updated++;
            }

            var wrongOption = new Dictionary<string, int>(StringComparer.Ordinal);
            foreach (var item in plan.ToCreate)
            {
                var source = byUniqueId[item.SourceUniqueId];
                var copyId = ElementTransformUtils.CopyElement(document, source.Id, XYZ.Zero).FirstOrDefault();
                if (copyId is null || document.GetElement(copyId) is not { } copy)
                {
                    continue;
                }

                // Revit places new elements in the active design option; a copy that didn't land in
                // its original's option is undone and reported rather than left in the wrong scenario.
                if ((copy.DesignOption?.Id.Value ?? -1) != (source.DesignOption?.Id.Value ?? -1))
                {
                    document.Delete(copyId);
                    var label = source.DesignOption is null ? "Main model" : OptionLabel(document, source.DesignOption);
                    wrongOption[label] = wrongOption.GetValueOrDefault(label) + 1;
                    continue;
                }

                SetWorkset(copy, worksetIds[item.Year]);
                SetAge(document, copy, item.Year, matureAges);
                var entity = new Entity(schema);
                entity.Set("SourceUniqueId", item.SourceUniqueId);
                entity.Set("GrowthYear", item.Year);
                copy.SetEntity(entity);
                unlabelled += MarkGenerated(copy, item.Year, source) ? 0 : 1;
                SetPinned(copy, true);
                created++;
            }

            foreach (var (label, count) in wrongOption)
            {
                notes.Add($"{count:N0} copies for trees in '{label}' were skipped: Revit puts new elements into the active design option. Make '{label}' the active option (Manage > Design Options) and run again.");
            }

            var deleteIds = plan.ToDelete
                .Select(copy => byUniqueId[copy.CopyUniqueId].Id)
                .Where(id => editable.Contains(id.Value))
                .ToList();
            if (deleteIds.Count > 0)
            {
                foreach (var id in deleteIds)
                {
                    SetPinned(document.GetElement(id), false);
                }

                deleted = document.Delete(deleteIds).Count;
            }

            if (unlabelled > 0)
            {
                notes.Add($"{unlabelled:N0} copies couldn't be labelled in {GeneratedByParameter}: run Import Shared Parameter, then run again.");
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

        return new GrowthProjectionResult(document.Title, false, inScope.Count, moved, created, updated, deleted, worksetsCreated, notes);
    }

    private sealed record LinkedCopy(Element Element, GrowthCopyLink Link);

    private sealed record ModelTrees(
        List<Element> Trees,
        List<Element> Originals,
        List<LinkedCopy> Copies,
        Dictionary<int, Workset> YearWorksets,
        Dictionary<long, int> YearByWorkset,
        int UnlinkedGrowthTrees);

    /// <summary>
    /// Every Planting instance, split into originals (not made by this tool, and on no growth year
    /// above 0) and linked copies, plus the model's growth-year worksets.
    /// </summary>
    private static ModelTrees ReadModel(Document document)
    {
        var yearWorksets = FindYearWorksets(document);
        var yearByWorkset = document.IsWorkshared
            ? new FilteredWorksetCollector(document).OfKind(WorksetKind.UserWorkset)
                .Where(workset => GrowthYearNames.FromName(workset.Name) is not null)
                .ToDictionary(workset => (long)workset.Id.IntegerValue, workset => GrowthYearNames.FromName(workset.Name)!.Value)
            : [];

        var trees = new FilteredElementCollector(document)
            .OfCategory(BuiltInCategory.OST_Planting)
            .WhereElementIsNotElementType()
            .OfClass(typeof(FamilyInstance))
            .ToList();

        var schema = Schema.Lookup(LinkSchemaGuid);
        var originals = new List<Element>();
        var copies = new List<LinkedCopy>();
        var unlinkedGrowthTrees = 0;
        foreach (var tree in trees)
        {
            if (LinkOf(tree, schema) is { } link)
            {
                copies.Add(new LinkedCopy(tree, link));
            }
            // On a growth year above 0, or given one by hand (its projected year decides its
            // workset; Sort & check trees moves it there): not an original to project.
            else if (yearByWorkset.TryGetValue(tree.WorksetId.IntegerValue, out var year) && year > 0 ||
                     ProjectedYearOf(tree) is > 0)
            {
                unlinkedGrowthTrees++;
            }
            else
            {
                originals.Add(tree);
            }
        }

        return new ModelTrees(trees, originals, copies, yearWorksets, yearByWorkset, unlinkedGrowthTrees);
    }

    /// <summary>The model's growth-year worksets by year; of two for one year, the one with the standard name.</summary>
    internal static Dictionary<int, Workset> FindYearWorksets(Document document)
    {
        var yearWorksets = new Dictionary<int, Workset>();
        if (!document.IsWorkshared)
        {
            return yearWorksets;
        }

        foreach (var workset in new FilteredWorksetCollector(document).OfKind(WorksetKind.UserWorkset))
        {
            if (GrowthYearNames.FromName(workset.Name) is { } year &&
                (!yearWorksets.TryGetValue(year, out var existing) || workset.Name == GrowthYearNames.WorksetName(year) && existing.Name != workset.Name))
            {
                yearWorksets[year] = workset;
            }
        }

        return yearWorksets;
    }

    /// <summary>The link a Growth Projections copy carries to its original; null for any other tree.</summary>
    internal static GrowthCopyLink? LinkOf(Element tree, Schema? schema = null)
    {
        schema ??= Schema.Lookup(LinkSchemaGuid);
        return schema is not null && tree.GetEntity(schema) is { } entity && entity.IsValid()
            ? new GrowthCopyLink(tree.UniqueId, entity.Get<string>("SourceUniqueId"), entity.Get<int>("GrowthYear"))
            : null;
    }

    /// <summary>The tree's <c>!_S_PLT_TreeGrowth_ProjectedYears_Number</c>, if it has a value.</summary>
    internal static int? ProjectedYearOf(Element tree) =>
        tree.LookupParameter(TreeAgeSyncService.ProjectedYearsParameter) is { HasValue: true, StorageType: StorageType.Integer } projected
            ? projected.AsInteger()
            : null;

    private static List<Element> SelectScope(UIDocument uiDocument, ModelTrees model, GrowthProjectionRequest request, List<string> notes)
    {
        switch (request.Scope)
        {
            case GrowthProjectionScopes.Selection:
            {
                var selected = uiDocument.Selection.GetElementIds().Select(id => id.Value).ToHashSet();
                var picked = model.Originals.Where(tree => selected.Contains(tree.Id.Value)).ToList();
                if (picked.Count == 0)
                {
                    throw new InvalidOperationException("No original trees are selected in Revit — select the trees to project (copies made by Growth Projections don't count).");
                }

                return picked;
            }
            case GrowthProjectionScopes.DesignOptions:
            {
                var keys = request.OptionKeys.ToHashSet(StringComparer.Ordinal);
                if (keys.Count == 0)
                {
                    throw new InvalidOperationException("Tick at least one design option (or the main model) to project.");
                }

                return model.Originals.Where(tree => keys.Contains(OptionKey(tree))).ToList();
            }
            default:
                return model.Originals;
        }
    }

    private static Dictionary<int, WorksetId> EnsureWorksets(Document document, ModelTrees model, IEnumerable<int> years, List<string> created)
    {
        var ids = new Dictionary<int, WorksetId>();
        foreach (var year in years)
        {
            if (model.YearWorksets.TryGetValue(year, out var existing))
            {
                ids[year] = existing.Id;
                continue;
            }

            var name = GrowthYearNames.WorksetName(year);
            if (!WorksetTable.IsWorksetNameUnique(document, name))
            {
                throw new InvalidOperationException($"A workset named '{name}' exists but isn't a user workset — rename it and run again.");
            }

            ids[year] = Workset.Create(document, name).Id;
            created.Add(name);
        }

        return ids;
    }

    private static int? GrowthYearOf(ModelTrees model, Element tree) =>
        model.YearByWorkset.TryGetValue(tree.WorksetId.IntegerValue, out var year) ? year : null;

    internal static void SetWorkset(Element element, WorksetId worksetId)
    {
        if (element.WorksetId != worksetId &&
            element.get_Parameter(BuiltInParameter.ELEM_PARTITION_PARAM) is { IsReadOnly: false } parameter)
        {
            parameter.Set(worksetId.IntegerValue);
        }
    }

    /// <summary>
    /// Writes the tree's growth year to <c>!_S_PLT_TreeGrowth_ProjectedYears_Number</c>; the family
    /// computes its age from that. Only when the family has no such formula yet (Years still editable)
    /// is Years set directly: age at planting + year, capped at mature size.
    /// </summary>
    internal static void SetAge(Document document, Element tree, int growthYear, IReadOnlyDictionary<long, int> matureAges)
    {
        if (tree.LookupParameter(TreeAgeSyncService.ProjectedYearsParameter) is { IsReadOnly: false, StorageType: StorageType.Integer } projected)
        {
            projected.Set(growthYear);
        }

        if (tree.LookupParameter(TreeAgeSyncService.YearsParameter) is not { IsReadOnly: false, StorageType: StorageType.Integer } years)
        {
            return;
        }

        var typeId = tree.GetTypeId().Value;
        years.Set(GrowthYearNames.ModelledAge(
            TreeAgeSyncService.AgeAtPlanting(tree), growthYear, matureAges.TryGetValue(typeId, out var mature) ? mature : null));
    }

    /// <summary>
    /// Labels a copy so people leave it alone: which growth year it is and which tree it copies. False
    /// when the parameter isn't on the trees yet (Import Shared Parameter adds it).
    /// </summary>
    internal static bool MarkGenerated(Element copy, int year, Element source)
    {
        if (copy.LookupParameter(GeneratedByParameter) is not { IsReadOnly: false, StorageType: StorageType.String } label)
        {
            return false;
        }

        var text = $"LIM Growth Projections: {year}-year copy of tree {source.Id.Value}. Edit the original tree, not this copy.";
        if (label.AsString() != text)
        {
            label.Set(text);
        }

        return true;
    }

    /// <summary>Pins or unpins a copy: pinned while it waits for the next run, unpinned while the run moves, retypes or deletes it.</summary>
    internal static void SetPinned(Element? element, bool pinned)
    {
        if (element is not null && element.Pinned != pinned)
        {
            try
            {
                element.Pinned = pinned;
            }
            catch (Autodesk.Revit.Exceptions.InvalidOperationException)
            {
                // Some elements can't be pinned (e.g. in a group); the label still says what it is.
            }
        }
    }

    /// <summary>Moves, rotates and retypes a copy to match its original, and copies its i-Tree inputs (condition, crown exposure...).</summary>
    private static void MatchOriginal(Document document, Element source, Element copy)
    {
        if (copy.GetTypeId() != source.GetTypeId())
        {
            copy.ChangeTypeId(source.GetTypeId());
        }

        if (source.Location is LocationPoint sourceLocation && copy.Location is LocationPoint copyLocation)
        {
            if (!copyLocation.Point.IsAlmostEqualTo(sourceLocation.Point))
            {
                copyLocation.Point = sourceLocation.Point;
            }

            var turn = sourceLocation.Rotation - copyLocation.Rotation;
            if (Math.Abs(turn) > 1e-9)
            {
                var point = sourceLocation.Point;
                ElementTransformUtils.RotateElement(document, copy.Id, Line.CreateBound(point, point + XYZ.BasisZ), turn);
            }
        }

        foreach (Parameter parameter in source.Parameters)
        {
            if (!(parameter.Definition.Name.StartsWith(InputParameterPrefix, StringComparison.Ordinal) ||
                  parameter.Definition.Name == TreeAgeSyncService.AgeAtPlantingParameter) ||
                copy.LookupParameter(parameter.Definition.Name) is not { IsReadOnly: false } target ||
                target.StorageType != parameter.StorageType)
            {
                continue;
            }

            _ = parameter.StorageType switch
            {
                StorageType.String => target.Set(parameter.AsString() ?? string.Empty),
                StorageType.Integer => target.Set(parameter.AsInteger()),
                StorageType.Double => target.Set(parameter.AsDouble()),
                StorageType.ElementId => target.Set(parameter.AsElementId()),
                _ => false
            };
        }
    }

    internal static string OptionKey(Element tree) =>
        tree.DesignOption is null ? GrowthOptionKeys.MainModel : tree.DesignOption.Id.Value.ToString();

    private static long? ActiveOptionId(Document document)
    {
        var active = DesignOption.GetActiveDesignOptionId(document);
        return active == ElementId.InvalidElementId ? null : active.Value;
    }

    /// <summary>"Set : Option (primary)", matching the labels the Dashboard and BNG tab show.</summary>
    internal static string OptionLabel(Document document, DesignOption option)
    {
        // DesignOptionSet isn't a public API type; read the set's name through its element.
        var set = option.get_Parameter(BuiltInParameter.OPTION_SET_ID) is { HasValue: true } setParameter
            ? document.GetElement(setParameter.AsElementId())?.Name
            : null;
        return $"{set ?? "Option set"} : {DesignOptionScenarioService.CleanOptionName(option.Name)}{(option.IsPrimary ? " (primary)" : string.Empty)}";
    }

    /// <summary>The hidden link each copy carries to its original (vendor EGIS, matching the add-in manifest).</summary>
    private static Schema GetOrCreateSchema()
    {
        if (Schema.Lookup(LinkSchemaGuid) is { } existing)
        {
            return existing;
        }

        var builder = new SchemaBuilder(LinkSchemaGuid);
        builder.SetSchemaName("LIMGrowthProjectionLink");
        builder.SetReadAccessLevel(AccessLevel.Public);
        builder.SetWriteAccessLevel(AccessLevel.Vendor);
        builder.SetVendorId("EGIS");
        builder.SetDocumentation("Links a Growth Projections tree copy to the original tree it projects.");
        builder.AddSimpleField("SourceUniqueId", typeof(string));
        builder.AddSimpleField("GrowthYear", typeof(int));
        return builder.Finish();
    }

    /// <summary>Copies sit exactly on their originals (on other worksets), which Revit warns about once per copy — drop those warnings.</summary>
    private sealed class SuppressWarnings : IFailuresPreprocessor
    {
        public FailureProcessingResult PreprocessFailures(FailuresAccessor failuresAccessor)
        {
            foreach (var failure in failuresAccessor.GetFailureMessages())
            {
                if (failure.GetSeverity() == FailureSeverity.Warning)
                {
                    failuresAccessor.DeleteWarning(failure);
                }
            }

            return FailureProcessingResult.Continue;
        }
    }
}
