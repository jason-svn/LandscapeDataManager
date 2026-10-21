using Microsoft.Data.Sqlite;
using WWP.LandscapeDataManager.Contracts;

namespace WWP.LandscapeDataManager.Shared.Services;

/// <summary>A species picked by hand for a Revit planting type, remembered by its family and type name.</summary>
public sealed record RememberedSpeciesMatch(string FamilyName, string TypeName, string SpeciesCode);

/// <summary>
/// Remembers the species a user picked by hand in Tree Searcher for a planting type, keyed by
/// its family and type name, so the same type is matched the same way next time — in this project
/// or any other that uses the same family. Only manual picks that were assigned are stored: an
/// auto-match is found again by the name matcher anyway. Per user, beside the other local LIM data.
/// </summary>
public sealed class SpeciesMatchMemory
{
    private readonly string _connectionString;

    public SpeciesMatchMemory() : this(DefaultDatabasePath())
    {
    }

    /// <summary>For tests: point at an isolated database file instead of the per-user default.</summary>
    public SpeciesMatchMemory(string databasePath)
    {
        var directory = Path.GetDirectoryName(databasePath);
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        _connectionString = new SqliteConnectionStringBuilder { DataSource = databasePath }.ToString();
    }

    private static string DefaultDatabasePath() => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "EGIS",
        "WWP.LandscapeDataManager",
        "species-matches.db");

    /// <summary>"family : type", case- and spacing-insensitive, so "Acer_Campestre " finds "acer_campestre".</summary>
    public static string Key(string familyName, string typeName) => $"{Normalize(familyName)} : {Normalize(typeName)}";

    private static string Normalize(string text) =>
        string.Join(' ', text.Trim().ToLowerInvariant().Split([' ', '\t'], StringSplitOptions.RemoveEmptyEntries));

    private async Task<SqliteConnection> OpenAsync()
    {
        var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync();
        await using var createTable = connection.CreateCommand();
        createTable.CommandText = """
            CREATE TABLE IF NOT EXISTS SpeciesMatches (
                TypeKey TEXT NOT NULL PRIMARY KEY,
                FamilyName TEXT NOT NULL,
                TypeName TEXT NOT NULL,
                SpeciesCode TEXT NOT NULL,
                RememberedAtUtc TEXT NOT NULL
            );
            """;
        await createTable.ExecuteNonQueryAsync();
        return connection;
    }

    /// <summary>Every remembered match, by <see cref="Key"/>, mapped to its species code.</summary>
    public async Task<IReadOnlyDictionary<string, string>> LoadAsync()
    {
        var matches = new Dictionary<string, string>(StringComparer.Ordinal);
        await using var connection = await OpenAsync();
        await using var query = connection.CreateCommand();
        query.CommandText = "SELECT TypeKey, SpeciesCode FROM SpeciesMatches;";
        await using var reader = await query.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            matches[reader.GetString(0)] = reader.GetString(1);
        }

        return matches;
    }

    /// <summary>Remembers each pick; a later pick for the same family and type replaces the earlier one.</summary>
    public async Task RememberAsync(IReadOnlyList<RememberedSpeciesMatch> picks)
    {
        if (picks.Count == 0)
        {
            return;
        }

        await using var connection = await OpenAsync();
        await using var transaction = connection.BeginTransaction();
        await using var upsert = connection.CreateCommand();
        upsert.CommandText = """
            INSERT INTO SpeciesMatches (TypeKey, FamilyName, TypeName, SpeciesCode, RememberedAtUtc)
            VALUES ($key, $family, $type, $code, $at)
            ON CONFLICT(TypeKey) DO UPDATE SET
                FamilyName = excluded.FamilyName,
                TypeName = excluded.TypeName,
                SpeciesCode = excluded.SpeciesCode,
                RememberedAtUtc = excluded.RememberedAtUtc;
            """;
        var key = upsert.Parameters.Add("$key", SqliteType.Text);
        var family = upsert.Parameters.Add("$family", SqliteType.Text);
        var type = upsert.Parameters.Add("$type", SqliteType.Text);
        var code = upsert.Parameters.Add("$code", SqliteType.Text);
        var at = upsert.Parameters.Add("$at", SqliteType.Text);
        var now = DateTime.UtcNow.ToString("O");
        foreach (var pick in picks)
        {
            key.Value = Key(pick.FamilyName, pick.TypeName);
            family.Value = pick.FamilyName;
            type.Value = pick.TypeName;
            code.Value = pick.SpeciesCode;
            at.Value = now;
            await upsert.ExecuteNonQueryAsync();
        }

        await transaction.CommitAsync();
    }
}

/// <summary>Where a planting type's suggested species came from, strongest first.</summary>
public enum SpeciesMatchSource
{
    None,

    /// <summary>The type already carries this species code in the model.</summary>
    SavedOnType,

    /// <summary>The user picked this species by hand for the same family and type before.</summary>
    Remembered,

    /// <summary>Guessed from the family or type name.</summary>
    NameMatch
}

/// <summary>Picks a planting type's species: the one it already has, else a remembered manual pick, else a name match.</summary>
public static class SpeciesMatchResolver
{
    public static (SpeciesCatalogueRecord? Record, SpeciesMatchSource Source) Resolve(
        string familyName,
        string typeName,
        string? currentSpeciesCode,
        IReadOnlyDictionary<string, SpeciesCatalogueRecord> catalogueByCode,
        IReadOnlyDictionary<string, string> remembered,
        Func<SpeciesCatalogueRecord?> nameMatch)
    {
        // A code no longer in the catalogue (deprecated, or typed by hand) falls through.
        if (!string.IsNullOrWhiteSpace(currentSpeciesCode) && catalogueByCode.TryGetValue(currentSpeciesCode.Trim(), out var saved))
        {
            return (saved, SpeciesMatchSource.SavedOnType);
        }

        if (remembered.TryGetValue(SpeciesMatchMemory.Key(familyName, typeName), out var rememberedCode) &&
            catalogueByCode.TryGetValue(rememberedCode, out var picked))
        {
            return (picked, SpeciesMatchSource.Remembered);
        }

        return nameMatch() is { } guessed ? (guessed, SpeciesMatchSource.NameMatch) : (null, SpeciesMatchSource.None);
    }
}
