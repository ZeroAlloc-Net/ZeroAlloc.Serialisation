namespace ZeroAlloc.Serialisation.AotSmoke;

[ZeroAlloc.ValueObjects.ValueObject]
public readonly partial struct ValueObjectDateTimeId
{
    public System.DateTime Value { get; }
    public ValueObjectDateTimeId(System.DateTime value) => Value = value;
}
