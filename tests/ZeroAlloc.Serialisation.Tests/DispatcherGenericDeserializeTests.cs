using System.Buffers;
using System.Runtime.InteropServices;
using System.Text.Json.Serialization;
using MemoryPack;
using MessagePack;
using Xunit;

namespace ZeroAlloc.Serialisation.Tests;

// Value-type messages with no reference members, one per format, for the generic Deserialize<T>.
// Reading one back allocates nothing in the format itself, so any allocation the dispatcher adds,
// such as a box, shows up in full.

[StructLayout(LayoutKind.Auto)]
[ZeroAllocSerializable(SerializationFormat.SystemTextJson)]
public readonly record struct ReadStjStruct(int A, long B);

[JsonSerializable(typeof(ReadStjStruct))]
internal partial class ReadStjStructContext : JsonSerializerContext { }

// System.Text.Json binds a positional record through its constructor, and for that allocates
// argument state per call. A struct with settable properties is read with no allocation at all.
[StructLayout(LayoutKind.Auto)]
[ZeroAllocSerializable(SerializationFormat.SystemTextJson)]
public record struct ReadStjPropertyStruct
{
    public int A { get; set; }

    public long B { get; set; }
}

[JsonSerializable(typeof(ReadStjPropertyStruct))]
internal partial class ReadStjPropertyStructContext : JsonSerializerContext { }

[StructLayout(LayoutKind.Auto)]
[MemoryPackable]
[ZeroAllocSerializable(SerializationFormat.MemoryPack)]
public readonly partial record struct ReadMpStruct(int A, long B);

[StructLayout(LayoutKind.Auto)]
[MessagePackObject]
[ZeroAllocSerializable(SerializationFormat.MessagePack)]
public readonly record struct ReadMsgpStruct([property: Key(0)] int A, [property: Key(1)] long B);

public class DispatcherGenericDeserializeTests
{
    private const int Iterations = 200;

    /// <summary>Bytes allocated on this thread per call of <paramref name="action"/>, after warm-up.</summary>
    private static long AllocatedPerCall(Action action)
    {
        for (var i = 0; i < 20; i++) action();
        var before = GC.GetAllocatedBytesForCurrentThread();
        for (var i = 0; i < Iterations; i++) action();
        return (GC.GetAllocatedBytesForCurrentThread() - before) / Iterations;
    }

    private static readonly ReadStjStruct s_stj = new(7, 70);
    private static readonly ReadMpStruct s_mp = new(8, 80);
    private static readonly ReadMsgpStruct s_msgp = new(9, 90);

    private static ReadOnlyMemory<byte> Bytes<T>(ISerializer<T> serializer, T value)
    {
        var buffer = new ArrayBufferWriter<byte>();
        serializer.Serialize(buffer, value);
        return buffer.WrittenMemory.ToArray();
    }

    private static readonly ReadOnlyMemory<byte> s_stjBytes = Bytes(new ReadStjStructSerializer(), s_stj);
    private static readonly ReadOnlyMemory<byte> s_mpBytes = Bytes(new ReadMpStructSerializer(), s_mp);
    private static readonly ReadOnlyMemory<byte> s_msgpBytes = Bytes(new ReadMsgpStructSerializer(), s_msgp);

    private static StjOrder SampleStjOrder() => new()
    {
        Id = "ord-1",
        Total = 12.5m,
        PlacedAt = new DateTimeOffset(2026, 10, 11, 12, 0, 0, TimeSpan.Zero),
    };

    // ── Allocation: no box ───────────────────────────────────────────────────

    [Fact]
    public void Generic_Struct_SystemTextJson_AllocatesOnlyWhatSystemTextJsonAllocates()
    {
        ISerializerDispatcher dispatcher = new SerializerDispatcher();
        var value = new ReadStjPropertyStruct { A = 7, B = 70 };
        var data = Bytes(new ReadStjPropertyStructSerializer(), value);
        var serializer = new ReadStjPropertyStructSerializer();
        ReadStjPropertyStruct result = default;

        var backend = AllocatedPerCall(() => result = serializer.Deserialize(data.Span));
        var bytes = AllocatedPerCall(() => result = dispatcher.Deserialize<ReadStjPropertyStruct>(data));

        // System.Text.Json's object converter builds a value type as a boxed object and unboxes it
        // at the end: one 32 B box for this 16-byte struct, inside JsonSerializer.Deserialize itself.
        // The dispatcher adds nothing to it.
        Assert.Equal(backend, bytes);
        Assert.Equal(32, bytes);
        Assert.Equal(value, result);
    }

    [Fact]
    public void Generic_PositionalRecordStruct_SystemTextJson_AllocatesOnlyWhatSystemTextJsonAllocates()
    {
        ISerializerDispatcher dispatcher = new SerializerDispatcher();
        var serializer = new ReadStjStructSerializer();
        ReadStjStruct result = default;

        // System.Text.Json's own constructor-argument state, not the dispatcher: the per-type
        // serializer costs the same for the same bytes.
        var backend = AllocatedPerCall(() => result = serializer.Deserialize(s_stjBytes.Span));
        var bytes = AllocatedPerCall(() => result = dispatcher.Deserialize<ReadStjStruct>(s_stjBytes));

        Assert.Equal(backend, bytes);
        Assert.Equal(168, bytes);
        Assert.Equal(s_stj, result);
    }

    [Fact]
    public void Generic_Struct_MemoryPack_AllocatesNothing()
    {
        ISerializerDispatcher dispatcher = new SerializerDispatcher();
        ReadMpStruct result = default;

        var bytes = AllocatedPerCall(() => result = dispatcher.Deserialize<ReadMpStruct>(s_mpBytes));

        Assert.Equal(0, bytes);
        Assert.Equal(s_mp, result);
    }

    [Fact]
    public void Generic_Struct_MessagePack_AllocatesOnlyWhatMessagePackAllocates()
    {
        ISerializerDispatcher dispatcher = new SerializerDispatcher();
        ReadMsgpStruct result = default;

        // The dispatcher reads the caller's memory directly, so it costs exactly what MessagePack
        // itself costs for the same memory: no box and no copy of the payload.
        var backend = AllocatedPerCall(() => result = global::MessagePack.MessagePackSerializer.Deserialize<ReadMsgpStruct>(s_msgpBytes));
        var bytes = AllocatedPerCall(() => result = dispatcher.Deserialize<ReadMsgpStruct>(s_msgpBytes));

        Assert.Equal(backend, bytes);
        Assert.Equal(0, bytes);
        Assert.Equal(s_msgp, result);
    }

    [Fact]
    public void Generic_Struct_SavesExactlyTheBoxTheObjectOverloadMakes_InEveryFormat()
    {
        ISerializerDispatcher dispatcher = new SerializerDispatcher();
        var stjValue = new ReadStjPropertyStruct { A = 7, B = 70 };
        var stjData = Bytes(new ReadStjPropertyStructSerializer(), stjValue);

        // A box of a 16-byte struct is 32 B on 64-bit: object header, method table pointer, data.
        const int Box = 32;

        Assert.Equal(
            AllocatedPerCall(() => _ = (ReadStjPropertyStruct)dispatcher.Deserialize(stjData, typeof(ReadStjPropertyStruct))!) - Box,
            AllocatedPerCall(() => _ = dispatcher.Deserialize<ReadStjPropertyStruct>(stjData)));
        Assert.Equal(
            AllocatedPerCall(() => _ = (ReadMpStruct)dispatcher.Deserialize(s_mpBytes, typeof(ReadMpStruct))!) - Box,
            AllocatedPerCall(() => _ = dispatcher.Deserialize<ReadMpStruct>(s_mpBytes)));
        Assert.Equal(
            AllocatedPerCall(() => _ = (ReadMsgpStruct)dispatcher.Deserialize(s_msgpBytes, typeof(ReadMsgpStruct))!) - Box,
            AllocatedPerCall(() => _ = dispatcher.Deserialize<ReadMsgpStruct>(s_msgpBytes)));
    }

    // ── Values ───────────────────────────────────────────────────────────────

    [Fact]
    public void Generic_ReturnsWhatTheObjectOverloadReturns_InEveryFormat()
    {
        ISerializerDispatcher dispatcher = new SerializerDispatcher();

        Assert.Equal(s_stj, dispatcher.Deserialize<ReadStjStruct>(s_stjBytes));
        Assert.Equal(s_mp, dispatcher.Deserialize<ReadMpStruct>(s_mpBytes));
        Assert.Equal(s_msgp, dispatcher.Deserialize<ReadMsgpStruct>(s_msgpBytes));

        Assert.Equal(dispatcher.Deserialize(s_stjBytes, typeof(ReadStjStruct)), dispatcher.Deserialize<ReadStjStruct>(s_stjBytes));
        Assert.Equal(dispatcher.Deserialize(s_mpBytes, typeof(ReadMpStruct)), dispatcher.Deserialize<ReadMpStruct>(s_mpBytes));
        Assert.Equal(dispatcher.Deserialize(s_msgpBytes, typeof(ReadMsgpStruct)), dispatcher.Deserialize<ReadMsgpStruct>(s_msgpBytes));
    }

    [Fact]
    public void Generic_Class_ReturnsTheDeserializedInstance()
    {
        ISerializerDispatcher dispatcher = new SerializerDispatcher();
        var order = SampleStjOrder();
        var data = dispatcher.Serialize(order, typeof(StjOrder));

        var back = dispatcher.Deserialize<StjOrder>(data);

        Assert.NotNull(back);
        Assert.Equal(order.Id, back!.Id);
        Assert.Equal(order.Total, back.Total);
        Assert.Equal(order.PlacedAt, back.PlacedAt);
    }

    [Fact]
    public void Generic_EmptyData_ReturnsDefault_LikeTheObjectOverload()
    {
        ISerializerDispatcher dispatcher = new SerializerDispatcher();

        Assert.Equal(dispatcher.Deserialize(ReadOnlyMemory<byte>.Empty, typeof(ReadMsgpStruct)), dispatcher.Deserialize<ReadMsgpStruct>(ReadOnlyMemory<byte>.Empty));
        Assert.Equal(default(ReadStjStruct), dispatcher.Deserialize<ReadStjStruct>(ReadOnlyMemory<byte>.Empty));
        Assert.Equal(default(ReadMpStruct), dispatcher.Deserialize<ReadMpStruct>(ReadOnlyMemory<byte>.Empty));
        Assert.Null(dispatcher.Deserialize(ReadOnlyMemory<byte>.Empty, typeof(StjOrder)));
        Assert.Null(dispatcher.Deserialize<StjOrder>(ReadOnlyMemory<byte>.Empty));
    }

    // ── Unsupported types: the same exception as the object overload ────────

    [Fact]
    public void Generic_UnregisteredType_ThrowsNotSupported_LikeTheObjectOverload()
    {
        ISerializerDispatcher dispatcher = new SerializerDispatcher();

        var byType = Assert.Throws<NotSupportedException>(() => dispatcher.Deserialize(s_stjBytes, typeof(int)));
        var generic = Assert.Throws<NotSupportedException>(() => dispatcher.Deserialize<int>(s_stjBytes));

        Assert.Equal(byType.Message, generic.Message);
        Assert.Contains(typeof(int).FullName!, generic.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Generic_NonRegisteredStaticType_ThrowsNotSupported_LikeTheObjectOverload()
    {
        ISerializerDispatcher dispatcher = new SerializerDispatcher();

        // No runtime type to dispatch on when reading: object and Nullable<X> are not registered types.
        Assert.Throws<NotSupportedException>(() => dispatcher.Deserialize(s_mpBytes, typeof(object)));
        Assert.Throws<NotSupportedException>(() => dispatcher.Deserialize<object>(s_mpBytes));
        Assert.Throws<NotSupportedException>(() => dispatcher.Deserialize(s_mpBytes, typeof(ReadMpStruct?)));
        Assert.Throws<NotSupportedException>(() => dispatcher.Deserialize<ReadMpStruct?>(s_mpBytes));
    }

    // ── Default interface implementation, for dispatchers written by hand ───

    /// <summary>A dispatcher that implements only the members ISerializerDispatcher shipped with.</summary>
    private sealed class LegacyDispatcher(object? result) : ISerializerDispatcher
    {
        public Type? LastType { get; private set; }

        public ReadOnlyMemory<byte> Serialize(object value, Type type) => throw new NotSupportedException();

        public object? Deserialize(ReadOnlyMemory<byte> data, Type type)
        {
            LastType = type;
            if (result is Exception ex) throw ex;
            return result;
        }
    }

    [Fact]
    public void DefaultImplementation_PassesTheTypeArgument_AndUnboxes()
    {
        var legacy = new LegacyDispatcher(s_mp);
        ISerializerDispatcher dispatcher = legacy;

        Assert.Equal(s_mp, dispatcher.Deserialize<ReadMpStruct>(s_mpBytes));
        Assert.Equal(typeof(ReadMpStruct), legacy.LastType);
    }

    [Fact]
    public void DefaultImplementation_NullResult_ForAClass_ReturnsNull()
    {
        ISerializerDispatcher dispatcher = new LegacyDispatcher(null);

        Assert.Null(dispatcher.Deserialize<StjOrder>(s_stjBytes));
    }

    [Fact]
    public void DefaultImplementation_NullResult_ForANullableStruct_ReturnsNull()
    {
        var legacy = new LegacyDispatcher(null);
        ISerializerDispatcher dispatcher = legacy;

        Assert.Null(dispatcher.Deserialize<ReadMpStruct?>(s_mpBytes));
        Assert.Equal(typeof(ReadMpStruct?), legacy.LastType);
    }

    [Fact]
    public void DefaultImplementation_NullResult_ForANonNullableStruct_Throws()
    {
        ISerializerDispatcher dispatcher = new LegacyDispatcher(null);

        // A zero struct is not what the bytes held: returning one would hand a caller a fabricated value.
        var ex = Assert.Throws<InvalidCastException>(() => dispatcher.Deserialize<ReadMpStruct>(s_mpBytes));
        Assert.Contains(typeof(ReadMpStruct).FullName!, ex.Message, StringComparison.Ordinal);
        Assert.Contains("returned null", ex.Message, StringComparison.Ordinal);
        Assert.Contains("non-nullable value type", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void DefaultImplementation_PropagatesNotSupported()
    {
        ISerializerDispatcher dispatcher = new LegacyDispatcher(new NotSupportedException("nope"));

        Assert.Throws<NotSupportedException>(() => dispatcher.Deserialize<ReadMpStruct>(s_mpBytes));
    }

    [Fact]
    public void DefaultImplementation_ResultOfAnotherType_ThrowsInvalidCast()
    {
        ISerializerDispatcher dispatcher = new LegacyDispatcher("not a struct");

        Assert.Throws<InvalidCastException>(() => dispatcher.Deserialize<ReadMpStruct>(s_mpBytes));
        Assert.Throws<InvalidCastException>(() => dispatcher.Deserialize<ReadMpStruct?>(s_mpBytes));
        Assert.Throws<InvalidCastException>(() => dispatcher.Deserialize<StjOrder>(s_stjBytes));
    }
}
