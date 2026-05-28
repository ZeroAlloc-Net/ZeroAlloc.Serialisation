namespace ZeroAlloc.Serialisation.AotSmoke;

// [MessagePackObject] DTO containing a [ValueObject]-typed property. Under
// MessagePack-CSharp 3.x source-gen (MessagePackAnalyzer), the source-gen
// resolver builds typeinfo for this DTO at compile time — including a
// property descriptor for ValueObjectMpId that knows nothing about the
// [MessagePackFormatter] attribute ZA's generator adds via a partial
// (gens-can't-see-each-other). Without 2.3.3's resolver inserted into the
// chain, the wire format collapses to a wrapped sub-array [[42],"alpha"]
// instead of the bare [42,"alpha"] expected.
// global:: qualification: this assembly references ZeroAlloc.Serialisation.MessagePack
// (the ZA adapter lib), so `MessagePack` shorthand inside the
// ZeroAlloc.Serialisation.* namespace resolves ambiguously.
[global::MessagePack.MessagePackObject]
public sealed partial class ValueObjectMpDto
{
    [global::MessagePack.Key(0)] public ValueObjectMpId Id { get; set; }
    [global::MessagePack.Key(1)] public string Label { get; set; } = "";
}
