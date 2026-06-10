namespace ZeroAlloc.Serialisation.AotSmoke;

[ZeroAlloc.ValueObjects.ValueObject]
public readonly partial struct ValueObjectDecimalId
{
    public decimal Value { get; }
    public ValueObjectDecimalId(decimal value) => Value = value;
}
