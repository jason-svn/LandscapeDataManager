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

    public async Task SaveAsync(SharedParameterFileSettings settings) =>
        await ExportToAsync(_filePath, settings);

    /// <summary>Writes the settings to an arbitrary file, e.g. one the user picked to share with a teammate.</summary>
    public async Task ExportToAsync(string path, SharedParameterFileSettings settings)
    {
        var directory = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        var options = new JsonSerializerOptions(JsonDefaults.Options) { WriteIndented = true };
        await using var stream = File.Create(path);
        await JsonSerializer.SerializeAsync(stream, settings, options);
    }

    /// <summary>Reads settings from an arbitrary file, e.g. one a teammate shared.</summary>
    public async Task<SharedParameterFileSettings> ImportFromAsync(string path)
    {
        await using var stream = File.OpenRead(path);
        return await JsonSerializer.DeserializeAsync<SharedParameterFileSettings>(stream, JsonDefaults.Options)
               ?? new SharedParameterFileSettings(string.Empty);
    }
}

public sealed record SharedParameterFileSettings(string FilePath);
