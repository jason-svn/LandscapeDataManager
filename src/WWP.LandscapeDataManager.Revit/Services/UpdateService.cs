using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Reflection;
using System.Text.Json;
using WWP.LandscapeDataManager.Contracts;

namespace WWP.LandscapeDataManager.Revit.Services;

/// <summary>
/// Checks GitHub for a newer LIM release and hands the install to a separate PowerShell window.
/// The connector DLL is locked while Revit runs, so that window downloads the package (~660 MB)
/// while the user keeps working, waits for every Revit session to close, then runs the
/// package's own Deploy-BinaryPackage.ps1 — the same script a manual install uses.
/// </summary>
internal static class UpdateService
{
    private static readonly TimeSpan AutomaticCheckInterval = TimeSpan.FromDays(1);

    private static readonly string UpdatesDirectory = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "WWP.LandscapeDataManager", "Updates");

    // Machine-local updater bookkeeping (when we last asked GitHub, which version the user
    // skipped). Not a project setting, so it doesn't belong in ProjectSettingsSync.
    private static readonly string StatePath = Path.Combine(UpdatesDirectory, "update-state.json");

    /// <summary>Tag of the update whose installer window was started this session, so it isn't started twice.</summary>
    private static string? _scheduledTag;

    public static Version InstalledVersion =>
        Assembly.GetExecutingAssembly().GetName().Version ?? new Version(0, 0, 0);

    public static string InstalledVersionText => $"v{InstalledVersion.ToString(3)}";

    public static async Task<LimRelease?> GetLatestReleaseAsync(CancellationToken cancellationToken)
    {
        using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(15) };
        client.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("WWP.LandscapeDataManager", InstalledVersion.ToString(3)));
        client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));
        var json = await client.GetStringAsync(LimReleaseFeed.LatestReleaseApiUrl, cancellationToken);
        return LimReleaseFeed.Parse(json);
    }

    /// <summary>
    /// The startup check: at most once a day (GitHub allows 60 unauthenticated calls per hour per
    /// IP, shared by everyone in an office), never throws, and ignores a version the user skipped.
    /// </summary>
    public static async Task<LimRelease?> CheckInBackgroundAsync()
    {
        try
        {
            var state = ReadState();
            if (state.LastCheckedUtc is { } lastChecked && DateTime.UtcNow - lastChecked < AutomaticCheckInterval)
            {
                return null;
            }

            var release = await GetLatestReleaseAsync(CancellationToken.None);
            WriteState(state with { LastCheckedUtc = DateTime.UtcNow });
            return release is not null
                   && LimReleaseFeed.IsNewer(release.Version, InstalledVersion)
                   && !string.Equals(release.Tag, state.SkippedTag, StringComparison.OrdinalIgnoreCase)
                ? release
                : null;
        }
        catch
        {
            return null; // offline, proxy, rate-limited — try again next startup
        }
    }

    public static void SkipVersion(LimRelease release) => WriteState(ReadState() with { SkippedTag = release.Tag });

    public static bool IsScheduled(LimRelease release) => string.Equals(_scheduledTag, release.Tag, StringComparison.OrdinalIgnoreCase);

    /// <summary>Starts the visible installer window for this release and Revit version.</summary>
    public static void ScheduleInstall(LimRelease release, string revitVersion)
    {
        if (release.PackageUrl is null)
        {
            throw new InvalidOperationException(
                $"Release {release.Tag} has no {LimReleaseFeed.PackageAssetName} attached. Download it from {release.PageUrl}.");
        }

        Directory.CreateDirectory(UpdatesDirectory);
        var scriptPath = Path.Combine(UpdatesDirectory, "Install-LimUpdate.ps1");
        // With a BOM: Windows PowerShell 5.1 reads BOM-less scripts in the ANSI code page.
        File.WriteAllText(scriptPath, InstallerScript, new System.Text.UTF8Encoding(encoderShouldEmitUTF8Identifier: true));

        Process.Start(new ProcessStartInfo
        {
            FileName = "powershell.exe",
            // Bypass covers this one process only — the script is written by this add-in, not downloaded.
            Arguments = $"-NoProfile -ExecutionPolicy Bypass -File \"{scriptPath}\" " +
                        $"-PackageUrl \"{release.PackageUrl}\" -Tag \"{release.Tag}\" -RevitVersion {revitVersion} " +
                        $"-WorkDirectory \"{Path.Combine(UpdatesDirectory, release.Tag)}\"",
            UseShellExecute = true,
            WindowStyle = ProcessWindowStyle.Normal
        });
        _scheduledTag = release.Tag;
    }

    private static UpdateState ReadState()
    {
        try
        {
            return File.Exists(StatePath)
                ? JsonSerializer.Deserialize<UpdateState>(File.ReadAllText(StatePath)) ?? new UpdateState()
                : new UpdateState();
        }
        catch
        {
            return new UpdateState();
        }
    }

    private static void WriteState(UpdateState state)
    {
        try
        {
            Directory.CreateDirectory(UpdatesDirectory);
            File.WriteAllText(StatePath, JsonSerializer.Serialize(state));
        }
        catch
        {
            // Best effort: losing this only means checking again sooner.
        }
    }

    private sealed record UpdateState(DateTime? LastCheckedUtc = null, string? SkippedTag = null);

    private const string InstallerScript = """
        param(
            [Parameter(Mandatory)] [string] $PackageUrl,
            [Parameter(Mandatory)] [string] $Tag,
            [Parameter(Mandatory)] [int] $RevitVersion,
            [Parameter(Mandatory)] [string] $WorkDirectory
        )

        $ErrorActionPreference = 'Stop'
        $ProgressPreference = 'SilentlyContinue'
        $Host.UI.RawUI.WindowTitle = "LIM- Landscape Data $Tag update"

        try {
            Write-Host "Updating LIM- Landscape Data to $Tag for Revit $RevitVersion." -ForegroundColor Cyan
            Write-Host 'You can keep working in Revit while it downloads. Leave this window open.'
            Write-Host ''

            New-Item -ItemType Directory -Path $WorkDirectory -Force | Out-Null
            $zipPath = Join-Path $WorkDirectory 'LIM-Landscape-Data-2025plus.zip'
            $packageDirectory = Join-Path $WorkDirectory 'package'

            Write-Host 'Downloading the update (about 660 MB)...'
            try {
                Start-BitsTransfer -Source $PackageUrl -Destination $zipPath -DisplayName "LIM $Tag" -Description 'LIM- Landscape Data update'
            }
            catch {
                # BITS can be disabled by policy; fall back to a plain download.
                (New-Object Net.WebClient).DownloadFile($PackageUrl, $zipPath)
            }

            Write-Host 'Extracting...'
            if (Test-Path -LiteralPath $packageDirectory) {
                Remove-Item -LiteralPath $packageDirectory -Recurse -Force
            }
            Expand-Archive -LiteralPath $zipPath -DestinationPath $packageDirectory -Force

            $deployScript = Join-Path $packageDirectory 'Deploy-BinaryPackage.ps1'
            if (-not (Test-Path -LiteralPath $deployScript)) {
                throw "The downloaded package has no Deploy-BinaryPackage.ps1."
            }

            if (Get-Process -Name Revit -ErrorAction SilentlyContinue) {
                Write-Host ''
                Write-Host 'Download complete. Save your work and close Revit (all open sessions) to install.' -ForegroundColor Yellow
                while (Get-Process -Name Revit -ErrorAction SilentlyContinue) {
                    Start-Sleep -Seconds 2
                }
            }

            # The LIM tools are separate processes that keep their own .exe files locked, and do
            # nothing once Revit (their pipe server) is gone.
            $tools = Get-Process | Where-Object { $_.ProcessName -like 'WWP.LandscapeDataManager.*' }
            if ($tools) {
                $tools | Wait-Process -Timeout 10 -ErrorAction SilentlyContinue
                Get-Process | Where-Object { $_.ProcessName -like 'WWP.LandscapeDataManager.*' } | Stop-Process -Force -ErrorAction SilentlyContinue
                Start-Sleep -Seconds 1
            }

            Write-Host 'Installing...'
            & $deployScript -RevitVersion $RevitVersion

            Remove-Item -LiteralPath $zipPath -Force -ErrorAction SilentlyContinue
            Write-Host ''
            Write-Host "LIM- Landscape Data $Tag is installed. Start Revit to use it." -ForegroundColor Green
            if (Test-Path -LiteralPath (Join-Path $packageDirectory 'Shared_Parameters_WWP.txt')) {
                Write-Host "If this update adds parameters, run Import Shared Parameter with:"
                Write-Host "  $(Join-Path $packageDirectory 'Shared_Parameters_WWP.txt')"
            }
        }
        catch {
            Write-Host ''
            Write-Host "The update failed: $($_.Exception.Message)" -ForegroundColor Red
            Write-Host 'If it stopped part-way through installing, LIM may not load until it is reinstalled:'
            Write-Host "download $Tag from https://github.com/jason-svn/LandscapeDataManager/releases/latest and run Install.cmd."
        }

        Write-Host ''
        Read-Host 'Press Enter to close this window'
        """;
}
