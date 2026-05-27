using System;
using System.Buffers;
using System.Text.Json;
using ZeroAlloc.Serialisation;
using ZeroAlloc.Serialisation.AotSmoke;
using ZeroAlloc.Serialisation.SystemTextJson;

// Round-trip all three V0 [ZeroAllocSerializable] formats under PublishAot=true.
// Since #15 landed, the SystemTextJson path routes through a [JsonSerializable]-
// sourced JsonTypeInfo<T> — no reflection, no JsonSerializerOptions.Default
// fallback.

var stj = new StjMessage { Id = "stj-1", Value = 42 };
var mp = new MpMessage { Id = "mp-1", Value = 99 };
var msgp = new MsgpMessage { Id = "msgp-1", Value = 7 };

var stjBuf = new ArrayBufferWriter<byte>();
new StjMessageSerializer().Serialize(stjBuf, stj);
var stjBack = new StjMessageSerializer().Deserialize(stjBuf.WrittenSpan);

var mpBuf = new ArrayBufferWriter<byte>();
new MpMessageSerializer().Serialize(mpBuf, mp);
var mpBack = new MpMessageSerializer().Deserialize(mpBuf.WrittenSpan);

var msgpBuf = new ArrayBufferWriter<byte>();
new MsgpMessageSerializer().Serialize(msgpBuf, msgp);
var msgpBack = new MsgpMessageSerializer().Deserialize(msgpBuf.WrittenSpan);

var v0Ok = string.Equals(stjBack?.Id, stj.Id, StringComparison.Ordinal)
    && string.Equals(mpBack?.Id, mp.Id, StringComparison.Ordinal)
    && string.Equals(msgpBack?.Id, msgp.Id, StringComparison.Ordinal);

// V1 value-object path under PublishAot=true + JsonSerializerContext. This is
// the surface the templates exercise: a [ValueObject] embedded in a DTO that
// the source-gen-emitted JsonTypeInfo walks at startup. Without 2.3.2's
// per-assembly IJsonTypeInfoResolver, options.GetTypeInfo(ValueObjectId)
// throws NotSupportedException("metadata not provided") because JsonContext
// can't see the [JsonConverter] attribute that ZA's generator adds via a
// partial-struct extension (gens can't see each other's output).
var jsonOptions = new JsonSerializerOptions
{
    TypeInfoResolver = ValueObjectDtoContext.Default,
};
jsonOptions.AddZeroAllocValueObjectConverters();

// 2.3.2 invariant: typeinfo for the value-object resolves through the
// per-assembly resolver inserted by the registrar.
var voTypeInfo = jsonOptions.GetTypeInfo(typeof(ValueObjectId));
var resolverWired = voTypeInfo is not null && voTypeInfo.Type == typeof(ValueObjectId);

// 2.3.1 + 2.3.2 invariant: the DTO round-trips with bare-integer wire format.
// If the resolver returned JsonContext's broken stub instead of ours, this
// would write {"Id":{"Value":42},"Label":"x"} and the bare-integer assertion
// would fail.
var dto = new ValueObjectDto(new ValueObjectId(42), "alpha");
var dtoJson = JsonSerializer.Serialize(dto, jsonOptions);
var bareIntegerWire = dtoJson.Contains("\"Id\":42", StringComparison.Ordinal)
    && !dtoJson.Contains("\"value\"", StringComparison.OrdinalIgnoreCase)
    && !dtoJson.Contains("\"Value\"", StringComparison.Ordinal);

var dtoBack = JsonSerializer.Deserialize<ValueObjectDto>(dtoJson, jsonOptions);
var roundTrip = dtoBack is not null
    && dtoBack.Id.Value == dto.Id.Value
    && string.Equals(dtoBack.Label, dto.Label, StringComparison.Ordinal);

var v1Ok = resolverWired && bareIntegerWire && roundTrip;

var ok = v0Ok && v1Ok;
if (!ok)
{
    Console.WriteLine($"AOT smoke: FAIL (v0={v0Ok}, resolver={resolverWired}, wire={bareIntegerWire}, roundTrip={roundTrip})");
    Console.WriteLine($"  dtoJson={dtoJson}");
    return 1;
}

Console.WriteLine("AOT smoke: PASS");
return 0;
