// V2 underlying-type-coverage smoke: Guid-backed [ValueObject]. Exercises the
// resolver-dispatch arm in MessagePackReadWriteForType + the GetGuid/WriteStringValue
// arm in SystemTextJsonReadWriteForType.
namespace ZeroAlloc.Serialisation.AotSmoke;

[ZeroAlloc.ValueObjects.ValueObject]
public readonly partial struct ValueObjectGuidId
{
    public System.Guid Value { get; }
    public ValueObjectGuidId(System.Guid value) => Value = value;
}
