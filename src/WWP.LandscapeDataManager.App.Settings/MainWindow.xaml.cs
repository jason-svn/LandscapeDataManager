using Microsoft.UI.Xaml;
using WinRT.Interop;

using WWP.LandscapeDataManager.Shared.Services;
using WWP.LandscapeDataManager.SharedUI;

namespace WWP.LandscapeDataManager.App.Settings;

public sealed partial class MainWindow : Window
{
    public MainWindow(string pipeName)
    {
        InitializeComponent();
        Title = $"{Title} {LimVersion.Text}";

        var windowHandle = WindowNative.GetWindowHandle(this);
        MainContent.Initialize(pipeName, windowHandle);
        WindowSizing.SetInitialSize(this, 900, 760);

        Closed += OnClosed;
    }

    private async void OnClosed(object sender, WindowEventArgs args)
    {
        await MainContent.DisposeAsync();
    }
}
