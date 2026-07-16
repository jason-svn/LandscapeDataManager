using System.IO.Pipes;
using System.Text.Json;
using WWP.LandscapeDataManager.Contracts;

namespace WWP.LandscapeDataManager.App.Services;

internal sealed class RevitPipeClient(string pipeName) : IAsyncDisposable
{
    private readonly SemaphoreSlim _sendLock = new(1, 1);
    private NamedPipeClientStream? _pipe;
    private StreamReader? _reader;
    private StreamWriter? _writer;

    public bool IsConnected => _pipe?.IsConnected == true;

    public async Task ConnectAsync(CancellationToken cancellationToken = default)
    {
        if (IsConnected)
        {
            return;
        }

        _pipe = new NamedPipeClientStream(
            ".",
            pipeName,
            PipeDirection.InOut,
            PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(8));
        await _pipe.ConnectAsync(timeout.Token);

        _reader = new StreamReader(_pipe, leaveOpen: true);
        _writer = new StreamWriter(_pipe, leaveOpen: true) { AutoFlush = true };
    }

    public async Task<T> SendAsync<T>(
        string command,
        object? payload = null,
        CancellationToken cancellationToken = default)
    {
        await ConnectAsync(cancellationToken);
        await _sendLock.WaitAsync(cancellationToken);

        try
        {
            var requestId = Guid.NewGuid().ToString("N");
            var request = new PipeRequest(
                requestId,
                command,
                payload is null ? null : JsonDefaults.ToElement(payload));

            await _writer!.WriteLineAsync(
                JsonSerializer.Serialize(request, JsonDefaults.Options));

            var line = await _reader!.ReadLineAsync(cancellationToken)
                       ?? throw new IOException("Revit closed the connection.");
            var response = JsonSerializer.Deserialize<PipeResponse>(line, JsonDefaults.Options)
                           ?? throw new InvalidDataException("Revit returned an empty response.");

            if (!response.Success)
            {
                throw new InvalidOperationException(response.Error ?? "The Revit request failed.");
            }

            if (response.Data is null)
            {
                throw new InvalidDataException("Revit did not return response data.");
            }

            return response.Data.Value.Deserialize<T>(JsonDefaults.Options)
                   ?? throw new InvalidDataException("The Revit response could not be read.");
        }
        finally
        {
            _sendLock.Release();
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (_writer is not null)
        {
            await _writer.DisposeAsync();
        }

        _reader?.Dispose();
        _pipe?.Dispose();
        _sendLock.Dispose();
    }
}
