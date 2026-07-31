using System.ComponentModel;
using System.Runtime.CompilerServices;
using Microsoft.UI;
using Microsoft.UI.Xaml.Media;
using WWP.LandscapeDataManager.Contracts;

namespace WWP.LandscapeDataManager.App.Parameters;

public sealed class SetupRow(SharedParameterSetupRow row) : INotifyPropertyChanged
{
    private string _status = row.Status;
    private string _message = row.Message ?? string.Empty;
    private bool _isIncluded = row.Status != "Error";

    public string Name { get; } = row.Name;
    public string Scope { get; } = row.Scope;
    public string Description { get; } = row.Description;

    public bool IsIncluded
    {
        get => _isIncluded;
        set
        {
            if (_isIncluded != value)
            {
                _isIncluded = value;
                OnPropertyChanged();
            }
        }
    }

    public string Status
    {
        get => _status;
        set
        {
            if (_status != value)
            {
                _status = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(StatusBrush));
            }
        }
    }

    public string Message
    {
        get => _message;
        set
        {
            if (_message != value)
            {
                _message = value;
                OnPropertyChanged();
            }
        }
    }

    public SolidColorBrush StatusBrush => Status switch
    {
        "Created" => new SolidColorBrush(Colors.SeaGreen),
        "Regrouped" => new SolidColorBrush(Colors.SeaGreen),
        "Already valid" => new SolidColorBrush(Colors.Gray),
        "Will create" => new SolidColorBrush(Colors.SteelBlue),
        "Will regroup" => new SolidColorBrush(Colors.SteelBlue),
        "Conflict" => new SolidColorBrush(Colors.DarkOrange),
        "Error" => new SolidColorBrush(Colors.Crimson),
        _ => new SolidColorBrush(Colors.Gray)
    };

    public event PropertyChangedEventHandler? PropertyChanged;

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));

    public void ApplyResult(SharedParameterSetupRow updated)
    {
        Status = updated.Status;
        Message = updated.Message ?? string.Empty;
    }
}
