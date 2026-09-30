namespace ZeroAlloc.Serialisation.AotSmoke;

// Every #177 shape as a System.Text.Json DTO member; the reference types are nullable.
public sealed record ValueObjectShapesDto(
    ValueObjectClassId? Id,
    ValueObjectRecordName? Name,
    ValueObjectContainer.NestedId Nested,
    ValueObjectContainer.Inner.DeepName? Deep);
