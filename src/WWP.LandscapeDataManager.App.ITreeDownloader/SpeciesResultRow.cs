using WWP.LandscapeDataManager.Contracts;

namespace WWP.LandscapeDataManager.App.ITreeDownloader;

public sealed class SpeciesResultRow(SpeciesCatalogueUpdateRow row)
{
    public string Status { get; } = row.Status;
    public string SpeciesCode { get; } = row.SpeciesCode;
    public string TypeName { get; } = row.TypeName;
    public string Message { get; } = row.Message ?? string.Empty;
}
