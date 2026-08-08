using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using Microsoft.UI;
using Microsoft.UI.Xaml.Media;
using WWP.LandscapeDataManager.Contracts;

namespace WWP.LandscapeDataManager.App.FloorCalculator;

/// <summary>One selected Floor instance, with whatever landscape data sheet match (automatic or manual) is currently chosen for it.</summary>
public sealed class FloorAssignmentRow(SelectedFloorItem item) : INotifyPropertyChanged
{
    private WwpLdsCoefficientRecord? _selectedRecord;
    private string _matchStatus = "Searching…";
    private string _searchQuery = string.Empty;

    public string UniqueId { get; } = item.UniqueId;
    public string FamilyName { get; } = item.FamilyName;
    public string TypeName { get; } = item.TypeName;
    public double AreaSquareMeters { get; } = item.AreaSquareMeters;
    public string DisplayName => $"{FamilyName} : {TypeName}";
    public string AreaDisplay => $"{AreaSquareMeters:N1} m²";
    public string? CurrentLdsType { get; } = item.CurrentLdsType;

    public ObservableCollection<CoefficientRow> SearchResults { get; } = [];

    /// <summary>Fixed 8 metrics in a stable order: CO2, Runoff, PollutionMassRemoved, CostSaved, Oxygen, GWP, SurfaceTemp, AirTemp (see the named index constants below).</summary>
    public ObservableCollection<FloorMetricEntry> Metrics { get; } =
    [
        new("CO2 Sequestered", "kg/yr"),
        new("Runoff Avoided", "m³/yr"),
        new("Pollution Mitigated", "kg/yr"),
        new("Cost Saved", "/yr"),
        new("Oxygen Produced", "kg/yr"),
        new("Total GWP", "kg CO2e"),
        new("Surface Temp Reduction", "°"),
        new("Air Temp Reduction", "°"),
    ];

    public const int Co2Index = 0;
    public const int RunoffIndex = 1;
    public const int PollutionIndex = 2;
    public const int CostSavedIndex = 3;
    public const int OxygenIndex = 4;
    public const int GwpIndex = 5;
    public const int SurfaceTempIndex = 6;
    public const int AirTempIndex = 7;

    public WwpLdsCoefficientRecord? SelectedRecord
    {
        get => _selectedRecord;
        set
        {
            if (!Equals(_selectedRecord, value))
            {
                _selectedRecord = value;
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
        "Coefficient table" => new SolidColorBrush(Colors.SeaGreen),
        "Manual entry" => new SolidColorBrush(Colors.SeaGreen),
        "Coefficient table + manual entry" => new SolidColorBrush(Colors.SeaGreen),
        "Manually selected" => new SolidColorBrush(Colors.SteelBlue),
        "No match found" => new SolidColorBrush(Colors.DarkOrange),
        _ => new SolidColorBrush(Colors.Gray)
    };

    public event PropertyChangedEventHandler? PropertyChanged;

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
}
