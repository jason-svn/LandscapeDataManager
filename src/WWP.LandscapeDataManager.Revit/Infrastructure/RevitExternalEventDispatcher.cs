using System.Collections.Concurrent;
using Autodesk.Revit.UI;

namespace WWP.LandscapeDataManager.Revit.Infrastructure;

internal sealed class RevitExternalEventDispatcher : IExternalEventHandler, IDisposable
{
    private readonly ConcurrentQueue<IRevitRequest> _requests = new();
    private readonly ExternalEvent _externalEvent;

    public RevitExternalEventDispatcher()
    {
        _externalEvent = ExternalEvent.Create(this);
    }

    public Task<T> RunAsync<T>(Func<UIApplication, T> operation)
    {
        var request = new RevitRequest<T>(operation);
        _requests.Enqueue(request);
        _externalEvent.Raise();
        return request.Task;
    }

    public void Execute(UIApplication application)
    {
        while (_requests.TryDequeue(out var request))
        {
            request.Execute(application);
        }
    }

    public string GetName() => "WWP Landscape Data request dispatcher";

    public void Dispose() => _externalEvent.Dispose();

    private interface IRevitRequest
    {
        void Execute(UIApplication application);
    }

    private sealed class RevitRequest<T>(Func<UIApplication, T> operation) : IRevitRequest
    {
        private readonly TaskCompletionSource<T> _completion =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public Task<T> Task => _completion.Task;

        public void Execute(UIApplication application)
        {
            try
            {
                _completion.TrySetResult(operation(application));
            }
            catch (Exception exception)
            {
                _completion.TrySetException(exception);
            }
        }
    }
}
