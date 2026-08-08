using Microsoft.Data.Sqlite;
using WWP.LandscapeDataManager.Contracts;

namespace WWP.LandscapeDataManager.Shared.Services;

/// <summary>
/// Local cache of the WWP landscape data sheet (synced from Airtable) — the phase 2
/// coefficient table Floor Calculator matches Floors against, the same way
/// <see cref="SpeciesCatalogueDatabase"/> caches the i-Tree species catalogue for Tree Searcher.
/// </summary>
public sealed class WwpLdsCoefficientDatabase
{
    private readonly string _connectionString;

    public WwpLdsCoefficientDatabase() : this(DefaultDatabasePath())
    {
    }

    /// <summary>For tests: point at an isolated database file instead of the per-user default.</summary>
    public WwpLdsCoefficientDatabase(string databasePath)
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
        return Path.Combine(directory, "wwp-lds-coefficients.db");
    }

    private async Task<SqliteConnection> OpenAsync()
    {
        var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync();
        await using var createTable = connection.CreateCommand();
        createTable.CommandText = """
            CREATE TABLE IF NOT EXISTS Coefficients (
                MatchKey TEXT PRIMARY KEY,
                Origin TEXT NOT NULL,
                PlantingTypeCode TEXT NOT NULL DEFAULT '',
                Category TEXT NOT NULL,
                SubCategory TEXT NOT NULL,
                TypeName TEXT NOT NULL,
                CostSavedAnnual REAL NULL,
                OxygenProducedAnnual REAL NULL,
                TotalGwp REAL NULL,
                Co2SequesteredAnnual REAL NULL,
                RunoffAvoidedAnnual REAL NULL,
                PollutionMassRemovedAnnual REAL NULL,
                SurfaceTempReduction REAL NULL,
                AirTempReduction REAL NULL
            );
            CREATE TABLE IF NOT EXISTS CoefficientMeta (
                Key TEXT PRIMARY KEY,
                Value TEXT NOT NULL
            );
            """;
        await createTable.ExecuteNonQueryAsync();

        // Migration for caches created before PlantingTypeCode existed.
        await using (var migrate = connection.CreateCommand())
        {
            migrate.CommandText = "ALTER TABLE Coefficients ADD COLUMN PlantingTypeCode TEXT NOT NULL DEFAULT '';";
            try
            {
                await migrate.ExecuteNonQueryAsync();
            }
            catch (SqliteException)
            {
                // Column already exists.
            }
        }

        // Migration for caches created before PollutionMassRemovedAnnual existed.
        await using (var migrate = connection.CreateCommand())
        {
            migrate.CommandText = "ALTER TABLE Coefficients ADD COLUMN PollutionMassRemovedAnnual REAL NULL;";
            try
            {
                await migrate.ExecuteNonQueryAsync();
            }
            catch (SqliteException)
            {
                // Column already exists.
            }
        }

        return connection;
    }

    public async Task SaveAsync(IReadOnlyList<WwpLdsCoefficientRecord> records)
    {
        await using var connection = await OpenAsync();
        await using var transaction = connection.BeginTransaction();

        await using (var clear = connection.CreateCommand())
        {
            clear.CommandText = "DELETE FROM Coefficients;";
            await clear.ExecuteNonQueryAsync();
        }

        await using (var upsert = connection.CreateCommand())
        {
            upsert.CommandText = """
                INSERT OR REPLACE INTO Coefficients (
                    MatchKey, Origin, PlantingTypeCode, Category, SubCategory, TypeName,
                    CostSavedAnnual, OxygenProducedAnnual, TotalGwp,
                    Co2SequesteredAnnual, RunoffAvoidedAnnual, PollutionMassRemovedAnnual, SurfaceTempReduction, AirTempReduction)
                VALUES (
                    $matchKey, $origin, $plantingTypeCode, $category, $subCategory, $typeName,
                    $costSaved, $oxygen, $gwp, $co2, $runoff, $pollution, $surfaceTemp, $airTemp);
                """;
            var matchKey = upsert.CreateParameter(); matchKey.ParameterName = "$matchKey"; upsert.Parameters.Add(matchKey);
            var origin = upsert.CreateParameter(); origin.ParameterName = "$origin"; upsert.Parameters.Add(origin);
            var plantingTypeCode = upsert.CreateParameter(); plantingTypeCode.ParameterName = "$plantingTypeCode"; upsert.Parameters.Add(plantingTypeCode);
            var category = upsert.CreateParameter(); category.ParameterName = "$category"; upsert.Parameters.Add(category);
            var subCategory = upsert.CreateParameter(); subCategory.ParameterName = "$subCategory"; upsert.Parameters.Add(subCategory);
            var typeName = upsert.CreateParameter(); typeName.ParameterName = "$typeName"; upsert.Parameters.Add(typeName);
            var costSaved = upsert.CreateParameter(); costSaved.ParameterName = "$costSaved"; upsert.Parameters.Add(costSaved);
            var oxygen = upsert.CreateParameter(); oxygen.ParameterName = "$oxygen"; upsert.Parameters.Add(oxygen);
            var gwp = upsert.CreateParameter(); gwp.ParameterName = "$gwp"; upsert.Parameters.Add(gwp);
            var co2 = upsert.CreateParameter(); co2.ParameterName = "$co2"; upsert.Parameters.Add(co2);
            var runoff = upsert.CreateParameter(); runoff.ParameterName = "$runoff"; upsert.Parameters.Add(runoff);
            var pollution = upsert.CreateParameter(); pollution.ParameterName = "$pollution"; upsert.Parameters.Add(pollution);
            var surfaceTemp = upsert.CreateParameter(); surfaceTemp.ParameterName = "$surfaceTemp"; upsert.Parameters.Add(surfaceTemp);
            var airTemp = upsert.CreateParameter(); airTemp.ParameterName = "$airTemp"; upsert.Parameters.Add(airTemp);

            foreach (var record in records)
            {
                matchKey.Value = record.MatchKey;
                origin.Value = record.Origin;
                plantingTypeCode.Value = record.PlantingTypeCode;
                category.Value = record.Category;
                subCategory.Value = record.SubCategory;
                typeName.Value = record.TypeName;
                costSaved.Value = (object?)record.CostSavedAnnual ?? DBNull.Value;
                oxygen.Value = (object?)record.OxygenProducedAnnual ?? DBNull.Value;
                gwp.Value = (object?)record.TotalGwp ?? DBNull.Value;
                co2.Value = (object?)record.Co2SequesteredAnnual ?? DBNull.Value;
                runoff.Value = (object?)record.RunoffAvoidedAnnual ?? DBNull.Value;
                pollution.Value = (object?)record.PollutionMassRemovedAnnual ?? DBNull.Value;
                surfaceTemp.Value = (object?)record.SurfaceTempReduction ?? DBNull.Value;
                airTemp.Value = (object?)record.AirTempReduction ?? DBNull.Value;
                await upsert.ExecuteNonQueryAsync();
            }
        }

        await using (var meta = connection.CreateCommand())
        {
            meta.CommandText = """
                INSERT INTO CoefficientMeta (Key, Value) VALUES ('DownloadedAtUtc', $downloadedAt)
                ON CONFLICT(Key) DO UPDATE SET Value = excluded.Value;
                """;
            meta.Parameters.AddWithValue("$downloadedAt", DateTimeOffset.UtcNow.ToString("O"));
            await meta.ExecuteNonQueryAsync();
        }

        await transaction.CommitAsync();
    }

    public async Task<IReadOnlyList<WwpLdsCoefficientRecord>> GetAllAsync()
    {
        await using var connection = await OpenAsync();
        await using var select = connection.CreateCommand();
        select.CommandText = """
            SELECT MatchKey, Origin, PlantingTypeCode, Category, SubCategory, TypeName,
                   CostSavedAnnual, OxygenProducedAnnual, TotalGwp,
                   Co2SequesteredAnnual, RunoffAvoidedAnnual, PollutionMassRemovedAnnual, SurfaceTempReduction, AirTempReduction
            FROM Coefficients ORDER BY Category, SubCategory, TypeName;
            """;
        await using var reader = await select.ExecuteReaderAsync();

        var records = new List<WwpLdsCoefficientRecord>();
        while (await reader.ReadAsync())
        {
            records.Add(new WwpLdsCoefficientRecord(
                reader.GetString(0),
                reader.GetString(1),
                reader.GetString(2),
                reader.GetString(3),
                reader.GetString(4),
                reader.GetString(5),
                reader.IsDBNull(6) ? null : reader.GetDouble(6),
                reader.IsDBNull(7) ? null : reader.GetDouble(7),
                reader.IsDBNull(8) ? null : reader.GetDouble(8),
                reader.IsDBNull(9) ? null : reader.GetDouble(9),
                reader.IsDBNull(10) ? null : reader.GetDouble(10),
                reader.IsDBNull(11) ? null : reader.GetDouble(11),
                reader.IsDBNull(12) ? null : reader.GetDouble(12),
                reader.IsDBNull(13) ? null : reader.GetDouble(13)));
        }

        return records;
    }

    public async Task<string?> GetDownloadedAtUtcAsync()
    {
        await using var connection = await OpenAsync();
        await using var select = connection.CreateCommand();
        select.CommandText = "SELECT Value FROM CoefficientMeta WHERE Key = 'DownloadedAtUtc';";
        var result = await select.ExecuteScalarAsync();
        return result as string;
    }
}
