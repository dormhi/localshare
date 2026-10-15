// SPDX-License-Identifier: Apache-2.0
namespace TabletDisplay.Host;

internal interface IDriverControl : IDisposable
{
    Task SendAsync(byte command, CancellationToken cancellationToken);
}

// Owns exactly one driver file. Renew only after an acknowledged USB heartbeat;
// a disconnected tablet must never be kept alive by an independent host timer.
internal sealed class DisplayLease : IAsyncDisposable
{
    private readonly IDriverControl _control;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private bool _disposed;

    private DisplayLease(IDriverControl control) => _control = control;

    internal static async Task<DisplayLease> StartAsync(IDriverControl control, CancellationToken token)
    {
        try
        {
            await control.SendAsync(1, token);
            return new DisplayLease(control);
        }
        catch
        {
            control.Dispose(); // file cleanup also covers an ambiguous Start result
            throw;
        }
    }

    internal async Task RenewAsync(CancellationToken token)
    {
        await _gate.WaitAsync(token);
        try
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            await _control.SendAsync(2, token);
        }
        finally { _gate.Release(); }
    }

    public async ValueTask DisposeAsync()
    {
        await _gate.WaitAsync();
        try
        {
            if (_disposed) return;
            _disposed = true;
            try
            {
                using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(3));
                await _control.SendAsync(3, timeout.Token);
            }
            finally { _control.Dispose(); }
        }
        finally { _gate.Release(); }
        // Do not dispose the gate: an already queued concurrent disposer may use it.
    }
}
