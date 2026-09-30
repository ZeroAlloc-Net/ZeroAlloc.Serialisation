namespace ZeroAlloc.Serialisation.AotSmoke;

// Every #177 shape as a member of a MemoryPack source-generated DTO. MemoryPack's generator
// cannot see the formatter this generator registers, so a class member that is not
// [MemoryPackable] needs [MemoryPackAllowSerialize] to defer to the registered formatter.
[global::MemoryPack.MemoryPackable]
public sealed partial class ValueObjectShapesMemoryPackDto
{
    [global::MemoryPack.MemoryPackAllowSerialize] public ValueObjectClassId? Id { get; set; }
    [global::MemoryPack.MemoryPackAllowSerialize] public ValueObjectRecordName? Name { get; set; }
    public ValueObjectContainer.NestedId Nested { get; set; }
    [global::MemoryPack.MemoryPackAllowSerialize] public ValueObjectContainer.Inner.DeepName? Deep { get; set; }
}
