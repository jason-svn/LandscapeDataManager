using Microsoft.Data.Sqlite;
using WWP.LandscapeDataManager.Contracts;

namespace WWP.LandscapeDataManager.Shared.Services;

/// <summary>
/// Local cache of the i-Tree species catalogue. Keeping the full catalogue here (rather than in
/// Revit) means a Full Catalogue download doesn't have to touch the model at all — Revit only
/// ever receives species codes that already exist on a Planting type in the project, since a
/// native Revit schedule can't list species that have no corresponding modeled Type.
/// </summary>
public sealed class SpeciesCatalogueDatabase
{
    private readonly string _connectionString;

    public SpeciesCatalogueDatabase() : this(DefaultDatabasePath())
    {
    }

    /// <summary>For tests: point at an isolated database file instead of the per-user default.</summary>
    public SpeciesCatalogueDatabase(string databasePath)
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
        return Path.Combine(directory, "species-catalogue.db");
    }

    private async Task<SqliteConnection> OpenAsync()
    {
        var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync();
        await using var createTable = connection.CreateCommand();
        createTable.CommandText = """
            CREATE TABLE IF NOT EXISTS Species (
                SpeciesCode TEXT PRIMARY KEY,
                CommonName TEXT NOT NULL,
                ScientificName TEXT NOT NULL,
                SpeciesType TEXT NOT NULL,
                ReplaceBy TEXT NULL
            );
            CREATE TABLE IF NOT EXISTS CatalogueMeta (
                Key TEXT PRIMARY KEY,
                Value TEXT NOT NULL
            );
            """;
        await createTable.ExecuteNonQueryAsync();
        return connection;
    }

    public async Task SaveAsync(IReadOnlyList<SpeciesCatalogueRecord> records, string catalogueVersion)
    {
        await using var connection = await OpenAsync();
        await using var transaction = connection.BeginTransaction();

        await using (var upsert = connection.CreateCommand())
        {
            upsert.CommandText = """
                INSERT INTO Species (SpeciesCode, CommonName, ScientificName, SpeciesType, ReplaceBy)
                VALUES ($code, $common, $scientific, $type, $replaceBy)
                ON CONFLICT(SpeciesCode) DO UPDATE SET
                    CommonName = excluded.CommonName,
                    ScientificName = excluded.ScientificName,
                    SpeciesType = excluded.SpeciesType,
                    ReplaceBy = excluded.ReplaceBy;
                """;
            var code = upsert.CreateParameter(); code.ParameterName = "$code"; upsert.Parameters.Add(code);
            var common = upsert.CreateParameter(); common.ParameterName = "$common"; upsert.Parameters.Add(common);
            var scientific = upsert.CreateParameter(); scientific.ParameterName = "$scientific"; upsert.Parameters.Add(scientific);
            var type = upsert.CreateParameter(); type.ParameterName = "$type"; upsert.Parameters.Add(type);
            var replaceBy = upsert.CreateParameter(); replaceBy.ParameterName = "$replaceBy"; upsert.Parameters.Add(replaceBy);

            foreach (var record in records)
            {
                code.Value = record.SpeciesCode;
                common.Value = record.CommonName;
                scientific.Value = record.ScientificName;
                type.Value = record.SpeciesType;
                replaceBy.Value = (object?)record.ReplaceBy ?? DBNull.Value;
                await upsert.ExecuteNonQueryAsync();
            }
        }

        await using (var meta = connection.CreateCommand())
        {
            meta.CommandText = """
                INSERT INTO CatalogueMeta (Key, Value) VALUES ('Version', $version)
                ON CONFLICT(Key) DO UPDATE SET Value = excluded.Value;
                INSERT INTO CatalogueMeta (Key, Value) VALUES ('DownloadedAtUtc', $downloadedAt)
                ON CONFLICT(Key) DO UPDATE SET Value = excluded.Value;
                """;
            meta.Parameters.AddWithValue("$version", catalogueVersion);
            meta.Parameters.AddWithValue("$downloadedAt", DateTimeOffset.UtcNow.ToString("O"));
            await meta.ExecuteNonQueryAsync();
        }

        await transaction.CommitAsync();
    }

    public async Task<IReadOnlyList<SpeciesCatalogueRecord>> GetAllAsync()
    {
        await using var connection = await OpenAsync();
        await using var select = connection.CreateCommand();
        select.CommandText = "SELECT SpeciesCode, CommonName, ScientificName, SpeciesType, ReplaceBy FROM Species ORDER BY SpeciesCode;";
        await using var reader = await select.ExecuteReaderAsync();

        var records = new List<SpeciesCatalogueRecord>();
        while (await reader.ReadAsync())
        {
            records.Add(new SpeciesCatalogueRecord(
                reader.GetString(0),
                reader.GetString(1),
                reader.GetString(2),
                reader.GetString(3),
                reader.IsDBNull(4) ? null : reader.GetString(4)));
        }

        return records;
    }

    public async Task<CatalogueMetadata> GetMetadataAsync()
    {
        await using var connection = await OpenAsync();
        await using var select = connection.CreateCommand();
        select.CommandText = "SELECT Key, Value FROM CatalogueMeta;";
        await using var reader = await select.ExecuteReaderAsync();

        string? version = null;
        string? downloadedAt = null;
        while (await reader.ReadAsync())
        {
            var key = reader.GetString(0);
            var value = reader.GetString(1);
            if (key == "Version") version = value;
            if (key == "DownloadedAtUtc") downloadedAt = value;
        }

        return new CatalogueMetadata(version, downloadedAt);
    }
}

public sealed record CatalogueMetadata(string? Version, string? DownloadedAtUtc)
{
    /// <summary>The download timestamp formatted for display in the current Windows user's local time, or null if never downloaded.</summary>
    public string? FormatDownloadedAtLocal() =>
        DateTimeOffset.TryParse(DownloadedAtUtc, out var parsed)
            ? parsed.ToLocalTime().ToString("MMM d, yyyy h:mm tt")
            : null;
}
