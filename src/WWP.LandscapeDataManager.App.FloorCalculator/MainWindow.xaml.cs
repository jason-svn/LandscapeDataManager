using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Windows.Graphics;
using WinRT.Interop;

using WWP.LandscapeDataManager.Shared.Services;

namespace WWP.LandscapeDataManager.App.FloorCalculator;

public sealed partial class MainWindow : Window
{
    public MainWindow(string pipeName)
    {
        InitializeComponent();
        Title = $"{Title} {LimVersion.Text}";

        var windowHandle = WindowNative.GetWindowHandle(this);
        MainContent.Initialize(pipeName, windowHandle);
        var windowId = Microsoft.UI.Win32Interop.GetWindowIdFromWindow(windowHandle);
        var appWindow = AppWindow.GetFromWindowId(windowId);
        // Wide enough for the BNG tab's A-2 row without scrolling the first dozen columns.
        appWindow.Resize(new SizeInt32(1440, 860));
        appWindow.SetPresenter(AppWindowPresenterKind.Default);

        Closed += OnClosed;
    }

    private async void OnClosed(object sender, WindowEventArgs args)
    {
        await MainContent.DisposeAsync();
    }
}
