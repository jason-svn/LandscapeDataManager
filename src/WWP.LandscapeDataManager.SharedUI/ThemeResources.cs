using Microsoft.UI.Xaml;
using Microsoft.UI.System;
using Windows.UI.ViewManagement;

namespace WWP.LandscapeDataManager.SharedUI;

internal static class ThemeResources
{
    /// <summary>Refreshes code-assigned brushes when the element theme or system contrast palette changes.</summary>
    public static void Observe(FrameworkElement element, Action refresh)
    {
        var colours = new UISettings();
        ThemeSettings? theme = null;
        element.ActualThemeChanged += (_, _) => refresh();
        // Subscribed once and never removed: the subscriptions live as long as the window, and
        // QueueRefresh skips an unloaded element. Removing ThemeSettings.Changed in Unloaded failed
        // fast in native code when the window closed (Unloaded runs after the window is gone), so
        // every tool that observes the theme crashed on close.
        element.Loaded += (_, _) =>
        {
            if (theme is null)
            {
                colours.ColorValuesChanged += ColoursChanged;
                // ThemeSettings is the desktop counterpart to AccessibilitySettings.HighContrastChanged.
                theme = ThemeSettings.CreateForWindowId(element.XamlRoot.ContentIslandEnvironment.AppWindowId);
                theme.Changed += ContrastChanged;
            }

            refresh();
        };

        // System notifications can arrive on a background thread and can precede XAML's update.
        // Queue the refresh so the resource lookup observes the current theme on the UI thread.
        void QueueRefresh() => element.DispatcherQueue.TryEnqueue(() =>
        {
            if (element.IsLoaded)
            {
                refresh();
            }
        });

        void ColoursChanged(UISettings sender, object args) => QueueRefresh();
        void ContrastChanged(ThemeSettings sender, object args) => QueueRefresh();
    }
}
