using System.Text.Json;

namespace WWP.LandscapeDataManager.Contracts;

public static class PipeCommands
{
    public const string GetStatus = "get-status";
    public const string ScanModel = "scan-model";
    public const string GetParameterCatalog = "get-parameter-catalog";
}

public sealed record PipeRequest(string RequestId, string Command, JsonElement? Payload);

public sealed record PipeResponse(
    string RequestId,
    bool Success,
    JsonElement? Data = null,
    string? Error = null);

public sealed record RevitStatus(
    string RevitVersion,
    bool HasActiveDocument,
    string? DocumentTitle);

public sealed record ModelScanOptions(bool PrimaryDesignOptionsOnly = true);

public sealed record ModelScanItem(
    string Category,
    string TypeName,
    long TypeId,
    int ElementCount,
    double AreaSquareMetres,
    string? CalculationType);

public sealed record ModelScanResult(
    string DocumentTitle,
    IReadOnlyList<ModelScanItem> Items);

public sealed record RevitParameterDescriptor(
    string Name,
    string Scope,
    string StorageType,
    string DataTypeId,
    bool IsWritable,
    IReadOnlyList<string> Categories);

public sealed record ParameterCatalogResult(
    string DocumentTitle,
    IReadOnlyList<RevitParameterDescriptor> Parameters);

public sealed record ParameterMappingDefinition(
    string AirtableField,
    string RevitParameter,
    string Scope,
    string Conversion,
    bool Enabled = true);

public static class JsonDefaults
{
    public static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = false
    };

    public static JsonElement ToElement<T>(T value) =>
        JsonSerializer.SerializeToElement(value, Options);
}
