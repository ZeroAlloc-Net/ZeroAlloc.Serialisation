namespace ZeroAlloc.Serialisation.Generator;

/// <summary>
/// The names the generator gives its [ZeroAllocSerializable] and [ValueObject] pipeline steps, so
/// tests can check they stay cached.
/// </summary>
internal static class TrackingNames
{
    public const string ExtractionResults = nameof(ExtractionResults);
    public const string StjContextEntries = nameof(StjContextEntries);
    public const string BoundResults = nameof(BoundResults);
    public const string Models = nameof(Models);
    public const string AllModels = nameof(AllModels);
    public const string ValueObjectResults = nameof(ValueObjectResults);
    public const string ValueObjectModels = nameof(ValueObjectModels);
    public const string ValueObjectBackends = nameof(ValueObjectBackends);
    public const string ValueObjectInput = nameof(ValueObjectInput);
    public const string AllValueObjectModels = nameof(AllValueObjectModels);
    public const string ValueObjectRegistrarInput = nameof(ValueObjectRegistrarInput);
}
