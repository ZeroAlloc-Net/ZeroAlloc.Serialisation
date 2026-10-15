using System.Buffers;
using System.Globalization;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using MessagePack;
using Xunit;
using ZeroAlloc.TestHelpers;
using ZeroAlloc.Serialisation.MessagePack;
using ZeroAlloc.Serialisation.SystemTextJson;

namespace ZeroAlloc.Serialisation.Tests;

// Models for the reentrancy and exception tests. A converter on Child serializes the child through
// the same serializer, on the same thread, while the outer call is still writing.

public sealed class ReentrantNode
{
    public string Name { get; set; } = "";

    [JsonConverter(typeof(ReentrantNodeChildConverter))]
    public ReentrantNode? Child { get; set; }
}

[ZeroAllocSerializable(SerializationFormat.SystemTextJson)]
public sealed class GenReentrantNode
{
    public string Name { get; set; } = "";

    [JsonConverter(typeof(GenReentrantNodeChildConverter))]
    public GenReentrantNode? Child { get; set; }
}

public sealed class Bomb
{
    public bool Explode { get; set; }
}

public sealed class BombHolder
{
    public int Before { get; set; }

    [JsonConverter(typeof(BombConverter))]
    public Bomb Bomb { get; set; } = new();
}

[ZeroAllocSerializable(SerializationFormat.SystemTextJson)]
public sealed class GenBombHolder
{
    public int Before { get; set; }

    [JsonConverter(typeof(BombConverter))]
    public Bomb Bomb { get; set; } = new();
}

[JsonSerializable(typeof(ReentrantNode))]
[JsonSerializable(typeof(GenReentrantNode))]
[JsonSerializable(typeof(BombHolder))]
[JsonSerializable(typeof(GenBombHolder))]
internal partial class HotPathJsonContext : JsonSerializerContext { }

public sealed class ReentrantNodeChildConverter : JsonConverter<ReentrantNode>
{
    internal static readonly SystemTextJsonSerializer<ReentrantNode> Serializer =
        new(HotPathJsonContext.Default.ReentrantNode);

    public override ReentrantNode? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        => JsonSerializer.Deserialize(ref reader, HotPathJsonContext.Default.ReentrantNode);

    public override void Write(Utf8JsonWriter writer, ReentrantNode value, JsonSerializerOptions options)
    {
        var inner = new ArrayBufferWriter<byte>();
        Serializer.Serialize(inner, value);
        writer.WriteRawValue(inner.WrittenSpan);
    }
}

public sealed class GenReentrantNodeChildConverter : JsonConverter<GenReentrantNode>
{
    public override GenReentrantNode? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        => JsonSerializer.Deserialize(ref reader, HotPathJsonContext.Default.GenReentrantNode);

    public override void Write(Utf8JsonWriter writer, GenReentrantNode value, JsonSerializerOptions options)
    {
        var inner = new ArrayBufferWriter<byte>();
        new GenReentrantNodeSerializer().Serialize(inner, value);
        writer.WriteRawValue(inner.WrittenSpan);
    }
}

public sealed class BombConverter : JsonConverter<Bomb>
{
    public override Bomb Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        reader.Skip();
        return new Bomb();
    }

    public override void Write(Utf8JsonWriter writer, Bomb value, JsonSerializerOptions options)
    {
        // Leave the writer mid-array, at depth 2, with bytes pending, then fail.
        writer.WriteStartArray();
        writer.WriteNumberValue(1);
        if (value.Explode) throw new InvalidOperationException("boom");
        writer.WriteEndArray();
    }
}

public class HotPathAllocationTests
{
    private const int Iterations = 200;

    private static StjOrder SampleStjOrder() => new()
    {
        Id = "ord-1",
        Total = 12.5m,
        PlacedAt = new DateTimeOffset(2026, 10, 11, 12, 0, 0, TimeSpan.Zero),
    };

    // ── System.Text.Json: no Utf8JsonWriter per Serialize ────────────────────

    [Fact]
    public void Stj_Adapter_Serialize_AllocatesNothing()
    {
        var serializer = new SystemTextJsonSerializer<StjOrder>(StjOrderContext.Default.StjOrder);
        var buffer = new ArrayBufferWriter<byte>(1024);
        var order = SampleStjOrder();

        AllocationGate.AssertBudget(0, Iterations, () =>
        {
            buffer.ResetWrittenCount();
            serializer.Serialize(buffer, order);
        }, "Stj_Adapter_Serialize_AllocatesNothing");
    }

    [Fact]
    public void Stj_Generated_Serialize_AllocatesNothing()
    {
        var serializer = new StjOrderSerializer();
        var buffer = new ArrayBufferWriter<byte>(1024);
        var order = SampleStjOrder();

        AllocationGate.AssertBudget(0, Iterations, () =>
        {
            buffer.ResetWrittenCount();
            serializer.Serialize(buffer, order);
        }, "Stj_Generated_Serialize_AllocatesNothing");
    }

    [Fact]
    public void Stj_Adapter_ReentrantSerializeOnSameThread_WritesBothLevels()
    {
        var root = new ReentrantNode
        {
            Name = "a",
            Child = new ReentrantNode { Name = "b", Child = new ReentrantNode { Name = "c" } },
        };

        // Twice: the second run starts from a cached writer, the first possibly not.
        for (var run = 0; run < 2; run++)
        {
            var buffer = new ArrayBufferWriter<byte>();
            ReentrantNodeChildConverter.Serializer.Serialize(buffer, root);
            Assert.Equal(
                """{"Name":"a","Child":{"Name":"b","Child":{"Name":"c","Child":null}}}""",
                Encoding.UTF8.GetString(buffer.WrittenSpan));
        }
    }

    [Fact]
    public void Stj_Generated_ReentrantSerializeOnSameThread_WritesBothLevels()
    {
        var root = new GenReentrantNode
        {
            Name = "a",
            Child = new GenReentrantNode { Name = "b", Child = new GenReentrantNode { Name = "c" } },
        };

        for (var run = 0; run < 2; run++)
        {
            var buffer = new ArrayBufferWriter<byte>();
            new GenReentrantNodeSerializer().Serialize(buffer, root);
            Assert.Equal(
                """{"Name":"a","Child":{"Name":"b","Child":{"Name":"c","Child":null}}}""",
                Encoding.UTF8.GetString(buffer.WrittenSpan));
        }
    }

    [Fact]
    public void Stj_Adapter_ExceptionMidWrite_NextSerializeOnSameThreadIsClean()
    {
        var serializer = new SystemTextJsonSerializer<BombHolder>(HotPathJsonContext.Default.BombHolder);

        Assert.Throws<InvalidOperationException>(() =>
            serializer.Serialize(new ArrayBufferWriter<byte>(), new BombHolder { Before = 7, Bomb = new Bomb { Explode = true } }));

        var buffer = new ArrayBufferWriter<byte>();
        serializer.Serialize(buffer, new BombHolder { Before = 8 });

        Assert.Equal("""{"Before":8,"Bomb":[1]}""", Encoding.UTF8.GetString(buffer.WrittenSpan));
    }

    [Fact]
    public void Stj_Generated_ExceptionMidWrite_NextSerializeOnSameThreadIsClean()
    {
        var serializer = new GenBombHolderSerializer();

        Assert.Throws<InvalidOperationException>(() =>
            serializer.Serialize(new ArrayBufferWriter<byte>(), new GenBombHolder { Before = 7, Bomb = new Bomb { Explode = true } }));

        var buffer = new ArrayBufferWriter<byte>();
        serializer.Serialize(buffer, new GenBombHolder { Before = 8 });

        Assert.Equal("""{"Before":8,"Bomb":[1]}""", Encoding.UTF8.GetString(buffer.WrittenSpan));
    }

    [Fact]
    public void Stj_Adapter_DoesNotKeepCallersBufferWriterAlive()
    {
        var serializer = new SystemTextJsonSerializer<StjOrder>(StjOrderContext.Default.StjOrder);
        var weak = SerializeIntoDroppedWriter(serializer);

        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();

        Assert.False(weak.IsAlive);
    }

    [Fact]
    public void Stj_Generated_DoesNotKeepCallersBufferWriterAlive()
    {
        var weak = SerializeIntoDroppedWriter(new StjOrderSerializer());

        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();

        Assert.False(weak.IsAlive);
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static WeakReference SerializeIntoDroppedWriter(ISerializer<StjOrder> serializer)
    {
        var buffer = new ArrayBufferWriter<byte>();
        serializer.Serialize(buffer, SampleStjOrder());
        return new WeakReference(buffer);
    }

    // ── MessagePack: no payload copy per Deserialize ─────────────────────────

    private static readonly MsgpOrder s_msgpOrder = new() { Id = "ord-1", Total = 12.5m, Tags = ["a", "b"] };

    private static long RawMessagePackDeserializeBytes(ReadOnlyMemory<byte> payload, MessagePackSerializerOptions? options)
        => AllocationGate.MeasureBytesPerCall(Iterations, () => global::MessagePack.MessagePackSerializer.Deserialize<MsgpOrder>(payload, options));

    [Fact]
    public void MessagePack_Adapter_Deserialize_AllocatesOnlyWhatMessagePackItselfDoes()
    {
        var serializer = new MessagePackSerializer<MsgpOrder>(MessagePackSerializerOptions.Standard);
        var payload = global::MessagePack.MessagePackSerializer.Serialize(s_msgpOrder, MessagePackSerializerOptions.Standard);

        var raw = RawMessagePackDeserializeBytes(payload, MessagePackSerializerOptions.Standard);
        var wrapped = AllocationGate.MeasureBytesPerCall(Iterations, () => serializer.Deserialize(payload));

        Assert.Equal(raw, wrapped);
    }

    [Fact]
    public void MessagePack_Generated_Deserialize_AllocatesOnlyWhatMessagePackItselfDoes()
    {
        var serializer = new MsgpOrderSerializer();
        var payload = global::MessagePack.MessagePackSerializer.Serialize(s_msgpOrder);

        var raw = RawMessagePackDeserializeBytes(payload, options: null);
        var wrapped = AllocationGate.MeasureBytesPerCall(Iterations, () => serializer.Deserialize(payload));

        Assert.Equal(raw, wrapped);
    }

    [Fact]
    public void MessagePack_Adapter_Deserialize_FromLargePayload_RoundTrips()
    {
        var serializer = new MessagePackSerializer<MsgpOrder>(MessagePackSerializerOptions.Standard);
        var big = new MsgpOrder { Id = new string('x', 100_000), Tags = Enumerable.Range(0, 1000).Select(i => i.ToString(CultureInfo.InvariantCulture)).ToArray() };
        var buffer = new ArrayBufferWriter<byte>();
        serializer.Serialize(buffer, big);

        var result = serializer.Deserialize(buffer.WrittenSpan);

        Assert.NotNull(result);
        Assert.Equal(big.Id, result.Id);
        Assert.Equal(big.Tags, result.Tags);
    }

    // ── Generated dispatcher: no serializer instance per call ────────────────

    [Fact]
    public void Dispatcher_Deserialize_MessagePack_AllocatesOnlyWhatMessagePackItselfDoes()
    {
        var dispatcher = new SerializerDispatcher();
        ReadOnlyMemory<byte> payload = global::MessagePack.MessagePackSerializer.Serialize(s_msgpOrder);

        var raw = RawMessagePackDeserializeBytes(payload, options: null);
        var dispatched = AllocationGate.MeasureBytesPerCall(Iterations, () => dispatcher.Deserialize(payload, typeof(MsgpOrder)));

        Assert.Equal(raw, dispatched);
    }

    [Fact]
    public void Dispatcher_Deserialize_MessagePack_RoundTrips()
    {
        var dispatcher = new SerializerDispatcher();
        var payload = dispatcher.Serialize(s_msgpOrder, typeof(MsgpOrder));

        var result = Assert.IsType<MsgpOrder>(dispatcher.Deserialize(payload, typeof(MsgpOrder)));

        Assert.Equal(s_msgpOrder.Id, result.Id);
        Assert.Equal(s_msgpOrder.Total, result.Total);
        Assert.Equal(s_msgpOrder.Tags, result.Tags);
        Assert.Null(dispatcher.Deserialize(ReadOnlyMemory<byte>.Empty, typeof(MsgpOrder)));
    }

    [Fact]
    public void Dispatcher_Deserialize_SystemTextJson_AllocatesOnlyWhatJsonSerializerItselfDoes()
    {
        var dispatcher = new SerializerDispatcher();
        ReadOnlyMemory<byte> payload = JsonSerializer.SerializeToUtf8Bytes(SampleStjOrder(), StjOrderContext.Default.StjOrder);

        var raw = AllocationGate.MeasureBytesPerCall(Iterations, () => JsonSerializer.Deserialize(payload.Span, StjOrderContext.Default.StjOrder));
        var dispatched = AllocationGate.MeasureBytesPerCall(Iterations, () => dispatcher.Deserialize(payload, typeof(StjOrder)));

        Assert.Equal(raw, dispatched);
    }

    [Fact]
    public void Dispatcher_Serialize_SystemTextJson_AllocatesOnlyTheReturnedBuffer()
    {
        var dispatcher = new SerializerDispatcher();
        var order = SampleStjOrder();

        // The dispatcher returns memory the caller owns, so the one ArrayBufferWriter and its
        // array are the contract. Everything else, serializer and Utf8JsonWriter, must be reused.
        var expected = AllocationGate.MeasureBytesPerCall(Iterations, () =>
        {
            var w = new ArrayBufferWriter<byte>();
            w.GetSpan(1);
            w.Advance(0);
        });
        var dispatched = AllocationGate.MeasureBytesPerCall(Iterations, () => dispatcher.Serialize(order, typeof(StjOrder)));

        Assert.Equal(expected, dispatched);
    }
}

// ── TimeSpan value objects under System.Text.Json ───────────────────────────

public class ValueObjectTimeSpanSystemTextJsonTests
{
    private static readonly TimeSpan[] s_values =
    [
        TimeSpan.Zero,
        TimeSpan.MaxValue,
        TimeSpan.MinValue,
        TimeSpan.FromTicks(1),
        TimeSpan.FromTicks(-15_000_000),
        new TimeSpan(10, 2, 3, 4, 567),
    ];

    [Fact]
    public void RoundTrips_WithSystemTextJsonsOwnTimeSpanWireFormat()
    {
        foreach (var value in s_values)
        {
            var json = JsonSerializer.Serialize(new ShiftLength(value));

            Assert.Equal(JsonSerializer.Serialize(value), json);
            Assert.Equal(value, JsonSerializer.Deserialize<ShiftLength>(json).Value);
        }
    }

    [Fact]
    public void Read_IgnoresCurrentCulture()
    {
        var previous = CultureInfo.CurrentCulture;
        try
        {
            // fr-FR's TimeSpan.Parse accepts a comma before the fraction; en-US rejects it. The wire
            // format must not depend on the process culture, so both reject it.
            foreach (var culture in new[] { "en-US", "fr-FR" })
            {
                CultureInfo.CurrentCulture = new CultureInfo(culture);
                Assert.Throws<JsonException>(() => JsonSerializer.Deserialize<ShiftLength>("\"00:00:01,5\""));
                Assert.Equal(TimeSpan.FromSeconds(1.5), JsonSerializer.Deserialize<ShiftLength>("\"00:00:01.5\"").Value);
            }
        }
        finally
        {
            CultureInfo.CurrentCulture = previous;
        }
    }

    [Fact]
    public void Read_UnescapesTheString()
    {
        Assert.Equal(TimeSpan.FromSeconds(1), JsonSerializer.Deserialize<ShiftLength>("\"\\u0030\\u0030:00:01\"").Value);
    }

    [Fact]
    public void Read_RejectsNonString()
    {
        Assert.Throws<JsonException>(() => JsonSerializer.Deserialize<ShiftLength>("12"));
    }

    [Fact]
    public void Read_RejectsOverlongString()
    {
        Assert.Throws<JsonException>(() => JsonSerializer.Deserialize<ShiftLength>("\"" + new string('1', 500) + "\""));
    }

    [Fact]
    public void Converter_WriteAndRead_AllocateNothing()
    {
        var converter = new ShiftLengthSystemTextJsonConverter();
        var options = JsonSerializerOptions.Default;
        var value = new ShiftLength(new TimeSpan(10, 2, 3, 4, 567));
        var buffer = new ArrayBufferWriter<byte>(256);
        using var writer = new Utf8JsonWriter(buffer);
        var payload = Encoding.UTF8.GetBytes("\"10.02:03:04.5670000\"");

        AllocationGate.AssertBudget(0, 200, () =>
        {
            buffer.ResetWrittenCount();
            writer.Reset(buffer);
            converter.Write(writer, value, options);
            writer.Flush();
        }, "ShiftLength converter Write");
        AllocationGate.AssertBudget(0, 200, () =>
        {
            var reader = new Utf8JsonReader(payload);
            reader.Read();
            _ = converter.Read(ref reader, typeof(ShiftLength), options);
        }, "ShiftLength converter Read");

        Assert.Equal("\"10.02:03:04.5670000\"", Encoding.UTF8.GetString(buffer.WrittenSpan));
    }
}
