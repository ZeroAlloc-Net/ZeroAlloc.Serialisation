namespace ZeroAlloc.Serialisation.AotSmoke;

// Nullable [ValueObject] member: STJ asks the resolver chain for typeinfo of
// Nullable<ValueObjectId>, which must wrap the generator-emitted converter so the
// wire stays a bare integer, or null when the member is null.
public sealed record ValueObjectNullableDto(ValueObjectId? Id, string Label);
