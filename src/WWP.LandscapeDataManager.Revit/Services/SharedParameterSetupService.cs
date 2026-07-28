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

    public static SharedParameterSetupResult EnsureParameters(
        UIApplication application,
        EnsureSharedParametersRequest request)
    {
        var document = application.ActiveUIDocument?.Document
                       ?? throw new InvalidOperationException("Open a Revit project before setting up parameters.");

        application.Application.SharedParametersFilename = request.SharedParameterFilePath;
        var definitionFile = application.Application.OpenSharedParameterFile()
                              ?? throw new InvalidOperationException(
                                  $"'{request.SharedParameterFilePath}' could not be opened as a Revit shared parameter file.");

        var group = definitionFile.Groups.get_Item(SharedGroupName)
                    ?? throw new InvalidOperationException(
                        $"The shared parameter file does not contain a group named '{SharedGroupName}'.");

        var definitionsByName = group.Definitions
            .OfType<ExternalDefinition>()
            .ToDictionary(definition => definition.Name, StringComparer.Ordinal);

        var rows = new List<SharedParameterSetupRow>();
        using var transaction = new Transaction(document, "LIM Shared Parameter Setup");
        transaction.Start();
        try
        {
            foreach (var ownership in ParameterOwnership.All)
            {
                rows.Add(EnsureOne(document, application, definitionsByName, ownership));
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

    private static SharedParameterSetupRow EnsureOne(
        Document document,
        UIApplication application,
        IReadOnlyDictionary<string, ExternalDefinition> definitionsByName,
        ParameterOwnership ownership)
    {
        if (!definitionsByName.TryGetValue(ownership.Name, out var definition))
        {
            return new SharedParameterSetupRow(
                ownership.Name,
                string.Empty,
                ownership.Scope,
                string.Join(", ", ownership.Categories),
                "Error",
                "Not found in the shared parameter file's 'PLANTING - iTree' group.");
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
                $"with a different ID ({conflictingElement.GuidValue}). It was left unchanged.");
        }

        var existingBinding = FindExistingBinding(document, ownership.Name);
        if (existingBinding is null)
        {
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
                    "Created", null)
                : new SharedParameterSetupRow(
                    ownership.Name, guidText, ownership.Scope, string.Join(", ", ownership.Categories),
                    "Error", "Revit rejected the new parameter binding.");
        }

        var (isInstance, boundCategories) = existingBinding.Value;
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
                "Conflict", $"Already bound, but not as expected ({reason}). Left unchanged.");
        }

        return new SharedParameterSetupRow(
            ownership.Name, guidText, ownership.Scope, string.Join(", ", ownership.Categories),
            "Already valid", null);
    }

    private static (bool IsInstance, CategorySet Categories)? FindExistingBinding(Document document, string name)
    {
        var iterator = document.ParameterBindings.ForwardIterator();
        iterator.Reset();
        while (iterator.MoveNext())
        {
            if (iterator.Key is not Definition definition || !string.Equals(definition.Name, name, StringComparison.Ordinal))
            {
                continue;
            }

            return iterator.Current switch
            {
                InstanceBinding instanceBinding => (true, instanceBinding.Categories),
                TypeBinding typeBinding => (false, typeBinding.Categories),
                _ => null
            };
        }

        return null;
    }

    private sealed record ParameterOwnership(
        string Name,
        string Scope,
        IReadOnlyList<BuiltInCategory> Categories,
        ForgeTypeId Group)
    {
        private static ParameterOwnership PlantingType(string name, ForgeTypeId group) =>
            new(name, "Type", [BuiltInCategory.OST_Planting], group);

        private static ParameterOwnership PlantingInstance(string name, ForgeTypeId group) =>
            new(name, "Instance", [BuiltInCategory.OST_Planting], group);

        private static ParameterOwnership ProjectInfo(string name, ForgeTypeId group) =>
            new(name, "Instance", [BuiltInCategory.OST_ProjectInformation], group);

        public static readonly IReadOnlyList<ParameterOwnership> All =
        [
            PlantingType("!_S_PLANTING_GrowthRatio_HeightbyYear_Number", GroupTypeId.Constraints),
            PlantingType("!_S_PLANTING_GrowthRatio_TrunkDiameterbyYear_Number", GroupTypeId.Constraints),
            PlantingType("!_S_PLANTING_GrowthRatio_WidthbyYear_Number", GroupTypeId.Constraints),
            PlantingType("!_S_PLANTING_DataSync_InputSignature_Text", GroupTypeId.Data),
            PlantingType("!_S_PLANTING_DataSync_LastUpdated_Text", GroupTypeId.Data),
            PlantingType("!_S_PLANTING_DataSync_Status_Text", GroupTypeId.Data),
            PlantingType("!_S_PLANTING_DataSync_SourceName_Text", GroupTypeId.IdentityData),
            PlantingType("!_S_PLANTING_DataSync_SourceRecordId_Text", GroupTypeId.IdentityData),
            PlantingType("!_S_PLANTING_iTreeSpecies_Code_Text", GroupTypeId.IdentityData),
            PlantingType("!_S_PLANTING_iTreeSpecies_CommonName_Text", GroupTypeId.IdentityData),
            PlantingType("!_S_PLANTING_iTreeSpecies_ReplaceBy_Text", GroupTypeId.IdentityData),
            PlantingType("!_S_PLANTING_iTreeSpecies_ScientificName_Text", GroupTypeId.IdentityData),
            PlantingType("!_S_PLANTING_iTreeSpecies_Type_Text", GroupTypeId.IdentityData),

            PlantingInstance("!_S_PLANTING_TreeGrowth_Years_Number", GroupTypeId.Constraints),
            PlantingInstance("!_S_PLANTING_iTreeInput_Condition_Text", GroupTypeId.Data),
            PlantingInstance("!_S_PLANTING_iTreeInput_CrownExposure_Number", GroupTypeId.Data),
            PlantingInstance("!_S_PLANTING_iTreeResult_Details_Text", GroupTypeId.Data),
            PlantingInstance("!_S_PLANTING_iTreeResult_EngineVersion_Text", GroupTypeId.Data),
            PlantingInstance("!_S_PLANTING_iTreeResult_InputSignature_Text", GroupTypeId.Data),
            PlantingInstance("!_S_PLANTING_iTreeResult_LastUpdated_Text", GroupTypeId.Data),
            PlantingInstance("!_S_PLANTING_iTreeResult_Status_Text", GroupTypeId.Data),
            PlantingInstance("!_S_PLANTING_iTreeResult_UnitSystem_Text", GroupTypeId.Data),
            PlantingInstance("!_S_PLANTING_TreeFoliage_Height", GroupTypeId.Geometry),
            PlantingInstance("!_S_PLANTING_TreeFoliage_Width", GroupTypeId.Geometry),
            PlantingInstance("!_S_PLANTING_TreeOverall_Height", GroupTypeId.Geometry),
            PlantingInstance("!_S_PLANTING_TreeTrunk_DBH_Diameter", GroupTypeId.Geometry),
            PlantingInstance("!_S_PLANTING_TreeTrunk_Diameter", GroupTypeId.Geometry),
            PlantingInstance("!_S_PLANTING_TreeTrunk_Height", GroupTypeId.Geometry),
            PlantingInstance("!_S_PLANTING_iTreeAir_CORemovedAnnual_Number", GroupTypeId.GreenBuilding),
            PlantingInstance("!_S_PLANTING_iTreeAir_NO2RemovedAnnual_Number", GroupTypeId.GreenBuilding),
            PlantingInstance("!_S_PLANTING_iTreeAir_O3RemovedAnnual_Number", GroupTypeId.GreenBuilding),
            PlantingInstance("!_S_PLANTING_iTreeAir_PM25RemovedAnnual_Number", GroupTypeId.GreenBuilding),
            PlantingInstance("!_S_PLANTING_iTreeAir_SO2RemovedAnnual_Number", GroupTypeId.GreenBuilding),
            PlantingInstance("!_S_PLANTING_iTreeCarbon_CO2SequesteredAnnual_Number", GroupTypeId.GreenBuilding),
            PlantingInstance("!_S_PLANTING_iTreeWater_RainfallInterceptedAnnual_Volume", GroupTypeId.GreenBuilding),
            PlantingInstance("!_S_PLANTING_iTreeWater_RunoffAvoidedAnnual_Volume", GroupTypeId.GreenBuilding),

            ProjectInfo("!_S_PLANTING_iTreeLocation_Latitude_Number", GroupTypeId.IdentityData),
            ProjectInfo("!_S_PLANTING_iTreeLocation_Longitude_Number", GroupTypeId.IdentityData),
            ProjectInfo("!_S_PLANTING_iTreeUnits_PreferredSystem_Text", GroupTypeId.IdentityData)
        ];
    }
}
