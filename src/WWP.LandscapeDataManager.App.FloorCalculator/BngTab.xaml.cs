using System.Collections.ObjectModel;
using System.Globalization;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Windows.System;
using WWP.LandscapeDataManager.Contracts;
using WWP.LandscapeDataManager.Shared.Services;

namespace WWP.LandscapeDataManager.App.FloorCalculator;

/// <summary>
/// Floor Calculator's "Biodiversity (BNG)" tab: one Statutory Biodiversity Metric A-2 row per
/// floor, calculated live in the tool (see <see cref="BngHabitatCreationCalculator"/>) and written
/// to the floor's <c>!_S_PLT_BNG*</c> parameters. Habitats are pre-filled from each floor type's
/// remembered mapping, else guessed from the family/type name (flagged Auto-matched until confirmed).
/// </summary>
public sealed partial class BngTab : UserControl
{
    private const int MaxSuggestions = 25;
    private const string KeepOption = "(keep each row's)";
    private const string BulkTargetProposed = "Proposed habitat (new and enhanced floors)";
    private const string BulkTargetBaseline = "Baseline habitat (Existing-phase floors)";

    private readonly BngMetricCatalog _catalog = BngMetricCatalog.Default;
    private Func<RevitPipeClient>? _getClient;
    private string _pipeName = string.Empty;
    private bool _syncingColumnScroll;
    private string? _activeFilter;
    private BngHabitat? _bulkHabitat;

    public BngTab()
    {
        InitializeComponent();
        IntroText.Text += $" Metric: {_catalog.DisplayVersion}.";
        // handledEventsToo: the ListView inside consumes wheel events for its own vertical scrolling.
        TableScroller.AddHandler(PointerWheelChangedEvent, new PointerEventHandler(TableScroller_PointerWheelChanged), true);
        UpdateCounts();
    }

    /// <summary>Every loaded floor; <see cref="FilteredRows"/> is what the list shows.</summary>
    public ObservableCollection<BngFloorRow> AllRows { get; } = [];

    public ObservableCollection<BngFloorRow> FilteredRows { get; } = [];

    public bool HasLoaded { get; private set; }

    public void Initialize(Func<RevitPipeClient> getClient, string pipeName)
    {
        _getClient = getClient;
        _pipeName = pipeName;
    }

    /// <summary>Loads the current Revit selection the first time the tab is shown; a no-op afterwards.</summary>
    public async Task LoadOnFirstShowAsync()
    {
        if (HasLoaded)
        {
            return;
        }

        HasLoaded = true;
        await RunBusyAsync(() => LoadAsync(selectionOnly: true));
    }

    private async void LoadSelected_Click(object sender, RoutedEventArgs e) => await RunBusyAsync(() => LoadAsync(selectionOnly: true));

    private async void LoadAll_Click(object sender, RoutedEventArgs e) => await RunBusyAsync(() => LoadAsync(selectionOnly: false));

    private async Task LoadAsync(bool selectionOnly)
    {
        HasLoaded = true;
        var result = await GetClient().SendAsync<BngFloorsResult>(PipeCommands.GetBngFloors, new BngFloorsRequest(selectionOnly));

        foreach (var row in AllRows)
        {
            row.StatusChanged -= Row_StatusChanged;
        }

        AllRows.Clear();
        int fromType = 0, guessed = 0;
        foreach (var item in result.Items)
        {
            var row = new BngFloorRow(item, _catalog);
            // Pre-fill the row's primary habitat — the baseline one for an Existing-phase floor
            // (an enhanced floor's target is always a deliberate choice), else the proposed one.
            var isBaseline = row.IsBaselineRow;
            var hasHabitat = isBaseline ? !string.IsNullOrWhiteSpace(item.BaselineHabitat) : !string.IsNullOrWhiteSpace(item.ProposedHabitat);
            if (!hasHabitat && row.Role != BngPhaseRoles.Excluded)
            {
                bool Fits(BngHabitat habitat) => isBaseline ? habitat.CanBeBaseline : habitat.CanBeCreated;
                if (_catalog.FindHabitat(item.TypeHabitatMapping) is { } remembered && Fits(remembered))
                {
                    if (isBaseline)
                    {
                        row.SetBaselineHabitat(remembered);
                    }
                    else
                    {
                        row.SetHabitat(remembered);
                    }

                    fromType++;
                }
                else if (BngHabitatMatcher.FindBestMatch(item.FamilyName, item.TypeName, _catalog) is { } match && Fits(match))
                {
                    row.SetAutoMatchedHabitat(match);
                    guessed++;
                }
            }

            row.StatusChanged += Row_StatusChanged;
            AllRows.Add(row);
        }

        _activeFilter = null;
        ApplyFilter();

        var scope = selectionOnly ? "from the current Revit selection" : "in the model";
        var notes = new List<string> { $"Loaded {AllRows.Count:N0} floor(s) {scope}." };
        var baselineCount = AllRows.Count(row => row.IsBaselineRow);
        if (baselineCount > 0)
        {
            notes.Add($"{baselineCount:N0} are Existing-phase (baseline) floors — {AllRows.Count(row => row.Role == BngPhaseRoles.Lost):N0} of them demolished.");
        }
        if (fromType > 0)
        {
            notes.Add($"{fromType:N0} pre-filled from their floor type's remembered habitat.");
        }

        if (guessed > 0)
        {
            notes.Add($"{guessed:N0} habitat(s) guessed from the family/type name — review the Auto-matched rows, then \"Confirm auto-matches\".");
        }

        if (AllRows.Any(row => !row.ParametersBound))
        {
            notes.Add("The BNG parameters aren't set up in this project yet — run Parameters from the LIM ribbon before writing.");
        }

        ParametersInfoBar.IsOpen = AllRows.Any(row => !row.ParametersBound);
        StatusText.Text = string.Join(" ", notes);
        DispatcherQueue.TryEnqueue(UpdateColumnScrollRange);
    }

    private void OpenParameters_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            SiblingToolLauncher.ShowOrStart(Path.Combine("Parameters", "WWP.LandscapeDataManager.Parameters.exe"), _pipeName);
        }
        catch (Exception exception)
        {
            StatusText.Text = $"Failed to open Parameters: {exception.Message}";
        }
    }

    private void Row_StatusChanged(object? sender, EventArgs e) => UpdateCounts();

    // ---- Habitat search (per row) ----

    private void HabitatBox_TextChanged(AutoSuggestBox sender, AutoSuggestBoxTextChangedEventArgs args)
    {
        if (args.Reason != AutoSuggestionBoxTextChangeReason.UserInput || sender.DataContext is not BngFloorRow row)
        {
            return;
        }

        row.HabitatSuggestions.Clear();
        foreach (var habitat in SearchHabitats(sender.Text, ProposedHabitats(row)))
        {
            row.HabitatSuggestions.Add(new BngHabitatOption(habitat));
        }
    }

    private void HabitatBox_SuggestionChosen(AutoSuggestBox sender, AutoSuggestBoxSuggestionChosenEventArgs args)
    {
        if (sender.DataContext is BngFloorRow row && args.SelectedItem is BngHabitatOption option)
        {
            row.SetHabitat(option.Habitat);
        }
    }

    private void HabitatBox_QuerySubmitted(AutoSuggestBox sender, AutoSuggestBoxQuerySubmittedEventArgs args)
    {
        if (sender.DataContext is BngFloorRow row)
        {
            SubmitHabitat(args, ProposedHabitats(row), row.SetHabitat);
        }
    }

    private void BaselineHabitatBox_TextChanged(AutoSuggestBox sender, AutoSuggestBoxTextChangedEventArgs args)
    {
        if (args.Reason != AutoSuggestionBoxTextChangeReason.UserInput || sender.DataContext is not BngFloorRow row)
        {
            return;
        }

        row.BaselineHabitatSuggestions.Clear();
        foreach (var habitat in SearchHabitats(sender.Text, _catalog.BaselineHabitats))
        {
            row.BaselineHabitatSuggestions.Add(new BngHabitatOption(habitat));
        }
    }

    private void BaselineHabitatBox_SuggestionChosen(AutoSuggestBox sender, AutoSuggestBoxSuggestionChosenEventArgs args)
    {
        if (sender.DataContext is BngFloorRow row && args.SelectedItem is BngHabitatOption option)
        {
            row.SetBaselineHabitat(option.Habitat);
        }
    }

    private void BaselineHabitatBox_QuerySubmitted(AutoSuggestBox sender, AutoSuggestBoxQuerySubmittedEventArgs args)
    {
        if (sender.DataContext is BngFloorRow row)
        {
            SubmitHabitat(args, _catalog.BaselineHabitats, row.SetBaselineHabitat);
        }
    }

    private void RowEnhanced_Click(object sender, RoutedEventArgs e)
    {
        if (sender is CheckBox { DataContext: BngFloorRow row } checkBox)
        {
            row.Enhanced = checkBox.IsChecked == true;
            UpdateCounts();
        }
    }

    /// <summary>A new floor picks from the A-2 habitats, an enhanced one from the A-3 habitats.</summary>
    private IEnumerable<BngHabitat> ProposedHabitats(BngFloorRow row) =>
        row.Role == BngPhaseRoles.Enhanced ? _catalog.EnhancementHabitats : _catalog.CreatableHabitats;

    private void SubmitHabitat(AutoSuggestBoxQuerySubmittedEventArgs args, IEnumerable<BngHabitat> allowed, Action<BngHabitat?> apply)
    {
        var allowedList = allowed.ToList();
        if (args.ChosenSuggestion is BngHabitatOption chosen)
        {
            apply(chosen.Habitat);
        }
        else if (string.IsNullOrWhiteSpace(args.QueryText))
        {
            apply(null);
        }
        else if (_catalog.FindHabitat(args.QueryText) is { } typed && allowedList.Contains(typed))
        {
            apply(typed);
        }
        else if (SearchHabitats(args.QueryText, allowedList).ToList() is [var onlyMatch])
        {
            apply(onlyMatch);
        }
        else
        {
            StatusText.Text = $"'{args.QueryText}' isn't a metric habitat for this row — pick one from the suggestions.";
        }
    }

    /// <summary>Habitats whose description contains every word typed, in metric order.</summary>
    private static IEnumerable<BngHabitat> SearchHabitats(string query, IEnumerable<BngHabitat> habitats)
    {
        var words = query.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (words.Length == 0)
        {
            return [];
        }

        return habitats
            .Where(habitat => words.All(word => habitat.Description.Contains(word, StringComparison.OrdinalIgnoreCase)))
            .Take(MaxSuggestions);
    }

    // ---- Toolbar actions ----

    private void ConfirmAutoMatches_Click(object sender, RoutedEventArgs e)
    {
        var targets = GetTargetRows().Where(row => row.IsAutoMatched).ToList();
        foreach (var row in targets)
        {
            row.ConfirmAutoMatch();
        }

        StatusText.Text = targets.Count == 0
            ? "No auto-matched rows in the current selection/filter."
            : $"Confirmed {targets.Count:N0} auto-matched habitat(s).";
    }

    private void RowConfirm_Click(object sender, RoutedEventArgs e)
    {
        if (((FrameworkElement)sender).DataContext is BngFloorRow row)
        {
            row.ConfirmAutoMatch();
            UpdateCounts();
        }
    }

    private async void SelectInRevit_Click(object sender, RoutedEventArgs e)
    {
        var ids = GetTargetRows().Select(row => row.UniqueId).ToList();
        if (ids.Count == 0)
        {
            return;
        }

        await RunBusyAsync(async () =>
        {
            var result = await GetClient().SendAsync<OperationResult>(PipeCommands.SelectElements, new ElementSelectionRequest(ids));
            StatusText.Text = result.Message ?? $"Selected {ids.Count:N0} floor(s) in Revit.";
        });
    }

    private async void BulkEdit_Click(object sender, RoutedEventArgs e)
    {
        var targets = GetTargetRows();
        if (targets.Count == 0)
        {
            StatusText.Text = "Load floors first.";
            return;
        }

        _bulkHabitat = null;
        BulkHabitatBox.Text = string.Empty;
        BulkTargetBox.ItemsSource = new[] { BulkTargetProposed, BulkTargetBaseline };
        BulkTargetBox.SelectedIndex = targets.All(row => row.IsBaselineRow) ? 1 : 0;
        BulkConditionBox.ItemsSource = new[] { KeepOption }.Concat(_catalog.Conditions).ToList();
        BulkConditionBox.SelectedIndex = 0;
        BulkStrategicBox.ItemsSource = new[] { KeepOption }.Concat(_catalog.StrategicSignificance.Select(option => option.Description)).ToList();
        BulkStrategicBox.SelectedIndex = 0;
        BulkSetYearOffsetCheckBox.IsChecked = false;
        BulkSameTypeCheckBox.IsChecked = false;
        BulkTargetText.Text = RowsList.SelectedItems.Count > 0
            ? $"Applies to the {targets.Count:N0} selected row(s). Anything left as-is is kept per row."
            : $"No rows are selected, so this applies to all {targets.Count:N0} row(s) currently shown. Anything left as-is is kept per row.";

        BulkEditDialog.XamlRoot = XamlRoot;
        if (await BulkEditDialog.ShowAsync() != ContentDialogResult.Primary)
        {
            return;
        }

        if (BulkSameTypeCheckBox.IsChecked == true)
        {
            var typeIds = targets.Select(row => row.TypeId).ToHashSet();
            targets = AllRows.Where(row => typeIds.Contains(row.TypeId)).ToList();
        }

        var condition = BulkConditionBox.SelectedItem as string is { } pickedCondition && pickedCondition != KeepOption ? pickedCondition : null;
        var strategic = BulkStrategicBox.SelectedItem as string is { } pickedStrategic && pickedStrategic != KeepOption ? pickedStrategic : null;
        int? yearOffset = BulkSetYearOffsetCheckBox.IsChecked == true && !double.IsNaN(BulkYearOffsetBox.Value)
            ? (int)Math.Round(BulkYearOffsetBox.Value)
            : null;

        var toBaseline = ReferenceEquals(BulkTargetBox.SelectedItem, BulkTargetBaseline);
        var conditionSkipped = 0;
        var roleSkipped = 0;
        foreach (var row in targets)
        {
            // Baseline inputs only exist on Existing-phase floors, proposed ones only on new/enhanced floors.
            var applies = toBaseline ? row.IsBaselineRow : row.UsesProposedHabitat;
            if (!applies && (_bulkHabitat is not null || condition is not null))
            {
                roleSkipped++;
            }

            if (applies && _bulkHabitat is not null && (toBaseline ? _bulkHabitat.CanBeBaseline : ProposedHabitats(row).Contains(_bulkHabitat)))
            {
                if (toBaseline)
                {
                    row.SetBaselineHabitat(_bulkHabitat);
                }
                else
                {
                    row.SetHabitat(_bulkHabitat);
                }
            }

            if (applies && condition is not null)
            {
                var options = toBaseline ? row.BaselineConditionOptions : row.ConditionOptions;
                if (options.Contains(condition))
                {
                    if (toBaseline)
                    {
                        row.BaselineCondition = condition;
                    }
                    else
                    {
                        row.Condition = condition;
                    }
                }
                else
                {
                    conditionSkipped++;
                }
            }

            if (strategic is not null)
            {
                row.StrategicSignificance = strategic;
            }

            if (yearOffset is { } offset)
            {
                row.YearOffset = offset;
            }
        }

        var bulkNotes = new List<string> { $"Updated {targets.Count:N0} row(s)." };
        if (conditionSkipped > 0)
        {
            bulkNotes.Add($"Condition '{condition}' was skipped on {conditionSkipped:N0} row(s) whose habitat doesn't offer it (or has no habitat yet).");
        }

        if (roleSkipped > 0)
        {
            bulkNotes.Add(toBaseline
                ? $"{roleSkipped:N0} new floor(s) have no baseline habitat, so habitat/condition were skipped there."
                : $"{roleSkipped:N0} retained or lost floor(s) have no proposed habitat, so habitat/condition were skipped there.");
        }

        StatusText.Text = string.Join(" ", bulkNotes);
    }

    private void BulkHabitatBox_TextChanged(AutoSuggestBox sender, AutoSuggestBoxTextChangedEventArgs args)
    {
        if (args.Reason != AutoSuggestionBoxTextChangeReason.UserInput)
        {
            return;
        }

        _bulkHabitat = null;
        var pool = ReferenceEquals(BulkTargetBox.SelectedItem, BulkTargetBaseline)
            ? _catalog.BaselineHabitats
            : _catalog.CreatableHabitats.Concat(_catalog.EnhancementHabitats).Distinct();
        sender.ItemsSource = SearchHabitats(sender.Text, pool).Select(habitat => new BngHabitatOption(habitat)).ToList();
    }

    private void BulkHabitatBox_SuggestionChosen(AutoSuggestBox sender, AutoSuggestBoxSuggestionChosenEventArgs args)
    {
        if (args.SelectedItem is BngHabitatOption option)
        {
            _bulkHabitat = option.Habitat;
        }
    }

    private void BulkSetYearOffset_Changed(object sender, RoutedEventArgs e) =>
        BulkYearOffsetBox.IsEnabled = BulkSetYearOffsetCheckBox.IsChecked == true;

    private async void Write_Click(object sender, RoutedEventArgs e)
    {
        // Guessed habitats are never written silently: ask whether to confirm them in the same step.
        var guessed = AllRows.Where(row => row.IsAutoMatched && row.HasAnyInput).ToList();
        if (guessed.Count > 0)
        {
            var dialog = new ContentDialog
            {
                XamlRoot = XamlRoot,
                Title = "Confirm guessed habitats?",
                Content = $"{guessed.Count:N0} floor(s) still have a habitat guessed from the family/type name (the Auto-matched card). " +
                          "Confirm those guesses and write them too, or write only the floors you've already confirmed?",
                PrimaryButtonText = "Confirm and write all",
                SecondaryButtonText = "Write confirmed only",
                CloseButtonText = "Cancel",
                DefaultButton = ContentDialogButton.Primary
            };

            var choice = await dialog.ShowAsync();
            if (choice == ContentDialogResult.None)
            {
                return;
            }

            if (choice == ContentDialogResult.Primary)
            {
                foreach (var row in guessed)
                {
                    row.ConfirmAutoMatch();
                }
            }
        }

        var autoMatched = AllRows.Count(row => row.IsAutoMatched);
        var toWrite = AllRows.Where(row => !row.IsAutoMatched && row.HasAnyInput && row.NeedsWrite).ToList();
        if (toWrite.Count == 0)
        {
            StatusText.Text = autoMatched > 0
                ? $"Nothing to write — {autoMatched:N0} auto-matched row(s) need confirming first."
                : "Nothing to write — every floor with BNG inputs is already up to date in Revit.";
            return;
        }

        var (mappings, conflictingTypes) = BuildTypeMappings(toWrite);

        await RunBusyAsync(async () =>
        {
            var request = new WriteBngFloorsRequest(
                toWrite.Select(row => new BngFloorWrite(row.UniqueId, row.BuildValues())).ToList(),
                mappings);
            var result = await GetClient().SendAsync<WriteBngFloorsResult>(PipeCommands.WriteBngFloors, request);

            var written = result.WrittenUniqueIds.ToHashSet(StringComparer.Ordinal);
            var failures = result.Failures.ToDictionary(failure => failure.UniqueId, failure => failure.Error, StringComparer.Ordinal);
            foreach (var row in toWrite)
            {
                if (written.Contains(row.UniqueId))
                {
                    row.MarkWritten(result.LastUpdated);
                }
                else
                {
                    row.MarkWriteFailed(failures.GetValueOrDefault(row.UniqueId, "Revit didn't report this floor as written."));
                }
            }

            var mappedTypes = mappings.ToDictionary(mapping => mapping.TypeId, mapping => mapping.Habitat);
            foreach (var row in AllRows.Where(row => mappedTypes.ContainsKey(row.TypeId)))
            {
                row.TypeHabitatMapping = mappedTypes[row.TypeId];
            }

            var notes = new List<string> { $"Wrote {written.Count:N0} floor(s) to Revit." };
            if (failures.Count > 0)
            {
                var reasons = failures.Values.Distinct(StringComparer.Ordinal).ToList();
                notes.Add(reasons.Count == 1
                    ? $"{failures.Count:N0} failed: {reasons[0]}"
                    : $"{failures.Count:N0} failed ({reasons.Count} different reasons — see each Failed row's status).");
            }

            if (mappings.Count > 0)
            {
                notes.Add($"Remembered the habitat for {mappings.Count:N0} floor type(s).");
            }

            if (conflictingTypes > 0)
            {
                notes.Add($"{conflictingTypes:N0} floor type(s) weren't remembered because their floors use different habitats.");
            }

            if (autoMatched > 0)
            {
                notes.Add($"{autoMatched:N0} auto-matched row(s) were skipped until confirmed.");
            }

            StatusText.Text = string.Join(" ", notes);
            UpdateCounts();
        });
    }

    /// <summary>
    /// One mapping per floor type whose written floors all share a habitat that differs from what
    /// the type already remembers. Types split across several habitats are left alone (and counted).
    /// </summary>
    private (IReadOnlyList<BngTypeMappingWrite> Mappings, int ConflictingTypes) BuildTypeMappings(IReadOnlyList<BngFloorRow> rows)
    {
        if (RememberTypeCheckBox.IsChecked != true)
        {
            return ([], 0);
        }

        var mappings = new List<BngTypeMappingWrite>();
        var conflicts = 0;
        foreach (var group in rows.Where(row => row.MappedHabitat is not null && row.TypeId >= 0).GroupBy(row => row.TypeId))
        {
            var habitats = group.Select(row => row.MappedHabitat!.Description).Distinct(StringComparer.Ordinal).ToList();
            if (habitats.Count > 1)
            {
                conflicts++;
            }
            else if (!string.Equals(group.First().TypeHabitatMapping, habitats[0], StringComparison.Ordinal))
            {
                mappings.Add(new BngTypeMappingWrite(group.Key, habitats[0]));
            }
        }

        return (mappings, conflicts);
    }

    // ---- Sideways scrolling of the wide A-2 table ----

    private void ScrollLeft_Click(object sender, RoutedEventArgs e) =>
        TableScroller.ChangeView(Math.Max(0, TableScroller.HorizontalOffset - TableScroller.ViewportWidth * 0.8), null, null);

    private void ScrollRight_Click(object sender, RoutedEventArgs e) =>
        TableScroller.ChangeView(Math.Min(TableScroller.ScrollableWidth, TableScroller.HorizontalOffset + TableScroller.ViewportWidth * 0.8), null, null);

    private void ColumnScrollSlider_ValueChanged(object sender, Microsoft.UI.Xaml.Controls.Primitives.RangeBaseValueChangedEventArgs e)
    {
        if (!_syncingColumnScroll)
        {
            TableScroller.ChangeView(e.NewValue, null, null, disableAnimation: true);
        }
    }

    private void TableScroller_ViewChanged(object? sender, ScrollViewerViewChangedEventArgs e) => UpdateColumnScrollRange();

    private void TableScroller_SizeChanged(object sender, SizeChangedEventArgs e) => UpdateColumnScrollRange();

    /// <summary>Shift + mouse wheel scrolls the table sideways, as in Excel.</summary>
    private void TableScroller_PointerWheelChanged(object sender, PointerRoutedEventArgs e)
    {
        if (!e.KeyModifiers.HasFlag(VirtualKeyModifiers.Shift))
        {
            return;
        }

        var delta = e.GetCurrentPoint(TableScroller).Properties.MouseWheelDelta;
        TableScroller.ChangeView(Math.Clamp(TableScroller.HorizontalOffset - delta, 0, TableScroller.ScrollableWidth), null, null, disableAnimation: true);
        e.Handled = true;
    }

    private void UpdateColumnScrollRange()
    {
        _syncingColumnScroll = true;
        try
        {
            var scrollable = TableScroller.ScrollableWidth;
            ColumnScrollSlider.Maximum = Math.Max(1, scrollable);
            ColumnScrollSlider.Value = TableScroller.HorizontalOffset;
            ColumnScrollSlider.IsEnabled = scrollable > 0;
            ColumnScrollText.Text = scrollable <= 0
                ? "All columns fit"
                : $"{Math.Round(100 * TableScroller.HorizontalOffset / scrollable):0}% across · Shift + wheel scrolls";
        }
        finally
        {
            _syncingColumnScroll = false;
        }
    }

    // ---- Filtering & counts ----

    /// <summary>Clicking the active card again clears the filter; clicking a different one switches to it.</summary>
    private void StatusCard_Tapped(object sender, TappedRoutedEventArgs e)
    {
        var status = (string)((FrameworkElement)sender).Tag;
        _activeFilter = _activeFilter == status ? null : status;
        ApplyFilter();
    }

    private void ApplyFilter()
    {
        FilteredRows.Clear();
        foreach (var row in AllRows.Where(row => _activeFilter is null || row.Status == _activeFilter))
        {
            FilteredRows.Add(row);
        }

        foreach (var card in StatusCards())
        {
            if (_activeFilter is not null && (string)card.Tag == _activeFilter)
            {
                card.BorderBrush = (Brush)Application.Current.Resources["LimAccentBrush"];
                card.BorderThickness = new Thickness(2);
            }
            else
            {
                card.ClearValue(Border.BorderBrushProperty);
                card.ClearValue(Border.BorderThicknessProperty);
            }
        }

        UpdateCounts();
    }

    private void UpdateCounts()
    {
        int Count(string status) => AllRows.Count(row => row.Status == status);
        CalculatedCountText.Text = Count(BngFloorStatus.Calculated).ToString("N0");
        ReadyCountText.Text = Count(BngFloorStatus.Ready).ToString("N0");
        AutoMatchedCountText.Text = Count(BngFloorStatus.AutoMatched).ToString("N0");
        NeedsInfoCountText.Text = Count(BngFloorStatus.NeedsInfo).ToString("N0");
        CheckDataCountText.Text = Count(BngFloorStatus.CheckData).ToString("N0");
        StaleCountText.Text = Count(BngFloorStatus.Stale).ToString("N0");
        FailedCountText.Text = Count(BngFloorStatus.Failed).ToString("N0");

        // The metric's on-site headline over the loaded rows: baseline = A-1 totals, post-intervention
        // = retained + enhanced + created units. Rows still missing inputs contribute nothing yet.
        var baseline = AllRows.Where(row => row.IsBaselineRow).Sum(row => row.Result.BaselineUnits ?? 0);
        var post = AllRows.Sum(row => row.Result.HabitatUnits ?? 0);
        var hasBaseline = AllRows.Any(row => row.IsBaselineRow && row.Result.BaselineUnits is not null);
        var hasAny = AllRows.Any(row => row.Result.HabitatUnits is not null) || hasBaseline;
        TotalUnitsText.Text = !hasAny
            ? string.Empty
            : hasBaseline && baseline > 0
                ? string.Format(CultureInfo.InvariantCulture, "Baseline {0:0.00} → post-intervention {1:0.00} habitat units ({2:+0.00;-0.00} units, {3:+0.00;-0.00}%)",
                    baseline, post, post - baseline, (post - baseline) / baseline * 100d)
                : string.Format(CultureInfo.InvariantCulture, "{0:0.00} post-intervention habitat units — no baseline (Existing-phase) floors loaded", post);

        FilterStatusText.Text = AllRows.Count == 0
            ? "Load floors to start — click a card above to filter the list to just that status."
            : _activeFilter is null
                ? $"Showing all {AllRows.Count:N0} floor(s) — click a card above to filter the list to just that status."
                : $"Showing {FilteredRows.Count:N0} {DisplayStatus(_activeFilter)} floor(s) — click the card again to show all. (The list refreshes when you click a card, so rows you fix stay in view.)";

        var guessedCount = AllRows.Count(row => row.IsAutoMatched);
        ConfirmAutoMatchesButton.IsEnabled = guessedCount > 0;
        ConfirmAutoMatchesButton.Content = guessedCount > 0 ? $"✓ Confirm {guessedCount:N0} auto-match{(guessedCount == 1 ? "" : "es")}" : "✓ Confirm auto-matches";

        // Auto-matched rows count too: clicking Write offers to confirm them (see Write_Click).
        WriteButton.IsEnabled = AllRows.Any(row => row.HasAnyInput && (row.IsAutoMatched || row.NeedsWrite));
    }

    private static string DisplayStatus(string status) => status switch
    {
        BngFloorStatus.AutoMatched => "auto-matched",
        BngFloorStatus.NeedsInfo => "needs-info",
        BngFloorStatus.CheckData => "check-data",
        BngFloorStatus.Ready => "ready-to-write",
        _ => status.ToLowerInvariant()
    };

    private IEnumerable<Border> StatusCards() =>
        [CalculatedCard, ReadyCard, AutoMatchedCard, NeedsInfoCard, CheckDataCard, StaleCard, FailedCard];

    /// <summary>The selected rows, or every shown row when nothing is selected.</summary>
    private List<BngFloorRow> GetTargetRows() =>
        RowsList.SelectedItems.Count > 0
            ? RowsList.SelectedItems.Cast<BngFloorRow>().ToList()
            : FilteredRows.ToList();

    private async Task RunBusyAsync(Func<Task> operation)
    {
        BusyIndicator.IsActive = true;
        BusyIndicator.Visibility = Visibility.Visible;
        try
        {
            await operation();
        }
        catch (Exception exception)
        {
            StatusText.Text = $"Failed: {exception.Message}";
        }
        finally
        {
            BusyIndicator.IsActive = false;
            BusyIndicator.Visibility = Visibility.Collapsed;
            UpdateCounts();
        }
    }

    private RevitPipeClient GetClient() =>
        _getClient?.Invoke() ?? throw new InvalidOperationException("The Revit connection has not been initialized.");
}
