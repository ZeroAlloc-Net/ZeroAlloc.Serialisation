// Stub the [ValueObjectAttribute] inline. The generator FQN-matches the
// attribute by name and doesn't depend on the ZeroAlloc.ValueObjects package's
// runtime semantics; keeping the smoke project self-contained avoids
// version-pinning a sibling repo just to exercise this code path.
namespace ZeroAlloc.ValueObjects
{
    [System.AttributeUsage(System.AttributeTargets.Class | System.AttributeTargets.Struct)]
    public sealed class ValueObjectAttribute : System.Attribute { }
}

namespace ZeroAlloc.Serialisation.AotSmoke;

// V1 value-object fixture: [ValueObject] partial struct wrapping a single
// primitive. The generator emits:
//   - A partial extension carrying [JsonConverter(typeof(...))]
//   - An internal sealed ValueObjectIdSystemTextJsonConverter class
//   - A per-assembly registrar (ValueObjectJsonConvertersExtensions)
//   - A per-assembly JsonTypeInfoResolver (2.3.2)
//
// All four must survive Native AOT publish + the trimmer's reachability
// analysis for the wire format to come out bare-integer at runtime.
[ZeroAlloc.ValueObjects.ValueObject]
public readonly partial struct ValueObjectId
{
    public int Value { get; }
    public ValueObjectId(int value) => Value = value;
}
