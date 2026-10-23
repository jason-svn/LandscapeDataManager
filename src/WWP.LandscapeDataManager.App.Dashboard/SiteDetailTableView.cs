using Microsoft.UI.Text;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
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
        foreach (var row in table.Rows)
        {
            list.Items.Add(Row(columns, row, bold: false));
        }

        if (table.Total is { } total)
        {
            list.Items.Add(Row(columns, total, bold: true));
        }

        root.Children.Add(lanes);
        root.Children.Add(header);
        root.Children.Add(list);
        return root;
    }

    private static Grid Row(IReadOnlyList<SiteDetailColumn> columns, IReadOnlyList<string> cells, bool bold)
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

            row.Children.Add(Place(text, 0, index, 1));
        }

        return row;
    }

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
