using System.Text.Json;
using WWP.LandscapeDataManager.Contracts;

namespace WWP.LandscapeDataManager.Shared.Services;

public sealed class ParameterMappingStore
{
    private readonly string _filePath;

    public ParameterMappingStore()
    {
        var directory = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "EGIS",
            "WWP.LandscapeDataManager");
        _filePath = Path.Combine(directory, "parameter-mappings.json");
    }

    public string FilePath => _filePath;

    public async Task<IReadOnlyList<ParameterMappingDefinition>> LoadAsync()
    {
        if (!File.Exists(_filePath))
        {
            return [];
        }

        await using var stream = File.OpenRead(_filePath);
        return await JsonSerializer.DeserializeAsync<List<ParameterMappingDefinition>>(
                   stream,
                   JsonDefaults.Options)
               ?? [];
    }

    public async Task SaveAsync(IReadOnlyList<ParameterMappingDefinition> mappings) =>
        await ExportToAsync(_filePath, mappings);

    /// <summary>Writes mappings to an arbitrary file, e.g. one the user picked to share with a teammate.</summary>
    public async Task ExportToAsync(string path, IReadOnlyList<ParameterMappingDefinition> mappings)
    {
        var directory = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        var options = new JsonSerializerOptions(JsonDefaults.Options)
        {
            WriteIndented = true
        };
        await using var stream = File.Create(path);
        await JsonSerializer.SerializeAsync(stream, mappings, options);
    }

    /// <summary>Reads mappings from an arbitrary file, e.g. one a teammate shared.</summary>
    public async Task<IReadOnlyList<ParameterMappingDefinition>> ImportFromAsync(string path)
    {
        await using var stream = File.OpenRead(path);
        return await JsonSerializer.DeserializeAsync<List<ParameterMappingDefinition>>(stream, JsonDefaults.Options)
               ?? [];
    }
}
