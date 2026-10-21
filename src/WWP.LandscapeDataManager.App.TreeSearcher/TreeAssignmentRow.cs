using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using Microsoft.UI;
using Microsoft.UI.Xaml.Media;
using WWP.LandscapeDataManager.Contracts;

namespace WWP.LandscapeDataManager.App.TreeSearcher;

/// <summary>One distinct selected Planting type, with whatever species match (automatic or manual) is currently chosen for it.</summary>
public sealed class TreeAssignmentRow(SelectedPlantingTypeItem item) : INotifyPropertyChanged
{
    public const string SavedMatch = "Saved match";
    public const string RememberedMatch = "Remembered match";
    public const string AutoMatched = "Auto-matched";
    public const string ManuallySelected = "Manually selected";
    public const string Assigned = "Assigned";
    public const string NoMatch = "No match found";

    private string? _currentSpeciesCode = item.CurrentSpeciesCode;
    private SpeciesCatalogueRecord? _selectedSpecies;
    private string _matchStatus = "Searching…";
    private string _searchQuery = string.Empty;

    public string UniqueId { get; } = item.UniqueId;
    public string FamilyName { get; } = item.FamilyName;
    public string TypeName { get; } = item.TypeName;
    public string DisplayName => $"{FamilyName} : {TypeName}";

    public ObservableCollection<SpeciesRow> SearchResults { get; } = [];

    public string? CurrentSpeciesCode
    {
        get => _currentSpeciesCode;
        set
        {
            if (_currentSpeciesCode != value)
            {
                _currentSpeciesCode = value;
                OnPropertyChanged();
            }
        }
    }

    public SpeciesCatalogueRecord? SelectedSpecies
    {
        get => _selectedSpecies;
        set
        {
            if (!Equals(_selectedSpecies, value))
            {
                _selectedSpecies = value;
                OnPropertyChanged();
            }
        }
    }

    public string MatchStatus
    {
        get => _matchStatus;
        set
        {
            if (_matchStatus != value)
            {
                _matchStatus = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(MatchStatusBrush));
            }
        }
    }

    public string SearchQuery
    {
        get => _searchQuery;
        set
        {
            if (_searchQuery != value)
            {
                _searchQuery = value;
                OnPropertyChanged();
            }
        }
    }

    /// <summary>A pick made by hand, now or in an earlier session: remembered when it's assigned.</summary>
    public bool IsManualMatch => MatchStatus is ManuallySelected or RememberedMatch;

    public SolidColorBrush MatchStatusBrush => MatchStatus switch
    {
        SavedMatch => new SolidColorBrush(Colors.SeaGreen),
        AutoMatched => new SolidColorBrush(Colors.SeaGreen),
        Assigned => new SolidColorBrush(Colors.SeaGreen),
        RememberedMatch => new SolidColorBrush(Colors.Teal),
        ManuallySelected => new SolidColorBrush(Colors.SteelBlue),
        NoMatch => new SolidColorBrush(Colors.DarkOrange),
        _ => new SolidColorBrush(Colors.Gray)
    };

    public event PropertyChangedEventHandler? PropertyChanged;

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
}
