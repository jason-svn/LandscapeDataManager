using System.IO;
using System.Reflection;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Windows.Storage.Pickers;
using WWP.LandscapeDataManager.Contracts;
using WWP.LandscapeDataManager.Shared.Services;

namespace WWP.LandscapeDataManager.App.Settings;

/// <summary>Export settings… / Import settings…: the shareable <c>.limsettings</c> file (see <see cref="LimSettingsFile"/>).</summary>
public sealed partial class MainPage
{
    private async void ExportSettings_Click(object sender, RoutedEventArgs e)
    {
        var includeSecrets = new CheckBox { Content = "Include the i-Tree API key and Airtable token" };
        var options = new StackPanel { Spacing = 10 };
        options.Children.Add(new TextBlock
        {
            TextWrapping = TextWrapping.Wrap,
            Text = "Saves this project's units and currency, data sources, shared parameter file path, and Excel Importer " +
                   "column mappings, type aliases and instance matching. The site location isn't included."
        });
        options.Children.Add(includeSecrets);
        options.Children.Add(new TextBlock
        {
            TextWrapping = TextWrapping.Wrap,
            Opacity = 0.75,
            Text = "Keys are saved as plain text. Anyone who gets the file can use your Airtable token to read your bases " +
                   "— only include them for people you'd give the token to anyway."
        });

        var dialog = new ContentDialog
        {
            XamlRoot = XamlRoot,
            Title = "Export settings",
            Content = options,
            PrimaryButtonText = "Export…",
            CloseButtonText = "Cancel",
            DefaultButton = ContentDialogButton.Primary
        };
        if (await dialog.ShowAsync() != ContentDialogResult.Primary)
        {
            return;
        }

        try
        {
            var snapshot = await ProjectSettingsSync.PullAsync(GetClient());
            var catalog = await GetClient().SendAsync<ParameterCatalogResult>(PipeCommands.GetParameterCatalog, new ModelScanOptions());
            var secrets = includeSecrets.IsChecked == true
                ? new LimSettingsSecrets(NullIfBlank(ITreeApiKeyBox.Password), NullIfBlank(AirtableTokenBox.Password))
                : null;
            var file = LimSettingsFileFormat.Build(
                snapshot,
                catalog.PreferredUnitSystem,
                catalog.PreferredCurrency,
                catalog.DocumentTitle,
                Assembly.GetExecutingAssembly().GetName().Version?.ToString(3),
                secrets,
                DateTimeOffset.Now);

            var picker = new FileSavePicker
            {
                SuggestedStartLocation = PickerLocationId.DocumentsLibrary,
                SuggestedFileName = $"{SafeFileName(catalog.DocumentTitle)} LIM settings"
            };
            picker.FileTypeChoices.Add("LIM settings", [LimSettingsFileFormat.FileExtension]);
            WinRT.Interop.InitializeWithWindow.Initialize(picker, _windowHandle);
            var target = await picker.PickSaveFileAsync();
            if (target is null)
            {
                return;
            }

            await File.WriteAllTextAsync(target.Path, LimSettingsFileFormat.Serialize(file));
            SettingsFileStatusText.Text = file.Secrets is null
                ? $"Exported settings to {target.Name}."
                : $"Exported settings, including keys, to {target.Name}. Share it only with people who should have those keys.";
        }
        catch (Exception exception)
        {
            SettingsFileStatusText.Text = $"Export failed: {exception.Message}";
        }
    }

    private async void ImportSettings_Click(object sender, RoutedEventArgs e)
    {
        var picker = new FileOpenPicker { ViewMode = PickerViewMode.List, SuggestedStartLocation = PickerLocationId.DocumentsLibrary };
        picker.FileTypeFilter.Add(LimSettingsFileFormat.FileExtension);
        WinRT.Interop.InitializeWithWindow.Initialize(picker, _windowHandle);
        var source = await picker.PickSingleFileAsync();
        if (source is null)
        {
            return;
        }

        LimSettingsFile file;
        try
        {
            file = LimSettingsFileFormat.Parse(await File.ReadAllTextAsync(source.Path));
        }
        catch (Exception exception) when (exception is InvalidDataException or IOException or UnauthorizedAccessException)
        {
            SettingsFileStatusText.Text = $"Couldn't import {source.Name}: {exception.Message}";
            return;
        }

        var settings = file.Settings;
        var choices = new List<(CheckBox Box, Func<Task<string>> Apply)>();

        void Offer(bool present, string label, string detail, Func<Task<string>> apply)
        {
            if (present)
            {
                choices.Add((new CheckBox { Content = $"{label} — {detail}", IsChecked = true }, apply));
            }
        }

        var unitSystem = settings.PreferredUnitSystem;
        var currency = settings.PreferredCurrency;
        var fixedRate = settings.ExchangeRateOverride is { } fileRate && currency is not null && fileRate.RateFor(currency) is not null
            ? fileRate
            : new ExchangeRateOverride(false, currency ?? "USD", 0);
        Offer(unitSystem is not null && currency is not null,
            "Units & currency",
            $"{unitSystem}, {currency}" +
            (fixedRate.Enabled ? $" at a fixed 1 USD = {fixedRate.UsdRate:G6} {currency}{(fixedRate.Note is null ? string.Empty : $" ({fixedRate.Note})")}" : " at today's rate") +
            " (rescales stored cost results)",
            async () => { await PublishUnitsAndCurrencyAsync(unitSystem!, currency!, fixedRate); return "units & currency"; });
        Offer(settings.WwpLdsSource is not null,
            "Landscape data sheet source", settings.WwpLdsSource?.BaseId ?? string.Empty,
            async () => { await ProjectSettingsSync.PushAsync(GetClient(), wwpLdsSource: settings.WwpLdsSource); return "data sheet source"; });
        Offer(settings.AirtableApi is not null || settings.DataSource is not null,
            "Planting data source", settings.AirtableApi?.BaseId ?? settings.DataSource?.Kind.ToString() ?? string.Empty,
            async () =>
            {
                await ProjectSettingsSync.PushAsync(GetClient(), dataSource: settings.DataSource, airtableApi: settings.AirtableApi);
                return "planting data source";
            });
        Offer(settings.ParameterMappings is { Count: > 0 } || settings.TypeAliases is { Count: > 0 } || settings.InstanceMatchKey is not null,
            "Excel Importer setup",
            $"{settings.ParameterMappings?.Count ?? 0} column mapping(s), {settings.TypeAliases?.Count ?? 0} type alias(es)" +
            (settings.InstanceMatchKey is null ? string.Empty : ", instance matching"),
            async () =>
            {
                await ProjectSettingsSync.PushAsync(
                    GetClient(),
                    parameterMappings: settings.ParameterMappings,
                    typeAliases: settings.TypeAliases,
                    instanceMatchKey: settings.InstanceMatchKey);
                return "Importer setup";
            });
        Offer(!string.IsNullOrWhiteSpace(settings.SharedParameterFilePath),
            "Shared parameter file path", $"{settings.SharedParameterFilePath} (a path on the exporter's computer)",
            async () =>
            {
                await ProjectSettingsSync.PushAsync(GetClient(), sharedParameterFilePath: settings.SharedParameterFilePath);
                return "shared parameter file path";
            });
        Offer(!string.IsNullOrWhiteSpace(file.Secrets?.ITreeApiKey),
            "i-Tree API key", "saved to your Windows Credential Manager",
            () => { _iTreeCredentialStore.Save(file.Secrets!.ITreeApiKey!.Trim()); return Task.FromResult("i-Tree API key"); });
        Offer(!string.IsNullOrWhiteSpace(file.Secrets?.AirtableToken),
            "Airtable token", "saved to your Windows Credential Manager",
            () => { _airtableCredentialStore.Save(file.Secrets!.AirtableToken!.Trim()); return Task.FromResult("Airtable token"); });

        if (choices.Count == 0)
        {
            SettingsFileStatusText.Text = $"{source.Name} doesn't contain any settings to import.";
            return;
        }

        var list = new StackPanel { Spacing = 8 };
        list.Children.Add(new TextBlock
        {
            TextWrapping = TextWrapping.Wrap,
            Text = $"Exported {file.ExportedAt.LocalDateTime:g}" +
                   (string.IsNullOrWhiteSpace(file.ExportedFromProject) ? string.Empty : $" from \"{file.ExportedFromProject}\"") +
                   ". Choose what to apply to this project — ticked items replace what's there now."
        });
        foreach (var (box, _) in choices)
        {
            list.Children.Add(box);
        }

        var dialog = new ContentDialog
        {
            XamlRoot = XamlRoot,
            Title = "Import settings",
            Content = new ScrollViewer { Content = list, MaxHeight = 420 },
            PrimaryButtonText = "Import",
            CloseButtonText = "Cancel",
            DefaultButton = ContentDialogButton.Primary
        };
        if (await dialog.ShowAsync() != ContentDialogResult.Primary)
        {
            return;
        }

        var applied = new List<string>();
        try
        {
            SettingsFileStatusText.Text = "Importing…";
            foreach (var (box, apply) in choices.Where(choice => choice.Box.IsChecked == true))
            {
                applied.Add(await apply());
            }

            await LoadAsync();
            SettingsFileStatusText.Text = applied.Count == 0
                ? "Nothing selected, nothing imported."
                : $"Imported from {source.Name}: {string.Join(", ", applied)}.";
        }
        catch (Exception exception)
        {
            await LoadAsync();
            SettingsFileStatusText.Text = applied.Count == 0
                ? $"Import failed: {exception.Message}"
                : $"Imported {string.Join(", ", applied)}, then failed: {exception.Message}";
        }
    }

    private static string? NullIfBlank(string value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static string SafeFileName(string name)
    {
        var cleaned = string.Concat(name.Split(Path.GetInvalidFileNameChars())).Trim();
        return cleaned.Length == 0 ? "Project" : cleaned;
    }
}
