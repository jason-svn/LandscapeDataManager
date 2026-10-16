using WWP.LandscapeDataManager.Contracts;

namespace WWP.LandscapeDataManager.Shared.Services;

/// <summary>What one batch of i-Tree calculations did. <see cref="Message"/> is set when nothing ran (no key, no exchange rate).</summary>
public sealed record ITreeBatchSummary(int Instances, int Succeeded, int Failed, string CurrencyCode, string? Message = null)
{
    public bool Ran => Message is null;
}

/// <summary>
/// Calculates i-Tree benefits for a batch of scanned Planting instances and writes the results back
/// to Revit — the one calculation path shared by Tree Calculator and Growth Projections, so a tree
/// calculated from either tool gets the same input signature and the same result parameters.
/// Identical inputs (same species, DBH, condition, age...) are sent to i-Tree once.
/// </summary>
public static class ITreeBatchCalculator
{
    /// <summary>Part of every input signature — must stay the same across tools, or results look stale.</summary>
    public const string SignatureEngineVersion = "LIM-iTreeCalculator-v1";

    public static async Task<ITreeBatchSummary> CalculateAsync(
        RevitPipeClient client,
        IReadOnlyList<PlantingInstanceValidationItem> items,
        string apiKey,
        string preferredUnitSystem,
        string preferredCurrency,
        ITreeApiClient iTreeApiClient,
        ExchangeRateService exchangeRateService)
    {
        if (items.Count == 0)
        {
            return new ITreeBatchSummary(0, 0, 0, preferredCurrency, "Nothing to calculate.");
        }

        if (string.IsNullOrWhiteSpace(apiKey))
        {
            return new ITreeBatchSummary(items.Count, 0, 0, preferredCurrency, "No i-Tree API key saved — open Settings from the LIM ribbon first.");
        }

        var signedInputs = items.Select(item => (
            Signature: PlantingInstanceStatusEvaluator.Evaluate(item, SignatureEngineVersion).InputSignature,
            Input: new ITreeRevitInput(
                item.SpeciesCode ?? string.Empty, string.Empty, string.Empty,
                item.FamilyName, item.TypeName, 0,
                item.Condition ?? string.Empty,
                item.DbhInches ?? 0,
                item.Latitude ?? 0,
                item.Longitude ?? 0,
                item.Years ?? 1,
                item.CrownExposure ?? 0))).ToList();

        // One rate lookup per batch, not per tree — and before calling i-Tree, so a failed lookup
        // doesn't spend API calls on results that can't be written in the project's currency.
        var exchangeRate = await ProjectExchangeRate.GetUsdRateAsync(client, exchangeRateService, preferredCurrency);
        if (!exchangeRate.Success)
        {
            return new ITreeBatchSummary(items.Count, 0, 0, preferredCurrency, ProjectExchangeRate.UnavailableMessage(exchangeRate));
        }

        var outcomes = await iTreeApiClient.CalculateForInstancesAsync(signedInputs, apiKey.Trim());

        var writeItems = new List<InstanceParameterWriteItem>();
        foreach (var (item, (signature, _)) in items.Zip(signedInputs))
        {
            var outcome = outcomes.GetValueOrDefault(signature);
            var status = outcome?.Error is null ? "Calculated" : "APIError";
            writeItems.AddRange(ITreeInstanceResultMapper.BuildWriteItems(
                item.UniqueId, status, outcome?.Error, signature, outcome,
                preferredUnitSystem, exchangeRate.CurrencyCode, exchangeRate.UsdRate));
        }

        await client.SendAsync<InstanceParameterWriteResult>(
            PipeCommands.ApplyInstanceParameterWrites,
            new InstanceParameterWriteBatch(writeItems));

        return new ITreeBatchSummary(
            items.Count,
            outcomes.Values.Count(outcome => outcome.Error is null),
            outcomes.Values.Count(outcome => outcome.Error is not null),
            exchangeRate.CurrencyCode);
    }
}
