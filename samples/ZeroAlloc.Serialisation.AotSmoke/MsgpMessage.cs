namespace ZeroAlloc.Serialisation.AotSmoke
{
    [global::MessagePack.MessagePackObject]
    [ZeroAllocSerializable(SerializationFormat.MessagePack)]
    public sealed class MsgpMessage
    {
        [global::MessagePack.Key(0)] public string Id { get; set; } = "";
        [global::MessagePack.Key(1)] public int Value { get; set; }
    }
}
