using System.Text.Json;
using WWP.LandscapeDataManager.Contracts;

namespace WWP.LandscapeDataManager.Shared.Services;

/// <summary>
/// Persisted, user-approved exceptions for matching a source type name to a specific Revit
/// Family + Type, used only to disambiguate when more than one Revit type shares the same
/// normalized name — never a substitute for the Family+Type key itself.
/// </summary>
public sealed class TypeAliasStore
{
    private readonly string _filePath;

    public TypeAliasStore()
    {
        var directory = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "EGIS",
            "WWP.LandscapeDataManager");
        _filePath = Path.Combine(directory, "type-aliases.json");
    }

    public string FilePath => _filePath;

    public async Task<IReadOnlyList<TypeAlias>> LoadAsync()
    {
        if (!File.Exists(_filePath))
        {
            return [];
        }

        await using var stream = File.OpenRead(_filePath);
        return await JsonSerializer.DeserializeAsync<IReadOnlyList<TypeAlias>>(stream, JsonDefaults.Options)
               ?? [];
    }

    public async Task SaveAsync(IReadOnlyList<TypeAlias> aliases)
    {
        var directory = Path.GetDirectoryName(_filePath)
                        ?? throw new InvalidOperationException("The settings directory could not be resolved.");
        Directory.CreateDirectory(directory);

        var options = new JsonSerializerOptions(JsonDefaults.Options) { WriteIndented = true };
        await using var stream = File.Create(_filePath);
        await JsonSerializer.SerializeAsync(stream, aliases, options);
    }
}

/// <summary>Approves that the source's <see cref="SourceTypeName"/> means this specific Revit Family + Type.</summary>
public sealed record TypeAlias(string SourceTypeName, string FamilyName, string TypeName);
