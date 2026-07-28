using System.IO;
using System.IO.Pipes;
using System.Text;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Windows.Graphics;
using WinRT.Interop;

namespace WWP.LandscapeDataManager.App;

public sealed partial class MainWindow : Window
{
    private readonly CancellationTokenSource _navigationCancellation = new();
    private readonly Task _navigationTask;

    public MainWindow(string pipeName, string workflow)
    {
        InitializeComponent();

        var windowHandle = WindowNative.GetWindowHandle(this);
        MainContent.Initialize(pipeName, windowHandle);
        MainContent.NavigateTo(workflow);
        var windowId = Microsoft.UI.Win32Interop.GetWindowIdFromWindow(windowHandle);
        var appWindow = AppWindow.GetFromWindowId(windowId);
        appWindow.Resize(new SizeInt32(1180, 780));
        appWindow.SetPresenter(AppWindowPresenterKind.Default);

        _navigationTask = ListenForNavigationAsync(
            $"{pipeName}.navigation",
            _navigationCancellation.Token);
        Closed += OnClosed;
    }

    private async void OnClosed(object sender, WindowEventArgs args)
    {
        await _navigationCancellation.CancelAsync();
        try
        {
            await _navigationTask;
        }
        catch (OperationCanceledException)
        {
            // Expected while the window and its navigation pipe are shutting down.
        }

        _navigationCancellation.Dispose();
        await MainContent.DisposeAsync();
    }

    private async Task ListenForNavigationAsync(string navigationPipeName, CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            await using var server = new NamedPipeServerStream(
                navigationPipeName,
                PipeDirection.In,
                1,
                PipeTransmissionMode.Byte,
                PipeOptions.Asynchronous);
            await server.WaitForConnectionAsync(cancellationToken);

            using var reader = new StreamReader(
                server,
                Encoding.UTF8,
                detectEncodingFromByteOrderMarks: true,
                leaveOpen: true);
            var workflow = await reader.ReadLineAsync(cancellationToken);
            if (string.IsNullOrWhiteSpace(workflow))
            {
                continue;
            }

            DispatcherQueue.TryEnqueue(() =>
            {
                MainContent.NavigateTo(workflow);
                Activate();
            });
        }
    }
}
