using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;

namespace WWP.LandscapeDataManager.SharedUI;

/// <summary>
/// Ends a table's swim lanes at its last row. Set <c>ui:TableLanes.FollowRows="True"</c> on the lane
/// grid (LimTableLanesStyle): it finds the ListView beside it in the same parent and keeps the lanes
/// as tall as the header plus the rows, so a short or empty list doesn't show lanes running down
/// blank space. A ListView that fills its space keeps lanes to the bottom of the list.
/// </summary>
public static class TableLanes
{
    public static readonly DependencyProperty FollowRowsProperty = DependencyProperty.RegisterAttached(
        "FollowRows", typeof(bool), typeof(TableLanes), new PropertyMetadata(false, OnFollowRowsChanged));

    public static bool GetFollowRows(DependencyObject element) => (bool)element.GetValue(FollowRowsProperty);

    public static void SetFollowRows(DependencyObject element, bool value) => element.SetValue(FollowRowsProperty, value);

    private static void OnFollowRowsChanged(DependencyObject sender, DependencyPropertyChangedEventArgs e)
    {
        if (sender is FrameworkElement lanes && e.NewValue is true)
        {
            // Every time: a Pivot unloads and reloads the content of the tabs it isn't showing.
            lanes.Loaded += Lanes_Loaded;
        }
    }

    private static void Lanes_Loaded(object sender, RoutedEventArgs e)
    {
        var lanes = (FrameworkElement)sender;
        if (lanes.Parent is not Panel parent || parent.Children.OfType<ListViewBase>().FirstOrDefault() is not { } list)
        {
            return;
        }

        lanes.VerticalAlignment = VerticalAlignment.Top;
        void Update(object? _, object? __) => Fit(lanes, parent, list);
        void Unloaded(object _, RoutedEventArgs __)
        {
            list.LayoutUpdated -= Update;
            lanes.Unloaded -= Unloaded;
        }

        list.LayoutUpdated += Update;
        lanes.Unloaded += Unloaded;
        Fit(lanes, parent, list);
    }

    private static void Fit(FrameworkElement lanes, Panel parent, ListViewBase list)
    {
        if (list.XamlRoot is null)
        {
            return;
        }

        // The list's scroller extent is the full (virtualized) height of its rows; the items panel
        // itself stretches to the viewport, so it can't tell a short list from a full one.
        var rows = list.Items.Count == 0 || FindScroller(list) is not { } scroller
            ? 0
            : Math.Min(list.ActualHeight, scroller.ExtentHeight);
        var top = list.TransformToVisual(parent).TransformPoint(default).Y;
        var height = Math.Max(0, top + rows);
        if (double.IsNaN(lanes.Height) || Math.Abs(lanes.Height - height) > 0.5)
        {
            lanes.Height = height;
        }
    }

    private static ScrollViewer? FindScroller(DependencyObject element)
    {
        for (var index = 0; index < VisualTreeHelper.GetChildrenCount(element); index++)
        {
            var child = VisualTreeHelper.GetChild(element, index);
            if ((child as ScrollViewer ?? FindScroller(child)) is { } scroller)
            {
                return scroller;
            }
        }

        return null;
    }
}
