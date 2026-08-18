using System.IO;
using Microsoft.UI.Xaml;

namespace WWP.LandscapeDataManager.App.Dashboard;

public partial class App : Application
{
    private Window? _window;

    public App()
    {
        InitializeComponent();
        UnhandledException += (_, e) =>
        {
            try
            {
                File.WriteAllText(Path.Combine(Path.GetTempPath(), "dashboard-crash.txt"), $"{e.Exception}\n\n{e.Message}");
            }
            catch { }
        };
    }

    protected override void OnLaunched(LaunchActivatedEventArgs args)
    {
        try
        {
            var arguments = Environment.GetCommandLineArgs();
            var pipeName = ReadRequiredArgument(arguments, "--pipe");
            _window = new MainWindow(pipeName);
            _window.Activate();
        }
        catch (Exception exception)
        {
            File.WriteAllText(Path.Combine(Path.GetTempPath(), "dashboard-crash.txt"), exception.ToString());
            throw;
        }
    }

    private static string ReadRequiredArgument(IReadOnlyList<string> arguments, string name)
    {
        for (var index = 0; index < arguments.Count - 1; index++)
        {
            if (string.Equals(arguments[index], name, StringComparison.OrdinalIgnoreCase))
            {
                return arguments[index + 1];
            }
        }

        throw new InvalidOperationException($"The required argument '{name}' was not provided.");
    }
}
