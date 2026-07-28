using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;

namespace WWP.LandscapeDataManager.Revit.Infrastructure;

/// <summary>
/// Starts (or refocuses) one dedicated tool executable. Each of the 5 LIM tools gets its own
/// instance of this launcher, keyed to its own exe — unlike the old single-process
/// CompanionLauncher, there is no cross-tool navigation: each exe hosts exactly one tool, so
/// "show or start" only ever means "start it" or "bring its one window to front."
/// </summary>
internal sealed class ToolProcessLauncher(string exeRelativePath, string pipeName) : IDisposable
{
    private Process? _process;

    public void ShowOrStart()
    {
        if (_process is { HasExited: false })
        {
            _process.Refresh();
            if (_process.MainWindowHandle != nint.Zero)
            {
                NativeMethods.SetForegroundWindow(_process.MainWindowHandle);
                return;
            }
        }

        var connectorDirectory = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location)
                                 ?? throw new InvalidOperationException("The add-in directory could not be resolved.");
        var executablePath = Path.Combine(connectorDirectory, exeRelativePath);

        if (!File.Exists(executablePath))
        {
            throw new FileNotFoundException(
                $"The '{exeRelativePath}' tool is not installed beside the Revit connector.",
                executablePath);
        }

        _process = Process.Start(new ProcessStartInfo
        {
            FileName = executablePath,
            Arguments = $"--pipe \"{pipeName}\"",
            UseShellExecute = true,
            WorkingDirectory = Path.GetDirectoryName(executablePath)
        });
    }

    public void Dispose()
    {
        _process?.Dispose();
        _process = null;
    }
}

internal static class NativeMethods
{
    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool SetForegroundWindow(nint hWnd);
}
