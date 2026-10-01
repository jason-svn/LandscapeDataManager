using System.Text.Json;

namespace WWP.LandscapeDataManager.Contracts;

/// <summary>One published GitHub release of the LIM add-in, as the updater needs it.</summary>
public sealed record LimRelease(Version Version, string Tag, string Name, string Notes, string PageUrl, string? PackageUrl);

/// <summary>
/// Reads the public GitHub "latest release" feed for the add-in and decides whether it is newer
/// than what's installed. Releases are tagged vMAJOR.MINOR.PATCH and must match the
/// &lt;Version&gt; in Directory.Build.props of the build they ship.
/// </summary>
public static class LimReleaseFeed
{
    public const string LatestReleaseApiUrl = "https://api.github.com/repos/jason-svn/LandscapeDataManager/releases/latest";
    public const string ReleasesPageUrl = "https://github.com/jason-svn/LandscapeDataManager/releases/latest";
    public const string PackageAssetName = "LIM-Landscape-Data-2025plus.zip";

    /// <summary>Parses a GitHub releases/latest response; null when it isn't a usable release (no tag, draft, prerelease).</summary>
    public static LimRelease? Parse(string json)
    {
        using var document = JsonDocument.Parse(json);
        var root = document.RootElement;
        if (root.ValueKind != JsonValueKind.Object
            || GetBool(root, "draft")
            || GetBool(root, "prerelease")
            || !TryParseTag(GetString(root, "tag_name"), out var version))
        {
            return null;
        }

        string? packageUrl = null;
        if (root.TryGetProperty("assets", out var assets) && assets.ValueKind == JsonValueKind.Array)
        {
            packageUrl = assets.EnumerateArray()
                .Where(asset => string.Equals(GetString(asset, "name"), PackageAssetName, StringComparison.OrdinalIgnoreCase))
                .Select(asset => GetString(asset, "browser_download_url"))
                .FirstOrDefault(url => url.Length > 0);
        }

        var tag = GetString(root, "tag_name");
        var name = GetString(root, "name");
        var pageUrl = GetString(root, "html_url");
        return new LimRelease(
            version,
            tag,
            name.Length > 0 ? name : tag,
            GetString(root, "body"),
            pageUrl.Length > 0 ? pageUrl : ReleasesPageUrl,
            packageUrl);
    }

    /// <summary>Accepts "v1.2.0", "1.2.0" or "1.2"; normalised to MAJOR.MINOR.PATCH.</summary>
    public static bool TryParseTag(string? tag, out Version version)
    {
        version = new Version(0, 0, 0);
        var text = tag?.Trim().TrimStart('v', 'V') ?? string.Empty;
        if (!Version.TryParse(text, out var parsed))
        {
            return false;
        }

        version = Normalize(parsed);
        return true;
    }

    /// <summary>
    /// Assembly versions carry four parts (1.2.0.0) while tags carry three (1.2.0), and
    /// System.Version ranks a missing part below 0 — so both are compared as MAJOR.MINOR.PATCH.
    /// </summary>
    public static bool IsNewer(Version available, Version installed) => Normalize(available) > Normalize(installed);

    private static Version Normalize(Version version) => new(version.Major, version.Minor, Math.Max(version.Build, 0));

    private static string GetString(JsonElement element, string name) =>
        element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() ?? string.Empty : string.Empty;

    private static bool GetBool(JsonElement element, string name) =>
        element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.True;
}
