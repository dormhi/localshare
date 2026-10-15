using System.Diagnostics;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;

namespace TabletDisplay.Host;

internal sealed class AdbSession : IAsyncDisposable
{
    private const string PackageId = "com.dormhi.localshare";
    private const string AbstractSocket = "com.dormhi.localshare.session";
    private const int HandshakeBytes = 40;
    private static readonly byte[] StopMessage = [(byte)'T', (byte)'D', (byte)'S', (byte)'H', 1, 3, 0, 0];
    private static readonly byte[] PingMessage = [(byte)'T', (byte)'D', (byte)'S', (byte)'H', 1, 4, 0, 0];
    private static readonly byte[] PongMessage = [(byte)'T', (byte)'D', (byte)'S', (byte)'H', 1, 5, 0, 0];
    private readonly string _serial;
    private readonly int _forwardPort;
    private TcpClient _client; // replaced by the connection that completes the handshake
    private readonly byte[] _videoToken;
    private readonly CancellationTokenSource _sessionCancellation = new();
    private readonly SemaphoreSlim _writeLock = new(1, 1);
    private Task _monitorTask = Task.CompletedTask;
    private readonly object _disposeGate = new();
    private Task? _disposeTask;
    private Func<CancellationToken, Task>? _heartbeatAcknowledged;

    // Phase 4 renews a display lease only after a valid tablet pong.
    public Func<CancellationToken, Task>? HeartbeatAcknowledged
    {
        get => Volatile.Read(ref _heartbeatAcknowledged);
        set => Volatile.Write(ref _heartbeatAcknowledged, value);
    }

    private AdbSession(string serial, int forwardPort, TcpClient client, byte[] token)
    {
        _serial = serial;
        _forwardPort = forwardPort;
        _client = client;
        _videoToken = token;
    }

    public static async Task<AdbSession> ConnectAsync(CancellationToken cancellationToken)
    {
        var devices = await RunAdbAsync(["devices"], cancellationToken);
        var authorized = new List<string>();
        var unauthorized = false;
        foreach (var line in devices.StandardOutput.Split('\n', StringSplitOptions.RemoveEmptyEntries).Skip(1))
        {
            var fields = line.Trim().Split('\t', StringSplitOptions.RemoveEmptyEntries);
            if (fields.Length < 2) continue;
            if (fields[1] == "device") authorized.Add(fields[0]);
            if (fields[1] == "unauthorized") unauthorized = true;
        }
        if (authorized.Count == 0)
            throw new InvalidOperationException(unauthorized
                ? "Unlock the tablet and authorize USB debugging for this computer."
                : "No ADB tablet found. Connect one tablet by USB and enable USB debugging.");
        if (authorized.Count > 1)
            throw new InvalidOperationException("More than one authorized tablet is connected; disconnect all but one.");

        var serial = authorized[0];
        // adb devices also lists wireless/emulator transports. -d selects USB
        // hardware and refuses ambiguous USB devices; never tunnel to Wi-Fi.
        var usb = await RunAdbAsync(["-d", "get-serialno"], cancellationToken);
        if (!string.Equals(usb.StandardOutput.Trim(), serial, StringComparison.Ordinal))
            throw new InvalidOperationException("Connect exactly one authorized tablet by USB; wireless and emulator sessions are not supported.");
        var token = RandomNumberGenerator.GetBytes(32);
        var tokenHex = Convert.ToHexString(token).ToLowerInvariant();
        var port = await ForwardLoopbackAsync(serial, $"localabstract:{AbstractSocket}", cancellationToken);

        var session = new AdbSession(serial, port, new TcpClient(), token);
        try
        {
            // Keep the token out of command-line arguments and exported activity intents. Requires a debug APK.
            // adb joins shell arguments with spaces and the device shell re-parses them, so the
            // redirection must be quoted to run inside run-as (app data directory), not the outer shell.
            await RunAdbAsync(["-s", serial, "shell", "-T", "run-as", PackageId, "sh", "-c", "'cat > files/session_token'"], cancellationToken, tokenHex);
            await RunAdbAsync(["-s", serial, "shell", "am", "start", "-n", $"{PackageId}/.MainActivity"], cancellationToken);
            await session.ConnectTunnelAsync(token, cancellationToken);
            session._monitorTask = session.MonitorAsync();
            return session;
        }
        catch
        {
            try
            {
                await RunAdbAsync(["-s", serial, "shell", "run-as", PackageId, "rm", "-f", "files/session_token"], CancellationToken.None);
            }
            catch { }
            await session.DisposeAsync();
            throw;
        }
    }

    // adb accepts the local TCP connection even when the tablet app is not listening yet, then closes
    // it; an endpoint left over from an earlier session also closes on the new token. Both look like
    // end-of-stream, so retry with a fresh connection until the app restarts its endpoint.
    private async Task ConnectTunnelAsync(byte[] token, CancellationToken cancellationToken)
    {
        var deadline = DateTime.UtcNow.AddSeconds(15);
        Exception? lastError = null;
        var request = new byte[HandshakeBytes];
        Encoding.ASCII.GetBytes("TDSH").CopyTo(request, 0);
        request[4] = 1;
        request[5] = 1;
        request[7] = 32;
        token.CopyTo(request, 8);
        try
        {
            while (DateTime.UtcNow < deadline)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var candidate = new TcpClient();
                using var attempt = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                attempt.CancelAfter(TimeSpan.FromSeconds(3));
                try
                {
                    await candidate.ConnectAsync("127.0.0.1", _forwardPort, attempt.Token);
                    var stream = candidate.GetStream();
                    await stream.WriteAsync(request, attempt.Token);
                    var response = new byte[8];
                    await stream.ReadExactlyAsync(response, attempt.Token);
                    if (!response.AsSpan().SequenceEqual(new byte[] { (byte)'T', (byte)'D', (byte)'S', (byte)'H', 1, 2, 0, 0 }))
                    {
                        candidate.Dispose();
                        throw new InvalidOperationException("Tablet rejected the session handshake.");
                    }
                    _client.Dispose();
                    _client = candidate;
                    return;
                }
                catch (Exception ex) when (ex is SocketException or IOException ||
                                           (ex is OperationCanceledException && !cancellationToken.IsCancellationRequested))
                {
                    candidate.Dispose();
                    lastError = ex;
                    await Task.Delay(200, cancellationToken);
                }
            }
        }
        finally { CryptographicOperations.ZeroMemory(request); }
        throw new InvalidOperationException("The tablet did not complete the session handshake. Keep the LocalShare app open on the tablet and connect again.", lastError);
    }

    internal async Task<VideoTunnel> OpenVideoTunnelAsync(CancellationToken token)
    {
        var port = await ForwardLoopbackAsync(_serial, "localabstract:com.dormhi.localshare.video", token);
        // Best effort, like the control forward: when the cable is pulled adb has already dropped the
        // device's forwards, so this removal fails and must not be reported as a cleanup failure.
        async Task Cleanup() {
            try { await RunAdbAsync(["-s", _serial, "forward", "--remove", $"tcp:{port}"], CancellationToken.None); }
            catch (InvalidOperationException) { }
        }
        TcpClient? client = null;
        try
        {
            using var deadline = CancellationTokenSource.CreateLinkedTokenSource(token);
            deadline.CancelAfter(TimeSpan.FromSeconds(10));
            while (true)
            {
                deadline.Token.ThrowIfCancellationRequested();
                client = new TcpClient();
                try {
                    await client.ConnectAsync("127.0.0.1", port, deadline.Token);
                    await VideoWire.WriteAsync(client.GetStream(), new(1,0,0,0,_videoToken), deadline.Token);
                    var capabilities = await VideoWire.ReadAsync(client.GetStream(), deadline.Token);
                    return new VideoTunnel(client, Cleanup, capabilities);
                }
                catch (Exception error) when (error is SocketException or EndOfStreamException || error.InnerException is SocketException) {
                    client.Dispose(); client = null; await Task.Delay(100, deadline.Token);
                }
            }
        }
        catch
        {
            client?.Dispose();
            try { await Cleanup(); } catch { }
            throw;
        }
    }

    public Task Completion => _monitorTask;

    private async Task MonitorAsync()
    {
        try
        {
            var stream = _client.GetStream();
            using var timer = new PeriodicTimer(TimeSpan.FromSeconds(2));
            while (await timer.WaitForNextTickAsync(_sessionCancellation.Token))
            {
                await _writeLock.WaitAsync(_sessionCancellation.Token);
                try { await stream.WriteAsync(PingMessage, _sessionCancellation.Token); }
                finally { _writeLock.Release(); }
                var response = new byte[8];
                using var timeout = CancellationTokenSource.CreateLinkedTokenSource(_sessionCancellation.Token);
                timeout.CancelAfter(TimeSpan.FromSeconds(5));
                await stream.ReadExactlyAsync(response, timeout.Token);
                if (!response.AsSpan().SequenceEqual(PongMessage))
                    throw new InvalidOperationException("Tablet heartbeat was invalid.");
                if (HeartbeatAcknowledged is { } acknowledge)
                    await acknowledge(_sessionCancellation.Token);
            }
        }
        catch (OperationCanceledException) when (_sessionCancellation.IsCancellationRequested) { }
    }

    // Platform-Tools 37 rejects a host in the source spec (tcp:localhost:0), so request tcp:0
    // and then verify the bind: a normal ADB server listens on loopback only, but one started
    // with -a listens on all interfaces. Refuse that rather than expose the session to the network.
    private static async Task<int> ForwardLoopbackAsync(string serial, string remote, CancellationToken cancellationToken)
    {
        var forward = await RunAdbAsync(["-s", serial, "forward", "tcp:0", remote], cancellationToken);
        if (!int.TryParse(forward.StandardOutput.Trim(), out var port) || port is < 1 or > 65535)
            throw new InvalidOperationException("ADB did not return a valid local forwarding port.");
        if (!IsLoopbackOnly(port))
        {
            try { await RunAdbAsync(["-s", serial, "forward", "--remove", $"tcp:{port}"], CancellationToken.None); } catch { }
            throw new InvalidOperationException("The ADB server is accepting network connections (started with -a). Run 'adb kill-server' and connect again.");
        }
        return port;
    }

    internal static bool IsLoopbackOnly(int port)
    {
        var listeners = IPGlobalProperties.GetIPGlobalProperties().GetActiveTcpListeners().Where(e => e.Port == port).ToArray();
        return listeners.Length != 0 && listeners.All(e => IPAddress.IsLoopback(e.Address));
    }

    private static async Task<ProcessResult> RunAdbAsync(IReadOnlyList<string> arguments, CancellationToken cancellationToken, string? standardInput = null)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(30));
        var start = new ProcessStartInfo("adb") { UseShellExecute = false, RedirectStandardInput = standardInput is not null, RedirectStandardOutput = true, RedirectStandardError = true, CreateNoWindow = true };
        foreach (var argument in arguments) start.ArgumentList.Add(argument);
        using var process = new Process { StartInfo = start };
        try
        {
            process.Start();
        }
        catch (System.ComponentModel.Win32Exception ex)
        {
            throw new InvalidOperationException("Install Android Platform-Tools and make adb available on PATH.", ex);
        }
        var stdout = process.StandardOutput.ReadToEndAsync(timeout.Token);
        var stderr = process.StandardError.ReadToEndAsync(timeout.Token);
        try
        {
            if (standardInput is not null)
            {
                await process.StandardInput.WriteAsync(standardInput.AsMemory(), timeout.Token);
                process.StandardInput.Close();
            }
            await process.WaitForExitAsync(timeout.Token);
        }
        catch
        {
            try { process.Kill(entireProcessTree: true); } catch { }
            throw;
        }
        var result = new ProcessResult(await stdout, await stderr);
        if (process.ExitCode != 0)
            // Raw adb diagnostics can contain device identifiers and paths.
            throw new InvalidOperationException($"ADB failed (exit code {process.ExitCode}). Check the USB connection, authorization and debug APK installation.");
        return result;
    }

    public ValueTask DisposeAsync()
    {
        lock (_disposeGate) return new ValueTask(_disposeTask ??= DisposeCoreAsync());
    }

    private async Task DisposeCoreAsync()
    {
        HeartbeatAcknowledged = null;
        _sessionCancellation.Cancel();
        try { await _monitorTask; } catch { }
        if (_client.Connected)
        {
            try
            {
                await _writeLock.WaitAsync();
                try { await _client.GetStream().WriteAsync(StopMessage); }
                finally { _writeLock.Release(); }
            }
            catch { }
        }
        _client.Dispose();
        CryptographicOperations.ZeroMemory(_videoToken);
        try
        {
            await RunAdbAsync(["-s", _serial, "forward", "--remove", $"tcp:{_forwardPort}"], CancellationToken.None);
        }
        catch
        {
            // Best-effort cleanup after disconnect/ADB shutdown; never mask the session result.
        }
        _sessionCancellation.Dispose();
        _writeLock.Dispose();
    }

    private sealed record ProcessResult(string StandardOutput, string StandardError);
}
