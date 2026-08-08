namespace WWP.LandscapeDataManager.Shared.Services;

/// <summary>Persisted only via <see cref="ProjectSettingsSync"/> — no local cache. A path saved from one machine may not resolve on another; that just means Browse gets used again there.</summary>
public sealed record SharedParameterFileSettings(string FilePath);
