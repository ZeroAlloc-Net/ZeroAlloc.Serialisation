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
        if (back is null)
        {
            failure = $"stj round-trip returned null for {typeof(T).Name}";
            return false;
        }
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

// Value-type coverage (ZeroAlloc-Net/ZeroAlloc.Cache#182, dotnet/runtime#134799):
// NativeAOT miscompiles some generic paths only when T is a value type, in
// particular Nullable<X>. Every block below drives a user-reachable generic or
// type-dispatch path with a struct or nullable shape and asserts the values.
var valueTypeFailures = new System.Collections.Generic.List<string>();

static string Utf8(ReadOnlySpan<byte> bytes) => System.Text.Encoding.UTF8.GetString(bytes);

// Struct [ZeroAllocSerializable] in all three formats, through the generated
// XSerializer and through the generated SerializerDispatcher.
var dispatcher = new SerializerDispatcher();
try
{
    var stjStruct = new StjStructMessage(7, "seven");
    var stjStructBuf = new ArrayBufferWriter<byte>();
    new StjStructMessageSerializer().Serialize(stjStructBuf, stjStruct);
    var stjStructJson = Utf8(stjStructBuf.WrittenSpan);
    if (!string.Equals(stjStructJson, "{\"A\":7,\"B\":\"seven\"}", StringComparison.Ordinal))
        valueTypeFailures.Add($"stj struct wire={stjStructJson}");
    var stjStructBack = new StjStructMessageSerializer().Deserialize(stjStructBuf.WrittenSpan);
    if (stjStructBack != stjStruct)
        valueTypeFailures.Add($"stj struct round-trip={stjStructBack}");
    var stjStructDisp = dispatcher.Serialize(stjStruct, typeof(StjStructMessage));
    if (!string.Equals(Utf8(stjStructDisp.Span), stjStructJson, StringComparison.Ordinal))
        valueTypeFailures.Add($"stj struct dispatcher wire={Utf8(stjStructDisp.Span)}");
    if (dispatcher.Deserialize(stjStructDisp, typeof(StjStructMessage)) is not StjStructMessage stjStructDispBack
        || stjStructDispBack != stjStruct)
        valueTypeFailures.Add("stj struct dispatcher round-trip mismatch");

    var mpStruct = new MpStructMessage(8, "eight");
    var mpStructBuf = new ArrayBufferWriter<byte>();
    new MpStructMessageSerializer().Serialize(mpStructBuf, mpStruct);
    var mpStructBack = new MpStructMessageSerializer().Deserialize(mpStructBuf.WrittenSpan);
    if (mpStructBack != mpStruct)
        valueTypeFailures.Add($"memorypack struct round-trip={mpStructBack}");
    var mpStructDisp = dispatcher.Serialize(mpStruct, typeof(MpStructMessage));
    if (!mpStructDisp.Span.SequenceEqual(mpStructBuf.WrittenSpan))
        valueTypeFailures.Add("memorypack struct dispatcher bytes differ from serializer bytes");
    if (dispatcher.Deserialize(mpStructDisp, typeof(MpStructMessage)) is not MpStructMessage mpStructDispBack
        || mpStructDispBack != mpStruct)
        valueTypeFailures.Add("memorypack struct dispatcher round-trip mismatch");

    var msgpStruct = new MsgpStructMessage(9, "nine");
    var msgpStructBuf = new ArrayBufferWriter<byte>();
    new MsgpStructMessageSerializer().Serialize(msgpStructBuf, msgpStruct);
    var msgpStructJson = global::MessagePack.MessagePackSerializer.ConvertToJson(msgpStructBuf.WrittenMemory);
    if (!string.Equals(msgpStructJson, "[9,\"nine\"]", StringComparison.Ordinal))
        valueTypeFailures.Add($"messagepack struct wire={msgpStructJson}");
    var msgpStructBack = new MsgpStructMessageSerializer().Deserialize(msgpStructBuf.WrittenSpan);
    if (msgpStructBack != msgpStruct)
        valueTypeFailures.Add($"messagepack struct round-trip={msgpStructBack}");
    var msgpStructDisp = dispatcher.Serialize(msgpStruct, typeof(MsgpStructMessage));
    if (!msgpStructDisp.Span.SequenceEqual(msgpStructBuf.WrittenSpan))
        valueTypeFailures.Add("messagepack struct dispatcher bytes differ from serializer bytes");
    if (dispatcher.Deserialize(msgpStructDisp, typeof(MsgpStructMessage)) is not MsgpStructMessage msgpStructDispBack
        || msgpStructDispBack != msgpStruct)
        valueTypeFailures.Add("messagepack struct dispatcher round-trip mismatch");
}
catch (Exception ex)
{
    valueTypeFailures.Add($"struct serializers threw {ex.GetType().Name}: {ex.Message}");
}

// Nullable [ValueObject] member under STJ source-gen: value and null.
try
{
    var withId = new ValueObjectNullableDto(new ValueObjectId(42), "alpha");
    var withIdJson = JsonSerializer.Serialize(withId, customContext.ValueObjectNullableDto);
    if (!string.Equals(withIdJson, "{\"Id\":42,\"Label\":\"alpha\"}", StringComparison.Ordinal))
        valueTypeFailures.Add($"stj nullable VO wire={withIdJson}");
    var withIdBack = JsonSerializer.Deserialize(withIdJson, customContext.ValueObjectNullableDto);
    if (withIdBack is null || withIdBack.Id?.Value != 42 || !string.Equals(withIdBack.Label, "alpha", StringComparison.Ordinal))
        valueTypeFailures.Add($"stj nullable VO round-trip={withIdBack}");

    var noId = new ValueObjectNullableDto(null, "beta");
    var noIdJson = JsonSerializer.Serialize(noId, customContext.ValueObjectNullableDto);
    if (!string.Equals(noIdJson, "{\"Id\":null,\"Label\":\"beta\"}", StringComparison.Ordinal))
        valueTypeFailures.Add($"stj null VO wire={noIdJson}");
    var noIdBack = JsonSerializer.Deserialize(noIdJson, customContext.ValueObjectNullableDto);
    if (noIdBack is null || noIdBack.Id is not null || !string.Equals(noIdBack.Label, "beta", StringComparison.Ordinal))
        valueTypeFailures.Add($"stj null VO round-trip={noIdBack}");
}
catch (Exception ex)
{
    valueTypeFailures.Add($"stj nullable VO threw {ex.GetType().Name}: {ex.Message}");
}

// Nullable [ValueObject] member under MessagePack source-gen: the emitted resolver's
// FormatterCache<T> is instantiated with T = Nullable<ValueObjectMpId>.
try
{
    var withId = new ValueObjectMpNullableDto { Id = new ValueObjectMpId(42), Label = "alpha" };
    var withIdBytes = global::MessagePack.MessagePackSerializer.Serialize(withId, mpOptions);
    var withIdJson = global::MessagePack.MessagePackSerializer.ConvertToJson(withIdBytes);
    if (!string.Equals(withIdJson, "[42,\"alpha\"]", StringComparison.Ordinal))
        valueTypeFailures.Add($"messagepack nullable VO wire={withIdJson}");
    var withIdBack = global::MessagePack.MessagePackSerializer.Deserialize<ValueObjectMpNullableDto>(withIdBytes, mpOptions);
    if (withIdBack is null || withIdBack.Id?.Value != 42 || !string.Equals(withIdBack.Label, "alpha", StringComparison.Ordinal))
        valueTypeFailures.Add("messagepack nullable VO round-trip mismatch");

    var noId = new ValueObjectMpNullableDto { Id = null, Label = "beta" };
    var noIdBytes = global::MessagePack.MessagePackSerializer.Serialize(noId, mpOptions);
    var noIdJson = global::MessagePack.MessagePackSerializer.ConvertToJson(noIdBytes);
    if (!string.Equals(noIdJson, "[null,\"beta\"]", StringComparison.Ordinal))
        valueTypeFailures.Add($"messagepack null VO wire={noIdJson}");
    var noIdBack = global::MessagePack.MessagePackSerializer.Deserialize<ValueObjectMpNullableDto>(noIdBytes, mpOptions);
    if (noIdBack is null || noIdBack.Id is not null || !string.Equals(noIdBack.Label, "beta", StringComparison.Ordinal))
        valueTypeFailures.Add("messagepack null VO round-trip mismatch");
}
catch (Exception ex)
{
    valueTypeFailures.Add($"messagepack nullable VO threw {ex.GetType().Name}: {ex.Message}");
}

// SystemTextJsonSerializer<T> with T = int?: value round-trip, and the empty-buffer
// short-circuit returning default, which for Nullable<int> is null.
try
{
    ISerializer<int?> nullableInt = new SystemTextJsonSerializer<int?>(NullableIntContext.Default.NullableInt32);
    var intBuf = new ArrayBufferWriter<byte>();
    nullableInt.Serialize(intBuf, 5);
    var intJson = Utf8(intBuf.WrittenSpan);
    if (!string.Equals(intJson, "5", StringComparison.Ordinal))
        valueTypeFailures.Add($"stj int? wire={intJson}");
    var intBack = nullableInt.Deserialize(intBuf.WrittenSpan);
    if (intBack != 5)
        valueTypeFailures.Add($"stj int? round-trip={intBack}");
    var intEmpty = nullableInt.Deserialize(ReadOnlySpan<byte>.Empty);
    if (intEmpty is not null)
        valueTypeFailures.Add($"stj int? empty buffer={intEmpty}");
}
catch (Exception ex)
{
    valueTypeFailures.Add($"stj int? threw {ex.GetType().Name}: {ex.Message}");
}

// Reference-type and nested [ValueObject] shapes, #177: a class, a record class, a struct
// nested in a static class and a record class nested two levels deep, through every backend,
// with values and with null for the reference types.
var shapeFailures = new System.Collections.Generic.List<string>();

var fullShapes = new ValueObjectShapesDto(
    new ValueObjectClassId(1), new ValueObjectRecordName("n"), new ValueObjectContainer.NestedId(2), new ValueObjectContainer.Inner.DeepName("d"));
var emptyShapes = new ValueObjectShapesDto(null, null, new ValueObjectContainer.NestedId(3), null);

static bool SameShapes(ValueObjectShapesDto? a, ValueObjectShapesDto b) =>
    a is not null
    && a.Id?.Value == b.Id?.Value
    && string.Equals(a.Name?.Value, b.Name?.Value, StringComparison.Ordinal)
    && a.Nested.Value == b.Nested.Value
    && string.Equals(a.Deep?.Value, b.Deep?.Value, StringComparison.Ordinal);

try
{
    foreach (var (shapes, expected) in new[]
             {
                 (fullShapes, "{\"Id\":1,\"Name\":\"n\",\"Nested\":2,\"Deep\":\"d\"}"),
                 (emptyShapes, "{\"Id\":null,\"Name\":null,\"Nested\":3,\"Deep\":null}"),
             })
    {
        var json = JsonSerializer.Serialize(shapes, customContext.ValueObjectShapesDto);
        if (!string.Equals(json, expected, StringComparison.Ordinal))
            shapeFailures.Add($"stj shapes wire={json}");
        if (!SameShapes(JsonSerializer.Deserialize(json, customContext.ValueObjectShapesDto), shapes))
            shapeFailures.Add($"stj shapes round-trip mismatch for {json}");
    }
}
catch (Exception ex)
{
    shapeFailures.Add($"stj shapes threw {ex.GetType().Name}: {ex.Message}");
}

try
{
    foreach (var (shapes, expected) in new[]
             {
                 (fullShapes, "[1,\"n\",2,\"d\"]"),
                 (emptyShapes, "[null,null,3,null]"),
             })
    {
        var dtoIn = new ValueObjectShapesMpDto { Id = shapes.Id, Name = shapes.Name, Nested = shapes.Nested, Deep = shapes.Deep };
        var bytes = global::MessagePack.MessagePackSerializer.Serialize(dtoIn, mpOptions);
        var json = global::MessagePack.MessagePackSerializer.ConvertToJson(bytes);
        if (!string.Equals(json, expected, StringComparison.Ordinal))
            shapeFailures.Add($"messagepack shapes wire={json}");
        var back = global::MessagePack.MessagePackSerializer.Deserialize<ValueObjectShapesMpDto>(bytes, mpOptions);
        if (back is null || !SameShapes(new ValueObjectShapesDto(back.Id, back.Name, back.Nested, back.Deep), shapes))
            shapeFailures.Add($"messagepack shapes round-trip mismatch for {json}");
    }
}
catch (Exception ex)
{
    shapeFailures.Add($"messagepack shapes threw {ex.GetType().Name}: {ex.Message}");
}

try
{
    foreach (var shapes in new[] { fullShapes, emptyShapes })
    {
        var dtoIn = new ValueObjectShapesMemoryPackDto { Id = shapes.Id, Name = shapes.Name, Nested = shapes.Nested, Deep = shapes.Deep };
        var bytes = global::MemoryPack.MemoryPackSerializer.Serialize(dtoIn);
        var back = global::MemoryPack.MemoryPackSerializer.Deserialize<ValueObjectShapesMemoryPackDto>(bytes);
        if (back is null || !SameShapes(new ValueObjectShapesDto(back.Id, back.Name, back.Nested, back.Deep), shapes))
            shapeFailures.Add($"memorypack shapes round-trip mismatch, wire={Convert.ToHexString(bytes)}");
    }

    // Standalone reference-type value objects: null has its own encoding.
    var nullBytes = global::MemoryPack.MemoryPackSerializer.Serialize<ValueObjectClassId?>(null);
    if (nullBytes.Length != 1 || nullBytes[0] != global::MemoryPack.MemoryPackCode.NullObject)
        shapeFailures.Add($"memorypack null class VO wire={Convert.ToHexString(nullBytes)}");
    if (global::MemoryPack.MemoryPackSerializer.Deserialize<ValueObjectClassId?>(nullBytes) is not null)
        shapeFailures.Add("memorypack null class VO did not read back as null");
}
catch (Exception ex)
{
    shapeFailures.Add($"memorypack shapes threw {ex.GetType().Name}: {ex.Message}");
}

// Closed generic types declared with [assembly: ZeroAllocSerializable(typeof(...), format)], #183:
// Envelope<Order>, Pair<int, Order> and Envelope<Pair<int, Order>> under System.Text.Json and
// MemoryPack, through the generated serializer and through the generated SerializerDispatcher,
// which must pick the closed type by its runtime type with no reflection. MessagePack is not
// here: for a generic [MessagePackObject] type, MessagePack's own source generator emits a
// resolver that builds the formatter with MakeGenericType, which NativeAOT rejects with IL3050
// whatever this library generates. See #184.
var closedGenericFailures = new System.Collections.Generic.List<string>();

static void CheckClosedGeneric<T>(
    string label,
    ISerializer<T> serializer,
    ISerializerDispatcher dispatcher,
    T value,
    string? expectedWire,
    Func<ReadOnlyMemory<byte>, string> render,
    System.Collections.Generic.List<string> failures)
    where T : notnull
{
    try
    {
        var buf = new ArrayBufferWriter<byte>();
        serializer.Serialize(buf, value);
        var wire = render(buf.WrittenMemory);
        if (expectedWire is not null && !string.Equals(wire, expectedWire, StringComparison.Ordinal))
            failures.Add($"{label} wire={wire}");
        var back = serializer.Deserialize(buf.WrittenSpan);
        if (!System.Collections.Generic.EqualityComparer<T>.Default.Equals(back, value))
            failures.Add($"{label} round-trip={back}");

        var dispatched = dispatcher.Serialize(value, typeof(T));
        if (!dispatched.Span.SequenceEqual(buf.WrittenSpan))
            failures.Add($"{label} dispatcher bytes differ: {render(dispatched)}");
        if (dispatcher.Deserialize(dispatched, typeof(T)) is not T dispatchedBack
            || !System.Collections.Generic.EqualityComparer<T>.Default.Equals(dispatchedBack, value))
            failures.Add($"{label} dispatcher round-trip mismatch");
    }
    catch (Exception ex)
    {
        failures.Add($"{label} threw {ex.GetType().Name}: {ex.Message}");
    }
}

static string JsonWire(ReadOnlyMemory<byte> bytes) => System.Text.Encoding.UTF8.GetString(bytes.Span);
static string HexWire(ReadOnlyMemory<byte> bytes) => Convert.ToHexString(bytes.Span);

CheckClosedGeneric("stj Envelope<Order>", new StjEnvelopeOfStjOrderSerializer(), dispatcher,
    new StjEnvelope<StjOrder>("e-1", new StjOrder(7, "ada")),
    "{\"MessageId\":\"e-1\",\"Body\":{\"Id\":7,\"Customer\":\"ada\"}}", JsonWire, closedGenericFailures);
CheckClosedGeneric("stj Pair<int, Order>", new StjPairOfInt32AndStjOrderSerializer(), dispatcher,
    new StjPair<int, StjOrder>(3, new StjOrder(9, "bob")),
    "{\"Left\":3,\"Right\":{\"Id\":9,\"Customer\":\"bob\"}}", JsonWire, closedGenericFailures);
CheckClosedGeneric("stj Envelope<Pair<int, Order>>", new StjEnvelopeOfStjPairOfInt32AndStjOrderSerializer(), dispatcher,
    new StjEnvelope<StjPair<int, StjOrder>>("e-2", new StjPair<int, StjOrder>(4, new StjOrder(11, "cy"))),
    "{\"MessageId\":\"e-2\",\"Body\":{\"Left\":4,\"Right\":{\"Id\":11,\"Customer\":\"cy\"}}}", JsonWire, closedGenericFailures);

CheckClosedGeneric("memorypack Envelope<Order>", new MpEnvelopeOfMpOrderSerializer(), dispatcher,
    new MpEnvelope<MpOrder>("e-1", new MpOrder(7, "ada")), null, HexWire, closedGenericFailures);
CheckClosedGeneric("memorypack Pair<int, Order>", new MpPairOfInt32AndMpOrderSerializer(), dispatcher,
    new MpPair<int, MpOrder>(3, new MpOrder(9, "bob")), null, HexWire, closedGenericFailures);
CheckClosedGeneric("memorypack Envelope<Pair<int, Order>>", new MpEnvelopeOfMpPairOfInt32AndMpOrderSerializer(), dispatcher,
    new MpEnvelope<MpPair<int, MpOrder>>("e-2", new MpPair<int, MpOrder>(4, new MpOrder(11, "cy"))), null, HexWire, closedGenericFailures);

var closedGenericsOk = closedGenericFailures.Count == 0;
if (closedGenericsOk)
{
    Console.WriteLine("AOT smoke: closed generics OK (Envelope<Order>, Pair<int, Order>, Envelope<Pair<int, Order>> x STJ, MemoryPack + dispatcher)");
}

var shapesOk = shapeFailures.Count == 0;
if (shapesOk)
{
    Console.WriteLine("AOT smoke: value-object shapes OK (class, record class, nested, nested in nested x STJ, MessagePack, MemoryPack)");
}

var valueTypesOk = valueTypeFailures.Count == 0;
if (valueTypesOk)
{
    Console.WriteLine("AOT smoke: value types OK (struct serializers x3 + dispatcher, nullable VO STJ + MessagePack, SystemTextJsonSerializer<int?>)");
}

var ok = v0Ok && v1Ok && v2Ok && underlyingOk && valueTypesOk && shapesOk && closedGenericsOk;
if (!ok)
{
    Console.WriteLine($"AOT smoke: FAIL (v0={v0Ok}, v1.resolver={resolverWired}, v1.wire={bareIntegerWire}, v1.roundTrip={roundTrip}, v2.bareInt={mpBareInteger}, v2.roundTrip={mpRoundTrip}, underlying={underlyingOk}, valueTypes={valueTypesOk}, shapes={shapesOk}, closedGenerics={closedGenericsOk})");
    Console.WriteLine($"  dtoJson={dtoJson}");
    Console.WriteLine($"  mpJson={mpJson}");
    foreach (var failure in underlyingFailures)
    {
        Console.WriteLine($"  underlying: {failure}");
    }
    foreach (var failure in valueTypeFailures)
    {
        Console.WriteLine($"  valueTypes: {failure}");
    }
    foreach (var failure in shapeFailures)
    {
        Console.WriteLine($"  shapes: {failure}");
    }
    foreach (var failure in closedGenericFailures)
    {
        Console.WriteLine($"  closedGenerics: {failure}");
    }
    return 1;
}

Console.WriteLine("AOT smoke: PASS");
return 0;
