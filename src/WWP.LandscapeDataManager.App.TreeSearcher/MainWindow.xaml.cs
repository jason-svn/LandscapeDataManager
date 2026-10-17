using System.Runtime.InteropServices;
using Microsoft.UI.Composition.SystemBackdrops;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media;
using WinRT.Interop;

using WWP.LandscapeDataManager.Shared.Services;
using WWP.LandscapeDataManager.SharedUI;

namespace WWP.LandscapeDataManager.App.TreeSearcher;

public sealed partial class MainWindow : Window
{
    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(nint hwnd, int attribute, ref int attributeValue, int attributeSize);

    /// <summary>
    /// Windows' window-open transition animation is known to leave a stale/blank captured frame on
    /// screen over Remote Desktop sessions until something (a manual resize) forces DWM to
    /// recomposite the real content — matches this window opening "nearly empty" and only filling in
    /// as it's dragged wider. Disabling the transition for this window sidesteps it entirely.
    /// </summary>
    private const int DwmwaTransitionsForceDisabled = 3;

    public MainWindow(string pipeName)
    {
        InitializeComponent();
        Title = $"{Title} {LimVersion.Text}";

        // Mica composition is known to render incompletely (blank until a manual resize forces a
        // repaint) over Remote Desktop sessions — MicaController.IsSupported() already accounts for
        // that, so only apply the backdrop when it will actually paint correctly.
        if (MicaController.IsSupported())
        {
            SystemBackdrop = new MicaBackdrop { Kind = MicaKind.BaseAlt };
        }

        var windowHandle = WindowNative.GetWindowHandle(this);
        var disableTransitions = 1;
        DwmSetWindowAttribute(windowHandle, DwmwaTransitionsForceDisabled, ref disableTransitions, sizeof(int));

        MainContent.Initialize(pipeName, windowHandle);
        WindowSizing.SetInitialSize(this, 820, 680);

        Closed += OnClosed;
    }

    private async void OnClosed(object sender, WindowEventArgs args)
    {
        await MainContent.DisposeAsync();
    }
}
