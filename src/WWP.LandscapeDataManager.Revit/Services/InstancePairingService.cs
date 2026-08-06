using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using WWP.LandscapeDataManager.Contracts;

namespace WWP.LandscapeDataManager.Revit.Services;

/// <summary>
/// Explicit, user-driven first-sync pairing: writes a source record's stable ID onto whichever
/// single Planting instance the user has selected in Revit. This exists specifically so a first
/// sync never has to guess a match by display name — the user points at the element instead.
/// </summary>
internal static class InstancePairingService
{
    private const string SourceRecordIdParameter = "!_S_PLT_DataSync_SourceRecordId_Text";

    public static PairSelectedInstanceResult PairSelected(UIApplication application, PairSelectedInstanceRequest request)
    {
        var uiDocument = application.ActiveUIDocument
                         ?? throw new InvalidOperationException("Open a Revit project before pairing an element.");
        var selectedIds = uiDocument.Selection.GetElementIds();
        if (selectedIds.Count != 1)
        {
            return new PairSelectedInstanceResult(
                false,
                null,
                $"Select exactly one Planting element in Revit first (currently {selectedIds.Count} selected).");
        }

        var element = uiDocument.Document.GetElement(selectedIds.First());
        if (element is null || element.Category?.BuiltInCategory != BuiltInCategory.OST_Planting)
        {
            return new PairSelectedInstanceResult(false, null, "The selected element is not a Planting instance.");
        }

        var parameter = element.LookupParameter(SourceRecordIdParameter)
                         ?? throw new InvalidOperationException(
                             $"'{SourceRecordIdParameter}' is not bound to Planting instances yet — run Shared Parameter Setup first.");

        using var transaction = new Transaction(uiDocument.Document, "LIM Pair Planting Instance");
        transaction.Start();
        try
        {
            parameter.Set(request.SourceRecordId);
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

        return new PairSelectedInstanceResult(true, element.UniqueId, null);
    }
}
