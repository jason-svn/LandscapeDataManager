using Microsoft.UI;
using Microsoft.UI.Xaml.Media;
using WWP.LandscapeDataManager.Contracts;

namespace WWP.LandscapeDataManager.App.Parameters;

public sealed class SetupRow(SharedParameterSetupRow row)
{
    public string Status { get; } = row.Status;
    public string Name { get; } = row.Name;
    public string Scope { get; } = row.Scope;
    public string Message { get; } = row.Message ?? string.Empty;

    public SolidColorBrush StatusBrush { get; } = row.Status switch
    {
        "Created" => new SolidColorBrush(Colors.SeaGreen),
        "Already valid" => new SolidColorBrush(Colors.Gray),
        "Conflict" => new SolidColorBrush(Colors.DarkOrange),
        "Error" => new SolidColorBrush(Colors.Crimson),
        _ => new SolidColorBrush(Colors.Gray)
    };
}
