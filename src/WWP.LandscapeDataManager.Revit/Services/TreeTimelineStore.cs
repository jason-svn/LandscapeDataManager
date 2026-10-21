using System.Text.Json;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.ExtensibleStorage;
using Autodesk.Revit.UI;
using WWP.LandscapeDataManager.Contracts;

namespace WWP.LandscapeDataManager.Revit.Services;

/// <summary>
/// i-Tree's year-by-year forecasts, kept in the project once per unique planting input (see
/// <see cref="TreeBenefitTimeline.Key"/>) so identical trees share one forecast and nothing is
/// requested twice. Stored as JSON on one hidden DataStorage element (vendor EGIS, like the Growth
/// Projections link); each tree only carries the key, in <c>!_S_PLT_Result_iTreeTimelineKey_Text</c>.
/// </summary>
internal static class TreeTimelineStore
{
    private static readonly Guid SchemaGuid = new("9d3c6a41-7e25-4b8f-a0d4-2c51e8f9b736");
    private const string JsonField = "TimelinesJson";

    public const string KeyParameter = "!_S_PLT_Result_iTreeTimelineKey_Text";

    public static TreeTimelinesResult Get(UIApplication application)
    {
        var document = application.ActiveUIDocument?.Document
                       ?? throw new InvalidOperationException("Open a Revit project first.");
        return new TreeTimelinesResult(document.Title, Load(document));
    }

    public static IReadOnlyList<TreeBenefitTimeline> Load(Document document)
    {
        if (Schema.Lookup(SchemaGuid) is not { } schema || FindStorage(document, schema) is not { } storage)
        {
            return [];
        }

        var json = storage.GetEntity(schema).Get<string>(JsonField);
        if (string.IsNullOrWhiteSpace(json))
        {
            return [];
        }

        try
        {
            return JsonSerializer.Deserialize<List<TreeBenefitTimeline>>(json, JsonDefaults.Options) ?? [];
        }
        catch (JsonException)
        {
            return [];
        }
    }

    public static SaveTreeTimelinesResult Save(UIApplication application, SaveTreeTimelinesRequest request)
    {
        var document = application.ActiveUIDocument?.Document
                       ?? throw new InvalidOperationException("Open a Revit project first.");
        var merged = Load(document).ToDictionary(timeline => timeline.Key, StringComparer.Ordinal);
        foreach (var timeline in request.Timelines)
        {
            merged[timeline.Key] = timeline;
        }

        using var transaction = new Transaction(document, "LIM: store i-Tree timelines");
        transaction.Start();
        var schema = GetOrCreateSchema();
        var storage = FindStorage(document, schema) ?? DataStorage.Create(document);
        var entity = new Entity(schema);
        entity.Set(JsonField, JsonSerializer.Serialize(merged.Values.OrderBy(timeline => timeline.Key, StringComparer.Ordinal).ToList(), JsonDefaults.Options));
        storage.SetEntity(entity);
        transaction.Commit();

        return new SaveTreeTimelinesResult(document.Title, merged.Count);
    }

    private static DataStorage? FindStorage(Document document, Schema schema) =>
        new FilteredElementCollector(document)
            .OfClass(typeof(DataStorage))
            .Cast<DataStorage>()
            .FirstOrDefault(storage => storage.GetEntity(schema) is { } entity && entity.IsValid());

    private static Schema GetOrCreateSchema()
    {
        if (Schema.Lookup(SchemaGuid) is { } existing)
        {
            return existing;
        }

        var builder = new SchemaBuilder(SchemaGuid);
        builder.SetSchemaName("LIMTreeTimelines");
        builder.SetReadAccessLevel(AccessLevel.Public);
        builder.SetWriteAccessLevel(AccessLevel.Vendor);
        builder.SetVendorId("EGIS");
        builder.SetDocumentation("i-Tree year-by-year benefit forecasts, one per unique planting input, shared by identical trees.");
        builder.AddSimpleField(JsonField, typeof(string));
        return builder.Finish();
    }
}
