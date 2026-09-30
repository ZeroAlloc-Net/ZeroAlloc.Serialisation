namespace ZeroAlloc.Serialisation.AotSmoke;

// Nested [ValueObject] types, #177: the generator emits each partial declaration and its
// serializers inside the containing types, here a static class and a class nested in it.
public static partial class ValueObjectContainer
{
    [ZeroAlloc.ValueObjects.ValueObject]
    public readonly partial struct NestedId
    {
        public int Value { get; }
        public NestedId(int value) => Value = value;
    }

    public partial class Inner
    {
        [ZeroAlloc.ValueObjects.ValueObject]
        public sealed partial record DeepName(string Value);
    }
}
