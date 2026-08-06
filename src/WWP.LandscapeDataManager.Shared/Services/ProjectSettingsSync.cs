using WWP.LandscapeDataManager.Contracts;

namespace WWP.LandscapeDataManager.Shared.Services;

/// <summary>
/// Reads/writes <see cref="ProjectSettingsSnapshot"/> through the <c>!_S_PLT_Settings_Json_Text</c>
/// Project Information parameter, applying it against whichever local stores a given app actually
/// owns (pass null for stores the calling app doesn't have — e.g. the main App has no
/// <see cref="AirtableApiSettingsStore"/> or <see cref="TypeAliasStore"/>). Project-wins by design:
/// <see cref="PullAndApplyAsync"/> unconditionally overwrites local settings with whatever the
/// project's snapshot contains, every time it's called — callers should call it once per page load,
/// not repeatedly mid-session, so an in-progress edit isn't silently reverted.
/// </summary>
public static class ProjectSettingsSync
{
    /// <summary>Returns true if a snapshot existed and at least one local store was overwritten from it.</summary>
    public static async Task<bool> PullAndApplyAsync(
        RevitPipeClient client,
        DataSourceSettingsStore? dataSourceStore = null,
        AirtableApiSettingsStore? airtableApiStore = null,
        ParameterMappingStore? mappingStore = null,
        TypeAliasStore? typeAliasStore = null)
    {
        var result = await client.SendAsync<GetProjectSettingsJsonResult>(PipeCommands.GetProjectSettingsJson);
        var snapshot = ProjectSettingsJson.Deserialize(result.SettingsJson);
        if (snapshot is null)
        {
            return false;
        }

        var applied = false;
        if (dataSourceStore is not null && snapshot.DataSource is not null)
        {
            await dataSourceStore.SaveAsync(snapshot.DataSource);
            applied = true;
        }

        if (airtableApiStore is not null && snapshot.AirtableApi is not null)
        {
            await airtableApiStore.SaveAsync(snapshot.AirtableApi);
            applied = true;
        }

        if (mappingStore is not null && snapshot.ParameterMappings is not null)
        {
            await mappingStore.SaveAsync(snapshot.ParameterMappings);
            applied = true;
        }

        if (typeAliasStore is not null && snapshot.TypeAliases is not null)
        {
            await typeAliasStore.SaveAsync(snapshot.TypeAliases);
            applied = true;
        }

        return applied;
    }

    /// <summary>
    /// Merges whichever local stores are supplied, plus the project's own site location (fetched
    /// here so every call site gets it for free), into the snapshot ALREADY on the project — never
    /// a bare overwrite. This matters because no single app owns every field (e.g. i-Tree Calculator
    /// only ever knows unit system/currency; Importer only ever knows data-source/mapping/alias
    /// settings), so a naive "serialize what I have and publish" would silently erase whatever
    /// fields other apps had already saved. Fields the caller didn't supply keep whatever was
    /// already on the project. <paramref name="preferredUnitSystem"/>/<paramref name="preferredCurrency"/>
    /// should be whatever the caller already read from <see cref="ParameterCatalogResult"/> — those
    /// two have their own dedicated parameters, so this is a courtesy mirror, not the source of truth.
    /// </summary>
    public static async Task PushAsync(
        RevitPipeClient client,
        DataSourceSettingsStore? dataSourceStore = null,
        AirtableApiSettingsStore? airtableApiStore = null,
        ParameterMappingStore? mappingStore = null,
        TypeAliasStore? typeAliasStore = null,
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
            dataSourceStore is null ? existing.DataSource : await dataSourceStore.LoadAsync(),
            airtableApiStore is null ? existing.AirtableApi : await airtableApiStore.LoadAsync(),
            mappingStore is null ? existing.ParameterMappings : await mappingStore.LoadAsync(),
            typeAliasStore is null ? existing.TypeAliases : await typeAliasStore.LoadAsync());

        await client.SendAsync<PublishProjectSettingsJsonResult>(
            PipeCommands.PublishProjectSettingsJson,
            new PublishProjectSettingsJsonRequest(ProjectSettingsJson.Serialize(snapshot)));
    }
}
