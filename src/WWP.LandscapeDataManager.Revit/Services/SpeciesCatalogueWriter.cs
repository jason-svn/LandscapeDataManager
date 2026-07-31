using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using WWP.LandscapeDataManager.Contracts;

namespace WWP.LandscapeDataManager.Revit.Services;

/// <summary>
/// Updates Planting types whose species code is already known to Revit, matching by species
/// code only. A type with no Species_Code isn't part of this report at all — Full Catalogue
/// downloads still only ever write species that are actually used in the project (see
/// <see cref="Contracts.UpdateSpeciesCatalogueRequest"/> callers for why).
/// </summary>
internal static class SpeciesCatalogueWriter
{
    private const string CodeParameter = "!_S_PLANTING_iTreeSpecies_Code_Text";
    private const string CommonNameParameter = "!_S_PLANTING_iTreeSpecies_CommonName_Text";
    private const string ScientificNameParameter = "!_S_PLANTING_iTreeSpecies_ScientificName_Text";
    private const string SpeciesTypeParameter = "!_S_PLANTING_iTreeSpecies_Type_Text";
    private const string ReplaceByParameter = "!_S_PLANTING_iTreeSpecies_ReplaceBy_Text";

    public static UpdateSpeciesCatalogueResult Update(UIApplication application, UpdateSpeciesCatalogueRequest request)
    {
        var document = application.ActiveUIDocument?.Document
                       ?? throw new InvalidOperationException("Open a Revit project before updating the species catalogue.");

        var recordsByCode = request.Records
            .GroupBy(record => record.SpeciesCode.Trim(), StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => group.First(), StringComparer.OrdinalIgnoreCase);

        var types = new FilteredElementCollector(document)
            .OfCategory(BuiltInCategory.OST_Planting)
            .WhereElementIsElementType()
            .ToList();

        var rows = new List<SpeciesCatalogueUpdateRow>();
        var skipped = 0;
        var missingParameter = 0;
        var emptyCode = 0;

        using var transaction = new Transaction(document, "LIM Update Species Catalogue");
        transaction.Start();
        try
        {
            foreach (var type in types)
            {
                var codeParameter = type.LookupParameter(CodeParameter);
                if (codeParameter is null)
                {
                    // The Species_Code shared parameter isn't bound to Planting types in this
                    // project at all — distinct from a type that has the parameter but hasn't
                    // had a code entered yet, since the fix is different (run Shared Parameter
                    // Setup) rather than just entering a value.
                    missingParameter++;
                    continue;
                }

                var code = codeParameter.AsString()?.Trim();
                if (string.IsNullOrEmpty(code))
                {
                    emptyCode++;
                    continue;
                }

                if (!recordsByCode.TryGetValue(code, out var record))
                {
                    skipped++;
                    continue;
                }

                rows.Add(UpdateOne(type, code, record));
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

        return new UpdateSpeciesCatalogueResult(
            document.Title, rows, skipped, missingParameter, emptyCode, types.Count);
    }

    private static SpeciesCatalogueUpdateRow UpdateOne(Element type, string code, SpeciesCatalogueRecord record)
    {
        var commonNameParameter = type.LookupParameter(CommonNameParameter);
        var scientificNameParameter = type.LookupParameter(ScientificNameParameter);
        var speciesTypeParameter = type.LookupParameter(SpeciesTypeParameter);
        var replaceByParameter = type.LookupParameter(ReplaceByParameter);

        var previousCommonName = commonNameParameter?.AsString() ?? string.Empty;
        var previousScientificName = scientificNameParameter?.AsString() ?? string.Empty;
        var previousSpeciesType = speciesTypeParameter?.AsString() ?? string.Empty;
        var previousReplaceBy = replaceByParameter?.AsString() ?? string.Empty;
        var wasEmpty = string.IsNullOrEmpty(previousCommonName) &&
                       string.IsNullOrEmpty(previousScientificName) &&
                       string.IsNullOrEmpty(previousSpeciesType);
        var changed = !string.Equals(previousCommonName, record.CommonName, StringComparison.Ordinal) ||
                      !string.Equals(previousScientificName, record.ScientificName, StringComparison.Ordinal) ||
                      !string.Equals(previousSpeciesType, record.SpeciesType, StringComparison.Ordinal) ||
                      !string.Equals(previousReplaceBy, record.ReplaceBy ?? string.Empty, StringComparison.Ordinal);

        SetIfWritable(commonNameParameter, record.CommonName);
        SetIfWritable(scientificNameParameter, record.ScientificName);
        SetIfWritable(speciesTypeParameter, record.SpeciesType);
        SetIfWritable(replaceByParameter, record.ReplaceBy ?? string.Empty);

        var status = !string.IsNullOrWhiteSpace(record.ReplaceBy)
            ? "Deprecated"
            : wasEmpty ? "Added" : changed ? "Changed" : "Unchanged";
        var message = !string.IsNullOrWhiteSpace(record.ReplaceBy)
            ? $"Replaced by species code '{record.ReplaceBy}'."
            : null;

        return new SpeciesCatalogueUpdateRow(code, type.Name, status, message);
    }

    private static void SetIfWritable(Parameter? parameter, string value)
    {
        if (parameter is { IsReadOnly: false })
        {
            parameter.Set(value);
        }
    }

    /// <summary>
    /// Writes one chosen species record onto the ElementType of every currently-selected Revit
    /// element, deduped by type so multiple selected instances of the same type are only written
    /// once. Unlike <see cref="Update"/>, this never matches by an existing Species_Code — the
    /// caller has already picked the exact species to apply.
    /// </summary>
    public static AssignSpeciesResult AssignToSelection(UIApplication application, SpeciesCatalogueRecord record)
    {
        var uiDocument = application.ActiveUIDocument
                        ?? throw new InvalidOperationException("Open a Revit project before assigning species data.");
        var document = uiDocument.Document;

        var selectedIds = uiDocument.Selection.GetElementIds();
        if (selectedIds.Count == 0)
        {
            throw new InvalidOperationException("Select one or more planting instances or types in Revit first.");
        }

        var types = selectedIds
            .Select(document.GetElement)
            .Where(element => element is not null)
            .Select(element => element as ElementType ?? document.GetElement(element!.GetTypeId()) as ElementType)
            .Where(elementType => elementType is not null &&
                                  elementType.Category?.BuiltInCategory == BuiltInCategory.OST_Planting)
            .Cast<ElementType>()
            .DistinctBy(elementType => elementType.Id.Value)
            .ToList();

        if (types.Count == 0)
        {
            throw new InvalidOperationException("None of the selected elements are Planting instances or types.");
        }

        var updatedNames = new List<string>();
        using var transaction = new Transaction(document, "LIM Assign Species");
        transaction.Start();
        try
        {
            foreach (var type in types)
            {
                SetIfWritable(type.LookupParameter(CodeParameter), record.SpeciesCode);
                SetIfWritable(type.LookupParameter(CommonNameParameter), record.CommonName);
                SetIfWritable(type.LookupParameter(ScientificNameParameter), record.ScientificName);
                SetIfWritable(type.LookupParameter(SpeciesTypeParameter), record.SpeciesType);
                SetIfWritable(type.LookupParameter(ReplaceByParameter), record.ReplaceBy ?? string.Empty);
                updatedNames.Add(type.Name);
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

        return new AssignSpeciesResult(document.Title, updatedNames);
    }
}
