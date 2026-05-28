// The [ValueObjectAttribute] stub lives in ValueObjectId.cs (merged in PR #38).
// Reuse it via the FQN — declaring it again here would trip CS0101 / CS0579.

namespace ZeroAlloc.Serialisation.AotSmoke
{
    [ZeroAlloc.ValueObjects.ValueObject]
    public readonly partial struct ValueObjectMpId
    {
        public int Value { get; }
        public ValueObjectMpId(int value) => Value = value;
    }
}
