using WWP.LandscapeDataManager.Contracts;
using WWP.LandscapeDataManager.Shared.Services;
using Xunit;

namespace WWP.LandscapeDataManager.Tests;

/// <summary>Tree Searcher remembers hand-picked species per family type and prefers what the model already says.</summary>
public sealed class SpeciesMatchMemoryTests : IDisposable
{
    private readonly string _databasePath = Path.Combine(Path.GetTempPath(), $"lim-species-matches-{Guid.NewGuid():N}.db");

    private static readonly SpeciesCatalogueRecord Oak = new("QURO", "English oak", "Quercus robur", "Species", null);
    private static readonly SpeciesCatalogueRecord Maple = new("ACCA", "Hedge maple", "Acer campestre", "Species", null);
    private static readonly SpeciesCatalogueRecord Birch = new("BEPE", "European white birch", "Betula pendula", "Species", null);

    private static readonly IReadOnlyDictionary<string, SpeciesCatalogueRecord> Catalogue =
        new[] { Oak, Maple, Birch }.ToDictionary(record => record.SpeciesCode, StringComparer.OrdinalIgnoreCase);

    [Fact]
    public async Task A_manual_pick_is_remembered_and_a_later_pick_replaces_it()
    {
        var memory = new SpeciesMatchMemory(_databasePath);
        await memory.RememberAsync([new RememberedSpeciesMatch("WWP_Vegetation_Tree", "Street tree 1", "QURO")]);
        await memory.RememberAsync([new RememberedSpeciesMatch("WWP_Vegetation_Tree", "Street tree 1", "ACCA")]);

        var remembered = await new SpeciesMatchMemory(_databasePath).LoadAsync();

        Assert.Single(remembered);
        Assert.Equal("ACCA", remembered[SpeciesMatchMemory.Key("WWP_Vegetation_Tree", "Street tree 1")]);
    }

    [Fact]
    public void The_key_ignores_case_and_extra_spaces() =>
        Assert.Equal(SpeciesMatchMemory.Key("WWP_Vegetation_Tree", "Street  tree 1"), SpeciesMatchMemory.Key(" wwp_vegetation_tree ", "STREET tree 1"));

    [Fact]
    public void The_species_already_on_the_type_wins()
    {
        var remembered = new Dictionary<string, string> { [SpeciesMatchMemory.Key("F", "T")] = "ACCA" };

        var (record, source) = SpeciesMatchResolver.Resolve("F", "T", "quro", Catalogue, remembered, () => Birch);

        Assert.Equal(Oak, record);
        Assert.Equal(SpeciesMatchSource.SavedOnType, source);
    }

    [Fact]
    public void A_remembered_pick_beats_the_name_guess()
    {
        var remembered = new Dictionary<string, string> { [SpeciesMatchMemory.Key("F", "T")] = "ACCA" };

        var (record, source) = SpeciesMatchResolver.Resolve("F", "T", null, Catalogue, remembered, () => Birch);

        Assert.Equal(Maple, record);
        Assert.Equal(SpeciesMatchSource.Remembered, source);
    }

    [Fact]
    public void A_code_missing_from_the_catalogue_falls_through_to_the_next_source()
    {
        var remembered = new Dictionary<string, string> { [SpeciesMatchMemory.Key("F", "T")] = "GONE" };

        var (record, source) = SpeciesMatchResolver.Resolve("F", "T", "OLD", Catalogue, remembered, () => Birch);

        Assert.Equal(Birch, record);
        Assert.Equal(SpeciesMatchSource.NameMatch, source);
    }

    [Fact]
    public void Nothing_found_is_none() =>
        Assert.Equal((null, SpeciesMatchSource.None), SpeciesMatchResolver.Resolve("F", "T", null, Catalogue, new Dictionary<string, string>(), () => null));

    public void Dispose()
    {
        try
        {
            Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
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
