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

    public async Task SaveAsync(IReadOnlyList<ParameterMappingDefinition> mappings)
    {
        var directory = Path.GetDirectoryName(_filePath)
                        ?? throw new InvalidOperationException("The mapping directory could not be resolved.");
        Directory.CreateDirectory(directory);

        var options = new JsonSerializerOptions(JsonDefaults.Options)
        {
            WriteIndented = true
        };
        await using var stream = File.Create(_filePath);
        await JsonSerializer.SerializeAsync(stream, mappings, options);
    }
}
