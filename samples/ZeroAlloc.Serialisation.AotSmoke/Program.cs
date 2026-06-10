using System;
using System.Buffers;
using System.Text.Json;
using ZeroAlloc.Serialisation;
using ZeroAlloc.Serialisation.AotSmoke;
using ZeroAlloc.Serialisation.SystemTextJson;
using ZeroAlloc.Serialisation.MessagePack;

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
// Use the source-gen-typeinfo overloads of Serialize/Deserialize so the
// smoke compiles cleanly under <WarningsAsErrors>IL2026;IL3050;...</WarningsAsErrors>
// (the options-based JsonSerializer.Serialize<T>(value, options) overload
// triggers IL2026 + IL3050 because it walks reflection at compile-time
// analysis even when the runtime resolver is source-gen-backed). The
// typeinfo path STILL respects options.Converters (the 2.3.1 registrar's
// Converters.Add precedence flows through GetConverter), so the test
// invariants are preserved.
var dto = new ValueObjectDto(new ValueObjectId(42), "alpha");
var customContext = new ValueObjectDtoContext(jsonOptions);
var dtoJson = JsonSerializer.Serialize(dto, customContext.ValueObjectDto);
var bareIntegerWire = dtoJson.Contains("\"Id\":42", StringComparison.Ordinal)
    && !dtoJson.Contains("\"value\"", StringComparison.OrdinalIgnoreCase)
    && !dtoJson.Contains("\"Value\"", StringComparison.Ordinal);

var dtoBack = JsonSerializer.Deserialize(dtoJson, customContext.ValueObjectDto);
var roundTrip = dtoBack is not null
    && dtoBack.Id.Value == dto.Id.Value
    && string.Equals(dtoBack.Label, dto.Label, StringComparison.Ordinal);

var v1Ok = resolverWired && bareIntegerWire && roundTrip;

// V2 (2.3.3): MessagePack source-gen + [ValueObject] field on a [MessagePackObject]
// DTO. Without 2.3.3's resolver, the GeneratedMessagePackResolver returns a
// default-shape formatter for ValueObjectMpId, producing wrong wire format.
// AddZeroAllocValueObjectFormatters prepends our resolver to the chain,
// intercepting the value-object lookup before the source-gen resolver sees it.
var mpOptions = global::MessagePack.MessagePackSerializerOptions.Standard
    .AddZeroAllocValueObjectFormatters();

var mpDto = new ValueObjectMpDto { Id = new ValueObjectMpId(42), Label = "alpha" };
byte[] mpBytes;
ValueObjectMpDto? mpDtoBack;
string mpJson;
try
{
    mpBytes = global::MessagePack.MessagePackSerializer.Serialize(mpDto, mpOptions);
    mpJson = global::MessagePack.MessagePackSerializer.ConvertToJson(mpBytes);
    mpDtoBack = global::MessagePack.MessagePackSerializer.Deserialize<ValueObjectMpDto>(mpBytes, mpOptions);
}
catch (Exception ex)
{
    Console.WriteLine($"AOT smoke: FAIL (MessagePack source-gen DTO threw {ex.GetType().Name}: {ex.Message})");
    return 1;
}

// Wire format invariant: MessagePack 3.x with [Key(int)] uses intkey array
// layout — ConvertToJson renders as [42,"alpha"]. Without 2.3.3's resolver,
// the value-object field would serialize as a wrapped sub-array [[42],"alpha"].
var mpBareInteger = string.Equals(mpJson, "[42,\"alpha\"]", StringComparison.Ordinal);
var mpRoundTrip = mpDtoBack is not null
    && mpDtoBack.Id.Value == mpDto.Id.Value
    && string.Equals(mpDtoBack.Label, mpDto.Label, StringComparison.Ordinal);

var v2Ok = mpBareInteger && mpRoundTrip;

// V2 underlying-type coverage smoke (shipped 2.4.0): the generator's
// MessagePackReadWriteForType + SystemTextJsonReadWriteForType switches now
// cover Guid, DateTime, DateTimeOffset, TimeSpan, decimal, byte[] beyond the
// bare primitives. Each [ValueObject] fixture below has both an STJ converter
// and a MessagePack formatter emitted by the generator; we invoke them
// directly (instead of through JsonSerializerContext / GeneratedMessagePackResolver)
// so the smoke focuses on the per-type read/write emit shape — without
// requiring 6 extra [JsonSerializable] entries on ValueObjectDtoContext or
// 6 extra [MessagePackObject] DTO wrappers. The 2.3.x context/resolver
// integration paths are already covered by the v1Ok/v2Ok blocks above.
var underlyingOk = true;
var underlyingFailures = new System.Collections.Generic.List<string>();

static bool TryStj<T>(System.Text.Json.Serialization.JsonConverter<T> converter, T input, System.Func<T, T, bool> equals, out string failure)
{
    failure = "";
    try
    {
        var buf = new ArrayBufferWriter<byte>();
        using (var w = new System.Text.Json.Utf8JsonWriter(buf))
        {
            converter.Write(w, input, new JsonSerializerOptions());
        }
        var reader = new System.Text.Json.Utf8JsonReader(buf.WrittenSpan);
        reader.Read();
        var back = converter.Read(ref reader, typeof(T), new JsonSerializerOptions());
        if (!equals(input, back))
        {
            failure = $"stj round-trip mismatch for {typeof(T).Name}: wire={System.Text.Encoding.UTF8.GetString(buf.WrittenSpan)}";
            return false;
        }
        return true;
    }
    catch (Exception ex)
    {
        failure = $"stj threw {ex.GetType().Name} for {typeof(T).Name}: {ex.Message}";
        return false;
    }
}

static bool TryMp<T>(global::MessagePack.Formatters.IMessagePackFormatter<T> formatter, T input, System.Func<T, T, bool> equals, out string failure)
{
    failure = "";
    try
    {
        var buf = new ArrayBufferWriter<byte>();
        var writer = new global::MessagePack.MessagePackWriter(buf);
        formatter.Serialize(ref writer, input, global::MessagePack.MessagePackSerializerOptions.Standard);
        writer.Flush();
        var reader = new global::MessagePack.MessagePackReader(buf.WrittenMemory);
        var back = formatter.Deserialize(ref reader, global::MessagePack.MessagePackSerializerOptions.Standard);
        if (!equals(input, back))
        {
            failure = $"mp round-trip mismatch for {typeof(T).Name}";
            return false;
        }
        return true;
    }
    catch (Exception ex)
    {
        failure = $"mp threw {ex.GetType().Name} for {typeof(T).Name}: {ex.Message}";
        return false;
    }
}

// Guid
{
    var input = new ValueObjectGuidId(Guid.NewGuid());
    if (!TryStj(new ValueObjectGuidIdSystemTextJsonConverter(), input, (a, b) => a.Value == b.Value, out var f1))
    { underlyingOk = false; underlyingFailures.Add(f1); }
    if (!TryMp(new ValueObjectGuidIdMessagePackFormatter(), input, (a, b) => a.Value == b.Value, out var f2))
    { underlyingOk = false; underlyingFailures.Add(f2); }
}

// DateTime
{
    var input = new ValueObjectDateTimeId(new DateTime(2026, 6, 10, 12, 34, 56, DateTimeKind.Utc));
    if (!TryStj(new ValueObjectDateTimeIdSystemTextJsonConverter(), input, (a, b) => a.Value == b.Value, out var f1))
    { underlyingOk = false; underlyingFailures.Add(f1); }
    if (!TryMp(new ValueObjectDateTimeIdMessagePackFormatter(), input, (a, b) => a.Value == b.Value, out var f2))
    { underlyingOk = false; underlyingFailures.Add(f2); }
}

// DateTimeOffset
{
    var input = new ValueObjectDateTimeOffsetId(new DateTimeOffset(2026, 6, 10, 12, 34, 56, TimeSpan.FromHours(2)));
    if (!TryStj(new ValueObjectDateTimeOffsetIdSystemTextJsonConverter(), input, (a, b) => a.Value == b.Value, out var f1))
    { underlyingOk = false; underlyingFailures.Add(f1); }
    if (!TryMp(new ValueObjectDateTimeOffsetIdMessagePackFormatter(), input, (a, b) => a.Value == b.Value, out var f2))
    { underlyingOk = false; underlyingFailures.Add(f2); }
}

// TimeSpan
{
    var input = new ValueObjectTimeSpanId(TimeSpan.FromMinutes(42.5));
    if (!TryStj(new ValueObjectTimeSpanIdSystemTextJsonConverter(), input, (a, b) => a.Value == b.Value, out var f1))
    { underlyingOk = false; underlyingFailures.Add(f1); }
    if (!TryMp(new ValueObjectTimeSpanIdMessagePackFormatter(), input, (a, b) => a.Value == b.Value, out var f2))
    { underlyingOk = false; underlyingFailures.Add(f2); }
}

// decimal
{
    var input = new ValueObjectDecimalId(12345.6789m);
    if (!TryStj(new ValueObjectDecimalIdSystemTextJsonConverter(), input, (a, b) => a.Value == b.Value, out var f1))
    { underlyingOk = false; underlyingFailures.Add(f1); }
    if (!TryMp(new ValueObjectDecimalIdMessagePackFormatter(), input, (a, b) => a.Value == b.Value, out var f2))
    { underlyingOk = false; underlyingFailures.Add(f2); }
}

// byte[]
{
    var input = new ValueObjectBytesId(new byte[] { 1, 2, 3, 4, 5, 42, 99 });
    if (!TryStj(new ValueObjectBytesIdSystemTextJsonConverter(), input, (a, b) => a.Value.AsSpan().SequenceEqual(b.Value), out var f1))
    { underlyingOk = false; underlyingFailures.Add(f1); }
    if (!TryMp(new ValueObjectBytesIdMessagePackFormatter(), input, (a, b) => a.Value.AsSpan().SequenceEqual(b.Value), out var f2))
    { underlyingOk = false; underlyingFailures.Add(f2); }
}

var ok = v0Ok && v1Ok && v2Ok && underlyingOk;
if (!ok)
{
    Console.WriteLine($"AOT smoke: FAIL (v0={v0Ok}, v1.resolver={resolverWired}, v1.wire={bareIntegerWire}, v1.roundTrip={roundTrip}, v2.bareInt={mpBareInteger}, v2.roundTrip={mpRoundTrip}, underlying={underlyingOk})");
    Console.WriteLine($"  dtoJson={dtoJson}");
    Console.WriteLine($"  mpJson={mpJson}");
    foreach (var failure in underlyingFailures)
    {
        Console.WriteLine($"  underlying: {failure}");
    }
    return 1;
}

Console.WriteLine("AOT smoke: PASS");
return 0;
