using System.Globalization;
using System.Security.Cryptography;
using System.Text;

namespace WWP.LandscapeDataManager.Shared.Services;

/// <summary>
/// Computes a stable signature from every input that affects an i-Tree calculation result.
/// Two instances (or the same instance across two runs) with an identical signature are
/// guaranteed to produce the identical i-Tree result, which is what lets the Calculate tool
/// skip a redundant API call and what lets it detect a "Stale" (input changed since last
/// calculation) result without re-calling the API just to find out.
/// </summary>
public static class InputSignatureCalculator
{
    public static string Compute(
        string speciesCode,
        int years,
        string condition,
        int crownExposure,
        double dbhInches,
        double latitude,
        double longitude,
        string engineVersion)
    {
        var text = string.Join('|',
            speciesCode.Trim().ToUpperInvariant(),
            years.ToString(CultureInfo.InvariantCulture),
            condition.Trim().ToUpperInvariant(),
            crownExposure.ToString(CultureInfo.InvariantCulture),
            dbhInches.ToString("F4", CultureInfo.InvariantCulture),
            latitude.ToString("F6", CultureInfo.InvariantCulture),
            longitude.ToString("F6", CultureInfo.InvariantCulture),
            engineVersion.Trim());

        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(text));
        return Convert.ToHexString(hash);
    }
}
