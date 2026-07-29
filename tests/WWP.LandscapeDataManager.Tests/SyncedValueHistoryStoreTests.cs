using WWP.LandscapeDataManager.Shared.Services;
using Xunit;

namespace WWP.LandscapeDataManager.Tests;

public class SyncedValueHistoryStoreTests : IDisposable
{
    private readonly string _databasePath = Path.Combine(Path.GetTempPath(), $"lim-synced-values-{Guid.NewGuid():N}.db");

    [Fact]
    public async Task Records_and_retrieves_a_value_by_target_and_parameter()
    {
        var store = new SyncedValueHistoryStore(_databasePath);
        await store.RecordAsync([new SyncedValueRecord("uid-1", "Years", "20")]);

        var result = await store.GetForTargetsAsync(["uid-1"]);

        Assert.Equal("20", result[("uid-1", "Years")]);
    }

    [Fact]
    public async Task A_second_record_for_the_same_key_overwrites_instead_of_duplicating()
    {
        var store = new SyncedValueHistoryStore(_databasePath);
        await store.RecordAsync([new SyncedValueRecord("uid-1", "Years", "20")]);
        await store.RecordAsync([new SyncedValueRecord("uid-1", "Years", "25")]);

        var result = await store.GetForTargetsAsync(["uid-1"]);

        Assert.Equal("25", result[("uid-1", "Years")]);
    }

    [Fact]
    public async Task Returns_no_entry_for_a_target_that_was_never_recorded()
    {
        var store = new SyncedValueHistoryStore(_databasePath);
        await store.RecordAsync([new SyncedValueRecord("uid-1", "Years", "20")]);

        var result = await store.GetForTargetsAsync(["uid-2"]);

        Assert.Empty(result);
    }

    [Fact]
    public async Task Distinguishes_type_keys_from_instance_keys()
    {
        var store = new SyncedValueHistoryStore(_databasePath);
        await store.RecordAsync([
            new SyncedValueRecord(SyncedValueHistoryStore.TypeKey(42), "GrowthRatio", "1.5"),
            new SyncedValueRecord(SyncedValueHistoryStore.InstanceKey("uid-1"), "Years", "20")
        ]);

        var result = await store.GetForTargetsAsync(["type:42", "uid-1"]);

        Assert.Equal("1.5", result[("type:42", "GrowthRatio")]);
        Assert.Equal("20", result[("uid-1", "Years")]);
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
