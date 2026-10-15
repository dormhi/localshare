// SPDX-License-Identifier: Apache-2.0
// Drives the real Windows pattern encoder without a tablet. Synthetic ACK credits stand in for the
// tablet; output is parsed by the host's VideoWire and checked against the Android AvcConfig and
// StreamBudget rules. Only protocol metadata is printed, never pixel or bitstream contents.
using System.Diagnostics;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Threading.Channels;
using TabletDisplay.Host;

var encoder = FindEncoder(args);
var seconds = double.Parse(Option(args, "--seconds") ?? "3", CultureInfo.InvariantCulture);
var rates = (Option(args, "--fps") ?? "1,15,30,60,120,144").Split(',').Select(uint.Parse).ToArray();
Helper.Mode = Option(args, "--mode"); // auto (default), hardware or software
Console.WriteLine($"Encoder: {Path.GetFileName(encoder)} ({RuntimeInformation.ProcessArchitecture}), mode {Helper.Mode ?? "auto"}; {seconds:0.#} s per rate");

foreach (var bad in new[] {
    Array.Empty<string>(), ["30"], ["30", "3000000", "x"], ["0", "3000000"], ["1001", "3000000"],
    ["-1", "3000000"], ["30x", "3000000"], ["30", "99999"], ["30", "50000001"] })
    await Rejected(encoder, bad);
Console.WriteLine("PASS: invalid arguments exit with an error before writing protocol bytes");

await using (var run = Helper.Start(encoder, 30, Bitrate(30)))
{
    await VideoWire.ReadAsync(run.Output, CancellationToken.None);
    run.Kill();
    Check(run.Process.WaitForExit(5000), "helper survived kill while awaiting its first credit");
}
Console.WriteLine("PASS: host kill while awaiting a credit terminates the helper");

var failures = 0;
foreach (var fps in rates)
{
    try { await Measure(encoder, fps, seconds); }
    catch (Exception error) { failures++; Console.WriteLine($"FAIL: {fps} FPS limit: {error.Message}"); }
}
if (failures != 0) { Console.WriteLine($"FAIL: {failures} rate(s) violated the protocol"); return 1; }
Console.WriteLine("PASS: encoder output conforms at every requested limit (throughput above is a measurement, not a tablet claim)");
return 0;

static async Task Measure(string encoder, uint fps, double seconds)
{
    var bitrate = Bitrate(fps);
    await using var run = Helper.Start(encoder, fps, bitrate);
    var config = await VideoWire.ReadAsync(run.Output, CancellationToken.None);
    Check(config.Type == 4 && config.Sequence == 2 && config.Flags == 0 && config.Pts == 0, "first packet is not CONFIG sequence 2");
    var avc = Avc.ParseConfig(config.Payload, 1280, 800);
    // The helper must not encode ahead of the tablet while it configures the decoder.
    await Task.Delay(500);
    Check(run.Output.Pending == 0, "frame bytes arrived before the CONFIG credit");
    var budget = new StreamBudget(fps, bitrate);
    run.Credit();

    uint sequence = 3; ulong? firstPts = null, lastPts = null;
    int frames = 0, keyframes = 0, largest = 0;
    var nalTypes = new SortedSet<int>();
    var paused = TimeSpan.Zero;
    var clock = Stopwatch.StartNew();
    while (clock.Elapsed.TotalSeconds < seconds || frames < 3)
    {
        var frame = await VideoWire.ReadAsync(run.Output, CancellationToken.None);
        budget.Accept(frame.Payload.Length, Stopwatch.GetTimestamp());
        Check(frame.Type == 5 && frame.Sequence == sequence, $"expected FRAME sequence {sequence}");
        Check(sequence != 3 || frame.Flags == 1, "first frame is not marked IDR");
        Check(!lastPts.HasValue || frame.Pts > lastPts.Value, "timestamps do not strictly increase");
        Avc.ValidateFrame(frame.Payload, avc, frame.Flags == 1, nalTypes);
        firstPts ??= frame.Pts; lastPts = frame.Pts;
        keyframes += frame.Flags; largest = Math.Max(largest, frame.Payload.Length);
        sequence++; frames++;
        if (frames == 5)
        {
            // Withhold the credit for several frame intervals: nothing more may be produced.
            var hold = Stopwatch.StartNew();
            await Task.Delay(TimeSpan.FromMilliseconds(Math.Min(3000, 3000.0 / fps + 300)));
            Check(run.Output.Pending == 0, "helper produced a frame without a credit");
            paused += hold.Elapsed;
        }
        run.Credit();
    }
    var active = (clock.Elapsed - paused).TotalSeconds;
    var measured = frames / active;
    // pts is the helper's capture clock; across the credit hold it includes the pause, so use the
    // interval per frame only as an upper-bound check that the helper never bursts above the limit.
    var ptsRate = (frames - 1) / Math.Max(1e-6, (lastPts!.Value - firstPts!.Value) / 1e6);
    Check(ptsRate <= fps * 1.05 + 0.5, $"helper timestamps exceed the limit ({ptsRate:0.#}/s)");
    run.CloseInput();
    Check(run.Process.WaitForExit(5000), "helper did not exit after its credit pipe closed");
    Check(run.Process.ExitCode == 1, "closed credit pipe was not reported as a failure");
    var verdict = measured >= fps * 0.9 ? "sustained" : "BELOW LIMIT";
    Console.WriteLine($"PASS: {fps} FPS limit, {bitrate} bit/s: profile {avc.Profile} level {avc.Level}; {frames} frames, " +
        $"{keyframes} IDR, largest {largest} B, NAL types [{string.Join(",", nalTypes)}]; measured {measured:0.#} frames/s ({verdict})");
}

static async Task Rejected(string encoder, string[] arguments)
{
    await using var run = Helper.Start(encoder, arguments);
    Check(run.Process.WaitForExit(10000), "helper did not exit for invalid arguments");
    await run.Drained;
    Check(run.Process.ExitCode == 1 && run.Output.Received == 0, $"arguments [{string.Join(' ', arguments)}] were accepted");
}

static uint Bitrate(uint fps) => (uint)Math.Clamp(3_000_000L * fps / 30, 100_000, 50_000_000); // matches VideoSession
static void Check(bool value, string message) { if (!value) throw new InvalidOperationException(message); }
static string? Option(string[] args, string name)
{
    var index = Array.IndexOf(args, name);
    return index >= 0 && index + 1 < args.Length ? args[index + 1] : null;
}
static string FindEncoder(string[] args)
{
    if (Option(args, "--encoder") is { } explicitPath) return Path.GetFullPath(explicitPath);
    var arch = RuntimeInformation.ProcessArchitecture == Architecture.Arm64 ? "ARM64" : "x64";
    for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
    {
        var candidate = Path.Combine(directory.FullName, "src", "windows", "VideoPatternEncoder", "out", arch, "LocalShare.PatternEncoder.exe");
        if (File.Exists(candidate)) return candidate;
    }
    throw new FileNotFoundException("Build the helper first: tools/build-video-encoder.ps1 -Architecture " + arch);
}

sealed class Helper : IAsyncDisposable
{
    internal Process Process { get; }
    internal PipeReaderStream Output { get; }
    internal Task Drained => Output.Completion;
    private readonly Task _errors;
    private Helper(Process process)
    {
        Process = process;
        Output = new PipeReaderStream(process.StandardOutput.BaseStream);
        _errors = process.StandardError.ReadToEndAsync(); // drained so the helper never blocks on stderr
    }
    internal static string? Mode { get; set; }
    internal static Helper Start(string encoder, uint fps, uint bitrate) =>
        Start(encoder, Mode is null
            ? [fps.ToString(CultureInfo.InvariantCulture), bitrate.ToString(CultureInfo.InvariantCulture)]
            : [fps.ToString(CultureInfo.InvariantCulture), bitrate.ToString(CultureInfo.InvariantCulture), Mode]);
    internal static Helper Start(string encoder, string[] arguments)
    {
        var start = new ProcessStartInfo(encoder)
        {
            UseShellExecute = false, CreateNoWindow = true,
            RedirectStandardInput = true, RedirectStandardOutput = true, RedirectStandardError = true,
        };
        foreach (var argument in arguments) start.ArgumentList.Add(argument);
        return new Helper(Process.Start(start) ?? throw new IOException("Could not start the helper."));
    }
    internal void Credit() { Process.StandardInput.BaseStream.WriteByte((byte)'A'); Process.StandardInput.BaseStream.Flush(); }
    internal void CloseInput() => Process.StandardInput.Close();
    internal void Kill() { try { Process.Kill(entireProcessTree: true); } catch (InvalidOperationException) { } }
    public async ValueTask DisposeAsync()
    {
        Kill();
        await Process.WaitForExitAsync();
        await Output.Completion; await _errors;
        Process.Dispose();
    }
}

// Copies helper stdout on a background task so reads can time out and pending bytes can be observed.
sealed class PipeReaderStream : Stream
{
    private readonly Channel<byte[]> _chunks = Channel.CreateUnbounded<byte[]>();
    private ReadOnlyMemory<byte> _current;
    private long _received, _consumed;
    internal Task Completion { get; }
    internal long Received => Interlocked.Read(ref _received);
    internal long Pending => Received - _consumed;
    internal PipeReaderStream(Stream source) => Completion = Task.Run(async () =>
    {
        var buffer = new byte[65536];
        try
        {
            int count;
            while ((count = await source.ReadAsync(buffer)) > 0)
            {
                Interlocked.Add(ref _received, count);
                _chunks.Writer.TryWrite(buffer[..count]);
            }
        }
        catch (IOException) { }
        finally { _chunks.Writer.TryComplete(); }
    });
    public override async ValueTask<int> ReadAsync(Memory<byte> destination, CancellationToken token = default)
    {
        if (_current.IsEmpty)
        {
            if (!await _chunks.Reader.WaitToReadAsync(token)) return 0;
            _current = await _chunks.Reader.ReadAsync(token);
        }
        var count = Math.Min(destination.Length, _current.Length);
        _current[..count].CopyTo(destination);
        _current = _current[count..]; _consumed += count;
        return count;
    }
    public override int Read(byte[] buffer, int offset, int count) => ReadAsync(buffer.AsMemory(offset, count)).AsTask().GetAwaiter().GetResult();
    public override bool CanRead => true;
    public override bool CanSeek => false;
    public override bool CanWrite => false;
    public override long Length => throw new NotSupportedException();
    public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
    public override void Flush() { }
    public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
    public override void SetLength(long value) => throw new NotSupportedException();
    public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
}

// Mirrors AvcConfig in src/android/.../VideoProtocol.kt; keep both in step.
static class Avc
{
    internal sealed record Config(byte[] Sps, byte[] Pps, int Profile, int Level);
    internal static List<byte[]> Nals(byte[] data)
    {
        int Prefix(int i) =>
            i + 3 < data.Length && data[i] == 0 && data[i + 1] == 0 && data[i + 2] == 0 && data[i + 3] == 1 ? 4 :
            i + 2 < data.Length && data[i] == 0 && data[i + 1] == 0 && data[i + 2] == 1 ? 3 : 0;
        var output = new List<byte[]>();
        for (var start = 0; start < data.Length;)
        {
            var prefix = Prefix(start);
            Require(prefix != 0, "Annex-B prefix");
            var content = start + prefix; var end = content;
            while (end < data.Length && Prefix(end) == 0) end++;
            Require(end > content && output.Count < 64, "NAL count/size");
            output.Add(data[content..end]);
            start = end;
        }
        return output;
    }
    internal static Config ParseConfig(byte[] data, int width, int height)
    {
        Require(data.Length is >= 1 and <= VideoWire.MaxConfig, "configuration bound");
        var list = Nals(data);
        Require(list.Count == 2 && (list[0][0] & 31) == 7 && (list[1][0] & 31) == 8 && list.All(n => (n[0] & 128) == 0), "SPS/PPS configuration required");
        var sps = list[0];
        Require(sps.Length is >= 5 and <= 16384 && list[1].Length is >= 2 and <= 16384, "codec configuration size");
        var rbsp = new List<byte>(); var zeros = 0;
        for (var i = 1; i < sps.Length; i++)
        {
            if (zeros == 2 && sps[i] == 3) { Require(i + 1 < sps.Length && sps[i + 1] <= 3, "escape byte"); zeros = 0; continue; }
            rbsp.Add(sps[i]); zeros = sps[i] == 0 ? zeros + 1 : 0;
        }
        var bits = new Bits(rbsp.ToArray());
        var profile = bits.Read(8);
        Require(profile == 66, $"Baseline profile required (encoder produced profile_idc {profile})");
        bits.Read(8);
        var level = bits.Read(8);
        Require(new[] { 10, 11, 12, 13, 20, 21, 22, 30, 31, 32, 40, 41, 42, 50, 51, 52, 60, 61, 62 }.Contains(level), $"AVC level {level}");
        Require(bits.Ue() <= 31 && bits.Ue() <= 12, "SPS/frame number");
        switch (bits.Ue())
        {
            case 0: Require(bits.Ue() <= 12, "picture order count"); break;
            case 1:
                bits.Read(1); bits.Ue(); bits.Ue();
                var count = bits.Ue(); Require(count <= 255, "picture order cycle");
                for (var i = 0; i < count; i++) bits.Ue();
                break;
            case 2: break;
            default: throw new IOException("picture order type");
        }
        Require(bits.Ue() <= 4, "reference frame bound");
        bits.Read(1);
        long columns = bits.Ue() + 1L, rows = bits.Ue() + 1L;
        Require(columns <= 120 && rows <= 68 && bits.Read(1) == 1, "frame dimensions/interlace");
        bits.Read(1);
        long left = 0, right = 0, top = 0, bottom = 0;
        if (bits.Read(1) == 1) { left = bits.Ue(); right = bits.Ue(); top = bits.Ue(); bottom = bits.Ue(); }
        Require(columns * 16 - 2 * (left + right) == width && rows * 16 - 2 * (top + bottom) == height, "SPS size differs from negotiated format");
        return new(sps, list[1], profile, level);
    }
    internal static void ValidateFrame(byte[] data, Config config, bool keyframe, ISet<int> seen)
    {
        bool slice = false, idr = false;
        foreach (var nal in Nals(data))
        {
            Require((nal[0] & 128) == 0, "forbidden NAL bit");
            var type = nal[0] & 31; seen.Add(type);
            switch (type)
            {
                case 1: slice = true; break;
                case 5: slice = true; idr = true; break;
                case 7: Require(nal.AsSpan().SequenceEqual(config.Sps), "SPS change"); break;
                case 8: Require(nal.AsSpan().SequenceEqual(config.Pps), "PPS change"); break;
                case 6 or 9 or 12: break;
                default: throw new IOException($"unsupported NAL type {type}");
            }
        }
        Require(slice && (!keyframe || idr), "frame/keyframe content");
    }
    private static void Require(bool value, string message) { if (!value) throw new IOException(message); }
    private sealed class Bits(byte[] data)
    {
        private int _position;
        internal int Read(int count)
        {
            Require(count is >= 0 and <= 31 && _position + count <= data.Length * 8, "truncated SPS");
            var value = 0;
            for (var i = 0; i < count; i++, _position++) value = (value << 1) | ((data[_position / 8] >> (7 - _position % 8)) & 1);
            return value;
        }
        internal int Ue()
        {
            var zeros = 0;
            while (Read(1) == 0) { zeros++; Require(zeros <= 20, "SPS integer bound"); }
            return (1 << zeros) - 1 + Read(zeros);
        }
    }
}

// Mirrors StreamBudget in VideoProtocol.kt, using helper output arrival as the arrival clock.
sealed class StreamBudget(uint fps, uint bitrate)
{
    private readonly double _bytesPerSecond = bitrate / 8.0;
    private double _bytes = VideoWire.MaxFrame + bitrate / 8.0 * 2, _frames = 2;
    private long _last = Stopwatch.GetTimestamp();
    internal void Accept(int size, long now)
    {
        var elapsed = Math.Max(0, now - _last) / (double)Stopwatch.Frequency; _last = now;
        _frames = Math.Min(2, _frames + elapsed * fps);
        _bytes = Math.Min(VideoWire.MaxFrame + _bytesPerSecond * 2, _bytes + elapsed * _bytesPerSecond);
        if (!(_frames >= 1 && size is >= 1 and <= VideoWire.MaxFrame && _bytes >= size)) throw new IOException("Android stream budget would reject this output");
        _frames -= 1; _bytes -= size;
    }
}
