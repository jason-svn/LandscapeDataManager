using System.Text.Json;
using WWP.LandscapeDataManager.Contracts;

namespace WWP.LandscapeDataManager.Shared.Services;

/// <summary>
/// Everything a fresh machine needs to reconnect to the same source and reproduce the same i-Tree
/// reporting preferences as whoever last saved settings on this project — serialized into the
/// <c>!_S_PLT_Settings_Json_Text</c> Project Information parameter so it travels with the
/// Revit file instead of living only under a given machine's %LOCALAPPDATA%. Every property is
/// nullable/omittable: a snapshot built from just one app (e.g. i-Tree Calculator, which only ever
/// knows unit system and currency) still round-trips without clobbering fields it never touched.
/// Deliberately excludes secrets (Airtable token, i-Tree API key) — those stay in
/// Windows Credential Manager via <see cref="AirtableCredentialStore"/>/<see cref="ITreeCredentialStore"/>,
/// never written to a Revit file that gets shared or synced.
/// </summary>
public sealed record ProjectSettingsSnapshot(
    string? PreferredUnitSystem = null,
    string? PreferredCurrency = null,
    double? Latitude = null,
    double? Longitude = null,
    DataSourceSettings? DataSource = null,
    AirtableApiSettings? AirtableApi = null,
    IReadOnlyList<ParameterMappingDefinition>? ParameterMappings = null,
    IReadOnlyList<TypeAlias>? TypeAliases = null);

/// <summary>Pure (no I/O) serialize/deserialize for <see cref="ProjectSettingsSnapshot"/> — reading/writing the parameter itself is Revit-side (<c>ProjectSettingsService</c>) or local-store-side (each app's own stores), not this class's job.</summary>
public static class ProjectSettingsJson
{
    public static string Serialize(ProjectSettingsSnapshot snapshot) =>
        JsonSerializer.Serialize(snapshot, JsonDefaults.Options);

    /// <summary>Null for empty/missing input or malformed JSON — callers treat that as "nothing to sync yet," not an error.</summary>
    public static ProjectSettingsSnapshot? Deserialize(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return null;
        }

        try
        {
            return JsonSerializer.Deserialize<ProjectSettingsSnapshot>(json, JsonDefaults.Options);
        }
        catch (JsonException)
        {
            return null;
        }
    }
}
