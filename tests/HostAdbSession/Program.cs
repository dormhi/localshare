// SPDX-License-Identifier: Apache-2.0
// Executes real host code with a synthetic adb child and loopback tablet peer.
// Never executes Platform-Tools or inspects a physical device.
using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using TabletDisplay.Host;

if (args.Length != 0)
{
    var scenario = Environment.GetEnvironmentVariable("LOCALSHARE_TEST_SCENARIO")
        ?? throw new Exception("Synthetic adb must run inside its test harness.");
    if (args.Any(a => a.Length == 64 && a.All(Uri.IsHexDigit))) return 90;
    File.AppendAllText(Environment.GetEnvironmentVariable("LOCALSHARE_TEST_LOG")!, string.Join(' ', args) + "\n");
    if (args[0] == "devices")
    {
        Console.WriteLine("List of devices attached");
        switch (scenario)
        {
            case "none": break;
            case "unauthorized": Console.WriteLine("synthetic-tablet\tunauthorized"); break;
            case "multiple": Console.WriteLine("synthetic-tablet\tdevice\nsynthetic-other\tdevice"); break;
            default: Console.WriteLine("synthetic-tablet\tdevice"); break;
        }
        return 0;
    }
    if (args.SequenceEqual(new[] { "-d", "get-serialno" }))
    {
        if (scenario == "wireless") { Console.Error.WriteLine("synthetic-private-diagnostic"); return 1; }
        Console.WriteLine("synthetic-tablet"); return 0;
    }
    var forwardAt = Array.IndexOf(args, "forward");
    if (forwardAt >= 0 && args.Length > forwardAt + 1 && args[forwardAt + 1] != "--remove")
    {
        // Platform-Tools 37.0.1 accepts only a bare port here and rejects "tcp:localhost:0".
        if (args[forwardAt + 1] != "tcp:0") { Console.Error.WriteLine("adb.exe: error: Invalid source port"); return 1; }
        Console.WriteLine(Environment.GetEnvironmentVariable("LOCALSHARE_TEST_PORT")); return 0;
    }
    // Like real adb, join shell arguments with spaces: the device shell re-parses that string,
    // so only a quoted redirection runs inside run-as.
    var shellAt = Array.IndexOf(args, "shell");
    var remote = shellAt < 0 ? "" : string.Join(' ', args.Skip(shellAt + 1).Where(a => a != "-T"));
    if (remote.Contains("files/session_token") && remote.Contains("cat"))
    {
        if (remote != "run-as com.dormhi.localshare sh -c 'cat > files/session_token'") return 92;
        var bootstrap = await Console.In.ReadToEndAsync();
        if (bootstrap.Length != 64 || !bootstrap.All(Uri.IsHexDigit)) return 91;
        if (scenario == "provision-failure") return 1;
    }
    return 0;
}
if (!OperatingSystem.IsWindows())
{
    Console.WriteLine("UNAVAILABLE: Windows adb child-process harness requires Windows."); return 2;
}

static void Check(bool condition, string label)
{
    if (!condition) throw new Exception(label);
}
static async Task Reject(Func<Task> action, string expected)
{
    try { await action(); }
    catch (Exception error) when (error.Message.Contains(expected, StringComparison.OrdinalIgnoreCase)) { return; }
    throw new Exception($"Expected rejection containing '{expected}'.");
}

var directory = Path.Combine(Path.GetTempPath(), "localshare-synthetic-adb-" + Guid.NewGuid().ToString("N"));
Directory.CreateDirectory(directory);
var oldPath = Environment.GetEnvironmentVariable("PATH");
var log = Path.Combine(directory, "commands.txt");
try
{
    foreach (var file in Directory.GetFiles(AppContext.BaseDirectory))
        File.Copy(file, Path.Combine(directory, Path.GetFileName(file)));
    File.Copy(Path.Combine(AppContext.BaseDirectory, "HostAdbSession.exe"), Path.Combine(directory, "adb.exe"));
    Environment.SetEnvironmentVariable("PATH", directory + Path.PathSeparator + oldPath);
    Environment.SetEnvironmentVariable("LOCALSHARE_TEST_LOG", log);

    foreach (var (scenario, message) in new[] {
        ("none", "No ADB tablet"), ("unauthorized", "authorize USB debugging"),
        ("multiple", "More than one"), ("wireless", "ADB failed"), ("provision-failure", "ADB failed"),
        ("network-forward", "accepting network connections") })
    {
        Environment.SetEnvironmentVariable("LOCALSHARE_TEST_SCENARIO", scenario);
        // Stand in for the port adb would listen on; an ADB server started with -a binds all interfaces.
        var forwardListener = new TcpListener(scenario == "network-forward" ? IPAddress.Any : IPAddress.Loopback, 0);
        forwardListener.Start();
        var forwardPort = ((IPEndPoint)forwardListener.LocalEndpoint).Port;
        Environment.SetEnvironmentVariable("LOCALSHARE_TEST_PORT", forwardPort.ToString());
        File.WriteAllText(log, "");
        try { await Reject(async () => { await using var session = await AdbSession.ConnectAsync(CancellationToken.None); }, message); }
        finally { forwardListener.Stop(); }
        var commands = File.ReadAllLines(log);
        Check(!commands.Any(c => c.Contains("--remove-all") || c.Contains("tcpip")), "unsafe adb command");
        if (scenario is "provision-failure" or "network-forward")
            Check(commands.Count(c => c.EndsWith($"forward --remove tcp:{forwardPort}")) == 1, $"{scenario} forward cleanup");
        else Check(!commands.Any(c => c.Contains("forward")), "rejected selection created a forward");
        if (scenario == "network-forward") Check(!commands.Any(c => c.Contains("session_token")), "token provisioned to a network-exposed forward");
    }

    foreach (var scenario in new[] { "valid", "stale-endpoint", "bad-hello", "truncated-hello", "bad-pong", "silent-pong", "cable-loss" })
    {
        File.WriteAllText(log, "");
        Environment.SetEnvironmentVariable("LOCALSHARE_TEST_SCENARIO", scenario);
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        Environment.SetEnvironmentVariable("LOCALSHARE_TEST_PORT", port.ToString());
        // Above the host's 15-second handshake retry budget, so a never-completing hello fails in the host.
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        var receivedStop = false;
        var peer = Task.Run(async () =>
        {
            // Physical finding 2026-10-15: after a session ends, the first connection can reach an
            // endpoint that is not listening yet (or still holds the old token) and is closed unanswered.
            if (scenario == "stale-endpoint")
                using (await listener.AcceptTcpClientAsync(deadline.Token)) { }
            using var client = await listener.AcceptTcpClientAsync(deadline.Token);
            var stream = client.GetStream();
            var hello = new byte[40];
            await stream.ReadExactlyAsync(hello, deadline.Token);
            Check(hello.AsSpan(0, 8).SequenceEqual(new byte[] {84,68,83,72,1,1,0,32}), "real hello bytes");
            CryptographicOperations.ZeroMemory(hello);
            byte[] accepted = [84,68,83,72,1,2,0,0];
            if (scenario == "bad-hello") accepted[4] = 99;
            if (scenario == "truncated-hello") { await stream.WriteAsync(accepted.AsMemory(0, 3), deadline.Token); return; }
            // Exercise partial reads, not just one whole-buffer write.
            foreach (var value in accepted) await stream.WriteAsync(new byte[] { value }, deadline.Token);
            if (scenario == "bad-hello" || scenario == "cable-loss") return;
            var ping = new byte[8];
            await stream.ReadExactlyAsync(ping, deadline.Token);
            Check(ping.SequenceEqual(new byte[] {84,68,83,72,1,4,0,0}), "real heartbeat bytes");
            if (scenario == "silent-pong") { await Task.Delay(6000, deadline.Token); return; }
            byte[] pong = [84,68,83,72,1,5,0,0];
            if (scenario == "bad-pong") pong[5] = 2;
            await stream.WriteAsync(pong, deadline.Token);
            if (scenario is not ("valid" or "stale-endpoint")) return;
            await stream.ReadExactlyAsync(ping, deadline.Token);
            receivedStop = ping.SequenceEqual(new byte[] {84,68,83,72,1,3,0,0});
        });
        try
        {
            if (scenario is "bad-hello" or "truncated-hello")
            {
                try { await using var rejected = await AdbSession.ConnectAsync(deadline.Token); throw new Exception("invalid hello accepted"); }
                catch (InvalidOperationException error) when (error.Message.Contains("handshake")) { }
                catch (EndOfStreamException) { }
            }
            else
            {
                var session = await AdbSession.ConnectAsync(deadline.Token);
                if (scenario is "valid" or "stale-endpoint")
                {
                    var heartbeat = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
                    session.HeartbeatAcknowledged = _ => { heartbeat.TrySetResult(); return Task.CompletedTask; };
                    await heartbeat.Task.WaitAsync(deadline.Token);
                    await Task.WhenAll(session.DisposeAsync().AsTask(), session.DisposeAsync().AsTask());
                }
                else
                {
                    try { await session.Completion.WaitAsync(deadline.Token); throw new Exception("broken peer accepted"); }
                    catch (Exception error) when (error is IOException or InvalidOperationException or OperationCanceledException) { }
                    await session.DisposeAsync();
                }
            }
            await peer;
            if (scenario is "valid" or "stale-endpoint") Check(receivedStop, "graceful stop bytes");
            var commands = File.ReadAllLines(log);
            Check(commands.Count(c => c.EndsWith($"forward --remove tcp:{port}")) == 1, "exactly one owned forward removed");
            Check(!commands.Any(c => c.Contains("--remove-all") || c.Contains("tcpip")), "unsafe cleanup");
        }
        finally { deadline.Cancel(); listener.Stop(); }
    }
    Console.WriteLine("PASS: network-exposed (-a) forward refused and removed before token provisioning.");
    Console.WriteLine("PASS: synthetic adb process selection, stdin bootstrap, fragmented hello, invalid/truncated hello, heartbeat, timeout, cable-loss simulation, concurrent disposal and exact forward cleanup.");

    // VideoSession with the real Windows pattern encoder and a synthetic tablet video endpoint.
    // The synthetic adb returns the same port for both forwards, so one listener accepts the control
    // connection first and the video connection second.
    if (!File.Exists(Path.Combine(AppContext.BaseDirectory, "LocalShare.PatternEncoder.exe")))
        Console.WriteLine("NOT RUN: VideoSession scenarios; build tools/build-video-encoder.ps1 and rebuild this test.");
    else
    {
        foreach (var scenario in new[] { "video", "video-error", "video-unsupported-fps", "video-slow" })
        {
            File.WriteAllText(log, "");
            Environment.SetEnvironmentVariable("LOCALSHARE_TEST_SCENARIO", scenario);
            var listener = new TcpListener(IPAddress.Loopback, 0);
            listener.Start();
            var port = ((IPEndPoint)listener.LocalEndpoint).Port;
            Environment.SetEnvironmentVariable("LOCALSHARE_TEST_PORT", port.ToString());
            using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(30));
            var receivedStop = false;
            var framesAcked = 0;
            var helpersBefore = System.Diagnostics.Process.GetProcessesByName("LocalShare.PatternEncoder").Select(p => p.Id).ToHashSet();
            // Accept strictly in order: a fixed delay let the video task steal the control connection
            // whenever starting the synthetic adb processes took longer than the delay.
            var controlAccepted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var control = Task.Run(async () =>
            {
                using var client = await listener.AcceptTcpClientAsync(deadline.Token);
                controlAccepted.SetResult();
                var stream = client.GetStream();
                var hello = new byte[40];
                await stream.ReadExactlyAsync(hello, deadline.Token);
                CryptographicOperations.ZeroMemory(hello);
                await stream.WriteAsync(new byte[] {84,68,83,72,1,2,0,0}, deadline.Token);
                var message = new byte[8];
                while (true)
                {
                    try { await stream.ReadExactlyAsync(message, deadline.Token); }
                    catch (Exception error) when (error is EndOfStreamException or IOException) { return; }
                    if (message[5] == 3) { receivedStop = true; return; }
                    await stream.WriteAsync(new byte[] {84,68,83,72,1,5,0,0}, deadline.Token); // pong while video runs
                }
            });
            var video = Task.Run(async () =>
            {
                await controlAccepted.Task.WaitAsync(deadline.Token);
                using var client = await listener.AcceptTcpClientAsync(deadline.Token);
                var stream = client.GetStream();
                var hello = await VideoWire.ReadAsync(stream, deadline.Token);
                Check(hello.Type == 1 && hello.Payload.Length == 32, "video hello");
                CryptographicOperations.ZeroMemory(hello.Payload);
                var maximum = scenario == "video-unsupported-fps" ? 30u : 240u;
                await VideoWire.WriteAsync(stream, new(2, 0, 0, 0, VideoWire.Integers(1280, 800, maximum, 60000, 120000)), deadline.Token);
                if (scenario == "video-unsupported-fps") { await Task.Delay(500, deadline.Token); return; }
                var format = await VideoWire.ReadAsync(stream, deadline.Token);
                Check(format.Type == 3 && format.Sequence == 1 && VideoWire.Integer(format.Payload, 2) == 60, "format packet");
                await VideoWire.WriteAsync(stream, new(6, 0, 1, 0, []), deadline.Token);
                var config = await VideoWire.ReadAsync(stream, deadline.Token);
                Check(config.Type == 4 && config.Sequence == 2, "config packet");
                if (scenario == "video-error") { await VideoWire.WriteAsync(stream, new(8, 0, 0, 0, [1]), deadline.Token); return; }
                await VideoWire.WriteAsync(stream, new(6, 0, 2, 0, []), deadline.Token);
                while (true)
                {
                    VideoWire.Packet frame;
                    try { frame = await VideoWire.ReadAsync(stream, deadline.Token); }
                    catch (Exception error) when (error is EndOfStreamException or IOException) { return; }
                    Check(frame.Type == 5 && frame.Sequence == 3 + (uint)framesAcked, "frame order");
                    if (scenario == "video-slow" && framesAcked == 3) { await Task.Delay(7000, deadline.Token); return; }
                    await VideoWire.WriteAsync(stream, new(6, 0, frame.Sequence, 0, []), deadline.Token);
                    framesAcked++;
                }
            });
            try
            {
                var session = await AdbSession.ConnectAsync(deadline.Token);
                try
                {
                    switch (scenario)
                    {
                        case "video":
                            await using (var stream = await VideoSession.StartAsync(session, 60, _ => { }, deadline.Token))
                            {
                                while (framesAcked < 30) { Check(!stream.Completion.IsCompleted, "video stopped early"); await Task.Delay(50, deadline.Token); }
                            }
                            break;
                        case "video-error":
                            await Reject(async () => { await using var _ = await VideoSession.StartAsync(session, 60, _ => { }, deadline.Token); }, "rejected the stream");
                            break;
                        case "video-unsupported-fps":
                            await Reject(async () => { await using var _ = await VideoSession.StartAsync(session, 60, _ => { }, deadline.Token); }, "maximum of 30 FPS");
                            break;
                        case "video-slow":
                            await using (var stream = await VideoSession.StartAsync(session, 60, _ => { }, deadline.Token))
                                await Reject(() => stream.Completion.WaitAsync(deadline.Token), "deadline");
                            break;
                    }
                }
                finally { await session.DisposeAsync(); }
                await Task.WhenAll(control, video);
                Check(receivedStop, "graceful stop after video");
                var orphans = System.Diagnostics.Process.GetProcessesByName("LocalShare.PatternEncoder").Where(p => !helpersBefore.Contains(p.Id) && !p.WaitForExit(5000));
                Check(!orphans.Any(), "pattern encoder left running");
                var commands = File.ReadAllLines(log);
                Check(commands.Count(c => c.EndsWith($"forward --remove tcp:{port}")) == 2, "control and video forwards each removed once");
                Check(!commands.Any(c => c.Contains("--remove-all") || c.Contains("tcpip")), "unsafe cleanup");
            }
            finally { deadline.Cancel(); listener.Stop(); }
        }
        Console.WriteLine("PASS: VideoSession with the real pattern encoder: 30 acknowledged frames, tablet ERROR, unsupported FPS, slow-ACK deadline, helper termination and exact video/control forward cleanup.");
    }
    Console.WriteLine("NOT TESTED: actual USB/ADB authorization, Android endpoint or physical tablet.");
}
finally
{
    Environment.SetEnvironmentVariable("PATH", oldPath);
    foreach (var name in new[] { "LOCALSHARE_TEST_LOG", "LOCALSHARE_TEST_SCENARIO", "LOCALSHARE_TEST_PORT" })
        Environment.SetEnvironmentVariable(name, null);
    Directory.Delete(directory, recursive: true);
}
return 0;
