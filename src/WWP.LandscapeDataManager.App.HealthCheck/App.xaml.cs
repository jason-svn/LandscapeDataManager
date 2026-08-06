using Microsoft.UI.Xaml;

namespace WWP.LandscapeDataManager.App.HealthCheck;

public partial class App : Application
{
    private Window? _window;

    public App()
    {
        InitializeComponent();
    }

    protected override void OnLaunched(LaunchActivatedEventArgs args)
    {
        var arguments = Environment.GetCommandLineArgs();
        var pipeName = ReadRequiredArgument(arguments, "--pipe");
        _window = new MainWindow(pipeName);
        _window.Activate();
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
