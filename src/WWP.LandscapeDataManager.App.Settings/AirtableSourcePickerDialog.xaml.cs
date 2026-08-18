using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using WWP.LandscapeDataManager.Shared.Services;

namespace WWP.LandscapeDataManager.App.Settings;

/// <summary>One row in any of the three picker lists (base, table, or view) — a plain Id/Name pair for x:Bind.</summary>
public sealed record AirtablePickerItem(string Id, string Name);

/// <summary>
/// Lets the user drill Base → Table → View instead of typing exact Airtable IDs/names into the
/// Settings text boxes. Selecting a view (or "All records") resolves the result and closes the
/// dialog; the caller still owns saving the values into whichever section's text boxes it came from.
/// </summary>
public sealed partial class AirtableSourcePickerDialog : ContentDialog
{
    private const string AllRecordsViewId = "__all_records__";

    private enum Step { Base, Table, View }

    private readonly AirtableApiClient _client = new();
    private string _apiToken = string.Empty;
    private Step _step = Step.Base;

    private IReadOnlyList<AirtableTableSummary> _tablesInSelectedBase = [];
    private string _selectedBaseId = string.Empty;
    private string _selectedTableName = string.Empty;

    private TaskCompletionSource<(string BaseId, string TableIdOrName, string? ViewName)?>? _completionSource;

    public AirtableSourcePickerDialog()
    {
        InitializeComponent();
    }

    public Task<(string BaseId, string TableIdOrName, string? ViewName)?> PickAsync(string apiToken)
    {
        _apiToken = apiToken;
        _completionSource = new TaskCompletionSource<(string, string, string?)?>();
        _ = ShowAsync();
        return _completionSource.Task;
    }

    private async void Dialog_Opened(ContentDialog sender, ContentDialogOpenedEventArgs args)
    {
        await LoadBasesAsync();
    }

    private async Task LoadBasesAsync()
    {
        _step = Step.Base;
        StepText.Text = "Select a base";
        IsSecondaryButtonEnabled = false;
        await RunWithLoadingAsync(async () =>
        {
            var bases = await _client.GetBasesAsync(_apiToken);
            ItemsListView.ItemsSource = bases.Select(b => new AirtablePickerItem(b.Id, b.Name)).ToList();
        });
    }

    private async Task LoadTablesAsync(string baseId, string baseName)
    {
        _step = Step.Table;
        _selectedBaseId = baseId;
        StepText.Text = $"Select a table in \"{baseName}\"";
        IsSecondaryButtonEnabled = true;
        await RunWithLoadingAsync(async () =>
        {
            _tablesInSelectedBase = await _client.GetTablesAsync(_apiToken, baseId);
            ItemsListView.ItemsSource = _tablesInSelectedBase.Select(t => new AirtablePickerItem(t.Id, t.Name)).ToList();
        });
    }

    private void ShowViewsForSelectedTable(string tableName)
    {
        _step = Step.View;
        _selectedTableName = tableName;
        StepText.Text = $"Select a view in \"{tableName}\" (optional)";
        IsSecondaryButtonEnabled = true;

        var table = _tablesInSelectedBase.First(t => t.Name == tableName);
        var items = new List<AirtablePickerItem> { new(AllRecordsViewId, "All records (no view filter)") };
        items.AddRange(table.Views.Select(v => new AirtablePickerItem(v.Id, v.Name)));
        ItemsListView.ItemsSource = items;
    }

    private void ShowTablesInSelectedBase()
    {
        _step = Step.Table;
        StepText.Text = "Select a table";
        IsSecondaryButtonEnabled = true;
        ItemsListView.ItemsSource = _tablesInSelectedBase.Select(t => new AirtablePickerItem(t.Id, t.Name)).ToList();
    }

    private async Task RunWithLoadingAsync(Func<Task> operation)
    {
        LoadingRing.IsActive = true;
        ErrorText.Visibility = Visibility.Collapsed;
        ItemsListView.Visibility = Visibility.Collapsed;
        try
        {
            await operation();
            ItemsListView.Visibility = Visibility.Visible;
        }
        catch (AirtableInsufficientScopeException exception)
        {
            ErrorText.Text = exception.Message + " Close this dialog and enter the Base ID/Table/View manually below instead.";
            ErrorText.Visibility = Visibility.Visible;
        }
        catch (Exception exception)
        {
            ErrorText.Text = $"Couldn't load from Airtable: {exception.Message}";
            ErrorText.Visibility = Visibility.Visible;
        }
        finally
        {
            LoadingRing.IsActive = false;
        }
    }

    private async void ItemsListView_ItemClick(object sender, ItemClickEventArgs e)
    {
        var item = (AirtablePickerItem)e.ClickedItem;
        switch (_step)
        {
            case Step.Base:
                await LoadTablesAsync(item.Id, item.Name);
                break;
            case Step.Table:
                ShowViewsForSelectedTable(item.Name);
                break;
            case Step.View:
                var viewName = item.Id == AllRecordsViewId ? null : item.Name;
                _completionSource?.TrySetResult((_selectedBaseId, _selectedTableName, viewName));
                Hide();
                break;
        }
    }

    private async void Dialog_SecondaryButtonClick(ContentDialog sender, ContentDialogButtonClickEventArgs args)
    {
        args.Cancel = true;
        switch (_step)
        {
            case Step.Table:
                await LoadBasesAsync();
                break;
            case Step.View:
                ShowTablesInSelectedBase();
                break;
        }
    }

    private void Dialog_Closed(ContentDialog sender, ContentDialogClosedEventArgs args)
    {
        _completionSource?.TrySetResult(null);
    }
}
