using System.Diagnostics;
using System.Runtime.InteropServices;

namespace WWP.LandscapeDataManager.Shared.Services;

/// <summary>
/// Lets one LIM tool exe start (or refocus) another sibling tool exe deployed alongside it — used
/// for the "Settings" button every tool carries, so clicking it from any tool brings up the exact
/// same Settings window rather than spawning a duplicate. Mirrors
/// <c>WWP.LandscapeDataManager.Revit.Infrastructure.ToolProcessLauncher</c>'s show-or-start logic,
/// but looks up any already-running instance by process name system-wide (via
/// <see cref="Process.GetProcessesByName"/>) instead of remembering one process handle — the caller
/// here is a different process each time (a different tool exe), so there's no single long-lived
/// launcher instance to remember it for.
/// </summary>
public static class SiblingToolLauncher
{
    /// <param name="exeRelativePath">Path to the sibling tool's exe, relative to the shared LIM tools folder — e.g. <c>"Settings\WWP.LandscapeDataManager.Settings.exe"</c> — every tool exe is deployed one level under that same shared folder.</param>
    /// <param name="pipeName">The current tool's own <c>--pipe</c> name, passed through so the sibling tool talks to the same Revit session.</param>
    public static void ShowOrStart(string exeRelativePath, string pipeName)
    {
        var processName = Path.GetFileNameWithoutExtension(exeRelativePath);
        var existing = Process.GetProcessesByName(processName).FirstOrDefault(process => !process.HasExited);
        if (existing is not null)
        {
            existing.Refresh();
            if (existing.MainWindowHandle != nint.Zero)
            {
                NativeMethods.SetForegroundWindow(existing.MainWindowHandle);
                return;
            }
        }

        var currentExePath = Environment.ProcessPath
                              ?? throw new InvalidOperationException("The current tool's executable path could not be resolved.");
        var currentExeDirectory = Path.GetDirectoryName(currentExePath)
                                  ?? throw new InvalidOperationException("The current tool's directory could not be resolved.");
        var toolsRoot = Path.GetDirectoryName(currentExeDirectory)
                        ?? throw new InvalidOperationException("The LIM tools root directory could not be resolved.");
        var executablePath = Path.Combine(toolsRoot, exeRelativePath);

        if (!File.Exists(executablePath))
        {
            throw new FileNotFoundException(
                $"The '{exeRelativePath}' tool is not installed beside this one.", executablePath);
        }

        Process.Start(new ProcessStartInfo
        {
            FileName = executablePath,
            Arguments = $"--pipe \"{pipeName}\"",
            UseShellExecute = true,
            WorkingDirectory = Path.GetDirectoryName(executablePath)
        });
    }
}

internal static class NativeMethods
{
    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool SetForegroundWindow(nint hWnd);
}
