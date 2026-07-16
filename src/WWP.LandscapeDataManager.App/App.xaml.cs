using Microsoft.UI.Xaml;

namespace WWP.LandscapeDataManager.App;

public partial class App : Application
{
    private Window? _window;

    public App()
    {
        InitializeComponent();
    }

    protected override void OnLaunched(LaunchActivatedEventArgs args)
    {
        var pipeName = ReadPipeName(Environment.GetCommandLineArgs());
        _window = new MainWindow(pipeName);
        _window.Activate();
    }

    private static string ReadPipeName(IReadOnlyList<string> arguments)
    {
        for (var index = 0; index < arguments.Count - 1; index++)
        {
            if (string.Equals(arguments[index], "--pipe", StringComparison.OrdinalIgnoreCase))
            {
                return arguments[index + 1];
            }
        }

        throw new InvalidOperationException("The Revit pipe name was not provided.");
    }
}
