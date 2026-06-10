namespace ZeroAlloc.Serialisation.AotSmoke;

[ZeroAlloc.ValueObjects.ValueObject]
public readonly partial struct ValueObjectBytesId
{
    public byte[] Value { get; }
    public ValueObjectBytesId(byte[] value) => Value = value;
}
