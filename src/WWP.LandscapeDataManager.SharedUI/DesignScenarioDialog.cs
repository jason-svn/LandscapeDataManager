using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using WWP.LandscapeDataManager.Contracts;
using WWP.LandscapeDataManager.Shared.Services;

namespace WWP.LandscapeDataManager.SharedUI;

/// <summary>What the user did in <see cref="DesignScenarioDialog"/>.</summary>
internal enum DesignScenarioDialogAction
{
    Cancel,
    Save,
    ShowWithoutSaving,
    Delete
}

internal sealed record DesignScenarioDialogResult(DesignScenarioDialogAction Action, DesignScenario Scenario);

/// <summary>
/// Names a design scenario (Scheme A, Scheme B, …) and picks one option per design option set:
/// the editor behind the Dashboard's and the BNG tab's Edit…. Saving is the caller's job, onto the
/// project's settings (<c>ProjectSettingsSync</c>), so every tool offers the same schemes.
/// </summary>
internal static class DesignScenarioDialog
{
    /// <param name="current">The scenario to start from: its options are preselected.</param>
    /// <param name="editingSaved">True when <paramref name="current"/> is a saved scheme: keeps its name and offers Delete.</param>
    /// <param name="suggestedName">The name for a new scheme.</param>
    /// <param name="readActiveView">Asks Revit which option of each set the active view shows; null hides that button.</param>
    public static async Task<DesignScenarioDialogResult> ShowAsync(
        XamlRoot xamlRoot,
        DesignScenario current,
        bool editingSaved,
        IReadOnlyList<DesignOptionSetItem> sets,
        string suggestedName,
        Func<Task<ViewDesignScenarioResult?>>? readActiveView = null)
    {
        var nameBox = new TextBox
        {
            Header = "Scenario name",
            Text = editingSaved ? current.Name : suggestedName,
            PlaceholderText = "e.g. Scheme A"
        };
        var preview = new TextBlock { TextWrapping = TextWrapping.Wrap, Opacity = 0.75, FontSize = 12 };
        var optionBoxes = new List<(DesignOptionSetItem Set, ComboBox Box)>();
        var content = new StackPanel { Spacing = 12, MinWidth = 420 };
        content.Children.Add(nameBox);

        DesignScenario Picked() => new(
            string.IsNullOrWhiteSpace(nameBox.Text) ? "Unnamed scenario" : nameBox.Text.Trim(),
            optionBoxes.ToDictionary(pair => pair.Set.SetName, pair => pair.Box.SelectedItem as string ?? DesignScenarios.PrimaryOptionOf(pair.Set), StringComparer.Ordinal));
        void UpdatePreview() => preview.Text = DesignScenarios.Describe(Picked(), sets);

        if (sets.Count == 0)
        {
            content.Children.Add(new TextBlock
            {
                Text = "This model has no design options, so there is one scenario: the main model.",
                TextWrapping = TextWrapping.Wrap
            });
        }

        foreach (var set in sets)
        {
            var box = new ComboBox { Header = set.SetName, HorizontalAlignment = HorizontalAlignment.Stretch };
            foreach (var option in set.Options)
            {
                box.Items.Add(option.Name);
            }

            box.SelectedItem = DesignScenarios.ChosenOption(current, set);
            box.SelectionChanged += (_, _) => UpdatePreview();
            optionBoxes.Add((set, box));
            content.Children.Add(box);
        }

        if (readActiveView is not null)
        {
            var viewButton = new Button { Content = "Fill from Revit's active view", IsEnabled = sets.Count > 0 };
            ToolTipService.SetToolTip(viewButton, "Choose, for every set, the option the active view shows.");
            viewButton.Click += async (_, _) =>
            {
                if (await readActiveView() is not { } view)
                {
                    preview.Text = "Couldn't read the active view from Revit.";
                    return;
                }

                foreach (var (set, box) in optionBoxes)
                {
                    if (view.Choices.TryGetValue(set.SetName, out var option) && box.Items.Contains(option))
                    {
                        box.SelectedItem = option;
                    }
                }

                UpdatePreview();
                if (view.Notes.Count > 0)
                {
                    preview.Text += Environment.NewLine + string.Join(Environment.NewLine, view.Notes);
                }
            };
            content.Children.Add(viewButton);
        }

        content.Children.Add(preview);

        var deleteRequested = false;
        ContentDialog? dialog = null;
        if (editingSaved)
        {
            var deleteButton = new HyperlinkButton { Content = $"Delete saved scenario \"{current.Name}\"" };
            deleteButton.Click += (_, _) =>
            {
                deleteRequested = true;
                dialog?.Hide();
            };
            content.Children.Add(deleteButton);
        }

        UpdatePreview();
        dialog = new ContentDialog
        {
            XamlRoot = xamlRoot,
            Title = "Design scenario",
            Content = new ScrollViewer { Content = content, MaxHeight = 520 },
            PrimaryButtonText = "Save and show",
            SecondaryButtonText = "Show without saving",
            CloseButtonText = "Cancel",
            DefaultButton = ContentDialogButton.Primary
        };

        var result = await dialog.ShowAsync();
        if (deleteRequested)
        {
            return new DesignScenarioDialogResult(DesignScenarioDialogAction.Delete, current);
        }

        var picked = Picked();
        return result switch
        {
            ContentDialogResult.Primary => new DesignScenarioDialogResult(DesignScenarioDialogAction.Save,
                // "Primary options" is the built-in scenario, not a name to save under.
                string.Equals(picked.Name, DesignScenarios.PrimaryScenarioName, StringComparison.CurrentCultureIgnoreCase) ? picked with { Name = suggestedName } : picked),
            ContentDialogResult.Secondary => new DesignScenarioDialogResult(DesignScenarioDialogAction.ShowWithoutSaving, picked),
            _ => new DesignScenarioDialogResult(DesignScenarioDialogAction.Cancel, current)
        };
    }

    /// <summary>The first "Scheme A", "Scheme B", … not saved yet.</summary>
    public static string NextSchemeName(IReadOnlyList<DesignScenario> saved)
    {
        for (var index = 1; ; index++)
        {
            var name = $"Scheme {(char)('A' + (index - 1) % 26)}{(index > 26 ? (index / 26).ToString() : string.Empty)}";
            if (!saved.Any(scenario => string.Equals(scenario.Name, name, StringComparison.CurrentCultureIgnoreCase)))
            {
                return name;
            }
        }
    }
}
