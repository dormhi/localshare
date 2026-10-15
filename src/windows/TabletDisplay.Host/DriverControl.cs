// SPDX-License-Identifier: Apache-2.0
using System.ComponentModel;
using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

namespace TabletDisplay.Host;

// Windows-only transport. Compilation can be checked on macOS; P/Invoke cannot.
internal sealed class DriverControl : IDriverControl
{
    internal static readonly Guid InterfaceId = new("7e0a104a-6999-4f54-9262-58a25b3d878b");
    internal const uint ControlIoctl = 0x0022e000;
    private readonly SafeFileHandle _handle;
    private DriverControl(SafeFileHandle handle) => _handle = handle;

    internal static DriverControl Open()
    {
        if (!OperatingSystem.IsWindowsVersionAtLeast(10, 0, 22000) || !Environment.Is64BitProcess)
            throw new PlatformNotSupportedException("The display driver requires Windows 11 x64 or ARM64.");
        var id = InterfaceId;
        var devices = SetupDiGetClassDevsW(ref id, null, IntPtr.Zero, 0x12); // PRESENT | DEVICEINTERFACE
        if (devices == new IntPtr(-1)) throw NativeError("Cannot enumerate the display driver");
        string? path = null;
        try
        {
            for (uint index = 0; ; index++)
            {
                var data = new InterfaceData { Size = (uint)Marshal.SizeOf<InterfaceData>() };
                if (!SetupDiEnumDeviceInterfaces(devices, IntPtr.Zero, ref id, index, ref data))
                {
                    if (Marshal.GetLastWin32Error() == 259) break; // NO_MORE_ITEMS
                    throw NativeError("Cannot enumerate the display driver");
                }
                if (path is not null)
                    throw new InvalidOperationException("More than one LocalShare driver instance is installed.");
                _ = SetupDiGetDeviceInterfaceDetailW(devices, ref data, IntPtr.Zero, 0, out uint size, IntPtr.Zero);
                if (Marshal.GetLastWin32Error() != 122 || size < 8 || size > 65536)
                    throw new InvalidOperationException("The display driver returned an invalid interface path size.");
                var detail = Marshal.AllocHGlobal((int)size);
                try
                {
                    Marshal.WriteInt32(detail, 8); // SP_DEVICE_INTERFACE_DETAIL_DATA_W on x64/ARM64
                    if (!SetupDiGetDeviceInterfaceDetailW(devices, ref data, detail, size, out _, IntPtr.Zero))
                        throw NativeError("Cannot read the display driver interface");
                    // DevicePath starts at offset 4 despite the 8-byte cbSize.
                    var characters = new char[((int)size - 4) / 2];
                    Marshal.Copy(IntPtr.Add(detail, 4), characters, 0, characters.Length);
                    var end = Array.IndexOf(characters, '\0');
                    if (end < 1) throw new InvalidOperationException("The display driver interface path is invalid.");
                    path = new string(characters, 0, end);
                }
                finally { Marshal.FreeHGlobal(detail); }
            }
        }
        finally { SetupDiDestroyDeviceInfoList(devices); }
        if (path is null)
            throw new InvalidOperationException("The LocalShare test display driver is not installed. Disable virtual display to test USB only.");
        var handle = CreateFileW(path, 0xc0000000, 3, IntPtr.Zero, 3, 0x40000000, IntPtr.Zero);
        if (handle.IsInvalid)
        {
            var error = NativeError("Cannot open the display driver; check its installation and access policy");
            handle.Dispose();
            throw error;
        }
        return new DriverControl(handle);
    }

    internal static byte[] Message(byte command)
    {
        if (command is < 1 or > 3) throw new ArgumentOutOfRangeException(nameof(command));
        return [(byte)'L', (byte)'S', (byte)'D', (byte)'4', 1, 0, command, 0, 0, 0, 0, 0, 0, 0, 0, 0];
    }

    public Task SendAsync(byte command, CancellationToken cancellationToken) =>
        Task.Run(() => Send(Message(command), cancellationToken), cancellationToken);

    private void Send(byte[] message, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        using var completed = new EventWaitHandle(false, EventResetMode.ManualReset);
        var input = Marshal.AllocHGlobal(message.Length);
        var overlapped = IntPtr.Zero;
        bool pending = false;
        try
        {
            Marshal.Copy(message, 0, input, message.Length);
            overlapped = Marshal.AllocHGlobal(Marshal.SizeOf<Overlapped>());
            Marshal.StructureToPtr(new Overlapped { Event = completed.SafeWaitHandle.DangerousGetHandle() }, overlapped, false);
            if (DeviceIoControl(_handle, ControlIoctl, input, (uint)message.Length,
                                IntPtr.Zero, 0, IntPtr.Zero, overlapped)) return;
            if (Marshal.GetLastWin32Error() != 997) throw NativeError("Display driver control failed");
            pending = true;
            var result = WaitHandle.WaitAny([completed, token.WaitHandle], TimeSpan.FromSeconds(3));
            if (result != 0)
            {
                _ = CancelIoEx(_handle, overlapped);
                // Native buffers cannot be released before cancellation completes.
                // A broken driver's cancellation behavior remains a Windows gate.
                _ = GetOverlappedResult(_handle, overlapped, out _, true);
                pending = false;
                token.ThrowIfCancellationRequested();
                throw new TimeoutException("The display driver did not respond within three seconds.");
            }
            var success = GetOverlappedResult(_handle, overlapped, out _, false);
            pending = false;
            if (!success) throw NativeError("Display driver control failed");
        }
        finally
        {
            if (pending)
            {
                _ = CancelIoEx(_handle, overlapped);
                _ = GetOverlappedResult(_handle, overlapped, out _, true);
            }
            if (overlapped != IntPtr.Zero) Marshal.FreeHGlobal(overlapped);
            Marshal.FreeHGlobal(input);
        }
    }
    public void Dispose() => _handle.Dispose();
    private static Exception NativeError(string context) =>
        new Win32Exception(Marshal.GetLastWin32Error(), context); // never print interface paths

    [StructLayout(LayoutKind.Sequential)]
    private struct InterfaceData { public uint Size; public Guid Class; public uint Flags; public UIntPtr Reserved; }
    [StructLayout(LayoutKind.Sequential)]
    private struct Overlapped { public UIntPtr Internal, InternalHigh; public uint Offset, OffsetHigh; public IntPtr Event; }
    [DllImport("setupapi.dll", CharSet = CharSet.Unicode, ExactSpelling = true, SetLastError = true)]
    private static extern IntPtr SetupDiGetClassDevsW(ref Guid guid, string? enumerator, IntPtr parent, uint flags);
    [DllImport("setupapi.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetupDiEnumDeviceInterfaces(IntPtr set, IntPtr device, ref Guid guid, uint index, ref InterfaceData data);
    [DllImport("setupapi.dll", CharSet = CharSet.Unicode, ExactSpelling = true, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetupDiGetDeviceInterfaceDetailW(IntPtr set, ref InterfaceData data, IntPtr detail, uint size, out uint required, IntPtr device);
    [DllImport("setupapi.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetupDiDestroyDeviceInfoList(IntPtr set);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, ExactSpelling = true, SetLastError = true)]
    private static extern SafeFileHandle CreateFileW(string path, uint access, uint share, IntPtr security, uint disposition, uint flags, IntPtr template);
    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool DeviceIoControl(SafeFileHandle file, uint code, IntPtr input, uint inputSize, IntPtr output, uint outputSize, IntPtr returned, IntPtr overlapped);
    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CancelIoEx(SafeFileHandle file, IntPtr overlapped);
    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetOverlappedResult(SafeFileHandle file, IntPtr overlapped, out uint transferred, [MarshalAs(UnmanagedType.Bool)] bool wait);
}
