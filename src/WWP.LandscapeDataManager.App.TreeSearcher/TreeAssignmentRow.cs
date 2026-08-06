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

    public SolidColorBrush MatchStatusBrush => MatchStatus switch
    {
        "Auto-matched" => new SolidColorBrush(Colors.SeaGreen),
        "Assigned" => new SolidColorBrush(Colors.SeaGreen),
        "Manually selected" => new SolidColorBrush(Colors.SteelBlue),
        "No match found" => new SolidColorBrush(Colors.DarkOrange),
        _ => new SolidColorBrush(Colors.Gray)
    };

    public event PropertyChangedEventHandler? PropertyChanged;

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
}
