using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using WWP.LandscapeDataManager.Contracts;

namespace WWP.LandscapeDataManager.Revit.Services;

/// <summary>Preserves agreeing instance values while migrating the three LDS fields to Type.</summary>
internal static class ParameterBindingScopeMigrationService
{
    public static SharedParameterSetupRow MigrateToType(
        Document document,
        UIApplication application,
        ExternalDefinition definition,
        InternalDefinition existingDefinition,
        CategorySet existingCategories,
        IReadOnlyList<BuiltInCategory> expectedCategories,
        ForgeTypeId group,
        string description,
        bool apply)
    {
        SharedParameterSetupRow Result(string status, string message) => new(
            definition.Name, definition.GUID.ToString(), "Type",
            string.Join(", ", expectedCategories), status, message, description);

        if (!InstanceToTypeMigrationPlanner.Supports(definition.Name))
        {
            return Result("Conflict", "This parameter is not eligible for automatic scope migration. Left unchanged.");
        }

        if (document.GetElement(existingDefinition.Id) is not SharedParameterElement shared ||
            shared.GuidValue != definition.GUID ||
            !existingDefinition.GetDataType().Equals(definition.GetDataType()))
        {
            return Result("Conflict", "The existing parameter's shared ID or data type differs. Left unchanged.");
        }

        // Retain every existing category, including any added by the project, while also adding
        // categories required by the current catalog. Snapshot values across all design options.
        var categories = application.Application.Create.NewCategorySet();
        var includedCategoryIds = new HashSet<long>();
        foreach (Category category in existingCategories)
        {
            categories.Insert(category);
            includedCategoryIds.Add(category.Id.Value);
        }
        foreach (var category in expectedCategories)
        {
            var expectedCategory = document.Settings.Categories.get_Item(category);
            if (includedCategoryIds.Add(expectedCategory.Id.Value))
            {
                categories.Insert(expectedCategory);
            }
        }

        var storageType = definition.Name.EndsWith("_Text", StringComparison.Ordinal)
            ? StorageType.String : StorageType.Double;
        InstanceToTypeMigrationPlan plan;
        try
        {
            var categoryIds = existingCategories.Cast<Category>().Select(category => category.Id).ToList();
            var instances = new FilteredElementCollector(document)
                .WhereElementIsNotElementType()
                .WherePasses(new ElementMulticategoryFilter(categoryIds));
            var samples = new List<BindingMigrationSample>();
            foreach (var instance in instances)
            {
                var typeId = instance.GetTypeId();
                if (document.GetElement(typeId) is not ElementType type)
                {
                    return Result("Conflict", $"Element {instance.Id.Value} has no valid type. Binding and values left unchanged.");
                }

                var parameter = instance.get_Parameter(definition.GUID);
                if (parameter is null || parameter.StorageType != storageType)
                {
                    return Result("Conflict", $"Cannot read the expected parameter on element {instance.Id.Value}. Binding and values left unchanged.");
                }

                samples.Add(new BindingMigrationSample(
                    instance.Id.Value, typeId.Value, $"{type.FamilyName} : {type.Name}", ReadValue(parameter)));
            }

            var existingTypeValues = new List<BindingMigrationTypeValue>();
            var allCategoryIds = categories.Cast<Category>().Select(category => category.Id).ToList();
            var types = new FilteredElementCollector(document)
                .WhereElementIsElementType()
                .WherePasses(new ElementMulticategoryFilter(allCategoryIds))
                .OfType<ElementType>();
            foreach (var type in types)
            {
                var parameter = type.get_Parameter(definition.GUID);
                if (parameter is null)
                {
                    continue;
                }
                if (parameter.StorageType != storageType)
                {
                    return Result("Conflict", $"The parameter on type {type.Id.Value} has a different storage type. Left unchanged.");
                }

                existingTypeValues.Add(new BindingMigrationTypeValue(
                    type.Id.Value, $"{type.FamilyName} : {type.Name}", ReadValue(parameter)));
            }

            plan = InstanceToTypeMigrationPlanner.Build(samples, existingTypeValues);
        }
        catch (Exception exception)
        {
            return Result("Conflict", $"Cannot verify existing values: {exception.Message} Binding and values left unchanged.");
        }

        if (!plan.CanMigrate)
        {
            var examples = plan.Conflicts.Take(3).Select(conflict =>
                $"'{conflict.TypeName}' (type {conflict.TypeId}; elements {string.Join(", ", conflict.ElementIds.Take(4))}): {conflict.Reason}");
            var more = plan.Conflicts.Count > 3 ? $"; and {plan.Conflicts.Count - 3} more types" : string.Empty;
            return Result("Conflict", "Cannot migrate Instance to Type: " + string.Join("; ", examples) + more +
                ". Resolve these differences and reload. The entire parameter binding and all values were left unchanged.");
        }

        var populatedCount = plan.TypeValues.Count(value => !value.Value.IsEmpty);
        var summary = $"Convert Instance to Type; preserve values for {populatedCount:N0} type(s) " +
                      $"from {plan.InstanceCount:N0} instance(s). Types with no stored value have nothing to restore.";
        if (!apply)
        {
            return Result("Will update", summary);
        }

        using var migration = new SubTransaction(document);
        if (migration.Start() != TransactionStatus.Started)
        {
            return Result("Conflict", "Could not start scope migration. Binding and values left unchanged.");
        }

        try
        {
            var binding = application.Application.Create.NewTypeBinding(categories);
            if (!document.ParameterBindings.ReInsert(existingDefinition, binding, group))
            {
                throw new InvalidOperationException("Revit rejected the Type binding.");
            }

            document.Regenerate();
            if (!HasTypeBinding(document, definition.GUID, categories))
            {
                throw new InvalidOperationException("The new Type binding or its categories did not verify.");
            }

            foreach (var value in plan.TypeValues)
            {
                var type = document.GetElement(new ElementId(value.TypeId));
                var parameter = type?.get_Parameter(definition.GUID);
                if (parameter is null || parameter.StorageType != storageType)
                {
                    throw new InvalidOperationException($"The Type parameter is unavailable on type {value.TypeId}.");
                }

                // An unset source has no stored value to restore; keep Revit's empty/default value.
                if (value.Value.IsEmpty || ReadValue(parameter) == value.Value)
                {
                    continue;
                }
                if (parameter.IsReadOnly)
                {
                    throw new InvalidOperationException($"The parameter on type {value.TypeId} is read-only.");
                }

                var restored = value.Value.Text is { } text
                    ? parameter.Set(text)
                    : parameter.Set(value.Value.Number!.Value);
                if (!restored)
                {
                    throw new InvalidOperationException($"Revit rejected the value for type {value.TypeId}.");
                }
            }

            document.Regenerate();
            foreach (var value in plan.TypeValues.Where(value => !value.Value.IsEmpty))
            {
                var parameter = document.GetElement(new ElementId(value.TypeId))?.get_Parameter(definition.GUID);
                if (parameter is null || ReadValue(parameter) != value.Value)
                {
                    throw new InvalidOperationException($"The restored value did not verify on type {value.TypeId}.");
                }
            }

            if (migration.Commit() != TransactionStatus.Committed)
            {
                throw new InvalidOperationException("Revit did not commit the scope migration.");
            }

            return Result("Updated", $"Converted Instance to Type and preserved values for {populatedCount:N0} type(s) " +
                $"from {plan.InstanceCount:N0} instance(s).");
        }
        catch (Exception exception)
        {
            if (migration.GetStatus() == TransactionStatus.Started &&
                migration.RollBack() != TransactionStatus.RolledBack)
            {
                throw new InvalidOperationException("Could not roll back the scope migration; cancel parameter setup.", exception);
            }
            if (migration.GetStatus() != TransactionStatus.RolledBack)
            {
                // Do not continue the outer setup transaction when rollback cannot be established.
                throw;
            }

            return Result("Conflict", $"Scope migration failed: {exception.Message} The previous Instance binding and values were restored.");
        }
    }

    private static BindingMigrationValue ReadValue(Parameter parameter) => !parameter.HasValue
        ? new BindingMigrationValue()
        : parameter.StorageType == StorageType.String
            ? BindingMigrationValue.FromText(parameter.AsString())
            : new BindingMigrationValue(Number: parameter.AsDouble());

    private static bool HasTypeBinding(Document document, Guid guid, CategorySet expectedCategories)
    {
        var expectedIds = expectedCategories.Cast<Category>().Select(category => category.Id.Value).ToHashSet();
        var iterator = document.ParameterBindings.ForwardIterator();
        iterator.Reset();
        while (iterator.MoveNext())
        {
            if (iterator.Key is not InternalDefinition definition ||
                document.GetElement(definition.Id) is not SharedParameterElement shared || shared.GuidValue != guid)
            {
                continue;
            }

            return iterator.Current is TypeBinding binding &&
                   expectedIds.SetEquals(binding.Categories.Cast<Category>().Select(category => category.Id.Value));
        }

        return false;
    }
}
