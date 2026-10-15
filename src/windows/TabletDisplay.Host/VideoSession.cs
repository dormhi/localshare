// SPDX-License-Identifier: Apache-2.0
using System.Diagnostics;
using System.Net.Sockets;
using System.Security.Cryptography;
namespace TabletDisplay.Host;

internal sealed class VideoTunnel(TcpClient client, Func<Task> cleanup, VideoWire.Packet capabilities) : IAsyncDisposable
{
    internal NetworkStream Stream => client.GetStream();
    internal VideoWire.Packet Capabilities => capabilities;
    public async ValueTask DisposeAsync() { client.Dispose(); await cleanup(); }
}
internal sealed class VideoSession : IAsyncDisposable
{
    private readonly VideoTunnel _tunnel;
    private readonly Process _encoder;
    private readonly CancellationTokenSource _stop = new();
    private Task _sending = Task.CompletedTask;
    private readonly object _disposeGate = new();
    private Task? _disposeTask;
    private VideoSession(VideoTunnel tunnel, Process encoder) { _tunnel = tunnel; _encoder = encoder; }
    internal Task Completion => _sending;

    internal static async Task<VideoSession> StartAsync(AdbSession usb, int fps, Action<string> status, CancellationToken token)
    {
        if (fps is < 1 or > VideoWire.MaxFps) throw new ArgumentOutOfRangeException(nameof(fps));
        var executable = Path.Combine(AppContext.BaseDirectory, "LocalShare.PatternEncoder.exe");
        if (!File.Exists(executable)) throw new InvalidOperationException("Build the Windows pattern encoder first (tools/build-video-encoder.ps1), then rebuild the host.");
        var tunnel = await usb.OpenVideoTunnelAsync(token);
        Process? encoder = null;
        try
        {
            var caps = tunnel.Capabilities;
            if (caps.Type != 2 || caps.Sequence != 0 || VideoWire.Integer(caps.Payload, 0) != 1280 || VideoWire.Integer(caps.Payload, 1) != 800)
                throw new IOException("Unexpected tablet video capabilities.");
            var maximum = VideoWire.Integer(caps.Payload, 2);
            if (maximum > VideoWire.MaxFps || fps > maximum)
                throw new InvalidOperationException($"The tablet decoder reports a maximum of {maximum} FPS for the test resolution. Choose a supported lower limit.");
            var bitrate = (uint)Math.Clamp(3_000_000L * fps / 30, 100_000, 50_000_000);
            await VideoWire.WriteAsync(tunnel.Stream, new(3,0,1,0,VideoWire.Integers(1280,800,(uint)fps,bitrate)), token);
            await AckAsync(tunnel.Stream, 1, token);
            var start = new ProcessStartInfo(executable) {
                UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true, RedirectStandardInput = true, CreateNoWindow = true,
            };
            start.ArgumentList.Add(fps.ToString(System.Globalization.CultureInfo.InvariantCulture));
            start.ArgumentList.Add(bitrate.ToString(System.Globalization.CultureInfo.InvariantCulture));
            encoder = new Process { StartInfo = start };
            if (!encoder.Start()) throw new IOException("Could not start the pattern encoder.");
            VideoWire.Packet config;
            using (var startup = CancellationTokenSource.CreateLinkedTokenSource(token))
            {
                startup.CancelAfter(TimeSpan.FromSeconds(5));
                using var abortStartup = startup.Token.Register(() => { try { encoder.Kill(entireProcessTree: true); } catch { } });
                try { config = await VideoWire.ReadAsync(encoder.StandardOutput.BaseStream, startup.Token); }
                catch (EndOfStreamException error) { throw new IOException("Windows H.264 encoder stopped. Verify the selected FPS and encoder runtime on Windows.", error); }
            }
            if (config.Type != 4 || config.Sequence != 2) throw new IOException("Invalid encoder configuration.");
            await VideoWire.WriteAsync(tunnel.Stream, config, token);
            await AckAsync(tunnel.Stream, 2, token);
            await encoder.StandardInput.BaseStream.WriteAsync(new byte[] { 0x41 }, token);
            await encoder.StandardInput.BaseStream.FlushAsync(token);
            var panel = $"panel at connection {VideoWire.Integer(caps.Payload,3)/1000.0:0.#} Hz, maximum reported {VideoWire.Integer(caps.Payload,4)/1000.0:0.#} Hz";
            status($"Pattern FPS limit {fps}; {panel}. Hardware refresh is unchanged.");
            var session = new VideoSession(tunnel, encoder);
            session._sending = session.SendAsync(fps, panel, status);
            return session;
        }
        catch
        {
            if (encoder is not null) { try { encoder.Kill(entireProcessTree: true); } catch { } encoder.Dispose(); }
            await tunnel.DisposeAsync();
            throw;
        }
    }
    private static async Task AckAsync(Stream stream, uint sequence, CancellationToken token)
    {
        var ack = await VideoWire.ReadAsync(stream, token);
        if (ack.Type == 8) throw new IOException("Tablet rejected the stream: unsupported format, invalid video, or slow/unavailable decoder.");
        if (ack.Type != 6 || ack.Sequence != sequence) throw new IOException("Unexpected video acknowledgement.");
    }
    private async Task SendAsync(int fps, string panel, Action<string> status)
    {
        uint sequence = 3;
        ulong? previous = null;
        var clock = Stopwatch.StartNew();
        var frames = 0;
        while (!_stop.IsCancellationRequested)
        {
            var frame = await VideoWire.ReadAsync(_encoder.StandardOutput.BaseStream, _stop.Token);
            if (frame.Type != 5 || frame.Sequence != sequence || (sequence == 3 && frame.Flags != 1) ||
                (previous.HasValue && frame.Pts <= previous.Value)) throw new IOException("Invalid encoder frame order.");
            await VideoWire.WriteAsync(_tunnel.Stream, frame, _stop.Token);
            await AckAsync(_tunnel.Stream, sequence, _stop.Token); // one compressed frame in flight; backpressure
            await _encoder.StandardInput.BaseStream.WriteAsync(new byte[] { 0x41 }, _stop.Token);
            await _encoder.StandardInput.BaseStream.FlushAsync(_stop.Token);
            previous = frame.Pts; sequence = checked(sequence + 1); frames++;
            if (clock.Elapsed >= TimeSpan.FromSeconds(2))
            {
                status($"Pattern FPS limit {fps}; decoder accepted {frames / clock.Elapsed.TotalSeconds:0.#} frames/sec; {panel}.");
                frames = 0; clock.Restart();
            }
        }
    }
    public ValueTask DisposeAsync()
    {
        lock (_disposeGate) return new(_disposeTask ??= CloseAsync());
    }
    private async Task CloseAsync()
    {
        _stop.Cancel();
        try { _encoder.Kill(entireProcessTree: true); } catch { }
        try { await _sending; } catch { }
        try { await _tunnel.DisposeAsync(); }
        finally { _encoder.Dispose(); _stop.Dispose(); }
    }
}
