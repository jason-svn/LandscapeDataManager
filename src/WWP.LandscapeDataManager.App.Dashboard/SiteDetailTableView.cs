using Microsoft.UI;
using Microsoft.UI.Text;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Shapes;
using WWP.LandscapeDataManager.Shared.Services;
using WWP.LandscapeDataManager.SharedUI;

namespace WWP.LandscapeDataManager.App.Dashboard;

/// <summary>
/// Draws a <see cref="SiteDetailTable"/> in the table language of every LIM table: swim lanes behind
/// the header and rows (ending at the last row), lane names over two-line column headers, plain
/// numbers aligned right. Built in code because each card's table has its own columns; rebuilt when
/// the theme changes (the brushes are looked up once).
/// </summary>
internal static class SiteDetailTableView
{
    public static FrameworkElement Build(SiteDetailTable table)
    {
        if (table.Rows.Count == 0 && table.EmptyText is { } empty)
        {
            return new TextBlock { Text = empty, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(8, 12, 8, 0), Foreground = Brush("LimLaneGreyTextBrush") };
        }

        var columns = table.Columns;
        var root = new Grid();
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });

        var lanes = Columns(new Grid { Style = Style("LimTableLanesStyle") }, columns);
        Grid.SetRowSpan(lanes, 2);
        TableLanes.SetFollowRows(lanes, true);
        var header = Columns(new Grid { Style = Style("LimTableHeaderRowStyle"), Padding = new Thickness(8, 10, 26, 8), RowSpacing = 6 }, columns);
        header.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        header.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        var start = 0;
        foreach (var lane in table.Lanes)
        {
            if (lane.Colour is { } colour)
            {
                lanes.Children.Add(Place(new Border { Style = Style("LimTableLaneStyle"), Background = Brush($"LimLane{colour}Brush") }, 0, start, lane.Columns.Count));
            }

            if (lane.Title is { } title)
            {
                header.Children.Add(Place(new TextBlock
                {
                    Text = title,
                    Style = Style("LimTableGroupStyle"),
                    Foreground = Brush(lane.Colour is null ? "LimLaneGreyTextBrush" : $"LimLane{lane.Colour}TextBrush")
                }, 0, start, lane.Columns.Count));
            }

            start += lane.Columns.Count;
        }

        for (var index = 0; index < columns.Count; index++)
        {
            var column = columns[index];
            var text = new TextBlock
            {
                Text = string.IsNullOrEmpty(column.Unit) ? column.Name : $"{column.Name}\n{column.Unit}",
                Style = Style(column.Align == SiteDetailAlign.Right ? "LimTableNumberHeaderStyle" : "LimTableHeaderStyle")
            };
            if (column.Align == SiteDetailAlign.Center)
            {
                text.TextAlignment = TextAlignment.Center;
            }

            header.Children.Add(Place(text, 1, index, 1));
        }

        var list = new ListView { SelectionMode = ListViewSelectionMode.None, ItemContainerStyle = Style("LimTableItemStyle") };
        Grid.SetRow(list, 1);
        for (var index = 0; index < table.Rows.Count; index++)
        {
            list.Items.Add(Row(columns, table.Rows[index], bold: false, table.Looks is { } looks && index < looks.Count ? looks[index] : null));
        }

        if (table.Total is { } total)
        {
            list.Items.Add(Row(columns, total, bold: true, null));
        }

        root.Children.Add(lanes);
        root.Children.Add(header);
        root.Children.Add(list);
        return root;
    }

    private static Grid Row(IReadOnlyList<SiteDetailColumn> columns, IReadOnlyList<string> cells, bool bold, IReadOnlyList<SiteDetailCellLook?>? looks)
    {
        var row = Columns(new Grid { Style = Style("LimTableRowStyle") }, columns);
        for (var index = 0; index < columns.Count && index < cells.Count; index++)
        {
            var text = new TextBlock
            {
                Text = cells[index],
                VerticalAlignment = VerticalAlignment.Center,
                TextWrapping = TextWrapping.Wrap,
                TextAlignment = columns[index].Align switch
                {
                    SiteDetailAlign.Right => TextAlignment.Right,
                    SiteDetailAlign.Center => TextAlignment.Center,
                    _ => TextAlignment.Left
                }
            };
            if (bold)
            {
                text.FontWeight = FontWeights.SemiBold;
            }

            var look = looks is not null && index < looks.Count ? looks[index] : null;
            row.Children.Add(Place(look is null ? text : Cell(look, text, columns[index].Align), 0, index, 1));
        }

        return row;
    }

    /// <summary>A cell with a look: its tint behind (a translucent fill, so it reads in either theme), its marks, then its text.</summary>
    private static FrameworkElement Cell(SiteDetailCellLook look, TextBlock text, SiteDetailAlign align)
    {
        var cell = new Grid { Background = new SolidColorBrush(Colors.Transparent) };
        if (Colour(look.Background) is { } background)
        {
            cell.Children.Add(new Border { Background = new SolidColorBrush(background) { Opacity = 0.5 }, CornerRadius = new CornerRadius(4), Margin = new Thickness(1, -4, 1, -4) });
        }

        var content = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 3,
            VerticalAlignment = VerticalAlignment.Center,
            HorizontalAlignment = align == SiteDetailAlign.Center ? HorizontalAlignment.Center : HorizontalAlignment.Left
        };
        foreach (var mark in look.Marks)
        {
            content.Children.Add(Mark(mark));
        }

        if (!string.IsNullOrEmpty(text.Text))
        {
            text.Margin = new Thickness(look.Marks.Count > 0 ? 3 : 0, 0, 0, 0);
            content.Children.Add(text);
        }

        cell.Children.Add(content);
        if (!string.IsNullOrEmpty(look.ToolTip))
        {
            ToolTipService.SetToolTip(cell, look.ToolTip);
        }

        return cell;
    }

    /// <summary>
    /// A flower (five petals) or a kind of fruit, its shape telling which: a round berry, drupe, pome
    /// or fig, an upright nut or acorn, a samara's diamond, a cone's triangle, a pod or capsule's
    /// bar, a catkin's stem, a seed head's star. Outlined so a white or pale colour still shows.
    /// </summary>
    private static FrameworkElement Mark(SiteDetailMark mark)
    {
        var fill = new SolidColorBrush(Colour(mark.Colour) ?? Colors.Gray);
        var outline = Brush("LimLaneGreyTextBrush");
        Shape shape = mark.Kind switch
        {
            SiteDetailMark.FlowerKind => new Microsoft.UI.Xaml.Shapes.Path { Data = Flower(), Width = 13, Height = 13 },
            "Nut" or "Acorn" => new Ellipse { Width = 8, Height = 11 },
            "Samara" => new Polygon { Points = [new(5, 0), new(10, 5.5), new(5, 11), new(0, 5.5)], Width = 10, Height = 11 },
            "Cone" => new Polygon { Points = [new(5, 0), new(10, 11), new(0, 11)], Width = 10, Height = 11 },
            "Pod" or "Capsule" => new Rectangle { Width = 13, Height = 6, RadiusX = 3, RadiusY = 3 },
            "Catkin" => new Rectangle { Width = 5, Height = 12, RadiusX = 2.5, RadiusY = 2.5 },
            "Seed head" => new Polygon { Points = Star(), Width = 12, Height = 12 },
            _ => new Ellipse { Width = 10, Height = 10 }
        };
        shape.Fill = fill;
        shape.Stroke = outline;
        shape.StrokeThickness = 0.9;
        shape.VerticalAlignment = VerticalAlignment.Center;
        return shape;
    }

    /// <summary>Five round petals about a centre, 13 px across.</summary>
    private static Geometry Flower()
    {
        var petals = new GeometryGroup { FillRule = FillRule.Nonzero };
        for (var petal = 0; petal < 5; petal++)
        {
            var angle = (petal * 72 - 90) * Math.PI / 180;
            petals.Children.Add(new EllipseGeometry { Center = new Windows.Foundation.Point(6.5 + 3.4 * Math.Cos(angle), 6.5 + 3.4 * Math.Sin(angle)), RadiusX = 2.9, RadiusY = 2.9 });
        }

        return petals;
    }

    /// <summary>A six-pointed star, 12 px across.</summary>
    private static PointCollection Star()
    {
        var points = new PointCollection();
        for (var point = 0; point < 12; point++)
        {
            var angle = (point * 30 - 90) * Math.PI / 180;
            var radius = point % 2 == 0 ? 6 : 2.6;
            points.Add(new Windows.Foundation.Point(6 + radius * Math.Cos(angle), 6 + radius * Math.Sin(angle)));
        }

        return points;
    }

    /// <summary>"#RRGGBB" as a colour; null for anything else.</summary>
    private static Windows.UI.Color? Colour(string? hex) =>
        hex is { Length: 7 } && hex[0] == '#' && uint.TryParse(hex.AsSpan(1), System.Globalization.NumberStyles.HexNumber, null, out var rgb)
            ? Windows.UI.Color.FromArgb(255, (byte)(rgb >> 16), (byte)(rgb >> 8), (byte)rgb)
            : null;

    private static Grid Columns(Grid grid, IReadOnlyList<SiteDetailColumn> columns)
    {
        foreach (var column in columns)
        {
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(column.Width) });
        }

        return grid;
    }

    private static T Place<T>(T element, int row, int column, int span) where T : FrameworkElement
    {
        Grid.SetRow(element, row);
        Grid.SetColumn(element, column);
        Grid.SetColumnSpan(element, Math.Max(1, span));
        return element;
    }

    private static Style Style(string key) => (Style)Application.Current.Resources[key];

    private static Brush Brush(string key) => (Brush)Application.Current.Resources[key];
}
