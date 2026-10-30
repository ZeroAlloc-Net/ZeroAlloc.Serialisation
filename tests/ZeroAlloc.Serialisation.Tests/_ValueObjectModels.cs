namespace ZeroAlloc.ValueObjects
{
    [System.AttributeUsage(System.AttributeTargets.Class | System.AttributeTargets.Struct)]
    public sealed class ValueObjectAttribute : System.Attribute { }

    [System.AttributeUsage(System.AttributeTargets.Class | System.AttributeTargets.Struct)]
    public sealed class ValueObjectFactoryAttribute : System.Attribute
    {
        public ValueObjectFactoryAttribute(string methodName) => MethodName = methodName;
        public string MethodName { get; }
    }
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

    // A non-validating constructor with a validating TryCreate: the convention picks TryCreate.
    [global::ZeroAlloc.ValueObjects.ValueObject]
    public readonly partial struct Sku
    {
        public string Value { get; }
        internal Sku(string value) => Value = value;

        public static bool TryCreate(string? input, out Sku sku)
        {
            sku = default;
            if (input is null || input.Length != 8 || !input.StartsWith("SKU-", global::System.StringComparison.Ordinal)) return false;
            sku = new Sku(input);
            return true;
        }
    }

    // A private constructor: only the factory can build it, so the generated code must not call it.
    [global::ZeroAlloc.ValueObjects.ValueObject]
    public readonly partial struct PrivateCtorSku
    {
        public string Value { get; }
        private PrivateCtorSku(string value) => Value = value;

        public static bool TryCreate(string? input, out PrivateCtorSku sku)
        {
            sku = default;
            if (input is null || input.Length != 8 || !input.StartsWith("SKU-", global::System.StringComparison.Ordinal)) return false;
            sku = new PrivateCtorSku(input);
            return true;
        }
    }

    // A class with a private constructor and a throwing Create.
    [global::ZeroAlloc.ValueObjects.ValueObject]
    public sealed partial class Slug
    {
        public string Value { get; }
        private Slug(string value) => Value = value;

        public static Slug Create(string value)
        {
            if (value.Length == 0 || value.Contains(' ', global::System.StringComparison.Ordinal))
                throw new global::System.ArgumentException("A slug has no spaces.", nameof(value));
            return new Slug(value);
        }
    }

    // A class with TryCreate.
    [global::ZeroAlloc.ValueObjects.ValueObject]
    public sealed partial class Code
    {
        public int Value { get; }
        private Code(int value) => Value = value;

        public static bool TryCreate(int input, out Code? code)
        {
            code = input > 0 ? new Code(input) : null;
            return code is not null;
        }
    }

    // Both shapes: TryCreate wins, so the throwing Create is never called.
    // ZASZ015 is expected here: MemoryPack copies this struct as raw memory, so its factory runs
    // only on the other backends, which is what ValueObjectFactoryTests checks.
#pragma warning disable ZASZ015
    [global::ZeroAlloc.ValueObjects.ValueObject]
    public readonly partial struct BothShapes
    {
        public int Value { get; }
        internal BothShapes(int value) => Value = value;

        public static bool TryCreate(int input, out BothShapes result)
        {
            result = new BothShapes(input);
            return input >= 0;
        }

        public static BothShapes Create(int input) => throw new global::System.InvalidOperationException("Create must not be called.");
    }
#pragma warning restore ZASZ015

    // The attribute overrides the convention: TryCreate rejects everything, Parse is used.
    [global::ZeroAlloc.ValueObjects.ValueObject]
    [global::ZeroAlloc.ValueObjects.ValueObjectFactory(nameof(Parse))]
    public readonly partial struct Explicit
    {
        public string Value { get; }
        internal Explicit(string value) => Value = value;

        public static bool TryCreate(string? input, out Explicit result)
        {
            result = default;
            return false;
        }

        public static bool Parse(string? input, out Explicit result)
        {
            result = default;
            if (string.IsNullOrEmpty(input)) return false;
            result = new Explicit(input);
            return true;
        }
    }

    // The attribute can name a Create-shaped method too.
    // ZASZ015 is expected here: MemoryPack copies this struct as raw memory, so its factory runs
    // only on the other backends, which is what ValueObjectFactoryTests checks.
#pragma warning disable ZASZ015
    [global::ZeroAlloc.ValueObjects.ValueObject]
    [global::ZeroAlloc.ValueObjects.ValueObjectFactory(nameof(From))]
    public readonly partial struct ExplicitCreate
    {
        public int Value { get; }
        private ExplicitCreate(int value) => Value = value;

        public static ExplicitCreate From(int value) =>
            value % 2 == 0 ? new ExplicitCreate(value) : throw new global::System.ArgumentOutOfRangeException(nameof(value));
    }
#pragma warning restore ZASZ015

    // The TimeSpan wire path has its own emit site.
    // ZASZ015 is expected here: MemoryPack copies this struct as raw memory, so its factory runs
    // only on the other backends, which is what ValueObjectFactoryTests checks.
#pragma warning disable ZASZ015
    [global::ZeroAlloc.ValueObjects.ValueObject]
    public readonly partial struct PositiveSpan
    {
        public global::System.TimeSpan Value { get; }
        private PositiveSpan(global::System.TimeSpan value) => Value = value;

        public static bool TryCreate(global::System.TimeSpan input, out PositiveSpan result)
        {
            result = default;
            if (input <= global::System.TimeSpan.Zero) return false;
            result = new PositiveSpan(input);
            return true;
        }
    }
#pragma warning restore ZASZ015
}
