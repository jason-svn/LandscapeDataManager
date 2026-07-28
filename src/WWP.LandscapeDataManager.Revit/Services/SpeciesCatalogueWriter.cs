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

        using var transaction = new Transaction(document, "LIM Update Species Catalogue");
        transaction.Start();
        try
        {
            foreach (var type in types)
            {
                var codeParameter = type.LookupParameter(CodeParameter);
                var code = codeParameter?.AsString()?.Trim();
                if (string.IsNullOrEmpty(code))
                {
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

        var scheduleCreated = false;
        try
        {
            using var scheduleTransaction = new Transaction(document, "LIM Create Planting Key Schedule");
            scheduleTransaction.Start();
            scheduleCreated = PlantingKeyScheduleService.EnsureSchedule(document);
            scheduleTransaction.Commit();
        }
        catch
        {
            // The species parameter updates above already succeeded and were committed; a
            // schedule-creation problem shouldn't hide that real, useful result from the user.
            scheduleCreated = false;
        }

        return new UpdateSpeciesCatalogueResult(document.Title, rows, scheduleCreated, skipped);
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
}
