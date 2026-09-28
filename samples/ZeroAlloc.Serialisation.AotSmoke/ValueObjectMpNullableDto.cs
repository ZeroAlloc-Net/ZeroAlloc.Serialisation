namespace ZeroAlloc.Serialisation.AotSmoke;

// Nullable [ValueObject] member under MessagePack source-gen: the resolver chain is
// asked for IMessagePackFormatter<Nullable<ValueObjectMpId>>, so the emitted
// ValueObjectMessagePackResolver.FormatterCache<T> runs with T = Nullable<ValueObjectMpId>.
[global::MessagePack.MessagePackObject]
public sealed partial class ValueObjectMpNullableDto
{
    [global::MessagePack.Key(0)] public ValueObjectMpId? Id { get; set; }
    [global::MessagePack.Key(1)] public string Label { get; set; } = "";
}
