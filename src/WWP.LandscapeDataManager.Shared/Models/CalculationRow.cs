namespace WWP.LandscapeDataManager.Shared.Models;

public sealed class CalculationRow
{
    public CalculationRow(
        string status,
        string typeName,
        string basis,
        double carbon,
        double oxygen,
        double runoff,
        double pollutants,
        double maintenanceCost,
        double costSavings)
    {
        Status = status;
        TypeName = typeName;
        Basis = basis;
        Carbon = Format(carbon);
        Oxygen = Format(oxygen);
        Runoff = Format(runoff);
        Pollutants = Format(pollutants);
        MaintenanceCost = Format(maintenanceCost);
        CostSavings = Format(costSavings);
    }

    public string Status { get; }
    public string TypeName { get; }
    public string Basis { get; }
    public string Carbon { get; }
    public string Oxygen { get; }
    public string Runoff { get; }
    public string Pollutants { get; }
    public string MaintenanceCost { get; }
    public string CostSavings { get; }

    private static string Format(double value) => value.ToString("N2");
}
