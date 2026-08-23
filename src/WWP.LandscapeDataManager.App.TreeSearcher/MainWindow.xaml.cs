using System.Runtime.InteropServices;
using Microsoft.UI.Composition.SystemBackdrops;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media;
using Windows.Graphics;
using WinRT.Interop;

namespace WWP.LandscapeDataManager.App.TreeSearcher;

public sealed partial class MainWindow : Window
{
    [DllImport("user32.dll")]
    private static extern uint GetDpiForWindow(nint hWnd);

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
        var windowId = Microsoft.UI.Win32Interop.GetWindowIdFromWindow(windowHandle);
        var appWindow = AppWindow.GetFromWindowId(windowId);
        appWindow.SetPresenter(AppWindowPresenterKind.Default);

        // Activate before Resize, not after: resizing an AppWindow before it's ever been shown
        // changes the OS window frame but the XAML content's own layout stays measured against the
        // pre-resize size until a real WM_SIZE arrives — which previously only happened once the
        // user manually dragged the window border. Activating first wires up the content pipeline
        // so the subsequent Resize (in physical pixels — AppWindow.Resize doesn't take DIPs, so this
        // still has to scale by the monitor's DPI) actually reaches the content on the first frame.
        Activate();
        var scale = GetDpiForWindow(windowHandle) / 96.0;
        appWindow.Resize(new SizeInt32((int)(820 * scale), (int)(680 * scale)));

        Closed += OnClosed;
    }

    private async void OnClosed(object sender, WindowEventArgs args)
    {
        await MainContent.DisposeAsync();
    }
}
