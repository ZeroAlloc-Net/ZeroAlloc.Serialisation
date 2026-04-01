namespace ZeroAlloc.Serialisation;

[AttributeUsage(AttributeTargets.Class | AttributeTargets.Struct)]
public sealed class ZeroAllocSerializableAttribute(SerializationFormat format) : Attribute
{
    public SerializationFormat Format { get; } = format;
}
