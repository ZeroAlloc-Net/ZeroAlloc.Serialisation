namespace ZeroAlloc.Serialisation.AotSmoke
{
    [MessagePack.MessagePackObject]
    [ZeroAllocSerializable(SerializationFormat.MessagePack)]
    public sealed class MsgpMessage
    {
        [MessagePack.Key(0)] public string Id { get; set; } = "";
        [MessagePack.Key(1)] public int Value { get; set; }
    }
}
