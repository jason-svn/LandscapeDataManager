using System.Runtime.InteropServices;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Windows.Graphics;
using WinRT.Interop;

namespace WWP.LandscapeDataManager.SharedUI;

internal static class WindowSizing
{
    [DllImport("user32.dll")]
    private static extern uint GetDpiForWindow(nint windowHandle);

    /// <summary>Sizes a tool in effective pixels, fitting it inside the current monitor's work area.</summary>
    public static void SetInitialSize(Window window, int widthDip, int heightDip)
    {
        window.AppWindow.SetPresenter(AppWindowPresenterKind.Default);
        window.Activated += ApplyInitialSize;

        // Wait until XAML is connected to the native window. Resizing earlier can leave the first
        // frame measured at the old size, particularly over Remote Desktop. Later user resizes
        // and monitor changes remain owned by Windows rather than resetting to our preferred size.
        void ApplyInitialSize(object sender, WindowActivatedEventArgs args)
        {
            if (args.WindowActivationState == WindowActivationState.Deactivated)
            {
                return;
            }

            window.Activated -= ApplyInitialSize;
            var dpi = GetDpiForWindow(WindowNative.GetWindowHandle(window));
            var scale = (dpi == 0 ? 96 : dpi) / 96.0;
            var width = (int)Math.Round(widthDip * scale);
            var height = (int)Math.Round(heightDip * scale);
            var display = DisplayArea.GetFromWindowId(window.AppWindow.Id, DisplayAreaFallback.Nearest);
            if (display is null)
            {
                window.AppWindow.Resize(new SizeInt32(width, height));
                return;
            }

            var area = display.WorkArea;
            var margin = (int)Math.Round(16 * scale);
            width = Math.Min(width, Math.Max(1, area.Width - 2 * margin));
            height = Math.Min(height, Math.Max(1, area.Height - 2 * margin));
            window.AppWindow.MoveAndResize(new RectInt32(
                area.X + (area.Width - width) / 2,
                area.Y + (area.Height - height) / 2,
                width,
                height));
        }
    }
}
