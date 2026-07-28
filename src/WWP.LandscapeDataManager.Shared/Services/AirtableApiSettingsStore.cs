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

    public async Task SaveAsync(AirtableApiSettings settings)
    {
        var directory = Path.GetDirectoryName(_filePath)
                        ?? throw new InvalidOperationException("The settings directory could not be resolved.");
        Directory.CreateDirectory(directory);

        var options = new JsonSerializerOptions(JsonDefaults.Options) { WriteIndented = true };
        await using var stream = File.Create(_filePath);
        await JsonSerializer.SerializeAsync(stream, settings, options);
    }
}

/// <summary>Non-secret Airtable connection details. The API token itself lives in <see cref="AirtableCredentialStore"/>.</summary>
public sealed record AirtableApiSettings(string BaseId, string TableIdOrName, string? ViewName);
