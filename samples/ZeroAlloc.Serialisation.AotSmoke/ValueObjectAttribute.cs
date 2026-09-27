// Stub the [ValueObjectAttribute] inline. The generator FQN-matches the
// attribute by name and doesn't depend on the ZeroAlloc.ValueObjects package's
// runtime semantics; keeping the smoke project self-contained avoids
// version-pinning a sibling repo just to exercise this code path.
namespace ZeroAlloc.ValueObjects
{
    [System.AttributeUsage(System.AttributeTargets.Class | System.AttributeTargets.Struct)]
    public sealed class ValueObjectAttribute : System.Attribute { }
}
