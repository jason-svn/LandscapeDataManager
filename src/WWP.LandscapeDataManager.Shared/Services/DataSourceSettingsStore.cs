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

    public async Task SaveAsync(DataSourceSettings settings)
    {
        var directory = Path.GetDirectoryName(_filePath)
                        ?? throw new InvalidOperationException("The settings directory could not be resolved.");
        Directory.CreateDirectory(directory);

        var options = new JsonSerializerOptions(JsonDefaults.Options)
        {
            WriteIndented = true
        };
        await using var stream = File.Create(_filePath);
        await JsonSerializer.SerializeAsync(stream, settings, options);
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
