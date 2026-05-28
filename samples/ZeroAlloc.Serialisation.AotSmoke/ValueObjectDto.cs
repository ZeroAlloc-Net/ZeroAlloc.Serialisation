namespace ZeroAlloc.Serialisation.AotSmoke;

// Container DTO mirroring the shape the templates use: a record carrying a
// typed-ID property. Without the 2.3.1 registrar + 2.3.2 resolver, the
// JsonSerializerContext source-gen path would either write {"id":{"value":42}}
// (wrong wire format) or throw NotSupportedException at startup when ASP.NET
// Core walks the property graph and asks for typeinfo for ValueObjectId.
public sealed record ValueObjectDto(ValueObjectId Id, string Label);
