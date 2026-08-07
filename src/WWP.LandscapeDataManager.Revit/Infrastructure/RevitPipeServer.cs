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
                PipeCommands.PreviewSharedParameters => await PreviewSharedParametersAsync(request).ConfigureAwait(false),
                PipeCommands.EnsureSharedParameters => await EnsureSharedParametersAsync(request).ConfigureAwait(false),
                PipeCommands.ScanPlantingInstances => await ScanPlantingInstancesAsync(request).ConfigureAwait(false),
                PipeCommands.PreviewInstanceParameterWrites => await PreviewInstanceParameterWritesAsync(request).ConfigureAwait(false),
                PipeCommands.ApplyInstanceParameterWrites => await ApplyInstanceParameterWritesAsync(request).ConfigureAwait(false),
                PipeCommands.PairSelectedInstance => await PairSelectedInstanceAsync(request).ConfigureAwait(false),
                PipeCommands.UpdateSpeciesCatalogue => await UpdateSpeciesCatalogueAsync(request).ConfigureAwait(false),
                PipeCommands.ValidatePlantingInstances => await ValidatePlantingInstancesAsync(request).ConfigureAwait(false),
                PipeCommands.SelectElements => await SelectElementsAsync(request).ConfigureAwait(false),
                PipeCommands.ZoomToElements => await ZoomToElementsAsync(request).ConfigureAwait(false),
                PipeCommands.IsolateElements => await IsolateElementsAsync(request).ConfigureAwait(false),
                PipeCommands.ResetIsolation => await ResetIsolationAsync(request).ConfigureAwait(false),
                PipeCommands.ApplyStatusColourOverrides => await ApplyStatusColourOverridesAsync(request).ConfigureAwait(false),
                PipeCommands.ResetColourOverrides => await ResetColourOverridesAsync(request).ConfigureAwait(false),
                PipeCommands.GetSelectedPlantingTypes => await GetSelectedPlantingTypesAsync(request).ConfigureAwait(false),
                PipeCommands.AssignSpeciesBatch => await AssignSpeciesBatchAsync(request).ConfigureAwait(false),
                PipeCommands.GetProjectSiteLocation => await GetProjectSiteLocationAsync(request).ConfigureAwait(false),
                PipeCommands.PublishProjectLocation => await PublishProjectLocationAsync(request).ConfigureAwait(false),
                PipeCommands.PublishPreferredCurrency => await PublishPreferredCurrencyAsync(request).ConfigureAwait(false),
                PipeCommands.GetProjectSettingsJson => await GetProjectSettingsJsonAsync(request).ConfigureAwait(false),
                PipeCommands.PublishProjectSettingsJson => await PublishProjectSettingsJsonAsync(request).ConfigureAwait(false),
                PipeCommands.GetSelectedFloors => await GetSelectedFloorsAsync(request).ConfigureAwait(false),
                PipeCommands.CalculateFloorsBatch => await CalculateFloorsBatchAsync(request).ConfigureAwait(false),
                PipeCommands.RunHealthCheck => await RunHealthCheckAsync(request).ConfigureAwait(false),
                PipeCommands.GetDashboardReport => await GetDashboardReportAsync(request).ConfigureAwait(false),
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

    private async Task<PipeResponse> PreviewSharedParametersAsync(PipeRequest request)
    {
        var options = request.Payload?.Deserialize<PreviewSharedParametersRequest>(JsonDefaults.Options)
                      ?? throw new InvalidDataException("The shared parameter file path was empty.");
        var result = await _dispatcher.RunAsync(application =>
            SharedParameterSetupService.PreviewParameters(application, options.SharedParameterFilePath)).ConfigureAwait(false);
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

    private async Task<PipeResponse> ScanPlantingInstancesAsync(PipeRequest request)
    {
        var result = await _dispatcher.RunAsync(RevitModelScanner.ScanPlantingInstances).ConfigureAwait(false);
        return new PipeResponse(request.RequestId, true, JsonDefaults.ToElement(result));
    }

    private async Task<PipeResponse> PreviewInstanceParameterWritesAsync(PipeRequest request)
    {
        var batch = request.Payload?.Deserialize<InstanceParameterWriteBatch>(JsonDefaults.Options)
                    ?? throw new InvalidDataException("The instance parameter-write preview was empty.");
        var result = await _dispatcher.RunAsync(application =>
            InstanceParameterWriter.Preview(application, batch)).ConfigureAwait(false);
        return new PipeResponse(request.RequestId, true, JsonDefaults.ToElement(result));
    }

    private async Task<PipeResponse> ApplyInstanceParameterWritesAsync(PipeRequest request)
    {
        var batch = request.Payload?.Deserialize<InstanceParameterWriteBatch>(JsonDefaults.Options)
                    ?? throw new InvalidDataException("The instance parameter-write batch was empty.");
        var result = await _dispatcher.RunAsync(application =>
            InstanceParameterWriter.Apply(application, batch)).ConfigureAwait(false);
        return new PipeResponse(request.RequestId, true, JsonDefaults.ToElement(result));
    }

    private async Task<PipeResponse> PairSelectedInstanceAsync(PipeRequest request)
    {
        var options = request.Payload?.Deserialize<PairSelectedInstanceRequest>(JsonDefaults.Options)
                      ?? throw new InvalidDataException("The pairing request was empty.");
        var result = await _dispatcher.RunAsync(application =>
            InstancePairingService.PairSelected(application, options)).ConfigureAwait(false);
        return new PipeResponse(request.RequestId, true, JsonDefaults.ToElement(result));
    }

    private async Task<PipeResponse> UpdateSpeciesCatalogueAsync(PipeRequest request)
    {
        var options = request.Payload?.Deserialize<UpdateSpeciesCatalogueRequest>(JsonDefaults.Options)
                      ?? throw new InvalidDataException("The species catalogue update request was empty.");
        var result = await _dispatcher.RunAsync(application =>
            SpeciesCatalogueWriter.Update(application, options)).ConfigureAwait(false);
        return new PipeResponse(request.RequestId, true, JsonDefaults.ToElement(result));
    }

    private async Task<PipeResponse> GetSelectedPlantingTypesAsync(PipeRequest request)
    {
        var result = await _dispatcher.RunAsync(SpeciesCatalogueWriter.GetSelectedTypes).ConfigureAwait(false);
        return new PipeResponse(request.RequestId, true, JsonDefaults.ToElement(result));
    }

    private async Task<PipeResponse> AssignSpeciesBatchAsync(PipeRequest request)
    {
        var options = request.Payload?.Deserialize<AssignSpeciesBatchRequest>(JsonDefaults.Options)
                      ?? throw new InvalidDataException("The species assignment batch was empty.");
        var result = await _dispatcher.RunAsync(application =>
            SpeciesCatalogueWriter.AssignBatch(application, options)).ConfigureAwait(false);
        return new PipeResponse(request.RequestId, true, JsonDefaults.ToElement(result));
    }

    private async Task<PipeResponse> GetProjectSiteLocationAsync(PipeRequest request)
    {
        var result = await _dispatcher.RunAsync(ProjectLocationService.GetSiteLocation).ConfigureAwait(false);
        return new PipeResponse(request.RequestId, true, JsonDefaults.ToElement(result));
    }

    private async Task<PipeResponse> PublishProjectLocationAsync(PipeRequest request)
    {
        var options = request.Payload?.Deserialize<PublishProjectLocationRequest>(JsonDefaults.Options)
                      ?? throw new InvalidDataException("The project location was empty.");
        var result = await _dispatcher.RunAsync(application =>
            ProjectLocationService.Publish(application, options)).ConfigureAwait(false);
        return new PipeResponse(request.RequestId, true, JsonDefaults.ToElement(result));
    }

    private async Task<PipeResponse> PublishPreferredCurrencyAsync(PipeRequest request)
    {
        var options = request.Payload?.Deserialize<PublishPreferredCurrencyRequest>(JsonDefaults.Options)
                      ?? throw new InvalidDataException("The currency preference was empty.");
        var result = await _dispatcher.RunAsync(application =>
            ProjectPreferencesService.PublishPreferredCurrency(application, options)).ConfigureAwait(false);
        return new PipeResponse(request.RequestId, true, JsonDefaults.ToElement(result));
    }

    private async Task<PipeResponse> GetProjectSettingsJsonAsync(PipeRequest request)
    {
        var result = await _dispatcher.RunAsync(ProjectPreferencesService.GetSettingsJson).ConfigureAwait(false);
        return new PipeResponse(request.RequestId, true, JsonDefaults.ToElement(result));
    }

    private async Task<PipeResponse> PublishProjectSettingsJsonAsync(PipeRequest request)
    {
        var options = request.Payload?.Deserialize<PublishProjectSettingsJsonRequest>(JsonDefaults.Options)
                      ?? throw new InvalidDataException("The project settings JSON was empty.");
        var result = await _dispatcher.RunAsync(application =>
            ProjectPreferencesService.PublishSettingsJson(application, options)).ConfigureAwait(false);
        return new PipeResponse(request.RequestId, true, JsonDefaults.ToElement(result));
    }

    private async Task<PipeResponse> GetSelectedFloorsAsync(PipeRequest request)
    {
        var result = await _dispatcher.RunAsync(FloorLdsCalculationService.GetSelectedFloors).ConfigureAwait(false);
        return new PipeResponse(request.RequestId, true, JsonDefaults.ToElement(result));
    }

    private async Task<PipeResponse> CalculateFloorsBatchAsync(PipeRequest request)
    {
        var options = request.Payload?.Deserialize<CalculateFloorsBatchRequest>(JsonDefaults.Options)
                      ?? throw new InvalidDataException("The floor calculation batch was empty.");
        var result = await _dispatcher.RunAsync(application =>
            FloorLdsCalculationService.CalculateBatch(application, options)).ConfigureAwait(false);
        return new PipeResponse(request.RequestId, true, JsonDefaults.ToElement(result));
    }

    private async Task<PipeResponse> RunHealthCheckAsync(PipeRequest request)
    {
        var result = await _dispatcher.RunAsync(HealthCheckScanner.Scan).ConfigureAwait(false);
        return new PipeResponse(request.RequestId, true, JsonDefaults.ToElement(result));
    }

    private async Task<PipeResponse> GetDashboardReportAsync(PipeRequest request)
    {
        var options = request.Payload?.Deserialize<DashboardReportRequest>(JsonDefaults.Options)
                      ?? new DashboardReportRequest();
        var result = await _dispatcher.RunAsync(application =>
            DashboardReportService.GetReport(application, options)).ConfigureAwait(false);
        return new PipeResponse(request.RequestId, true, JsonDefaults.ToElement(result));
    }

    private async Task<PipeResponse> ValidatePlantingInstancesAsync(PipeRequest request)
    {
        var options = request.Payload?.Deserialize<ValidatePlantingInstancesRequest>(JsonDefaults.Options)
                      ?? new ValidatePlantingInstancesRequest();
        var result = await _dispatcher.RunAsync(application =>
            PlantingInstanceValidationScanner.Scan(application, options.SelectedOnly)).ConfigureAwait(false);
        return new PipeResponse(request.RequestId, true, JsonDefaults.ToElement(result));
    }

    private async Task<PipeResponse> SelectElementsAsync(PipeRequest request)
    {
        var options = request.Payload?.Deserialize<ElementSelectionRequest>(JsonDefaults.Options)
                      ?? throw new InvalidDataException("The selection request was empty.");
        var result = await _dispatcher.RunAsync(application =>
            RevitViewInteractionService.Select(application, options)).ConfigureAwait(false);
        return new PipeResponse(request.RequestId, true, JsonDefaults.ToElement(result));
    }

    private async Task<PipeResponse> ZoomToElementsAsync(PipeRequest request)
    {
        var options = request.Payload?.Deserialize<ElementSelectionRequest>(JsonDefaults.Options)
                      ?? throw new InvalidDataException("The zoom request was empty.");
        var result = await _dispatcher.RunAsync(application =>
            RevitViewInteractionService.Zoom(application, options)).ConfigureAwait(false);
        return new PipeResponse(request.RequestId, true, JsonDefaults.ToElement(result));
    }

    private async Task<PipeResponse> IsolateElementsAsync(PipeRequest request)
    {
        var options = request.Payload?.Deserialize<ElementSelectionRequest>(JsonDefaults.Options)
                      ?? throw new InvalidDataException("The isolate request was empty.");
        var result = await _dispatcher.RunAsync(application =>
            RevitViewInteractionService.Isolate(application, options)).ConfigureAwait(false);
        return new PipeResponse(request.RequestId, true, JsonDefaults.ToElement(result));
    }

    private async Task<PipeResponse> ResetIsolationAsync(PipeRequest request)
    {
        var result = await _dispatcher.RunAsync(RevitViewInteractionService.ResetIsolation).ConfigureAwait(false);
        return new PipeResponse(request.RequestId, true, JsonDefaults.ToElement(result));
    }

    private async Task<PipeResponse> ApplyStatusColourOverridesAsync(PipeRequest request)
    {
        var options = request.Payload?.Deserialize<StatusColourOverrideRequest>(JsonDefaults.Options)
                      ?? throw new InvalidDataException("The colour override request was empty.");
        var result = await _dispatcher.RunAsync(application =>
            RevitViewInteractionService.ApplyColourOverrides(application, options)).ConfigureAwait(false);
        return new PipeResponse(request.RequestId, true, JsonDefaults.ToElement(result));
    }

    private async Task<PipeResponse> ResetColourOverridesAsync(PipeRequest request)
    {
        var options = request.Payload?.Deserialize<ElementSelectionRequest>(JsonDefaults.Options)
                      ?? throw new InvalidDataException("The reset-overrides request was empty.");
        var result = await _dispatcher.RunAsync(application =>
            RevitViewInteractionService.ResetColourOverrides(application, options)).ConfigureAwait(false);
        return new PipeResponse(request.RequestId, true, JsonDefaults.ToElement(result));
    }

    public void Dispose()
    {
        _cancellation.Cancel();
        _cancellation.Dispose();
    }
}
