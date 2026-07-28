using System.Text.Json;

namespace WWP.LandscapeDataManager.Shared.Models;

/// <summary>
/// A single source-data row, regardless of whether it came from Airtable or Excel — both
/// clients funnel into this shape so downstream matching/calculation code is source-agnostic.
/// </summary>
public sealed record AirtableRecord(
    string Id,
    Dictionary<string, JsonElement> Fields);
