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
        var subscribed = false;
        element.ActualThemeChanged += (_, _) => refresh();
        element.Loaded += (_, _) =>
        {
            if (subscribed)
            {
                return;
            }

            subscribed = true;
            colours.ColorValuesChanged += ColoursChanged;
            // ThemeSettings is the desktop counterpart to AccessibilitySettings.HighContrastChanged.
            theme = ThemeSettings.CreateForWindowId(element.XamlRoot.ContentIslandEnvironment.AppWindowId);
            theme.Changed += ContrastChanged;
            refresh();
        };
        element.Unloaded += (_, _) =>
        {
            subscribed = false;
            colours.ColorValuesChanged -= ColoursChanged;
            if (theme is not null)
            {
                theme.Changed -= ContrastChanged;
                theme = null;
            }
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
