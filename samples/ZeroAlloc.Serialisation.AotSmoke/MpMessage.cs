namespace ZeroAlloc.Serialisation.AotSmoke
{
    [MemoryPack.MemoryPackable]
    [ZeroAllocSerializable(SerializationFormat.MemoryPack)]
    public sealed partial class MpMessage
    {
        public string Id { get; set; } = "";
        public int Value { get; set; }
    }
}
