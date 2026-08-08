namespace WWP.LandscapeDataManager.Shared.Services;

public enum DataSourceKind
{
    Airtable,
    Excel
}

/// <summary>Persisted only via <see cref="ProjectSettingsSync"/> — no local cache.</summary>
public sealed record DataSourceSettings(
    DataSourceKind Kind,
    string SharedLink,
    string ExcelPath);
