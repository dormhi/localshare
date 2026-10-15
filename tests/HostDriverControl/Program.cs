// SPDX-License-Identifier: Apache-2.0
using TabletDisplay.Host;

static void Check(bool value, string message) { if (!value) throw new Exception(message); }
static async Task Throws<T>(Func<Task> action) where T : Exception
{
    try { await action(); } catch (T) { return; }
    throw new Exception($"Expected {typeof(T).Name}");
}
if (args.SequenceEqual(new[] { "--probe-missing-driver" }))
{
    if (!OperatingSystem.IsWindowsVersionAtLeast(10, 0, 22000))
        throw new PlatformNotSupportedException("This explicit native probe requires Windows 11.");
    try
    {
        using var driver = DriverControl.Open();
        throw new Exception("A LocalShare driver is present; use the approved installed-driver checklist instead.");
    }
    catch (InvalidOperationException error) when (error.Message.Contains("not installed"))
    {
        Console.WriteLine("PASS: real Windows SetupAPI enumeration reports the missing LocalShare driver cleanly; no IOCTL/install performed.");
    }
    return;
}
foreach (byte command in new byte[] {1, 2, 3})
{
    var bytes = DriverControl.Message(command);
    Check(Convert.ToHexString(bytes) == $"4C5344340100{command:X2}000000000000000000", "wire ABI mismatch");
}
await Throws<ArgumentOutOfRangeException>(() => { DriverControl.Message(0); return Task.CompletedTask; });
Check(DriverControl.ControlIoctl == 0x0022e000, "IOCTL requires buffered read/write access");
var control = new FakeControl();
var lease = await DisplayLease.StartAsync(control, CancellationToken.None);
Check(control.Commands.SequenceEqual(new byte[]{1}), "start command");
await lease.RenewAsync(CancellationToken.None);
await Task.WhenAll(lease.DisposeAsync().AsTask(), lease.DisposeAsync().AsTask());
Check(control.Commands.SequenceEqual(new byte[]{1, 2, 3}), "duplicate stop or missing heartbeat");
Check(control.CloseCount == 1, "close exactly once");
await Throws<ObjectDisposedException>(() => lease.RenewAsync(CancellationToken.None));
control = new FakeControl { FailCommand = 1 };
await Throws<IOException>(() => DisplayLease.StartAsync(control, CancellationToken.None));
Check(control.CloseCount == 1, "start failure closes file");
control = new FakeControl { FailCommand = 3 };
lease = await DisplayLease.StartAsync(control, CancellationToken.None);
await Throws<IOException>(() => lease.DisposeAsync().AsTask());
await lease.DisposeAsync();
Check(control.CloseCount == 1, "stop failure still closes file");
control = new FakeControl { FailCommand = 2 };
lease = await DisplayLease.StartAsync(control, CancellationToken.None);
await Throws<IOException>(() => lease.RenewAsync(CancellationToken.None));
await lease.DisposeAsync();
Check(control.CloseCount == 1, "renew failure cleanup");
control = new FakeControl();
using (var cancellation = new CancellationTokenSource())
{
    cancellation.Cancel();
    await Throws<OperationCanceledException>(() => DisplayLease.StartAsync(control, cancellation.Token));
}
Check(control.CloseCount == 1, "cancelled start closes file");
control = new FakeControl { BlockRenew = true };
lease = await DisplayLease.StartAsync(control, CancellationToken.None);
var renewal = lease.RenewAsync(CancellationToken.None);
await control.RenewEntered.Task;
var closing = lease.DisposeAsync().AsTask();
Check(!closing.IsCompleted && control.CloseCount == 0, "close cannot race pending native input buffers");
control.ReleaseRenew.SetResult();
await Task.WhenAll(renewal, closing);
Check(control.Commands.SequenceEqual(new byte[]{1, 2, 3}) && control.CloseCount == 1, "serialized close");
Console.WriteLine("PASS: control bytes, start/renew/stop, failures, cancellation, concurrent disposal and in-flight renewal");
Console.WriteLine("NOT TESTED: Windows SetupAPI, DeviceIoControl, WinUI or actual driver runtime");

sealed class FakeControl : IDriverControl
{
    public readonly List<byte> Commands = [];
    public byte FailCommand;
    public int CloseCount;
    public bool BlockRenew;
    public readonly TaskCompletionSource RenewEntered = new(TaskCreationOptions.RunContinuationsAsynchronously);
    public readonly TaskCompletionSource ReleaseRenew = new(TaskCreationOptions.RunContinuationsAsynchronously);
    public async Task SendAsync(byte command, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        Commands.Add(command);
        if (command == FailCommand) throw new IOException("synthetic driver failure");
        if (command == 2 && BlockRenew) { RenewEntered.SetResult(); await ReleaseRenew.Task.WaitAsync(token); }
    }
    public void Dispose() => CloseCount++;
}
