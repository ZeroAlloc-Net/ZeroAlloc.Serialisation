using System;
using System.Buffers;
using ZeroAlloc.Serialisation;
using ZeroAlloc.Serialisation.AotSmoke;

// Seed objects — one per serialization format.
var stj = new StjMessage { Id = "stj-1", Value = 42 };
var mp = new MpMessage { Id = "mp-1", Value = 99 };
var msgp = new MsgpMessage { Id = "msgp-1", Value = 7 };

var stjBuf = new ArrayBufferWriter<byte>();
new StjMessageSerializer().Serialize(stjBuf, stj);
var stjBack = new StjMessageSerializer().Deserialize(stjBuf.WrittenSpan);

var mpBuf = new ArrayBufferWriter<byte>();
new MpMessageSerializer().Serialize(mpBuf, mp);
var mpBack = new MpMessageSerializer().Deserialize(mpBuf.WrittenSpan);

var msgpBuf = new ArrayBufferWriter<byte>();
new MsgpMessageSerializer().Serialize(msgpBuf, msgp);
var msgpBack = new MsgpMessageSerializer().Deserialize(msgpBuf.WrittenSpan);

var ok = stjBack?.Id == stj.Id && mpBack?.Id == mp.Id && msgpBack?.Id == msgp.Id;
Console.WriteLine(ok ? "AOT smoke: PASS" : "AOT smoke: FAIL");
return ok ? 0 : 1;

namespace ZeroAlloc.Serialisation.AotSmoke
{
    [ZeroAllocSerializable(SerializationFormat.SystemTextJson)]
    public sealed class StjMessage
    {
        public string Id { get; set; } = "";
        public int Value { get; set; }
    }

    [MemoryPack.MemoryPackable]
    [ZeroAllocSerializable(SerializationFormat.MemoryPack)]
    public sealed partial class MpMessage
    {
        public string Id { get; set; } = "";
        public int Value { get; set; }
    }

    [MessagePack.MessagePackObject]
    [ZeroAllocSerializable(SerializationFormat.MessagePack)]
    public sealed class MsgpMessage
    {
        [MessagePack.Key(0)] public string Id { get; set; } = "";
        [MessagePack.Key(1)] public int Value { get; set; }
    }
}
