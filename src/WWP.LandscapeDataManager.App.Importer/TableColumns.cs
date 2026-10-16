using System.ComponentModel;
using System.Runtime.CompilerServices;
using Microsoft.UI.Input;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Shapes;

namespace WWP.LandscapeDataManager.App.Importer;

/// <summary>
/// Column widths shared by a table's header and every row, so dragging a header grip resizes the
/// whole column. Rows reach these through the static <see cref="ImporterColumns"/> instances
/// (x:Bind from a DataTemplate can't see the page).
/// </summary>
public sealed class TableColumns : INotifyPropertyChanged
{
    private const double MinimumWidth = 48;
    private readonly double[] _widths;

    public TableColumns(params double[] widths) => _widths = widths;

    public event PropertyChangedEventHandler? PropertyChanged;

    public GridLength C0 => Width(0);
    public GridLength C1 => Width(1);
    public GridLength C2 => Width(2);
    public GridLength C3 => Width(3);
    public GridLength C4 => Width(4);

    /// <summary>Widens or narrows column <paramref name="index"/> by <paramref name="delta"/> pixels.</summary>
    public void Resize(int index, double delta)
    {
        _widths[index] = Math.Max(MinimumWidth, _widths[index] + delta);
        OnPropertyChanged($"C{index}");
    }

    private GridLength Width(int index) => new(_widths[index]);

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
}

/// <summary>The Importer's resizable tables. The last column of each table fills the remaining width.</summary>
public static class ImporterColumns
{
    /// <summary>Map columns: source column, scope, Revit parameter, conversion (then status fills).</summary>
    public static TableColumns Mapping { get; } = new(240, 110, 340, 200);

    /// <summary>Type and instance results: status, type/element, parameter, current, proposed (then message fills).</summary>
    public static TableColumns Results { get; } = new(110, 240, 280, 140, 140);
}

/// <summary>
/// The drag handle on a column header's right edge: a thin divider with a wider hit area and the
/// left-right resize cursor. Raises <see cref="Resized"/> with the horizontal drag distance.
/// (WinUI's Thumb is sealed, so this does its own pointer capture.)
/// </summary>
public sealed partial class ColumnResizeGrip : Grid
{
    private double? _lastX;

    public ColumnResizeGrip()
    {
        Width = 9;
        Margin = new Thickness(0, 0, -9, 0);
        HorizontalAlignment = HorizontalAlignment.Right;
        Background = new SolidColorBrush(Microsoft.UI.Colors.Transparent);
        ProtectedCursor = InputSystemCursor.Create(InputSystemCursorShape.SizeWestEast);
        Children.Add(new Rectangle
        {
            Width = 1,
            Margin = new Thickness(0, 2, 0, 2),
            HorizontalAlignment = HorizontalAlignment.Center,
            Fill = (Brush)Application.Current.Resources["CardStrokeColorDefaultBrush"]
        });
        PointerPressed += Grip_PointerPressed;
        PointerMoved += Grip_PointerMoved;
        PointerReleased += Grip_PointerReleased;
        PointerCaptureLost += (_, _) => _lastX = null;
    }

    public event EventHandler<double>? Resized;

    private void Grip_PointerPressed(object sender, PointerRoutedEventArgs e)
    {
        if (CapturePointer(e.Pointer))
        {
            _lastX = e.GetCurrentPoint(null).Position.X;
            e.Handled = true;
        }
    }

    private void Grip_PointerMoved(object sender, PointerRoutedEventArgs e)
    {
        if (_lastX is not { } lastX)
        {
            return;
        }

        var x = e.GetCurrentPoint(null).Position.X;
        Resized?.Invoke(this, x - lastX);
        _lastX = x;
        e.Handled = true;
    }

    private void Grip_PointerReleased(object sender, PointerRoutedEventArgs e)
    {
        _lastX = null;
        ReleasePointerCapture(e.Pointer);
    }
}
