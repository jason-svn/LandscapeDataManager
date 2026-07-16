namespace WWP.LandscapeDataManager.App.Models;

public sealed class ReviewRow
{
    public ReviewRow(
        string status,
        string category,
        string typeName,
        string quantity,
        string area,
        string calculationType)
    {
        Status = status;
        Category = category;
        TypeName = typeName;
        Quantity = quantity;
        Area = area;
        CalculationType = calculationType;
    }

    public string Status { get; set; }
    public string Category { get; set; }
    public string TypeName { get; set; }
    public string Quantity { get; set; }
    public string Area { get; set; }
    public string CalculationType { get; set; }
}
