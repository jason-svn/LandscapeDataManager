using System.Text.Json;
using WWP.LandscapeDataManager.Contracts;

namespace WWP.LandscapeDataManager.Shared.Services;

public sealed class DataSourceSettingsStore
{
    private readonly string _filePath;

    public DataSourceSettingsStore()
    {
        var directory = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "EGIS",
            "WWP.LandscapeDataManager");
        _filePath = Path.Combine(directory, "data-source-settings.json");
    }

    public async Task<DataSourceSettings> LoadAsync()
    {
        if (!File.Exists(_filePath))
        {
            return new DataSourceSettings(DataSourceKind.Airtable, string.Empty, string.Empty);
        }

        await using var stream = File.OpenRead(_filePath);
        return await JsonSerializer.DeserializeAsync<DataSourceSettings>(
                   stream,
                   JsonDefaults.Options)
               ?? new DataSourceSettings(DataSourceKind.Airtable, string.Empty, string.Empty);
    }

    public async Task SaveAsync(DataSourceSettings settings) =>
        await ExportToAsync(_filePath, settings);

    /// <summary>Writes the settings to an arbitrary file, e.g. one the user picked to share with a teammate.</summary>
    public async Task ExportToAsync(string path, DataSourceSettings settings)
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
        await JsonSerializer.SerializeAsync(stream, settings, options);
    }

    /// <summary>Reads settings from an arbitrary file, e.g. one a teammate shared.</summary>
    public async Task<DataSourceSettings> ImportFromAsync(string path)
    {
        await using var stream = File.OpenRead(path);
        return await JsonSerializer.DeserializeAsync<DataSourceSettings>(stream, JsonDefaults.Options)
               ?? new DataSourceSettings(DataSourceKind.Airtable, string.Empty, string.Empty);
    }
}

public enum DataSourceKind
{
    Airtable,
    Excel
}

public sealed record DataSourceSettings(
    DataSourceKind Kind,
    string SharedLink,
    string ExcelPath);
