using System.Text.Json;
using WWP.LandscapeDataManager.Contracts;

namespace WWP.LandscapeDataManager.Shared.Services;

/// <summary>Credentials, carried only when the exporter explicitly opts in.</summary>
public sealed record LimSettingsSecrets(string? ITreeApiKey = null, string? AirtableToken = null)
{
    public bool IsEmpty => string.IsNullOrWhiteSpace(ITreeApiKey) && string.IsNullOrWhiteSpace(AirtableToken);
}

/// <summary>
/// A shareable <c>.limsettings</c> file: the project's <see cref="ProjectSettingsSnapshot"/> plus,
/// opt-in, the credentials Settings keeps in Windows Credential Manager. It lets one person set
/// LIM up once and send the file to colleagues or other projects. The site latitude/longitude and
/// the Dashboard's chosen site boundary (a property line in one model) are never written — they
/// describe one project, not a setup worth copying.
/// </summary>
public sealed record LimSettingsFile(
    string Format,
    int FormatVersion,
    DateTimeOffset ExportedAt,
    string? ExportedFromProject,
    string? AppVersion,
    ProjectSettingsSnapshot Settings,
    LimSettingsSecrets? Secrets = null);

/// <summary>Pure (no I/O) build/serialize/parse for <see cref="LimSettingsFile"/>.</summary>
public static class LimSettingsFileFormat
{
    public const string FormatName = "lim-settings";
    public const int CurrentVersion = 1;
    public const string FileExtension = ".limsettings";

    private static readonly JsonSerializerOptions WriteOptions = new(JsonDefaults.Options) { WriteIndented = true };

    public static LimSettingsFile Build(
        ProjectSettingsSnapshot? projectSettings,
        string? preferredUnitSystem,
        string? preferredCurrency,
        string? projectTitle,
        string? appVersion,
        LimSettingsSecrets? secrets,
        DateTimeOffset exportedAt)
    {
        var settings = (projectSettings ?? new ProjectSettingsSnapshot()) with
        {
            // The dedicated unit/currency parameters are the source of truth; the snapshot only mirrors them.
            PreferredUnitSystem = preferredUnitSystem ?? projectSettings?.PreferredUnitSystem,
            PreferredCurrency = preferredCurrency ?? projectSettings?.PreferredCurrency,
            Latitude = null,
            Longitude = null,
            SiteBoundary = null
        };

        return new LimSettingsFile(
            FormatName,
            CurrentVersion,
            exportedAt,
            projectTitle,
            appVersion,
            settings,
            secrets is { IsEmpty: false } ? secrets : null);
    }

    public static string Serialize(LimSettingsFile file) => JsonSerializer.Serialize(file, WriteOptions);

    /// <summary>Throws <see cref="InvalidDataException"/> with a message fit to show the user when the file isn't a LIM settings file this version can read.</summary>
    public static LimSettingsFile Parse(string json)
    {
        LimSettingsFile? file;
        try
        {
            file = JsonSerializer.Deserialize<LimSettingsFile>(json, JsonDefaults.Options);
        }
        catch (JsonException exception)
        {
            throw new InvalidDataException("This isn't a LIM settings file (it isn't valid JSON).", exception);
        }

        if (file is null || !string.Equals(file.Format, FormatName, StringComparison.Ordinal) || file.Settings is null)
        {
            throw new InvalidDataException("This isn't a LIM settings file. Choose a .limsettings file exported from LIM Settings.");
        }

        if (file.FormatVersion > CurrentVersion)
        {
            throw new InvalidDataException(
                $"This settings file was exported by a newer LIM version (format {file.FormatVersion}). Update LIM with Check for Updates, then import it again.");
        }

        // Whatever an older or hand-edited file says, location and site boundary never travel between projects.
        return file with { Settings = file.Settings with { Latitude = null, Longitude = null, SiteBoundary = null } };
    }
}
