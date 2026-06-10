namespace ZeroAlloc.Serialisation.AotSmoke;

[ZeroAlloc.ValueObjects.ValueObject]
public readonly partial struct ValueObjectDateTimeOffsetId
{
    public System.DateTimeOffset Value { get; }
    public ValueObjectDateTimeOffsetId(System.DateTimeOffset value) => Value = value;
}
