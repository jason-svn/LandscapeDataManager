using System.Text.Json;
using WWP.LandscapeDataManager.Contracts;

namespace WWP.LandscapeDataManager.Shared.Services;

public sealed class AirtableApiSettingsStore
{
    private readonly string _filePath;

    public AirtableApiSettingsStore()
    {
        var directory = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "EGIS",
            "WWP.LandscapeDataManager");
        _filePath = Path.Combine(directory, "airtable-api-settings.json");
    }

    public async Task<AirtableApiSettings> LoadAsync()
    {
        if (!File.Exists(_filePath))
        {
            return new AirtableApiSettings(string.Empty, string.Empty, string.Empty);
        }

        await using var stream = File.OpenRead(_filePath);
        return await JsonSerializer.DeserializeAsync<AirtableApiSettings>(stream, JsonDefaults.Options)
               ?? new AirtableApiSettings(string.Empty, string.Empty, string.Empty);
    }

    public async Task SaveAsync(AirtableApiSettings settings) =>
        await ExportToAsync(_filePath, settings);

    /// <summary>Writes the settings to an arbitrary file, e.g. one the user picked to share with a teammate.</summary>
    public async Task ExportToAsync(string path, AirtableApiSettings settings)
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
    public async Task<AirtableApiSettings> ImportFromAsync(string path)
    {
        await using var stream = File.OpenRead(path);
        return await JsonSerializer.DeserializeAsync<AirtableApiSettings>(stream, JsonDefaults.Options)
               ?? new AirtableApiSettings(string.Empty, string.Empty, string.Empty);
    }
}

/// <summary>Non-secret Airtable connection details. The API token itself lives in <see cref="AirtableCredentialStore"/>.</summary>
public sealed record AirtableApiSettings(string BaseId, string TableIdOrName, string? ViewName);
