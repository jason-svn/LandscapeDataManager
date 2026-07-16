using System.IO.Pipes;
using System.Text.Json;
using Autodesk.Revit.UI;
using WWP.LandscapeDataManager.Contracts;
using WWP.LandscapeDataManager.Revit.Services;

namespace WWP.LandscapeDataManager.Revit.Infrastructure;

internal sealed class RevitPipeServer : IDisposable
{
    private readonly RevitExternalEventDispatcher _dispatcher;
    private readonly CancellationTokenSource _cancellation = new();
    private Task? _serverTask;

    public RevitPipeServer(RevitExternalEventDispatcher dispatcher)
    {
        _dispatcher = dispatcher;
        PipeName = $"WWP.LandscapeDataManager.{Environment.ProcessId}";
    }

    public string PipeName { get; }

    public void Start()
    {
        _serverTask ??= Task.Run(() => ListenAsync(_cancellation.Token));
    }

    private async Task ListenAsync(CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                await using var pipe = new NamedPipeServerStream(
                    PipeName,
                    PipeDirection.InOut,
                    1,
                    PipeTransmissionMode.Byte,
                    PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);

                await pipe.WaitForConnectionAsync(cancellationToken).ConfigureAwait(false);
                using var reader = new StreamReader(pipe, leaveOpen: true);
                await using var writer = new StreamWriter(pipe, leaveOpen: true) { AutoFlush = true };

                while (pipe.IsConnected && !cancellationToken.IsCancellationRequested)
                {
                    var line = await reader.ReadLineAsync(cancellationToken).ConfigureAwait(false);
                    if (line is null)
                    {
                        break;
                    }

                    var response = await HandleRequestAsync(line).ConfigureAwait(false);
                    await writer.WriteLineAsync(
                        JsonSerializer.Serialize(response, JsonDefaults.Options)).ConfigureAwait(false);
                }
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                break;
            }
            catch
            {
                // A new server instance is created after a disconnected or malformed client.
            }
        }
    }

    private async Task<PipeResponse> HandleRequestAsync(string message)
    {
        PipeRequest? request = null;

        try
        {
            request = JsonSerializer.Deserialize<PipeRequest>(message, JsonDefaults.Options)
                      ?? throw new InvalidDataException("The request was empty.");

            return request.Command switch
            {
                PipeCommands.GetStatus => await GetStatusAsync(request).ConfigureAwait(false),
                PipeCommands.ScanModel => await ScanModelAsync(request).ConfigureAwait(false),
                PipeCommands.GetParameterCatalog => await GetParameterCatalogAsync(request).ConfigureAwait(false),
                _ => new PipeResponse(request.RequestId, false, Error: $"Unknown command: {request.Command}")
            };
        }
        catch (Exception exception)
        {
            return new PipeResponse(request?.RequestId ?? string.Empty, false, Error: exception.Message);
        }
    }

    private async Task<PipeResponse> GetStatusAsync(PipeRequest request)
    {
        var status = await _dispatcher.RunAsync(application =>
        {
            var document = application.ActiveUIDocument?.Document;
            return new RevitStatus(
                application.Application.VersionNumber,
                document is not null,
                document?.Title);
        }).ConfigureAwait(false);

        return new PipeResponse(request.RequestId, true, JsonDefaults.ToElement(status));
    }

    private async Task<PipeResponse> ScanModelAsync(PipeRequest request)
    {
        var options = request.Payload?.Deserialize<ModelScanOptions>(JsonDefaults.Options)
                      ?? new ModelScanOptions();
        var result = await _dispatcher.RunAsync(application =>
            RevitModelScanner.Scan(application, options)).ConfigureAwait(false);

        return new PipeResponse(request.RequestId, true, JsonDefaults.ToElement(result));
    }

    private async Task<PipeResponse> GetParameterCatalogAsync(PipeRequest request)
    {
        var options = request.Payload?.Deserialize<ModelScanOptions>(JsonDefaults.Options)
                      ?? new ModelScanOptions();
        var result = await _dispatcher.RunAsync(application =>
            RevitModelScanner.GetParameterCatalog(application, options)).ConfigureAwait(false);

        return new PipeResponse(request.RequestId, true, JsonDefaults.ToElement(result));
    }

    public void Dispose()
    {
        _cancellation.Cancel();
        _cancellation.Dispose();
    }
}
