namespace ZeroAlloc.ValueObjects
{
    [System.AttributeUsage(System.AttributeTargets.Class | System.AttributeTargets.Struct)]
    public sealed class ValueObjectAttribute : System.Attribute { }
}

namespace ZeroAlloc.Serialisation.Tests
{
    [global::ZeroAlloc.ValueObjects.ValueObject]
    public readonly partial struct CustomerId
    {
        public int Value { get; }
        public CustomerId(int value) => Value = value;
    }

    [global::ZeroAlloc.ValueObjects.ValueObject]
    public readonly partial struct ShiftLength
    {
        public global::System.TimeSpan Value { get; }
        public ShiftLength(global::System.TimeSpan value) => Value = value;
    }
}
