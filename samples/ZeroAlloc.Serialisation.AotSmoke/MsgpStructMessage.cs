namespace ZeroAlloc.Serialisation.AotSmoke;

// Value-type MessagePack target; see StjStructMessage.
[global::MessagePack.MessagePackObject]
[ZeroAllocSerializable(SerializationFormat.MessagePack)]
public readonly record struct MsgpStructMessage(
    [property: global::MessagePack.Key(0)] int A,
    [property: global::MessagePack.Key(1)] string B);
