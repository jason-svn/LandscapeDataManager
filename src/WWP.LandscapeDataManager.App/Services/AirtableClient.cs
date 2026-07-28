using System.Text;
using System.Text.Json;
using Microsoft.UI.Xaml.Controls;
using Microsoft.Web.WebView2.Core;
using WWP.LandscapeDataManager.Shared.Models;

namespace WWP.LandscapeDataManager.App.Services;

/// <summary>
/// Drives a WinUI WebView2 control. This stays in the exe project (not Shared) because the
/// Windows App SDK build resolves WebView2's CsWinRT projection assembly, which is a different
/// assembly identity than the plain Microsoft.Web.WebView2.Core.dll a non-WinUI class library
/// would resolve — the two CoreWebView2 types are not interchangeable across that boundary.
/// </summary>
internal sealed class AirtableClient
{
    private static readonly TimeSpan PageLoadTimeout = TimeSpan.FromSeconds(30);
    private static readonly TimeSpan DownloadTimeout = TimeSpan.FromSeconds(30);
    private readonly WebView2 _webView;

    public AirtableClient(WebView2 webView)
    {
        _webView = webView;
    }

    public async Task<IReadOnlyList<AirtableRecord>> GetRecordsAsync(
        string sharedLink,
        CancellationToken cancellationToken = default)
    {
        var sharedUri = ValidateSharedLink(sharedLink);
        await _webView.EnsureCoreWebView2Async();
        var core = _webView.CoreWebView2
                   ?? throw new InvalidOperationException("The Airtable browser could not be initialized.");
        core.Settings.AreDevToolsEnabled = false;
        core.Settings.AreDefaultContextMenusEnabled = false;
        core.Settings.IsStatusBarEnabled = false;

        await NavigateAsync(core, sharedUri, cancellationToken);
        await WaitForElementAsync(
            core,
            "[data-tutorial-selector-id='viewTopBarMenu']",
            PageLoadTimeout,
            cancellationToken);

        var csv = await DownloadCsvAsync(core, cancellationToken);
        return ParseRecords(csv);
    }

    private async Task NavigateAsync(
        CoreWebView2 core,
        Uri uri,
        CancellationToken cancellationToken)
    {
        var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        void NavigationCompleted(object? sender, CoreWebView2NavigationCompletedEventArgs args)
        {
            if (args.IsSuccess)
            {
                completion.TrySetResult();
            }
            else
            {
                completion.TrySetException(
                    new HttpRequestException($"Airtable navigation failed: {args.WebErrorStatus}."));
            }
        }

        core.NavigationCompleted += NavigationCompleted;
        try
        {
            core.Navigate(uri.AbsoluteUri);
            await completion.Task.WaitAsync(PageLoadTimeout, cancellationToken);
        }
        finally
        {
            core.NavigationCompleted -= NavigationCompleted;
        }
    }

    private async Task<string> DownloadCsvAsync(
        CoreWebView2 core,
        CancellationToken cancellationToken)
    {
        var temporaryFile = Path.Combine(
            Path.GetTempPath(),
            $"WWP-Landscape-{Guid.NewGuid():N}.csv");
        var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        CoreWebView2DownloadOperation? activeDownload = null;

        void DownloadStateChanged(object? sender, object args)
        {
            if (activeDownload is null)
            {
                return;
            }

            if (activeDownload.State == CoreWebView2DownloadState.Completed)
            {
                completion.TrySetResult();
            }
            else if (activeDownload.State == CoreWebView2DownloadState.Interrupted)
            {
                completion.TrySetException(
                    new IOException($"Airtable CSV download was interrupted: {activeDownload.InterruptReason}."));
            }
        }

        void DownloadStarting(object? sender, CoreWebView2DownloadStartingEventArgs args)
        {
            activeDownload = args.DownloadOperation;
            activeDownload.StateChanged += DownloadStateChanged;
            args.ResultFilePath = temporaryFile;
            args.Handled = true;
        }

        core.DownloadStarting += DownloadStarting;
        try
        {
            await core.ExecuteScriptAsync(
                "document.querySelector(\"[data-tutorial-selector-id='viewTopBarMenu']\")?.click();");
            await WaitForElementAsync(
                core,
                "[data-tutorial-selector-id='viewMenuItem-viewExportCsv']",
                TimeSpan.FromSeconds(5),
                cancellationToken);
            await core.ExecuteScriptAsync(
                "document.querySelector(\"[data-tutorial-selector-id='viewMenuItem-viewExportCsv']\")?.click();");

            await completion.Task.WaitAsync(DownloadTimeout, cancellationToken);
            return await File.ReadAllTextAsync(temporaryFile, cancellationToken);
        }
        finally
        {
            core.DownloadStarting -= DownloadStarting;
            if (activeDownload is not null)
            {
                activeDownload.StateChanged -= DownloadStateChanged;
            }

            try
            {
                File.Delete(temporaryFile);
            }
            catch (IOException)
            {
                // The operating system will clean up an isolated temporary file if WebView2 still holds it.
            }
        }
    }

    private static async Task WaitForElementAsync(
        CoreWebView2 core,
        string selector,
        TimeSpan timeout,
        CancellationToken cancellationToken)
    {
        var deadline = DateTimeOffset.UtcNow + timeout;
        var escapedSelector = JsonSerializer.Serialize(selector);

        while (DateTimeOffset.UtcNow < deadline)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var result = await core.ExecuteScriptAsync(
                $"Boolean(document.querySelector({escapedSelector}))");
            if (string.Equals(result, "true", StringComparison.OrdinalIgnoreCase))
            {
                return;
            }

            await Task.Delay(150, cancellationToken);
        }

        throw new InvalidOperationException(
            "The Airtable shared view did not expose its CSV download. Confirm that the link is public and that copying data is allowed.");
    }

    private static Uri ValidateSharedLink(string sharedLink)
    {
        if (!Uri.TryCreate(sharedLink.Trim(), UriKind.Absolute, out var uri) ||
            uri.Scheme != Uri.UriSchemeHttps ||
            !string.Equals(uri.Host, "airtable.com", StringComparison.OrdinalIgnoreCase) ||
            !uri.AbsolutePath.Split('/', StringSplitOptions.RemoveEmptyEntries)
                .Any(segment => segment.StartsWith("shr", StringComparison.OrdinalIgnoreCase)))
        {
            throw new ArgumentException(
                "Paste a public Airtable share link, for example https://airtable.com/app.../shr... .",
                nameof(sharedLink));
        }

        return uri;
    }

    private static IReadOnlyList<AirtableRecord> ParseRecords(string csv)
    {
        var rows = ParseCsv(csv);
        if (rows.Count == 0 || rows[0].Count == 0)
        {
            throw new InvalidDataException("Airtable returned an empty CSV file.");
        }

        var headers = rows[0]
            .Select(header => header.TrimStart('\uFEFF').Trim())
            .ToList();
        if (headers.Any(string.IsNullOrWhiteSpace))
        {
            throw new InvalidDataException("The Airtable view contains a column without a header.");
        }

        var duplicateHeader = headers
            .GroupBy(header => header, StringComparer.OrdinalIgnoreCase)
            .FirstOrDefault(group => group.Count() > 1);
        if (duplicateHeader is not null)
        {
            throw new InvalidDataException(
                $"The Airtable view contains the duplicate header '{duplicateHeader.Key}'.");
        }

        var records = new List<AirtableRecord>(Math.Max(0, rows.Count - 1));
        for (var rowIndex = 1; rowIndex < rows.Count; rowIndex++)
        {
            var row = rows[rowIndex];
            if (row.All(string.IsNullOrWhiteSpace))
            {
                continue;
            }

            var fields = new Dictionary<string, JsonElement>(StringComparer.OrdinalIgnoreCase);
            for (var columnIndex = 0; columnIndex < headers.Count; columnIndex++)
            {
                var value = columnIndex < row.Count ? row[columnIndex] : string.Empty;
                fields[headers[columnIndex]] = JsonSerializer.SerializeToElement(value);
            }

            records.Add(new AirtableRecord($"shared-{rowIndex}", fields));
        }

        return records;
    }

    private static IReadOnlyList<IReadOnlyList<string>> ParseCsv(string csv)
    {
        var rows = new List<IReadOnlyList<string>>();
        var row = new List<string>();
        var field = new StringBuilder();
        var insideQuotes = false;

        for (var index = 0; index < csv.Length; index++)
        {
            var character = csv[index];
            if (insideQuotes)
            {
                if (character == '"')
                {
                    if (index + 1 < csv.Length && csv[index + 1] == '"')
                    {
                        field.Append('"');
                        index++;
                    }
                    else
                    {
                        insideQuotes = false;
                    }
                }
                else
                {
                    field.Append(character);
                }

                continue;
            }

            switch (character)
            {
                case '"':
                    insideQuotes = true;
                    break;
                case ',':
                    row.Add(field.ToString());
                    field.Clear();
                    break;
                case '\r':
                    if (index + 1 < csv.Length && csv[index + 1] == '\n')
                    {
                        index++;
                    }

                    CompleteRow();
                    break;
                case '\n':
                    CompleteRow();
                    break;
                default:
                    field.Append(character);
                    break;
            }
        }

        if (insideQuotes)
        {
            throw new InvalidDataException("Airtable returned malformed CSV data with an unterminated quoted field.");
        }

        if (field.Length > 0 || row.Count > 0)
        {
            CompleteRow();
        }

        return rows;

        void CompleteRow()
        {
            row.Add(field.ToString());
            field.Clear();
            rows.Add(row);
            row = [];
        }
    }
}
