using System.Net.Http.Headers;
using System.Text.RegularExpressions;

namespace WWP.LandscapeDataManager.Shared.Services;

/// <summary>The fetched key (success) or a human-readable <see cref="Error"/> (failure), never both.</summary>
public sealed record ITreeMyTreeKeyResult(string? Key, bool Success, string? Error);

/// <summary>
/// Retrieves the public i-Tree API key that the free MyTree web tool (https://mytree.itreetools.org)
/// embeds in its own front-end JavaScript. MyTree has no login: it calls the same
/// api.itreetools.org v3 benefit engine this add-in uses, authenticated by a single fixed GUID that
/// is baked into its published bundle and shared by every visitor. There is nothing to "generate" —
/// this just reads the current value straight from the page, so a machine that has never opened
/// i-Tree in a browser can still obtain a working key with only an internet connection.
///
/// The bundle filename is content-hashed (e.g. main.90e73c08.js) and changes whenever Davey
/// redeploys MyTree, so the hash is never hard-coded — the shell HTML is fetched first and the
/// current bundle path is read out of it.
///
/// This rides on MyTree's shared free key against a metered API. It is appropriate for internal /
/// prototype use; a production, client-billed deployment should use an org key issued by Davey
/// (dtbe@davey.com) entered manually in Settings instead.
/// </summary>
public sealed class ITreeMyTreeKeyFetcher
{
    private const string MyTreeUrl = "https://mytree.itreetools.org/";

    private static readonly HttpClient HttpClient = CreateClient();

    private static readonly Regex ScriptSrcRegex =
        new("""src\s*=\s*["']([^"']+\.js[^"']*)["']""", RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static readonly Regex GuidRegex =
        new("[0-9a-fA-F]{8}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{12}", RegexOptions.Compiled);

    private static HttpClient CreateClient()
    {
        var client = new HttpClient { Timeout = TimeSpan.FromSeconds(30) };
        // A missing User-Agent can be treated as bot traffic and served a branded HTML block page
        // instead of the real content — same guard ITreeApiClient already uses.
        client.DefaultRequestHeaders.UserAgent.Add(
            new ProductInfoHeaderValue("WWP.LandscapeDataManager.Settings", "1.0"));
        return client;
    }

    public async Task<ITreeMyTreeKeyResult> FetchAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            var shell = await GetStringAsync(MyTreeUrl, cancellationToken);

            // The key sometimes appears in the shell itself; usually it lives in the JS bundle.
            if (TrySelectKey(shell, out var inlineKey))
            {
                return new ITreeMyTreeKeyResult(inlineKey, true, null);
            }

            var scripts = ScriptSrcRegex.Matches(shell)
                .Select(match => match.Groups[1].Value)
                .Distinct()
                .ToList();

            if (scripts.Count == 0)
            {
                return Failure("MyTree returned a page with no script references — its layout may have changed. Paste the key manually for now.");
            }

            // Check the main bundle(s) first — the key lives in the app code, not vendor chunks.
            foreach (var scriptPath in scripts.OrderByDescending(path => path.Contains("main", StringComparison.OrdinalIgnoreCase)))
            {
                var bundleUrl = new Uri(new Uri(MyTreeUrl), scriptPath).ToString();
                string bundle;
                try
                {
                    bundle = await GetStringAsync(bundleUrl, cancellationToken);
                }
                catch (Exception)
                {
                    continue;
                }

                if (TrySelectKey(bundle, out var key))
                {
                    return new ITreeMyTreeKeyResult(key, true, null);
                }
            }

            return Failure("Reached MyTree but could not find the API key in its scripts — the site may have changed. Paste the key manually for now.");
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception exception)
        {
            return Failure($"Could not reach MyTree ({exception.Message}). Check the internet connection, or paste the key manually.");
        }
    }

    /// <summary>
    /// Picks the API key out of a blob of text. If several GUIDs are present, prefer the one nearest
    /// a "benefit" / "api.itreetools" reference, since a bundle can contain unrelated GUIDs.
    /// </summary>
    private static bool TrySelectKey(string content, out string key)
    {
        key = string.Empty;
        var matches = GuidRegex.Matches(content);
        if (matches.Count == 0)
        {
            return false;
        }

        if (matches.Count == 1)
        {
            key = matches[0].Value;
            return true;
        }

        var anchors = new[] { "benefit", "api.itreetools" }
            .SelectMany(term => AllIndexesOf(content, term))
            .ToList();

        var best = matches
            .Cast<Match>()
            .OrderBy(match => anchors.Count == 0 ? 0 : anchors.Min(anchor => Math.Abs(anchor - match.Index)))
            .First();

        key = best.Value;
        return true;
    }

    private static IEnumerable<int> AllIndexesOf(string haystack, string needle)
    {
        var index = haystack.IndexOf(needle, StringComparison.OrdinalIgnoreCase);
        while (index >= 0)
        {
            yield return index;
            index = haystack.IndexOf(needle, index + needle.Length, StringComparison.OrdinalIgnoreCase);
        }
    }

    private async Task<string> GetStringAsync(string url, CancellationToken cancellationToken)
    {
        using var response = await HttpClient.GetAsync(url, cancellationToken);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadAsStringAsync(cancellationToken);
    }

    private static ITreeMyTreeKeyResult Failure(string error) => new(null, false, error);
}
