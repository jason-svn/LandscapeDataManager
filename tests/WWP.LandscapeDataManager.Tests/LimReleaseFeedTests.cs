using WWP.LandscapeDataManager.Contracts;
using Xunit;

namespace WWP.LandscapeDataManager.Tests;

public class LimReleaseFeedTests
{
    private const string LatestReleaseJson = """
        {
          "tag_name": "v1.2.0",
          "name": "LIM- Landscape Data v1.2.0",
          "html_url": "https://github.com/jason-svn/LandscapeDataManager/releases/tag/v1.2.0",
          "draft": false,
          "prerelease": false,
          "body": "Adds an updater.",
          "assets": [
            { "name": "notes.txt", "browser_download_url": "https://example.test/notes.txt" },
            { "name": "LIM-Landscape-Data-2025plus.zip", "browser_download_url": "https://example.test/LIM-Landscape-Data-2025plus.zip" }
          ]
        }
        """;

    [Fact]
    public void Parses_tag_name_notes_and_the_package_asset()
    {
        var release = LimReleaseFeed.Parse(LatestReleaseJson);

        Assert.NotNull(release);
        Assert.Equal(new Version(1, 2, 0), release.Version);
        Assert.Equal("v1.2.0", release.Tag);
        Assert.Equal("LIM- Landscape Data v1.2.0", release.Name);
        Assert.Equal("Adds an updater.", release.Notes);
        Assert.Equal("https://example.test/LIM-Landscape-Data-2025plus.zip", release.PackageUrl);
    }

    [Fact]
    public void Release_without_the_package_asset_has_no_package_url()
    {
        var release = LimReleaseFeed.Parse("""{ "tag_name": "v1.2.0", "assets": [] }""");

        Assert.NotNull(release);
        Assert.Null(release.PackageUrl);
        Assert.Equal(LimReleaseFeed.ReleasesPageUrl, release.PageUrl);
    }

    [Theory]
    [InlineData("""{ "tag_name": "v1.3.0", "prerelease": true }""")]
    [InlineData("""{ "tag_name": "v1.3.0", "draft": true }""")]
    [InlineData("""{ "tag_name": "latest" }""")]
    [InlineData("""{ "message": "API rate limit exceeded" }""")]
    public void Unusable_releases_parse_to_null(string json) => Assert.Null(LimReleaseFeed.Parse(json));

    [Theory]
    [InlineData("v1.2.0", "1.1.0.0", true)]
    [InlineData("v1.2.0", "1.2.0.0", false)] // four-part assembly version equals three-part tag
    [InlineData("v1.2.0", "1.2.1.0", false)]
    [InlineData("v1.10.0", "1.9.0.0", true)] // numeric, not string, comparison
    [InlineData("1.2", "1.1.9.0", true)]
    public void IsNewer_compares_major_minor_patch(string tag, string installed, bool expected)
    {
        Assert.True(LimReleaseFeed.TryParseTag(tag, out var available));
        Assert.Equal(expected, LimReleaseFeed.IsNewer(available, Version.Parse(installed)));
    }
}
