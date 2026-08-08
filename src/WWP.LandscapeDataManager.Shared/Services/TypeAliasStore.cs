namespace WWP.LandscapeDataManager.Shared.Services;

/// <summary>Approves that the source's <see cref="SourceTypeName"/> means this specific Revit Family + Type. Persisted only via <see cref="ProjectSettingsSync"/> — no local cache.</summary>
public sealed record TypeAlias(string SourceTypeName, string FamilyName, string TypeName);
