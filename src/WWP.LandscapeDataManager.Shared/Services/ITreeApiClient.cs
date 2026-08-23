using System.Collections.Concurrent;
using System.Globalization;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Text.RegularExpressions;
using WWP.LandscapeDataManager.Contracts;

namespace WWP.LandscapeDataManager.Shared.Services;

public sealed record ITreeExportProfile(
    bool Monetary,
    bool Carbon,
    bool Hydrology,
    bool AirQuality,
    bool Metadata,
    bool AnnualTimeline,
    bool CumulativeTimeline,
    bool FullResponse);

public sealed record ITreeExportRecord(
    string SpeciesCode,
    IReadOnlyDictionary<string, object?> Fields);

/// <summary>Either <see cref="Fields"/> (success) or <see cref="Error"/> (failure) is populated, never both.</summary>
public sealed record ITreeInstanceCalculationOutcome(
    IReadOnlyDictionary<string, object?>? Fields,
    string? Error);

public sealed record ITreeDownloadResult(
    IReadOnlyList<ITreeExportRecord> Records,
    IReadOnlyList<string> Errors,
    IReadOnlyList<string> DuplicateSpeciesCodes,
    int FieldCount);

public sealed class ITreeApiClient
{
    private const string ApiUrl = "https://api.itreetools.org/v3/benefit/";
    private const string SpeciesCatalogUrl = "https://dtbe-api.daveyinstitute.com/v2/getSpecies/";
    private static readonly HttpClient HttpClient = CreateClient();

    private static HttpClient CreateClient()
    {
        var client = new HttpClient { Timeout = TimeSpan.FromSeconds(90) };
        // Some API front-ends treat a missing User-Agent as bot traffic and reject it with a
        // branded HTML error page instead of a JSON error — this call previously sent none.
        client.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("WWP.LandscapeDataManager.ITreeCalculator", "1.0"));
        return client;
    }

    public async Task<ITreeDownloadResult> DownloadAsync(
        IReadOnlyList<ITreeRevitInput> inputs,
        string apiKey,
        ITreeExportProfile profile,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(apiKey))
        {
            throw new InvalidOperationException("Enter an i-Tree API key before downloading data.");
        }

        var grouped = inputs
            .GroupBy(item => NormalizeSpeciesCode(item.SpeciesCode), StringComparer.OrdinalIgnoreCase)
            .Where(group => !string.IsNullOrWhiteSpace(group.Key))
            .ToList();
        if (grouped.Count == 0)
        {
            throw new InvalidOperationException(
                "No planting types with a non-empty Species_Code parameter were found in the selected scope.");
        }

        var duplicates = grouped
            .Where(group => group.Count() > 1)
            .Select(group => group.First().SpeciesCode)
            .Order(StringComparer.OrdinalIgnoreCase)
            .ToList();
        var selectedInputs = grouped
            .Select(group => group.OrderBy(item => item.FamilyName).ThenBy(item => item.TypeName).First())
            .ToList();
        var records = new ConcurrentBag<ITreeExportRecord>();
        var errors = new ConcurrentBag<string>();
        using var throttle = new SemaphoreSlim(3);

        var tasks = selectedInputs.Select(async input =>
        {
            await throttle.WaitAsync(cancellationToken);
            try
            {
                records.Add(await DownloadOneAsync(input, apiKey.Trim(), profile, cancellationToken));
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                errors.Add($"{input.SpeciesCode}: {exception.Message}");
            }
            finally
            {
                throttle.Release();
            }
        });
        await Task.WhenAll(tasks);

        var orderedRecords = records.OrderBy(record => record.SpeciesCode, StringComparer.OrdinalIgnoreCase).ToList();
        if (orderedRecords.Count == 0)
        {
            throw new InvalidOperationException(
                "i-Tree did not return data for any Species_Code. " + string.Join("; ", errors.Take(3)));
        }

        var fieldCount = orderedRecords
            .SelectMany(record => record.Fields.Keys)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Count();
        if (fieldCount > 16_384)
        {
            throw new InvalidOperationException(
                $"The selected data would create {fieldCount:N0} columns, exceeding Excel's 16,384-column limit. " +
                "Turn off the full response or one of the timeline options.");
        }

        return new ITreeDownloadResult(
            orderedRecords,
            errors.Order(StringComparer.OrdinalIgnoreCase).ToList(),
            duplicates,
            fieldCount);
    }

    /// <summary>
    /// One representative input per unique input signature is sent to the API — the per-instance
    /// caching the Calculate &amp; QC tool relies on to avoid recomputing identical trees.
    /// </summary>
    public async Task<IReadOnlyDictionary<string, ITreeInstanceCalculationOutcome>> CalculateForInstancesAsync(
        IReadOnlyList<(string Signature, ITreeRevitInput Input)> signedInputs,
        string apiKey,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(apiKey))
        {
            throw new InvalidOperationException("Enter an i-Tree API key before calculating.");
        }

        var profile = new ITreeExportProfile(
            Monetary: true, Carbon: true, Hydrology: true, AirQuality: true, Metadata: true,
            AnnualTimeline: false, CumulativeTimeline: false, FullResponse: false);
        var representatives = signedInputs
            .GroupBy(pair => pair.Signature, StringComparer.Ordinal)
            .Select(group => group.First())
            .ToList();

        var results = new ConcurrentDictionary<string, ITreeInstanceCalculationOutcome>(StringComparer.Ordinal);
        using var throttle = new SemaphoreSlim(3);
        var tasks = representatives.Select(async pair =>
        {
            await throttle.WaitAsync(cancellationToken);
            try
            {
                var record = await DownloadOneAsync(pair.Input, apiKey.Trim(), profile, cancellationToken);
                results[pair.Signature] = new ITreeInstanceCalculationOutcome(record.Fields, null);
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                results[pair.Signature] = new ITreeInstanceCalculationOutcome(null, exception.Message);
            }
            finally
            {
                throttle.Release();
            }
        });
        await Task.WhenAll(tasks);

        return results;
    }

    public async Task<ITreeDownloadResult> DownloadSpeciesCatalogAsync(
        string apiKey,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(apiKey))
        {
            throw new InvalidOperationException("Enter an i-Tree API key before downloading the species catalog.");
        }

        var requestUrl = $"{SpeciesCatalogUrl}?key={Uri.EscapeDataString(apiKey.Trim())}&output=JSON";
        using var response = await HttpClient.GetAsync(requestUrl, cancellationToken);
        var json = await response.Content.ReadAsStringAsync(cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            throw new InvalidOperationException(
                $"The species catalog returned HTTP {(int)response.StatusCode} ({response.ReasonPhrase}).");
        }

        using var document = JsonDocument.Parse(json);
        var root = document.RootElement;
        if (root.TryGetProperty("status", out var catalogStatus) &&
            catalogStatus.ValueKind == JsonValueKind.Object &&
            catalogStatus.TryGetProperty("errno", out var errorNumber) &&
            errorNumber.ValueKind == JsonValueKind.Number &&
            errorNumber.GetInt32() != 0)
        {
            var message = catalogStatus.TryGetProperty("error", out var error)
                ? ReadScalarText(error)
                : "The i-Tree species catalog returned an error.";
            throw new InvalidOperationException(message);
        }

        if (!root.TryGetProperty("meta", out var meta) || meta.ValueKind != JsonValueKind.Array ||
            !root.TryGetProperty("data", out var data) || data.ValueKind != JsonValueKind.Array)
        {
            throw new InvalidOperationException("The i-Tree species catalog response was not recognized.");
        }

        var headers = meta.EnumerateArray()
            .Select(item => item.TryGetProperty("name", out var name) ? name.GetString() : null)
            .Where(name => !string.IsNullOrWhiteSpace(name))
            .Cast<string>()
            .ToList();
        var codeIndex = headers.FindIndex(header =>
            string.Equals(header, "Code", StringComparison.OrdinalIgnoreCase));
        if (codeIndex < 0)
        {
            throw new InvalidOperationException("The i-Tree species catalog did not include its Code column.");
        }

        var records = new List<ITreeExportRecord>();
        foreach (var row in data.EnumerateArray())
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (row.ValueKind != JsonValueKind.Array)
            {
                continue;
            }

            var values = row.EnumerateArray().ToList();
            if (codeIndex >= values.Count)
            {
                continue;
            }

            var speciesCode = ReadScalarText(values[codeIndex]).Trim();
            if (string.IsNullOrWhiteSpace(speciesCode))
            {
                continue;
            }

            var fields = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase)
            {
                ["Species_Code"] = speciesCode
            };
            for (var index = 0; index < Math.Min(headers.Count, values.Count); index++)
            {
                var header = headers[index] switch
                {
                    "Code" => "Species_Code",
                    "CommonName" => "Common_Name",
                    "ScientificName" => "Scientific_Name",
                    _ => headers[index]
                };
                fields[header] = ReadScalar(values[index]);
            }
            records.Add(new ITreeExportRecord(speciesCode, fields));
        }

        if (records.Count == 0)
        {
            throw new InvalidOperationException("The i-Tree species catalog returned no records.");
        }

        var duplicateCodes = records
            .GroupBy(record => NormalizeSpeciesCode(record.SpeciesCode), StringComparer.OrdinalIgnoreCase)
            .Where(group => group.Count() > 1)
            .Select(group => group.First().SpeciesCode)
            .Order(StringComparer.OrdinalIgnoreCase)
            .ToList();
        return new ITreeDownloadResult(records, [], duplicateCodes, FieldCount: headers.Count);
    }

    private static async Task<ITreeExportRecord> DownloadOneAsync(
        ITreeRevitInput input,
        string apiKey,
        ITreeExportProfile profile,
        CancellationToken cancellationToken)
    {
        var payload = new Dictionary<string, string>
        {
            ["mortality-rate"] = "0",
            ["report"] = "full",
            ["timeline"] = "forwards",
            ["years"] = input.Years.ToString(CultureInfo.InvariantCulture),
            ["longitude"] = input.Longitude.ToString(CultureInfo.InvariantCulture),
            ["latitude"] = input.Latitude.ToString(CultureInfo.InvariantCulture),
            ["electric-rate"] = "-1",
            ["natural-gas-rate"] = "-1",
            ["distance"] = "3",
            ["direction"] = "0",
            ["vintage"] = "pre-1950",
            ["heated"] = "1",
            ["cooled"] = "1",
            ["condition"] = input.Condition.ToLowerInvariant(),
            ["crown-exposure"] = input.CrownExposure.ToString(CultureInfo.InvariantCulture),
            ["crown-height"] = "-1",
            ["crown-width"] = "-1",
            ["diameter"] = (input.DiameterInches * 2.54d).ToString(CultureInfo.InvariantCulture),
            ["height"] = "-1",
            ["group-name"] = string.Empty,
            ["planting-type"] = string.Empty,
            ["species"] = input.SpeciesCode,
            ["tree-count"] = "1",
            ["trillion-trees"] = "false",
            ["dieback"] = string.Empty,
            ["transparency"] = string.Empty,
            ["uuid"] = Guid.NewGuid().ToString(),
            ["opt-in"] = "false",
            ["key"] = apiKey,
            ["url"] = "https://api.itreetools.org/",
            ["api"] = "v3"
        };

        using var response = await HttpClient.PostAsync(
            ApiUrl,
            new FormUrlEncodedContent(payload),
            cancellationToken);
        var json = await response.Content.ReadAsStringAsync(cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            throw new InvalidOperationException(
                $"HTTP {(int)response.StatusCode} ({response.ReasonPhrase}): {DescribeErrorBody(json)}");
        }

        using var document = JsonDocument.Parse(json);
        var root = document.RootElement;
        if (root.TryGetProperty("status", out var status) &&
            !string.Equals(status.GetString(), "ok", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(ReadApiError(root));
        }

        if (!TryGetTree(root, out var tree))
        {
            throw new InvalidOperationException("The response did not contain tree benefit data.");
        }

        var fields = CreateBaseFields(input, profile.Metadata);
        AddSummaryFields(tree, input.Years, profile, fields);
        AddStandardFields(root, tree, profile, fields);
        AddTimelineFields(tree, profile, fields);
        if (profile.FullResponse)
        {
            Flatten(root, "Raw", fields, profile);
        }

        return new ITreeExportRecord(input.SpeciesCode, fields);
    }

    private static Dictionary<string, object?> CreateBaseFields(ITreeRevitInput input, bool includeMetadata)
    {
        var fields = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase)
        {
            ["Species_Code"] = input.SpeciesCode,
            ["Common_Name"] = input.CommonName,
            ["Scientific_Name"] = input.ScientificName,
            ["Tree_Condition"] = input.Condition,
            ["Diameter_in"] = input.DiameterInches,
            ["Latitude"] = input.Latitude,
            ["Longitude"] = input.Longitude,
            ["Years"] = input.Years,
            ["CrownExposure"] = input.CrownExposure
        };
        if (includeMetadata)
        {
            fields["Revit_Family"] = input.FamilyName;
            fields["Revit_Type"] = input.TypeName;
            fields["Revit_TypeId"] = input.TypeId;
        }
        return fields;
    }

    private static void AddSummaryFields(
        JsonElement tree,
        int years,
        ITreeExportProfile profile,
        IDictionary<string, object?> fields)
    {
        if (!TryGetPath(tree, out var annualCategory, "benefits", "annual", "category") ||
            annualCategory.ValueKind != JsonValueKind.Array ||
            annualCategory.GetArrayLength() == 0)
        {
            return;
        }

        var annual = annualCategory.EnumerateArray().ToList();
        var first = annual[0];

        if (profile.Monetary)
        {
            // Confirmed against a live response cross-checked against the public i-Tree "MyTree
            // Benefits" report for the same species/diameter/location (see PR discussion): the
            // previous "pollution-avoided.co2-worth" path under-reported the carbon dollar benefit
            // by roughly 3x (it's a different i-Tree accounting bucket, not this tree's own carbon
            // benefit) — "carbon.sequestration-worth" is the field that actually matches MyTree's
            // "Carbon Dioxide Uptake $" line, both annually and summed across years. Likewise,
            // "pollution-removed.worth" never existed on a real response (always silently read as
            // 0) — MyTree's "Air Pollution Removal $" is the sum of that category's six per-pollutant
            // "<gas>-worth" siblings, not one combined field.
            var carbonWorthAnnual = ReadNumber(first, "carbon", "sequestration-worth");
            var carbonWorth20yr = annual.Sum(year => ReadNumber(year, "carbon", "sequestration-worth"));
            var stormWaterWorthAnnual = ReadNumber(first, "hydrology", "runoff-avoided-worth");
            var stormWaterWorth20yr = annual.Sum(year => ReadNumber(year, "hydrology", "runoff-avoided-worth"));
            var airPollutionWorthAnnual = SumPollutionRemovedWorth(first);
            var airPollutionWorth20yr = annual.Sum(SumPollutionRemovedWorth);

            fields["Annual_CarbonBenefit_USD"] = carbonWorthAnnual;
            fields["CarbonBenefit_20yr_USD"] = carbonWorth20yr;
            fields["Annual_StormWaterBenefit_USD"] = stormWaterWorthAnnual;
            fields["StormWaterBenefit_20yr_USD"] = stormWaterWorth20yr;
            fields["Annual_AirPollutionBenefit_USD"] = airPollutionWorthAnnual;
            fields["AirPollutionBenefit_20yr_USD"] = airPollutionWorth20yr;
            fields["Annual_Benefit_USD"] = carbonWorthAnnual + stormWaterWorthAnnual + airPollutionWorthAnnual;
            fields["Benefit_20yr_USD"] = carbonWorth20yr + stormWaterWorth20yr + airPollutionWorth20yr;
        }

        if (profile.Carbon)
        {
            var sequesteredAnnualLb = KgToPounds(ReadNumber(first, "carbon", "sequestration"));
            var sequestered20yrLb = KgToPounds(annual.Sum(year => ReadNumber(year, "carbon", "sequestration")));
            fields["Annual_CarbonSequestered_lb"] = sequesteredAnnualLb;
            fields["CarbonSequestered_20yr_lb"] = sequestered20yrLb;
            // Derived directly from sequestered carbon (standard 3.67 mass ratio of CO2 to C) rather
            // than a separate API "storage" path, so it always matches the public i-Tree "MyTree
            // Benefits" report's CO2 Equivalent figure (which tracks Carbon Sequestered x 3.67).
            fields["Annual_CO2Equivalent_lb"] = Math.Round(sequesteredAnnualLb * UnitConversions.CarbonToCo2MassRatio, 6);
            fields["CO2Equivalent_20yr_lb"] = Math.Round(sequestered20yrLb * UnitConversions.CarbonToCo2MassRatio, 6);
        }

        if (profile.Hydrology)
        {
            fields["Annual_RunoffAvoided_gal"] = CubicMetresToGallons(
                ReadNumber(first, "hydrology", "runoff-avoided"));
            fields["RunoffAvoided_20yr_gal"] = CubicMetresToGallons(
                annual.Sum(year => ReadNumber(year, "hydrology", "runoff-avoided")));
            fields["Annual_RainfallIntercepted_gal"] = CubicMetresToGallons(
                ReadNumber(first, "hydrology", "interception"));
            fields["RainfallIntercepted_20yr_gal"] = CubicMetresToGallons(
                annual.Sum(year => ReadNumber(year, "hydrology", "interception")));
        }

        if (profile.AirQuality)
        {
            foreach (var pollutant in new[] { "o3", "co", "no2", "so2", "pm25" })
            {
                fields[$"Annual_{pollutant.ToUpperInvariant()}_oz"] = KilogramsToOunces(
                    ReadNumber(first, "pollution-removed", pollutant));
                fields[$"{pollutant.ToUpperInvariant()}_20yr_oz"] = KilogramsToOunces(
                    annual.Sum(year => ReadNumber(year, "pollution-removed", pollutant)));
            }
        }

        if (profile.Metadata)
        {
            fields["Years"] = years;
        }
    }

    private static void AddStandardFields(
        JsonElement root,
        JsonElement tree,
        ITreeExportProfile profile,
        IDictionary<string, object?> fields)
    {
        if (profile.Metadata)
        {
            AddTopLevelScalar(root, "status", "API_Status", fields);
            AddTopLevelScalar(root, "api-version", "API_Version", fields);
            AddTopLevelScalar(root, "engine-version", "Engine_Version", fields);
            AddTopLevelScalar(root, "db-version", "Database_Version", fields);
        }

    }

    private static void AddTimelineFields(
        JsonElement tree,
        ITreeExportProfile profile,
        IDictionary<string, object?> fields)
    {
        if (profile.AnnualTimeline && TryGetPath(tree, out var annual, "benefits", "annual"))
        {
            Flatten(annual, "AnnualTimeline", fields, profile);
        }

        if (profile.CumulativeTimeline && TryGetPath(tree, out var cumulative, "benefits", "cumulative"))
        {
            Flatten(cumulative, "CumulativeTimeline", fields, profile);
        }
    }

    private static void Flatten(
        JsonElement element,
        string path,
        IDictionary<string, object?> fields,
        ITreeExportProfile profile)
    {
        switch (element.ValueKind)
        {
            case JsonValueKind.Object:
                foreach (var property in element.EnumerateObject())
                {
                    Flatten(property.Value, $"{path}.{property.Name}", fields, profile);
                }
                break;
            case JsonValueKind.Array:
                var index = 0;
                foreach (var child in element.EnumerateArray())
                {
                    Flatten(child, $"{path}[{index++}]", fields, profile);
                }
                break;
            case JsonValueKind.String:
                if (ShouldInclude(path, profile)) fields[path] = element.GetString();
                break;
            case JsonValueKind.Number:
                if (ShouldInclude(path, profile))
                {
                    fields[path] = element.TryGetInt64(out var integer) ? integer : element.GetDouble();
                }
                break;
            case JsonValueKind.True:
            case JsonValueKind.False:
                if (ShouldInclude(path, profile)) fields[path] = element.GetBoolean();
                break;
            case JsonValueKind.Null:
                if (ShouldInclude(path, profile)) fields[path] = null;
                break;
        }
    }

    private static bool ShouldInclude(string path, ITreeExportProfile profile)
    {
        if (profile.FullResponse)
        {
            return true;
        }

        var normalized = path.ToLowerInvariant();
        var monetary = ContainsAny(normalized, "worth", "currency", "cost", "value", "benefit");
        var carbon = ContainsAny(normalized, "carbon", "co2");
        var hydrology = ContainsAny(normalized, "hydrology", "runoff", "interception", "water");
        var air = ContainsAny(normalized, "pollution", ".o3", ".co", ".no2", ".so2", "pm25", "pm10", "voc");
        var classified = monetary || carbon || hydrology || air;

        return (profile.Monetary && monetary) ||
               (profile.Carbon && carbon) ||
               (profile.Hydrology && hydrology) ||
               (profile.AirQuality && air) ||
               (profile.Metadata && !classified);
    }

    private static bool ContainsAny(string value, params string[] terms) =>
        terms.Any(value.Contains);

    private static bool TryGetTree(JsonElement root, out JsonElement tree)
    {
        tree = default;
        return TryGetPath(root, out var trees, "data", "trees") &&
               trees.ValueKind == JsonValueKind.Object &&
               (trees.TryGetProperty("1", out tree) || trees.EnumerateObject().Select(item => item.Value).FirstOrDefault() is var first &&
                first.ValueKind != JsonValueKind.Undefined && Assign(first, out tree));
    }

    private static bool Assign(JsonElement value, out JsonElement result)
    {
        result = value;
        return true;
    }

    private static bool TryGetPath(JsonElement element, out JsonElement value, params string[] path)
    {
        value = element;
        foreach (var segment in path)
        {
            if (value.ValueKind != JsonValueKind.Object || !value.TryGetProperty(segment, out value))
            {
                value = default;
                return false;
            }
        }

        return true;
    }

    private static double ReadNumber(JsonElement element, params string[] path) =>
        TryGetPath(element, out var value, path) && value.ValueKind == JsonValueKind.Number
            ? value.GetDouble()
            : 0d;

    /// <summary>One year's total air-pollution dollar benefit — "pollution-removed" has no combined "worth" field of its own, only one per pollutant.</summary>
    private static double SumPollutionRemovedWorth(JsonElement year) =>
        ReadNumber(year, "pollution-removed", "co-worth") +
        ReadNumber(year, "pollution-removed", "no2-worth") +
        ReadNumber(year, "pollution-removed", "o3-worth") +
        ReadNumber(year, "pollution-removed", "pm25-worth") +
        ReadNumber(year, "pollution-removed", "so2-worth") +
        ReadNumber(year, "pollution-removed", "voc-worth");

    private static void AddTopLevelScalar(
        JsonElement root,
        string sourceName,
        string targetName,
        IDictionary<string, object?> fields)
    {
        if (!root.TryGetProperty(sourceName, out var value))
        {
            return;
        }

        fields[targetName] = value.ValueKind switch
        {
            JsonValueKind.String => value.GetString(),
            JsonValueKind.Number => value.TryGetInt64(out var integer) ? integer : value.GetDouble(),
            JsonValueKind.True => true,
            JsonValueKind.False => false,
            _ => value.GetRawText()
        };
    }

    private static string ReadApiError(JsonElement root)
    {
        foreach (var name in new[] { "message", "error", "status" })
        {
            if (root.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String)
            {
                return value.GetString() ?? "The i-Tree API returned an error.";
            }
        }

        return "The i-Tree API returned an error.";
    }

    /// <summary>
    /// A non-2xx response's body is usually the only place the actual reason (invalid/expired key,
    /// quota exceeded, etc.) shows up — surfacing it instead of just the HTTP status turns a bare
    /// "HTTP 403 (Forbidden)" into something a user can actually act on.
    /// </summary>
    private static string DescribeErrorBody(string body)
    {
        if (string.IsNullOrWhiteSpace(body))
        {
            return "The i-Tree API returned no further detail.";
        }

        try
        {
            using var document = JsonDocument.Parse(body);
            return ReadApiError(document.RootElement);
        }
        catch (JsonException)
        {
            // An HTML response (rather than the API's usual JSON) means something in front of the
            // calculation engine rejected the request outright — a bot-protection block, an
            // invalid-key page, a proxy/WAF error, etc. i-Tree Engine's own rejection pages (e.g.
            // "Permission Denied — Sorry, not authorized to access this." for an invalid/expired
            // key) put the actually-useful headline in the first <h1>, with a generic <title> like
            // "Error - i-Tree Engine" that doesn't say much on its own — so prefer <h1> when present.
            var headline =
                Regex.Match(body, "<h1[^>]*>(.*?)</h1>", RegexOptions.IgnoreCase | RegexOptions.Singleline) is { Success: true } h1Match
                    ? h1Match.Groups[1].Value
                    : Regex.Match(body, "<title[^>]*>(.*?)</title>", RegexOptions.IgnoreCase | RegexOptions.Singleline) is { Success: true } titleMatch
                        ? titleMatch.Groups[1].Value
                        : null;
            var cleanHeadline = headline is null ? null : Regex.Replace(headline, "<[^>]+>", " ").Trim();
            if (!string.IsNullOrWhiteSpace(cleanHeadline))
            {
                return $"The server returned an HTML page (\"{cleanHeadline}\") instead of a JSON response " +
                       "— likely an invalid/expired API key or a bot-protection block, not the calculation itself.";
            }

            const int maxLength = 300;
            return body.Length > maxLength ? body[..maxLength] + "…" : body;
        }
    }

    private static object? ReadScalar(JsonElement value) => value.ValueKind switch
    {
        JsonValueKind.String => value.GetString(),
        JsonValueKind.Number => value.TryGetInt64(out var integer) ? integer : value.GetDouble(),
        JsonValueKind.True => true,
        JsonValueKind.False => false,
        JsonValueKind.Null => null,
        _ => value.GetRawText()
    };

    private static string ReadScalarText(JsonElement value) =>
        Convert.ToString(ReadScalar(value), CultureInfo.InvariantCulture) ?? string.Empty;

    private static string NormalizeSpeciesCode(string value) => value.Trim().ToUpperInvariant();
    private static double KgToPounds(double value) => Math.Round(value * 2.20462d, 6);
    private static double CubicMetresToGallons(double value) => Math.Round(value * 264.172d, 6);
    private static double KilogramsToOunces(double value) => Math.Round(value * 35.27396195d, 6);
}
