using Autodesk.Revit.DB;
using WWP.LandscapeDataManager.Contracts;

namespace WWP.LandscapeDataManager.Revit.Services;

/// <summary>
/// Finds the age at which each planting type reaches its mature size — the WWP landscape data
/// sheet's <c>!_S_PLT_LDS_MaxHeight_Length</c> / <c>MaxWidth_Length</c> — so growth projections stop
/// growing a tree once it's mature instead of letting the family formulas run on forever. The
/// family's growth formulas aren't readable, so this measures them: on one sample tree per type it
/// sets the modelled age, lets Revit regenerate the crown, and searches for the first age where the
/// height or the width reaches its maximum. Everything is rolled back; nothing in the model changes.
/// </summary>
internal static class TreeMaturityService
{
    private const string HeightParameter = "!_S_PLT_TreeFoliage_Height";
    private const string WidthParameter = "!_S_PLT_TreeFoliage_Width";
    private const string MaxHeightParameter = "!_S_PLT_LDS_MaxHeight_Length";
    private const string MaxWidthParameter = "!_S_PLT_LDS_MaxWidth_Length";

    /// <summary>
    /// Mature age per planting type id; a type is missing when it has no maximum size, no growth
    /// parameters, or doesn't reach its maximum within <see cref="GrowthYearNames.MaximumYear"/> years.
    /// Must be called outside an open transaction.
    /// </summary>
    public static Dictionary<long, int> GetMatureAges(Document document, IEnumerable<Element> trees)
    {
        var matureAges = new Dictionary<long, int>();
        if (document.IsReadOnly || document.IsModifiable)
        {
            return matureAges;
        }

        var samples = trees
            .Where(IsProbeable)
            .GroupBy(tree => tree.GetTypeId().Value)
            .Select(group => group.First())
            .Where(tree => GetMaxima(document, tree) is var maxima && (maxima.Height is not null || maxima.Width is not null))
            .ToList();
        if (samples.Count == 0)
        {
            return matureAges;
        }

        using var probe = new Transaction(document, "LIM measure mature tree size");
        var failureOptions = probe.GetFailureHandlingOptions();
        failureOptions.SetFailuresPreprocessor(new IgnoreWarnings());
        failureOptions.SetClearAfterRollback(true);
        probe.SetFailureHandlingOptions(failureOptions);
        if (probe.Start() != TransactionStatus.Started)
        {
            return matureAges;
        }

        try
        {
            foreach (var tree in samples)
            {
                try
                {
                    if (FindMatureAge(document, tree) is { } age)
                    {
                        matureAges[tree.GetTypeId().Value] = age;
                    }
                }
                catch (Autodesk.Revit.Exceptions.ApplicationException)
                {
                    // A family that fails to regenerate at some age just goes uncapped.
                }
            }
        }
        finally
        {
            probe.RollBack();
        }

        return matureAges;
    }

    /// <summary>The smallest age (0–<see cref="GrowthYearNames.MaximumYear"/>) at which the sample reaches its maximum height or width.</summary>
    private static int? FindMatureAge(Document document, Element tree)
    {
        var (maxHeight, maxWidth) = GetMaxima(document, tree);
        var years = tree.LookupParameter(TreeAgeSyncService.YearsParameter)!;

        bool IsMatureAt(int age)
        {
            years.Set(age);
            document.Regenerate();
            var height = tree.LookupParameter(HeightParameter) is { HasValue: true, StorageType: StorageType.Double } h ? h.AsDouble() : (double?)null;
            var width = tree.LookupParameter(WidthParameter) is { HasValue: true, StorageType: StorageType.Double } w ? w.AsDouble() : (double?)null;
            return maxHeight is { } mh && height >= mh - 1e-6 || maxWidth is { } mw && width >= mw - 1e-6;
        }

        if (!IsMatureAt(GrowthYearNames.MaximumYear))
        {
            return null;
        }

        int low = 0, high = GrowthYearNames.MaximumYear;
        while (low < high)
        {
            var middle = (low + high) / 2;
            if (IsMatureAt(middle))
            {
                high = middle;
            }
            else
            {
                low = middle + 1;
            }
        }

        return low;
    }

    /// <summary>Maximum height/width in Revit internal units, from the tree's type; null when not set.</summary>
    private static (double? Height, double? Width) GetMaxima(Document document, Element tree)
    {
        double? Read(string name) =>
            ElementParameters.LookupOnInstanceOrType(tree, name) is { HasValue: true, StorageType: StorageType.Double } parameter && parameter.AsDouble() > 0
                ? parameter.AsDouble()
                : null;

        return (Read(MaxHeightParameter), Read(MaxWidthParameter));
    }

    /// <summary>A tree we can set the age of and read the crown size back from.</summary>
    private static bool IsProbeable(Element tree) =>
        tree.LookupParameter(TreeAgeSyncService.YearsParameter) is { IsReadOnly: false, StorageType: StorageType.Integer } &&
        tree.GroupId == ElementId.InvalidElementId &&
        tree.LookupParameter(HeightParameter) is not null &&
        tree.LookupParameter(WidthParameter) is not null &&
        (!tree.Document.IsWorkshared ||
         WorksharingUtils.GetCheckoutStatus(tree.Document, tree.Id) != CheckoutStatus.OwnedByOtherUser);

    private sealed class IgnoreWarnings : IFailuresPreprocessor
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
