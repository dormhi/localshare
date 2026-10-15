// SPDX-License-Identifier: Apache-2.0
using TabletDisplay.Host;
using System.Buffers.Binary;
static void Check(bool value) { if (!value) throw new Exception("Assertion failed"); }
static async Task Rejected(Func<Task> action) {
    try { await action(); } catch (IOException) { return; }
    throw new Exception("Malformed video packet accepted");
}
Check(Convert.ToHexString(VideoWire.Header(1,32,0)) == "545649440101000000000020000000000000000000000000");
using var stream = new MemoryStream();
await VideoWire.WriteAsync(stream, new(5,1,3,12345,new byte[]{9,8,7}), CancellationToken.None);
var bytes=stream.ToArray();
using var fragments=new Fragments(bytes);
var frame=await VideoWire.ReadAsync(fragments,CancellationToken.None);
Check(frame.Type==5 && frame.Sequence==3 && frame.Pts==12345 && frame.Payload.SequenceEqual(new byte[]{9,8,7}));
for(int n=0;n<bytes.Length;n++) {
    using var truncated=new MemoryStream(bytes[..n]);
    await Rejected(async()=>{await VideoWire.ReadAsync(truncated,CancellationToken.None);});
}
foreach(uint size in new uint[]{0,1048577,uint.MaxValue}) {
    var header=VideoWire.Header(5,1,3);
    BinaryPrimitives.WriteUInt32BigEndian(header.AsSpan(8),size);
    using var invalid=new MemoryStream(header);
    await Rejected(async()=>{await VideoWire.ReadAsync(invalid,CancellationToken.None);});
}
await Rejected(()=>{VideoWire.Header(5,1,3,ulong.MaxValue);return Task.CompletedTask;});
await Rejected(()=>{VideoWire.Header(2,20,0,1);return Task.CompletedTask;});
await Rejected(()=>{VideoWire.Header(4,65537,2);return Task.CompletedTask;});
await Rejected(()=>{VideoWire.Header(1,32,0,0,1);return Task.CompletedTask;});
foreach(uint fps in new uint[]{1,15,30,60,120,144,240}) Check(VideoWire.Integer(VideoWire.Integers(1280,800,fps,3000000),2)==fps);
using var stop=new CancellationTokenSource();stop.Cancel();
try { await VideoWire.ReadAsync(new MemoryStream(bytes),stop.Token);throw new Exception("Cancellation ignored"); }
catch(OperationCanceledException) { }
try { await VideoWire.ReadAsync(new Stalled(),CancellationToken.None); throw new Exception("Deadline ignored"); }
catch(IOException error) { Check(error.InnerException is OperationCanceledException); }
Console.WriteLine("PASS: shared video golden bytes, fragmented/truncated reads, pre-allocation length checks, FPS values and cancellation");
sealed class Fragments(byte[] data):MemoryStream(data) {
    public override ValueTask<int> ReadAsync(Memory<byte> buffer,CancellationToken token=default) => base.ReadAsync(buffer[..Math.Min(1,buffer.Length)],token);
}

sealed class Stalled:MemoryStream {
    public override async ValueTask<int> ReadAsync(Memory<byte> buffer,CancellationToken token=default) {
        await Task.Delay(Timeout.Infinite,token); return 0;
    }
}
