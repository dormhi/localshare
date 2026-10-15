// SPDX-License-Identifier: Apache-2.0
using System.Buffers.Binary;
namespace TabletDisplay.Host;
internal static class VideoWire
{
    internal const int MaxFrame = 1_048_576, MaxConfig = 65_536, MaxFps = 1000;
    internal sealed record Packet(byte Type, ushort Flags, uint Sequence, ulong Pts, byte[] Payload);
    internal static byte[] Header(byte type, int length, uint sequence, ulong pts = 0, ushort flags = 0)
    {
        var bytes = new byte[24];
        "TVID"u8.CopyTo(bytes); bytes[4] = 1; bytes[5] = type;
        BinaryPrimitives.WriteUInt16BigEndian(bytes.AsSpan(6), flags);
        BinaryPrimitives.WriteUInt32BigEndian(bytes.AsSpan(8), checked((uint)length));
        BinaryPrimitives.WriteUInt32BigEndian(bytes.AsSpan(12), sequence);
        BinaryPrimitives.WriteUInt64BigEndian(bytes.AsSpan(16), pts);
        Validate(bytes);
        return bytes;
    }
    private static void Validate(ReadOnlySpan<byte> bytes)
    {
        if (bytes.Length != 24 || !bytes[..4].SequenceEqual("TVID"u8) || bytes[4] != 1)
            throw new IOException("Unsupported video protocol.");
        var flags = BinaryPrimitives.ReadUInt16BigEndian(bytes[6..]);
        var length = BinaryPrimitives.ReadUInt32BigEndian(bytes[8..]);
        var pts = BinaryPrimitives.ReadUInt64BigEndian(bytes[16..]);
        bool validLength = bytes[5] switch
        {
            1 => length == 32, 2 => length == 20, 3 => length == 16,
            4 => length is >= 1 and <= MaxConfig, 5 => length is >= 1 and <= MaxFrame,
            6 or 7 => length == 0, 8 => length == 1, _ => false,
        };
        if (!validLength || flags > 1 || (bytes[5] != 5 && (flags != 0 || pts != 0)) || pts > long.MaxValue)
            throw new IOException("Invalid video packet size, flags or timestamp.");
    }
    internal static async Task<Packet> ReadAsync(Stream stream, CancellationToken token)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(token);
        timeout.CancelAfter(TimeSpan.FromSeconds(5));
        try
        {
            var header = new byte[24];
            await stream.ReadExactlyAsync(header, timeout.Token);
            Validate(header); // before trusting the payload length
            var payload = new byte[BinaryPrimitives.ReadUInt32BigEndian(header.AsSpan(8))];
            await stream.ReadExactlyAsync(payload, timeout.Token);
            return new(header[5], BinaryPrimitives.ReadUInt16BigEndian(header.AsSpan(6)),
                BinaryPrimitives.ReadUInt32BigEndian(header.AsSpan(12)), BinaryPrimitives.ReadUInt64BigEndian(header.AsSpan(16)), payload);
        }
        catch (OperationCanceledException error) when (!token.IsCancellationRequested)
        { throw new IOException("Video input exceeded its five-second deadline.", error); }
    }
    internal static async Task WriteAsync(Stream stream, Packet packet, CancellationToken token)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(token);
        timeout.CancelAfter(TimeSpan.FromSeconds(5));
        try
        {
            await stream.WriteAsync(Header(packet.Type, packet.Payload.Length, packet.Sequence, packet.Pts, packet.Flags), timeout.Token);
            await stream.WriteAsync(packet.Payload, timeout.Token);
            await stream.FlushAsync(timeout.Token);
        }
        catch (OperationCanceledException error) when (!token.IsCancellationRequested)
        { throw new IOException("Video output exceeded its five-second deadline.", error); }
    }
    internal static byte[] Integers(params uint[] values)
    {
        var bytes = new byte[values.Length * 4];
        for (int i = 0; i < values.Length; i++) BinaryPrimitives.WriteUInt32BigEndian(bytes.AsSpan(i * 4), values[i]);
        return bytes;
    }
    internal static uint Integer(byte[] bytes, int index) => BinaryPrimitives.ReadUInt32BigEndian(bytes.AsSpan(index * 4, 4));
}
