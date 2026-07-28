using WWP.LandscapeDataManager.Contracts;
using WWP.LandscapeDataManager.Shared.Services;
using Xunit;

namespace WWP.LandscapeDataManager.Tests;

public class SpeciesCatalogueDatabaseTests : IDisposable
{
    private readonly string _databasePath = Path.Combine(Path.GetTempPath(), $"lim-species-{Guid.NewGuid():N}.db");

    [Fact]
    public async Task Saves_and_reloads_records_and_metadata()
    {
        var database = new SpeciesCatalogueDatabase(_databasePath);
        var records = new[]
        {
            new SpeciesCatalogueRecord("QURO", "English oak", "Quercus robur", "Broadleaf", null),
            new SpeciesCatalogueRecord("BEPE", "Silver birch", "Betula pendula", "Broadleaf", null)
        };

        await database.SaveAsync(records, "hash-1");
        var reloaded = await database.GetAllAsync();
        var metadata = await database.GetMetadataAsync();

        Assert.Equal(2, reloaded.Count);
        Assert.Contains(reloaded, r => r.SpeciesCode == "QURO" && r.CommonName == "English oak");
        Assert.Equal("hash-1", metadata.Version);
        Assert.NotNull(metadata.DownloadedAtUtc);
    }

    [Fact]
    public async Task Upserts_on_a_second_save_instead_of_duplicating()
    {
        var database = new SpeciesCatalogueDatabase(_databasePath);
        await database.SaveAsync([new SpeciesCatalogueRecord("QURO", "English oak", "Quercus robur", "Broadleaf", null)], "hash-1");
        await database.SaveAsync([new SpeciesCatalogueRecord("QURO", "English oak (updated)", "Quercus robur", "Broadleaf", "QURO2")], "hash-2");

        var reloaded = await database.GetAllAsync();
        var metadata = await database.GetMetadataAsync();

        var record = Assert.Single(reloaded);
        Assert.Equal("English oak (updated)", record.CommonName);
        Assert.Equal("QURO2", record.ReplaceBy);
        Assert.Equal("hash-2", metadata.Version);
    }

    public void Dispose()
    {
        try
        {
            if (File.Exists(_databasePath))
            {
                File.Delete(_databasePath);
            }
        }
        catch (IOException)
        {
            // Best-effort cleanup; the OS temp directory will reclaim it eventually.
        }
    }
}
