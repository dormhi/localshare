using Microsoft.UI.Xaml;

namespace TabletDisplay.Host;

public sealed partial class MainWindow : Window
{
    private AdbSession? _session;
    private DisplayLease? _display;
    private VideoSession? _video;
    private CancellationTokenSource? _connectCancellation;
    private bool _closed;
    private bool _stopping;

    public MainWindow()
    {
        InitializeComponent();
        Closed += async (_, _) =>
        {
            _closed = true;
            _connectCancellation?.Cancel();
            await StopSessionAsync();
        };
    }

    private async void ConnectButton_Click(object sender, RoutedEventArgs e)
    {
        if (_session is not null || _connectCancellation is not null || _closed || _stopping) return;
        ConnectButton.IsEnabled = false;
        VirtualDisplayOption.IsEnabled = false;
        TestPatternOption.IsEnabled = false;
        StreamFpsLimit.IsEnabled = false;
        ConnectionStatus.Text = "Checking USB debugging authorization…";
        var cancellation = new CancellationTokenSource();
        _connectCancellation = cancellation;
        AdbSession? connecting = null;
        DisplayLease? display = null;
        VideoSession? video = null;
        try
        {
            connecting = await AdbSession.ConnectAsync(cancellation.Token);
            if (VirtualDisplayOption.IsChecked == true)
            {
                // No driver installation or elevation is performed here.
                display = await DisplayLease.StartAsync(DriverControl.Open(), cancellation.Token);
                connecting.HeartbeatAcknowledged = display.RenewAsync;
            }
            if (TestPatternOption.IsChecked == true)
            {
                var value = StreamFpsLimit.Value;
                if (!double.IsFinite(value) || value != Math.Floor(value) || value is < 1 or > VideoWire.MaxFps)
                    throw new InvalidOperationException("Choose a positive whole-number FPS limit.");
                var candidate = connecting;
                video = await VideoSession.StartAsync(connecting, (int)value, message =>
                    DispatcherQueue.TryEnqueue(() => { if (!_closed && ReferenceEquals(_session, candidate)) VideoStatus.Text = message; }),
                    cancellation.Token);
            }
            cancellation.Token.ThrowIfCancellationRequested();
            _session = connecting;
            _display = display;
            _video = video;
            video = null;
            connecting = null;
            display = null;
            ConnectionStatus.Text = _video is not null ? "USB connected; generated test pattern streaming. Real desktop video is not connected yet." : _display is null
                ? "USB handshake connected. Virtual display is disabled."
                : "Virtual monitor requested. Select Extend in Windows Display Settings; tablet video is not available yet.";
            DisconnectButton.IsEnabled = true;
            _ = WatchSessionAsync(_session, _video);
        }
        catch (OperationCanceledException)
        {
            if (!_closed) ConnectionStatus.Text = "Connection cancelled.";
        }
        catch (Exception ex)
        {
            if (!_closed) ConnectionStatus.Text = ex.Message;
        }
        finally
        {
            if (connecting is not null) connecting.HeartbeatAcknowledged = null;
            try { if (video is not null) await video.DisposeAsync(); }
            catch (Exception ex) { if (!_closed) VideoStatus.Text = $"Video cleanup failed: {ex.Message}"; }
            try { if (display is not null) await display.DisposeAsync(); }
            catch (Exception ex) { if (!_closed) ConnectionStatus.Text = $"Display cleanup failed: {ex.Message}"; }
            if (connecting is not null) await connecting.DisposeAsync();
            cancellation.Dispose();
            _connectCancellation = null;
            if (!_closed && _session is null && !_stopping)
            {
                ConnectButton.IsEnabled = true;
                VirtualDisplayOption.IsEnabled = true;
                TestPatternOption.IsEnabled = true;
                StreamFpsLimit.IsEnabled = true;
            }
        }
    }

    private async void DisconnectButton_Click(object sender, RoutedEventArgs e)
    {
        DisconnectButton.IsEnabled = false;
        await StopSessionAsync();
    }

    private async Task WatchSessionAsync(AdbSession session, VideoSession? video)
    {
        string? error = null;
        try {
            if (video is null) await session.Completion;
            else await await Task.WhenAny(session.Completion, video.Completion);
        }
        catch (Exception ex) { error = DescribeSessionEnd(ex); }
        if (!ReferenceEquals(_session, session)) return;
        await StopSessionAsync(error);
    }

    // Raw socket messages (for example Windows error 10053) mean nothing to most users.
    // Losing the tablet connection is an expected event: say what happened and what to do.
    private static string DescribeSessionEnd(Exception ex) => ex switch
    {
        OperationCanceledException => "the tablet stopped answering (no heartbeat). Check that the tablet is awake and the cable is connected, then connect again.",
        // Only transport failures; the video session also uses IOException for its own diagnostics.
        System.Net.Sockets.SocketException or EndOfStreamException or IOException { InnerException: System.Net.Sockets.SocketException } =>
            "the tablet was disconnected (USB cable removed, the tablet app was closed or restarted, the tablet was locked, or USB debugging was turned off). Reconnect it and select Connect tablet.",
        _ => ex.Message,
    };

    private async Task StopSessionAsync(string? error = null)
    {
        // Detach ownership before any await so Close/Disconnect/watch completion
        // cannot dispose or overwrite a subsequently connected session.
        var session = _session;
        var display = _display;
        var video = _video;
        _video = null;
        _session = null;
        _display = null;
        if (session is null && display is null && video is null) return;
        _stopping = true;
        if (session is not null) session.HeartbeatAcknowledged = null;
        if (!_closed) DisconnectButton.IsEnabled = false;
        try { if (video is not null) await video.DisposeAsync(); }
        catch (Exception ex) { error = $"Video cleanup failed: {ex.Message}"; }
        try { if (display is not null) await display.DisposeAsync(); }
        catch (Exception ex) { error = $"Display cleanup failed: {ex.Message}"; }
        finally { if (session is not null) await session.DisposeAsync(); }
        _stopping = false;
        if (!_closed)
        {
            ConnectionStatus.Text = error is null
                ? "Disconnected. Session cleanup requested."
                : $"Session ended: {error}";
            ConnectButton.IsEnabled = _connectCancellation is null;
            VirtualDisplayOption.IsEnabled = _connectCancellation is null;
            TestPatternOption.IsEnabled = _connectCancellation is null;
            StreamFpsLimit.IsEnabled = _connectCancellation is null;
        }
    }
}
