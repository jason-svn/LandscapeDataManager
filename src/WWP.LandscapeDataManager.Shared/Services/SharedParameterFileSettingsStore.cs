using System.Text.Json;
using WWP.LandscapeDataManager.Contracts;

namespace WWP.LandscapeDataManager.Shared.Services;

/// <summary>Remembers the last-used shared parameter file path, per Windows user.</summary>
public sealed class SharedParameterFileSettingsStore
{
    private readonly string _filePath;

    public SharedParameterFileSettingsStore()
    {
        var directory = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "EGIS",
            "WWP.LandscapeDataManager");
        _filePath = Path.Combine(directory, "shared-parameter-file-settings.json");
    }

    public async Task<SharedParameterFileSettings> LoadAsync()
    {
        if (!File.Exists(_filePath))
        {
            return new SharedParameterFileSettings(string.Empty);
        }

        await using var stream = File.OpenRead(_filePath);
        return await JsonSerializer.DeserializeAsync<SharedParameterFileSettings>(stream, JsonDefaults.Options)
               ?? new SharedParameterFileSettings(string.Empty);
    }

    public async Task SaveAsync(SharedParameterFileSettings settings)
    {
        var directory = Path.GetDirectoryName(_filePath)
                        ?? throw new InvalidOperationException("The settings directory could not be resolved.");
        Directory.CreateDirectory(directory);

        var options = new JsonSerializerOptions(JsonDefaults.Options) { WriteIndented = true };
        await using var stream = File.Create(_filePath);
        await JsonSerializer.SerializeAsync(stream, settings, options);
    }
}

public sealed record SharedParameterFileSettings(string FilePath);
