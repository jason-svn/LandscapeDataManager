using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Windows.Storage.Pickers;
using WWP.LandscapeDataManager.Shared.Services;

namespace WWP.LandscapeDataManager.App.Settings;

public sealed partial class MainPage : Page
{
    private readonly ITreeCredentialStore _iTreeCredentialStore = new();
    private readonly AirtableCredentialStore _airtableCredentialStore = new();
    private readonly WwpLdsAirtableSettingsStore _ldsSettingsStore = new();
    private readonly SharedParameterFileSettingsStore _sharedParameterFileSettingsStore = new();

    private RevitPipeClient? _revitClient;
    private nint _windowHandle;

    public MainPage()
    {
        InitializeComponent();
    }

    public void Initialize(string pipeName, nint windowHandle)
    {
        _revitClient = new RevitPipeClient(pipeName);
        _windowHandle = windowHandle;
        Loaded += Page_Loaded;
    }

    private async void Page_Loaded(object sender, RoutedEventArgs e)
    {
        ITreeApiKeyBox.Password = _iTreeCredentialStore.Load();
        AirtableTokenBox.Password = _airtableCredentialStore.Load();

        var ldsSettings = await _ldsSettingsStore.LoadAsync();
        LdsBaseIdBox.Text = ldsSettings.BaseId;
        LdsTableIdBox.Text = ldsSettings.TableIdOrName;
        LdsViewIdBox.Text = ldsSettings.ViewName ?? string.Empty;

        var sharedParameterSettings = await _sharedParameterFileSettingsStore.LoadAsync();
        SharedParameterFilePathBox.Text = sharedParameterSettings.FilePath;
    }

    private void SaveITreeKey_Click(object sender, RoutedEventArgs e)
    {
        var apiKey = ITreeApiKeyBox.Password.Trim();
        if (RememberITreeKeyCheckBox.IsChecked == true)
        {
            _iTreeCredentialStore.Save(apiKey);
            ITreeStatusText.Text = string.IsNullOrWhiteSpace(apiKey)
                ? "No i-Tree API key saved."
                : "i-Tree API key saved — every LIM tool that needs it will now read it from here.";
        }
        else
        {
            _iTreeCredentialStore.Delete();
            ITreeStatusText.Text = "i-Tree API key cleared (not saved for this Windows user).";
        }
    }

    private void ClearITreeKey_Click(object sender, RoutedEventArgs e)
    {
        _iTreeCredentialStore.Delete();
        ITreeApiKeyBox.Password = string.Empty;
        ITreeStatusText.Text = "i-Tree API key cleared.";
    }

    private void SaveAirtableToken_Click(object sender, RoutedEventArgs e)
    {
        var token = AirtableTokenBox.Password.Trim();
        if (RememberAirtableTokenCheckBox.IsChecked == true)
        {
            _airtableCredentialStore.Save(token);
            AirtableStatusText.Text = string.IsNullOrWhiteSpace(token)
                ? "No Airtable personal access token saved."
                : "Airtable personal access token saved — Importer, Refresh & Audit, and Floor Calculator will now read it from here.";
        }
        else
        {
            _airtableCredentialStore.Delete();
            AirtableStatusText.Text = "Airtable personal access token cleared (not saved for this Windows user).";
        }
    }

    private void ClearAirtableToken_Click(object sender, RoutedEventArgs e)
    {
        _airtableCredentialStore.Delete();
        AirtableTokenBox.Password = string.Empty;
        AirtableStatusText.Text = "Airtable personal access token cleared.";
    }

    private async void SaveLdsSettings_Click(object sender, RoutedEventArgs e)
    {
        var settings = new WwpLdsAirtableSettings(
            LdsBaseIdBox.Text.Trim(),
            LdsTableIdBox.Text.Trim(),
            string.IsNullOrWhiteSpace(LdsViewIdBox.Text) ? null : LdsViewIdBox.Text.Trim());
        await _ldsSettingsStore.SaveAsync(settings);
        LdsStatusText.Text = "WWP landscape data sheet source saved — Floor Calculator will use it on its next sync.";
    }

    private async void BrowseSharedParameterFile_Click(object sender, RoutedEventArgs e)
    {
        var picker = new FileOpenPicker { ViewMode = PickerViewMode.List, SuggestedStartLocation = PickerLocationId.DocumentsLibrary };
        picker.FileTypeFilter.Add(".txt");
        WinRT.Interop.InitializeWithWindow.Initialize(picker, _windowHandle);
        var file = await picker.PickSingleFileAsync();
        if (file is not null)
        {
            SharedParameterFilePathBox.Text = file.Path;
        }
    }

    private async void SaveSharedParameterFile_Click(object sender, RoutedEventArgs e)
    {
        await _sharedParameterFileSettingsStore.SaveAsync(new SharedParameterFileSettings(SharedParameterFilePathBox.Text.Trim()));
        SharedParameterFileStatusText.Text = "Shared parameter file path saved — Shared Parameter Setup will default to it next time.";
    }

    public async ValueTask DisposeAsync()
    {
        if (_revitClient is not null)
        {
            await _revitClient.DisposeAsync();
        }
    }
}
