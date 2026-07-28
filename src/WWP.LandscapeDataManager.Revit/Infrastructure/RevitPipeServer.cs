using System.IO;
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
        _serverTask ??= Task.Run(() => AcceptLoopAsync(_cancellation.Token));
    }

    /// <summary>
    /// Each of the 5 tool executables holds its own long-lived connection, so this must accept
    /// many concurrent clients rather than one at a time. A new listening instance is created
    /// immediately after each accepted connection, and every connected client is served on its
    /// own task. Every client task still funnels Revit-API work through the single
    /// <see cref="RevitExternalEventDispatcher"/>, so Revit-API access stays fully serialized
    /// regardless of how many tool processes are connected.
    /// </summary>
    private async Task AcceptLoopAsync(CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            NamedPipeServerStream pipe;
            try
            {
                pipe = new NamedPipeServerStream(
                    PipeName,
                    PipeDirection.InOut,
                    NamedPipeServerStream.MaxAllowedServerInstances,
                    PipeTransmissionMode.Byte,
                    PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
                await pipe.WaitForConnectionAsync(cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                break;
            }
            catch
            {
                // Try again with a fresh listening instance.
                continue;
            }

            _ = ServeClientAsync(pipe, cancellationToken);
        }
    }

    private async Task ServeClientAsync(NamedPipeServerStream pipe, CancellationToken cancellationToken)
    {
        await using (pipe.ConfigureAwait(false))
        {
            try
            {
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
                // Server is shutting down.
            }
            catch
            {
                // This client disconnected or sent malformed input; other clients are unaffected.
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
                PipeCommands.PreviewParameterWrites => await PreviewParameterWritesAsync(request).ConfigureAwait(false),
                PipeCommands.ApplyParameterWrites => await ApplyParameterWritesAsync(request).ConfigureAwait(false),
                PipeCommands.GetITreeInputs => await GetITreeInputsAsync(request).ConfigureAwait(false),
                PipeCommands.EnsureSharedParameters => await EnsureSharedParametersAsync(request).ConfigureAwait(false),
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

    private async Task<PipeResponse> PreviewParameterWritesAsync(PipeRequest request)
    {
        var batch = request.Payload?.Deserialize<ParameterWriteBatch>(JsonDefaults.Options)
                    ?? throw new InvalidDataException("The parameter-write preview was empty.");
        var result = await _dispatcher.RunAsync(application =>
            RevitParameterWriter.Preview(application, batch)).ConfigureAwait(false);
        return new PipeResponse(request.RequestId, true, JsonDefaults.ToElement(result));
    }

    private async Task<PipeResponse> ApplyParameterWritesAsync(PipeRequest request)
    {
        var batch = request.Payload?.Deserialize<ParameterWriteBatch>(JsonDefaults.Options)
                    ?? throw new InvalidDataException("The parameter-write batch was empty.");
        var result = await _dispatcher.RunAsync(application =>
            RevitParameterWriter.Apply(application, batch)).ConfigureAwait(false);
        return new PipeResponse(request.RequestId, true, JsonDefaults.ToElement(result));
    }

    private async Task<PipeResponse> GetITreeInputsAsync(PipeRequest request)
    {
        var options = request.Payload?.Deserialize<ITreeInputOptions>(JsonDefaults.Options)
                      ?? new ITreeInputOptions();
        var result = await _dispatcher.RunAsync(application =>
            RevitModelScanner.GetITreeInputs(application, options)).ConfigureAwait(false);
        return new PipeResponse(request.RequestId, true, JsonDefaults.ToElement(result));
    }

    private async Task<PipeResponse> EnsureSharedParametersAsync(PipeRequest request)
    {
        var options = request.Payload?.Deserialize<EnsureSharedParametersRequest>(JsonDefaults.Options)
                      ?? throw new InvalidDataException("The shared parameter file path was empty.");
        var result = await _dispatcher.RunAsync(application =>
            SharedParameterSetupService.EnsureParameters(application, options)).ConfigureAwait(false);
        return new PipeResponse(request.RequestId, true, JsonDefaults.ToElement(result));
    }

    public void Dispose()
    {
        _cancellation.Cancel();
        _cancellation.Dispose();
    }
}
