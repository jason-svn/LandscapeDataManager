namespace WWP.LandscapeDataManager.Shared.Services;

/// <summary>Non-secret Airtable connection details. The API token itself lives in <see cref="AirtableCredentialStore"/>. Persisted only via <see cref="ProjectSettingsSync"/> — no local cache.</summary>
public sealed record AirtableApiSettings(string BaseId, string TableIdOrName, string? ViewName);
