using System.Diagnostics;
using System.Reflection;

namespace WWP.LandscapeDataManager.Revit.Infrastructure;

internal sealed class CompanionLauncher(string pipeName) : IDisposable
{
    private Process? _process;

    public void ShowOrStart()
    {
        if (_process is { HasExited: false })
        {
            return;
        }

        var connectorDirectory = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location)
                                 ?? throw new InvalidOperationException("The add-in directory could not be resolved.");
        var executablePath = Path.Combine(
            connectorDirectory,
            "App",
            "WWP.LandscapeDataManager.App.exe");

        if (!File.Exists(executablePath))
        {
            throw new FileNotFoundException(
                "The WinUI 3 application is not installed beside the Revit connector.",
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
