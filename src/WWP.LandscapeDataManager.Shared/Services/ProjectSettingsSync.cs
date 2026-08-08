using WWP.LandscapeDataManager.Contracts;

namespace WWP.LandscapeDataManager.Shared.Services;

/// <summary>
/// Reads/writes <see cref="ProjectSettingsSnapshot"/> through the <c>!_S_PLT_Settings_Json_Text</c>
/// Project Information parameter — the only persistence path for non-secret settings; there is no
/// local machine cache to fall back to or keep in sync. Project-wins by design: <see cref="PullAsync"/>
/// returns whatever the project currently has, and callers assign it straight into their own UI
/// controls every time a page loads. <see cref="PushAsync"/> merges only the fields the caller
/// actually supplies into whatever snapshot already exists, so one app's save never erases a field
/// only some other app knows about (e.g. i-Tree Calculator only ever supplies unit system/currency;
/// Importer only ever supplies data-source/mapping/alias settings).
/// </summary>
public static class ProjectSettingsSync
{
    /// <summary>Null when nothing has ever been saved to this project yet — callers treat that as "use defaults," not an error.</summary>
    public static async Task<ProjectSettingsSnapshot?> PullAsync(RevitPipeClient client)
    {
        var result = await client.SendAsync<GetProjectSettingsJsonResult>(PipeCommands.GetProjectSettingsJson);
        return ProjectSettingsJson.Deserialize(result.SettingsJson);
    }

    /// <summary>
    /// Merges whichever fields the caller supplies, plus the project's own site location (fetched
    /// here so every call site gets it for free), into the snapshot already on the project — never a
    /// bare overwrite. <paramref name="preferredUnitSystem"/>/<paramref name="preferredCurrency"/>
    /// should be whatever the caller already read from <see cref="ParameterCatalogResult"/> — those
    /// two have their own dedicated parameters, so this is a courtesy mirror, not the source of truth.
    /// </summary>
    public static async Task PushAsync(
        RevitPipeClient client,
        DataSourceSettings? dataSource = null,
        AirtableApiSettings? airtableApi = null,
        IReadOnlyList<ParameterMappingDefinition>? parameterMappings = null,
        IReadOnlyList<TypeAlias>? typeAliases = null,
        WwpLdsAirtableSettings? wwpLdsSource = null,
        string? sharedParameterFilePath = null,
        string? preferredUnitSystem = null,
        string? preferredCurrency = null)
    {
        var existingResult = await client.SendAsync<GetProjectSettingsJsonResult>(PipeCommands.GetProjectSettingsJson);
        var existing = ProjectSettingsJson.Deserialize(existingResult.SettingsJson) ?? new ProjectSettingsSnapshot();

        var latitude = existing.Latitude;
        var longitude = existing.Longitude;
        try
        {
            var location = await client.SendAsync<ProjectSiteLocationResult>(PipeCommands.GetProjectSiteLocation);
            latitude = location.Latitude;
            longitude = location.Longitude;
        }
        catch (IOException)
        {
            // Site location is a nice-to-have in the snapshot, not load-bearing — if this particular
            // round-trip fails, keep whatever was already recorded rather than blanking it.
        }

        var snapshot = new ProjectSettingsSnapshot(
            preferredUnitSystem ?? existing.PreferredUnitSystem,
            preferredCurrency ?? existing.PreferredCurrency,
            latitude,
            longitude,
            dataSource ?? existing.DataSource,
            airtableApi ?? existing.AirtableApi,
            parameterMappings ?? existing.ParameterMappings,
            typeAliases ?? existing.TypeAliases,
            wwpLdsSource ?? existing.WwpLdsSource,
            sharedParameterFilePath ?? existing.SharedParameterFilePath);

        await client.SendAsync<PublishProjectSettingsJsonResult>(
            PipeCommands.PublishProjectSettingsJson,
            new PublishProjectSettingsJsonRequest(ProjectSettingsJson.Serialize(snapshot)));
    }
}
