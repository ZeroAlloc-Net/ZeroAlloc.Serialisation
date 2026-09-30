namespace ZeroAlloc.Serialisation.AotSmoke;

// Every #177 shape as a member of a MessagePack source-generated DTO; the reference types are
// nullable and resolve through the emitted ValueObjectMessagePackResolver.
[global::MessagePack.MessagePackObject]
public sealed partial class ValueObjectShapesMpDto
{
    [global::MessagePack.Key(0)] public ValueObjectClassId? Id { get; set; }
    [global::MessagePack.Key(1)] public ValueObjectRecordName? Name { get; set; }
    [global::MessagePack.Key(2)] public ValueObjectContainer.NestedId Nested { get; set; }
    [global::MessagePack.Key(3)] public ValueObjectContainer.Inner.DeepName? Deep { get; set; }
}
