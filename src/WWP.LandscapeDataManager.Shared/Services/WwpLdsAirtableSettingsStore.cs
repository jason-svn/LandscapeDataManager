using System.Text.Json;
using WWP.LandscapeDataManager.Contracts;

namespace WWP.LandscapeDataManager.Shared.Services;

/// <summary>
/// Remembers which Airtable base/table/view holds the WWP landscape data sheet — a single shared
/// company resource (unlike the per-project Importer data source), so it defaults to WWP's own
/// base rather than starting empty.
/// </summary>
public sealed class WwpLdsAirtableSettingsStore
{
    private static readonly WwpLdsAirtableSettings Defaults = new("apptELCzLzMbmrk54", "tblAwGKQjNQJ9XsKM", "viwg4x3F7UpiVYDsn");

    private readonly string _filePath;

    public WwpLdsAirtableSettingsStore()
    {
        var directory = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "EGIS",
            "WWP.LandscapeDataManager");
        _filePath = Path.Combine(directory, "wwp-lds-airtable-settings.json");
    }

    public async Task<WwpLdsAirtableSettings> LoadAsync()
    {
        if (!File.Exists(_filePath))
        {
            return Defaults;
        }

        await using var stream = File.OpenRead(_filePath);
        return await JsonSerializer.DeserializeAsync<WwpLdsAirtableSettings>(stream, JsonDefaults.Options)
               ?? Defaults;
    }

    public async Task SaveAsync(WwpLdsAirtableSettings settings)
    {
        var directory = Path.GetDirectoryName(_filePath)
                        ?? throw new InvalidOperationException("The settings directory could not be resolved.");
        Directory.CreateDirectory(directory);

        var options = new JsonSerializerOptions(JsonDefaults.Options) { WriteIndented = true };
        await using var stream = File.Create(_filePath);
        await JsonSerializer.SerializeAsync(stream, settings, options);
    }
}

public sealed record WwpLdsAirtableSettings(string BaseId, string TableIdOrName, string? ViewName);
