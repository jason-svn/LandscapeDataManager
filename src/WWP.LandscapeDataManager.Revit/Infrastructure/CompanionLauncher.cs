using System.Diagnostics;
using System.IO;
using System.IO.Pipes;
using System.Reflection;
using System.Text;

namespace WWP.LandscapeDataManager.Revit.Infrastructure;

internal sealed class CompanionLauncher(string pipeName) : IDisposable
{
    private Process? _process;

    public void ShowOrStart(string workflow)
    {
        if (_process is { HasExited: false })
        {
            NavigateExistingProcess(workflow);
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
            Arguments = $"--pipe \"{pipeName}\" --workflow \"{workflow}\"",
            UseShellExecute = true,
            WorkingDirectory = Path.GetDirectoryName(executablePath)
        });
    }

    private void NavigateExistingProcess(string workflow)
    {
        try
        {
            using var client = new NamedPipeClientStream(
                ".",
                $"{pipeName}.navigation",
                PipeDirection.Out,
                PipeOptions.Asynchronous);
            client.Connect(1000);

            using var writer = new StreamWriter(client, new UTF8Encoding(false))
            {
                AutoFlush = true
            };
            writer.WriteLine(workflow);
        }
        catch (Exception exception)
        {
            throw new InvalidOperationException(
                "The LIM Landscape Data window is running, but Revit could not switch it to the selected tool. Close that window and try again.",
                exception);
        }
    }

    public void Dispose()
    {
        _process?.Dispose();
        _process = null;
    }
}
