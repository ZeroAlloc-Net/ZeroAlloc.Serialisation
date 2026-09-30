namespace ZeroAlloc.Serialisation.AotSmoke;

// Reference-type [ValueObject], #177: the generator repeats the declaration kind, class here,
// and every backend writes and reads null for it.
[ZeroAlloc.ValueObjects.ValueObject]
public sealed partial class ValueObjectClassId
{
    public int Value { get; }
    public ValueObjectClassId(int value) => Value = value;
}
