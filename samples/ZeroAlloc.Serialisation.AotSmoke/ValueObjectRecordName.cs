namespace ZeroAlloc.Serialisation.AotSmoke;

// Record-class [ValueObject], #177: the generator's partial declaration must say record, not
// record struct.
[ZeroAlloc.ValueObjects.ValueObject]
public sealed partial record ValueObjectRecordName(string Value);
