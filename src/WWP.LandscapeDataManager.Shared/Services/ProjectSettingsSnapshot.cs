using System.Text.Json;
using WWP.LandscapeDataManager.Contracts;

namespace WWP.LandscapeDataManager.Shared.Services;

/// <summary>
/// The single source of truth for every non-secret LIM setting — serialized into the
/// <c>!_S_PLT_Settings_Json_Text</c> Project Information parameter. There is deliberately no local
/// machine cache backing this: every tool reads this snapshot fresh (via <see cref="ProjectSettingsSync.PullAsync"/>)
/// each time it opens and writes straight back (via <see cref="ProjectSettingsSync.PushAsync"/>) on
/// save, so a fresh machine opening the same project always sees the same settings as whoever saved
/// them last. Every property is nullable/omittable: a snapshot built from just one app (e.g. i-Tree
/// Calculator, which only ever knows unit system and currency) still round-trips without clobbering
/// fields it never touched. Deliberately excludes secrets (Airtable token, i-Tree API key) — those
/// stay in Windows Credential Manager via <see cref="AirtableCredentialStore"/>/<see cref="ITreeCredentialStore"/>,
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
    IReadOnlyList<TypeAlias>? TypeAliases = null,
    WwpLdsAirtableSettings? WwpLdsSource = null,
    string? SharedParameterFilePath = null);

/// <summary>Pure (no I/O) serialize/deserialize for <see cref="ProjectSettingsSnapshot"/> — reading/writing the Project Information parameter itself is <see cref="ProjectSettingsSync"/>'s job, via the Revit-side <c>ProjectPreferencesService</c>.</summary>
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
