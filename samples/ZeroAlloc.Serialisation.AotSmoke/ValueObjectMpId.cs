// Stub the [ValueObjectAttribute] inline. The generator FQN-matches by name
// and doesn't depend on the ZeroAlloc.ValueObjects package's runtime semantics.
// Mp-suffixed type name avoids collision with the (not-yet-merged) PR #38
// shared ValueObjectId fixture — both PRs stay independently reviewable.
namespace ZeroAlloc.ValueObjects
{
    [System.AttributeUsage(System.AttributeTargets.Class | System.AttributeTargets.Struct)]
    public sealed class ValueObjectAttribute : System.Attribute { }
}

namespace ZeroAlloc.Serialisation.AotSmoke
{
    [ZeroAlloc.ValueObjects.ValueObject]
    public readonly partial struct ValueObjectMpId
    {
        public int Value { get; }
        public ValueObjectMpId(int value) => Value = value;
    }
}
