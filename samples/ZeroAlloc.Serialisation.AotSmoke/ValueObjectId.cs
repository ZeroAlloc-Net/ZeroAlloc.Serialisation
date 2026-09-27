namespace ZeroAlloc.Serialisation.AotSmoke
{
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
}
