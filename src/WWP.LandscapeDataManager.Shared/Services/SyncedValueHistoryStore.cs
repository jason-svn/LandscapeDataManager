using Microsoft.Data.Sqlite;

namespace WWP.LandscapeDataManager.Shared.Services;

/// <summary>
/// Remembers the value each synced field held immediately after its last successful write, so
/// Refresh/Compare/Audit can do a true 3-way diff (previous synced vs current Revit vs latest
/// source) instead of just current-vs-latest. Without this, a manual edit made in Revit after a
/// sync is indistinguishable from a source-side change — this store is what makes that
/// distinction, and therefore "Conflict," possible at all.
///
/// <see cref="TargetKey"/> is the stable identity a value belongs to: a Revit <c>UniqueId</c> for
/// instance-scoped parameters, or <c>"type:{TypeId}"</c> for type-scoped ones — never a display
/// name, consistent with every other stable-identity rule in this add-in.
/// </summary>
public sealed class SyncedValueHistoryStore
{
    private readonly string _connectionString;

    public SyncedValueHistoryStore() : this(DefaultDatabasePath())
    {
    }

    /// <summary>For tests: point at an isolated database file instead of the per-user default.</summary>
    public SyncedValueHistoryStore(string databasePath)
    {
        var directory = Path.GetDirectoryName(databasePath);
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        _connectionString = new SqliteConnectionStringBuilder { DataSource = databasePath }.ToString();
    }

    private static string DefaultDatabasePath()
    {
        var directory = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "EGIS",
            "WWP.LandscapeDataManager");
        return Path.Combine(directory, "synced-values.db");
    }

    private async Task<SqliteConnection> OpenAsync()
    {
        var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync();
        await using var createTable = connection.CreateCommand();
        createTable.CommandText = """
            CREATE TABLE IF NOT EXISTS SyncedValues (
                TargetKey TEXT NOT NULL,
                Parameter TEXT NOT NULL,
                Value TEXT NOT NULL,
                SyncedAtUtc TEXT NOT NULL,
                PRIMARY KEY (TargetKey, Parameter)
            );
            """;
        await createTable.ExecuteNonQueryAsync();
        return connection;
    }

    public static string InstanceKey(string uniqueId) => uniqueId;

    public static string TypeKey(long typeId) => $"type:{typeId}";

    /// <summary>Records the value each (target, parameter) pair was just successfully written to.</summary>
    public async Task RecordAsync(IReadOnlyList<SyncedValueRecord> writes)
    {
        if (writes.Count == 0)
        {
            return;
        }

        await using var connection = await OpenAsync();
        await using var transaction = connection.BeginTransaction();
        await using var upsert = connection.CreateCommand();
        upsert.CommandText = """
            INSERT INTO SyncedValues (TargetKey, Parameter, Value, SyncedAtUtc)
            VALUES ($targetKey, $parameter, $value, $syncedAt)
            ON CONFLICT(TargetKey, Parameter) DO UPDATE SET
                Value = excluded.Value,
                SyncedAtUtc = excluded.SyncedAtUtc;
            """;
        var targetKeyParam = upsert.CreateParameter(); targetKeyParam.ParameterName = "$targetKey"; upsert.Parameters.Add(targetKeyParam);
        var parameterParam = upsert.CreateParameter(); parameterParam.ParameterName = "$parameter"; upsert.Parameters.Add(parameterParam);
        var valueParam = upsert.CreateParameter(); valueParam.ParameterName = "$value"; upsert.Parameters.Add(valueParam);
        var syncedAtParam = upsert.CreateParameter(); syncedAtParam.ParameterName = "$syncedAt"; upsert.Parameters.Add(syncedAtParam);

        var syncedAt = DateTimeOffset.UtcNow.ToString("O");
        foreach (var write in writes)
        {
            targetKeyParam.Value = write.TargetKey;
            parameterParam.Value = write.Parameter;
            valueParam.Value = write.Value;
            syncedAtParam.Value = syncedAt;
            await upsert.ExecuteNonQueryAsync();
        }

        await transaction.CommitAsync();
    }

    /// <summary>Looks up the previously-synced value for a specific (target, parameter) pair, or null if never synced.</summary>
    public async Task<IReadOnlyDictionary<(string TargetKey, string Parameter), string>> GetForTargetsAsync(
        IReadOnlyCollection<string> targetKeys)
    {
        var result = new Dictionary<(string, string), string>();
        if (targetKeys.Count == 0)
        {
            return result;
        }

        await using var connection = await OpenAsync();
        await using var select = connection.CreateCommand();
        var placeholders = string.Join(',', targetKeys.Select((_, index) => $"$key{index}"));
        select.CommandText = $"SELECT TargetKey, Parameter, Value FROM SyncedValues WHERE TargetKey IN ({placeholders});";
        var index2 = 0;
        foreach (var key in targetKeys)
        {
            var parameter = select.CreateParameter();
            parameter.ParameterName = $"$key{index2}";
            parameter.Value = key;
            select.Parameters.Add(parameter);
            index2++;
        }

        await using var reader = await select.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            result[(reader.GetString(0), reader.GetString(1))] = reader.GetString(2);
        }

        return result;
    }
}

public sealed record SyncedValueRecord(string TargetKey, string Parameter, string Value);
