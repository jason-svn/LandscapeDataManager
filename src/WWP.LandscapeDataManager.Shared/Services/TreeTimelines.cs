using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using WWP.LandscapeDataManager.Contracts;

namespace WWP.LandscapeDataManager.Shared.Services;

/// <summary>
/// i-Tree timelines: one year-by-year forecast per unique planting input (species, planting DBH,
/// condition, crown exposure, location and horizon), shared by every identical tree and stored in
/// the project (see <c>TreeTimelineStore</c>). The horizon is the tree's maturity — the family's
/// <c>Mature Age</c> less its <c>AgeAtPlanting</c> — at most 35 years; after it the benefit holds at
/// its mature value. Only planted trees get a timeline: a growth-year copy is that tree at a later
/// age, and the timeline already grows the original. Pure apart from <see cref="ITreeTimelineCalculator"/>.
/// </summary>
public static class TreeTimelines
{
    public const int MaximumHorizonYears = 35;

    /// <summary>Without a mature age the planting family stops growing a tree 25 years after planting.</summary>
    public const int DefaultHorizonYears = 25;

    /// <summary>The outlooks stored on each tree, matching the Dashboard's standard outlook years.</summary>
    public static readonly IReadOnlyList<int> StoredOutlookYears = [5, 10, 20, 25];

    public static int Horizon(int? matureAge, int? ageAtPlanting) =>
        matureAge is > 0
            ? Math.Clamp(matureAge.Value - Math.Max(0, ageAtPlanting ?? 0), 1, MaximumHorizonYears)
            : DefaultHorizonYears;

    /// <summary>
    /// A planted tree: an original, not a Growth Projections copy and not in a group — whichever year
    /// it's drawn at (an existing tree today, a new one at the design year).
    /// </summary>
    public static bool IsPlanted(PlantingInstanceValidationItem item) => !item.IsGrowthCopy && !item.InGroup;

    /// <summary>The planting input sent to i-Tree for the forecast, or null while something it needs is missing.</summary>
    public static ITreeRevitInput? PlantingInput(PlantingInstanceValidationItem item)
    {
        if (string.IsNullOrWhiteSpace(item.SpeciesCode) || item.DbhInches is not > 0 ||
            string.IsNullOrWhiteSpace(item.Condition) || item.Latitude is null || item.Longitude is null)
        {
            return null;
        }

        // The forecast starts at planting. A new tree drawn grown (at the design year, say) has the
        // family's planting DBH then, not the DBH it's drawn with; an existing tree starts as it is.
        var drawnGrown = item.IsNewConstruction && item.Years is { } age && age > (item.AgeAtPlanting ?? 0);
        var plantingDbhInches = drawnGrown ? GrowthRatesFromITree.PlantingDbhCm / 2.54 : item.DbhInches.Value;
        return new ITreeRevitInput(
            item.SpeciesCode.Trim(), string.Empty, string.Empty, item.FamilyName, item.TypeName, 0,
            item.Condition.Trim(), plantingDbhInches, item.Latitude.Value, item.Longitude.Value,
            Horizon(item.MatureAge, item.AgeAtPlanting), item.CrownExposure ?? 0);
    }

    /// <summary>Stable across tools and sessions: the same input always gives the same key.</summary>
    public static string Key(ITreeRevitInput input)
    {
        var text = string.Join("|",
            input.SpeciesCode.Trim().ToUpperInvariant(),
            Math.Round(input.DiameterInches, 2).ToString("0.00", CultureInfo.InvariantCulture),
            input.Condition.Trim().ToLowerInvariant(),
            input.CrownExposure.ToString(CultureInfo.InvariantCulture),
            Math.Round(input.Latitude, 4).ToString("0.0000", CultureInfo.InvariantCulture),
            Math.Round(input.Longitude, 4).ToString("0.0000", CultureInfo.InvariantCulture),
            input.Years.ToString(CultureInfo.InvariantCulture));
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(text));
        return "t1-" + Convert.ToHexString(hash, 0, 8).ToLowerInvariant();
    }

    /// <summary>
    /// What a planted tree carries from its timeline: the key, its totals over 5/10/20/25 years, and
    /// its lifetime totals — now the whole forecast (planting to maturity), the same span on every
    /// tree, instead of a sum over its own age from its current size.
    /// </summary>
    public static IReadOnlyList<InstanceParameterWriteItem> BuildWriteItems(string uniqueId, TreeBenefitTimeline timeline, double usdExchangeRate)
    {
        var items = new List<InstanceParameterWriteItem> { new(uniqueId, "!_S_PLT_Result_iTreeTimelineKey_Text", "Text", timeline.Key) };
        double Money(double usd) => usd * usdExchangeRate;
        static double Pollution(TreeBenefitYear sum) => sum.CoKg + sum.No2Kg + sum.O3Kg + sum.So2Kg + sum.Pm25Kg;

        foreach (var years in StoredOutlookYears)
        {
            var sum = TimelineSum.Over(timeline, years);
            items.Add(Auto(uniqueId, $"!_S_PLT_Result_CO2Equivalent{years}yr_Mass", sum.CarbonKg * UnitConversions.CarbonToCo2MassRatio));
            items.Add(Auto(uniqueId, $"!_S_PLT_Result_RunoffAvoided{years}yr_Volume", sum.RunoffM3));
            items.Add(Auto(uniqueId, $"!_S_PLT_Result_PollutantsRemoved{years}yr_Mass", Pollution(sum)));
            items.Add(Plain(uniqueId, $"!_S_PLT_Result_CostSaved{years}yr_Currency", Money(sum.CarbonUsd + sum.StormWaterUsd + sum.AirPollutionUsd)));
        }

        var lifetime = TimelineSum.Over(timeline, timeline.HorizonYears);
        var co2Equivalent = lifetime.CarbonKg * UnitConversions.CarbonToCo2MassRatio;
        items.Add(Auto(uniqueId, "!_S_PLT_iTreeResult_CarbonSequesteredLifetimeTotal_Mass", lifetime.CarbonKg));
        items.Add(Auto(uniqueId, "!_S_PLT_iTreeResult_CO2EquivalentLifetimeTotal_Mass", co2Equivalent));
        items.Add(Auto(uniqueId, "!_S_PLT_Result_OxygenProducedLifetimeTotal_Mass", co2Equivalent * UnitConversions.OxygenToCo2MassRatio));
        items.Add(Auto(uniqueId, "!_S_PLT_iTreeResult_CORemovedLifetimeTotal_Mass", lifetime.CoKg));
        items.Add(Auto(uniqueId, "!_S_PLT_iTreeResult_NO2RemovedLifetimeTotal_Mass", lifetime.No2Kg));
        items.Add(Auto(uniqueId, "!_S_PLT_iTreeResult_O3RemovedLifetimeTotal_Mass", lifetime.O3Kg));
        items.Add(Auto(uniqueId, "!_S_PLT_iTreeResult_SO2RemovedLifetimeTotal_Mass", lifetime.So2Kg));
        items.Add(Auto(uniqueId, "!_S_PLT_iTreeResult_PM25RemovedLifetimeTotal_Mass", lifetime.Pm25Kg));
        items.Add(Auto(uniqueId, "!_S_PLT_iTreeResult_RunoffAvoidedLifetimeTotal_Volume", lifetime.RunoffM3));
        items.Add(Auto(uniqueId, "!_S_PLT_iTreeResult_RainfallInterceptedLifetimeTotal_Volume", lifetime.InterceptionM3));
        items.Add(Plain(uniqueId, "!_S_PLT_iTreeResult_CarbonCostSavedLifetimeTotal_Currency", Money(lifetime.CarbonUsd)));
        items.Add(Plain(uniqueId, "!_S_PLT_iTreeResult_StormWaterCostSavedLifetimeTotal_Currency", Money(lifetime.StormWaterUsd)));
        items.Add(Plain(uniqueId, "!_S_PLT_iTreeResult_AirPollutionCostSavedLifetimeTotal_Currency", Money(lifetime.AirPollutionUsd)));
        items.Add(Plain(uniqueId, "!_S_PLT_iTreeResult_CostSavedLifetimeTotal_Currency", Money(lifetime.CarbonUsd + lifetime.StormWaterUsd + lifetime.AirPollutionUsd)));
        return items;
    }

    private static InstanceParameterWriteItem Auto(string uniqueId, string parameter, double value) =>
        new(uniqueId, parameter, "Auto (Revit spec)", value.ToString("G17", CultureInfo.InvariantCulture));

    private static InstanceParameterWriteItem Plain(string uniqueId, string parameter, double value) =>
        new(uniqueId, parameter, "Number (no conversion)", value.ToString("G17", CultureInfo.InvariantCulture));
}

/// <summary>What one timeline run did; <see cref="Message"/> explains a run that couldn't happen.</summary>
public sealed record TreeTimelineSummary(int PlantedTrees, int UniqueInputs, int Requested, int Reused, int Failed, int TreesWritten, string? Message = null)
{
    public string Describe() => Message ??
        $"i-Tree forecast: {UniqueInputs:N0} unique planting input(s) — {Requested:N0} requested, {Reused:N0} already stored" +
        (Failed > 0 ? $", {Failed:N0} failed" : string.Empty) +
        $"; 5/10/20/25-year totals written to {TreesWritten:N0} planted tree(s).";
}

/// <summary>Runs the forecast for every planted tree that needs one, stores it, and writes each tree's totals.</summary>
public static class ITreeTimelineCalculator
{
    public static async Task<TreeTimelineSummary> RunAsync(
        RevitPipeClient client,
        IReadOnlyList<PlantingInstanceValidationItem> items,
        string apiKey,
        ITreeApiClient iTreeApiClient,
        double usdExchangeRate,
        CancellationToken cancellationToken = default)
    {
        var planted = items
            .Where(TreeTimelines.IsPlanted)
            .Select(item => (Item: item, Input: TreeTimelines.PlantingInput(item)))
            .Where(pair => pair.Input is not null)
            .Select(pair => (pair.Item, Input: pair.Input!, Key: TreeTimelines.Key(pair.Input!)))
            .ToList();
        if (planted.Count == 0)
        {
            return new TreeTimelineSummary(0, 0, 0, 0, 0, 0, "i-Tree forecast: no planted trees with complete inputs in this batch (growth-year copies don't get one).");
        }

        IReadOnlyList<TreeBenefitTimeline> stored;
        try
        {
            stored = (await client.SendAsync<TreeTimelinesResult>(PipeCommands.GetTreeTimelines)).Timelines;
        }
        catch (InvalidOperationException exception)
        {
            // An older connector without timeline storage: nothing to store them in.
            return new TreeTimelineSummary(planted.Count, 0, 0, 0, 0, 0, $"i-Tree forecast skipped: {exception.Message}");
        }

        var timelines = stored.ToDictionary(timeline => timeline.Key, StringComparer.Ordinal);
        var unique = planted.GroupBy(pair => pair.Key, StringComparer.Ordinal).Select(group => group.First()).ToList();
        var missing = unique.Where(pair => !timelines.ContainsKey(pair.Key)).ToList();
        var fresh = new System.Collections.Concurrent.ConcurrentBag<TreeBenefitTimeline>();
        var failed = 0;
        using (var throttle = new SemaphoreSlim(3))
        {
            await Task.WhenAll(missing.Select(async pair =>
            {
                await throttle.WaitAsync(cancellationToken);
                try
                {
                    var years = await iTreeApiClient.ForecastAsync(pair.Input, pair.Input.Years, apiKey, cancellationToken);
                    fresh.Add(new TreeBenefitTimeline(pair.Key, pair.Input.SpeciesCode, pair.Input.DiameterInches * 2.54d, pair.Input.Years,
                        DateTimeOffset.UtcNow.ToString("O", CultureInfo.InvariantCulture), years));
                }
                catch (Exception exception) when (exception is not OperationCanceledException)
                {
                    Interlocked.Increment(ref failed);
                }
                finally
                {
                    throttle.Release();
                }
            }));
        }

        if (!fresh.IsEmpty)
        {
            await client.SendAsync<SaveTreeTimelinesResult>(PipeCommands.SaveTreeTimelines, new SaveTreeTimelinesRequest(fresh.ToList()));
            foreach (var timeline in fresh)
            {
                timelines[timeline.Key] = timeline;
            }
        }

        var writes = planted
            .Where(pair => timelines.ContainsKey(pair.Key))
            .SelectMany(pair => TreeTimelines.BuildWriteItems(pair.Item.UniqueId, timelines[pair.Key], usdExchangeRate))
            .ToList();
        var treesWritten = planted.Count(pair => timelines.ContainsKey(pair.Key));
        if (writes.Count > 0)
        {
            await client.SendAsync<InstanceParameterWriteResult>(PipeCommands.ApplyInstanceParameterWrites, new InstanceParameterWriteBatch(writes));
        }

        return new TreeTimelineSummary(planted.Count, unique.Count, fresh.Count, unique.Count - missing.Count, failed, treesWritten);
    }
}
