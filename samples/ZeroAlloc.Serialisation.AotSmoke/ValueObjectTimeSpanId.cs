namespace ZeroAlloc.Serialisation.AotSmoke;

[ZeroAlloc.ValueObjects.ValueObject]
public readonly partial struct ValueObjectTimeSpanId
{
    public System.TimeSpan Value { get; }
    public ValueObjectTimeSpanId(System.TimeSpan value) => Value = value;
}
