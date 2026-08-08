namespace WWP.LandscapeDataManager.Shared.Services;

/// <summary>Which Airtable base/table/view holds the WWP landscape data sheet. Persisted per-project only via <see cref="ProjectSettingsSync"/> — no local cache. <see cref="CompanyDefault"/> is the fallback for a project that hasn't saved its own value yet.</summary>
public sealed record WwpLdsAirtableSettings(string BaseId, string TableIdOrName, string? ViewName)
{
    /// <summary>WWP's own shared company Airtable base — used when a project's Project Information has no WWP LDS source saved yet, so Floor Calculator still works out of the box on a brand-new project.</summary>
    public static readonly WwpLdsAirtableSettings CompanyDefault = new("apptELCzLzMbmrk54", "tblAwGKQjNQJ9XsKM", "viwg4x3F7UpiVYDsn");
}
