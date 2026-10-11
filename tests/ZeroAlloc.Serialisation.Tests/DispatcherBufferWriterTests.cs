using System.Buffers;
using System.Text.Json.Serialization;
using MemoryPack;
using MessagePack;
using Xunit;

namespace ZeroAlloc.Serialisation.Tests;

// Value-type messages for the IBufferWriter dispatcher overloads, one per format. A struct is the
// case the object-typed Serialize cannot serve without boxing.

[ZeroAllocSerializable(SerializationFormat.SystemTextJson)]
public readonly record struct DispatchStjStruct(int A, string B);

[JsonSerializable(typeof(DispatchStjStruct))]
internal partial class DispatchStjStructContext : JsonSerializerContext { }

[MemoryPackable]
[ZeroAllocSerializable(SerializationFormat.MemoryPack)]
public readonly partial record struct DispatchMpStruct(int A, string B);

[MessagePackObject]
[ZeroAllocSerializable(SerializationFormat.MessagePack)]
public readonly record struct DispatchMsgpStruct([property: Key(0)] int A, [property: Key(1)] string B);

public class DispatcherBufferWriterTests
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

    private static readonly DispatchStjStruct s_stj = new(7, "seven");
    private static readonly DispatchMpStruct s_mp = new(8, "eight");
    private static readonly DispatchMsgpStruct s_msgp = new(9, "nine");

    private static StjOrder SampleStjOrder() => new()
    {
        Id = "ord-1",
        Total = 12.5m,
        PlacedAt = new DateTimeOffset(2026, 10, 11, 12, 0, 0, TimeSpan.Zero),
    };

    private static byte[] SerializerBytes<T>(ISerializer<T> serializer, T value)
    {
        var buffer = new ArrayBufferWriter<byte>();
        serializer.Serialize(buffer, value);
        return buffer.WrittenSpan.ToArray();
    }

    // ── Generic overload: no box, no intermediate buffer ─────────────────────

    [Fact]
    public void Generic_Struct_SystemTextJson_IntoReusedWriter_AllocatesNothing()
    {
        ISerializerDispatcher dispatcher = new SerializerDispatcher();
        var buffer = new ArrayBufferWriter<byte>(1024);

        var bytes = AllocatedPerCall(() =>
        {
            buffer.ResetWrittenCount();
            dispatcher.Serialize(buffer, s_stj);
        });

        Assert.Equal(0, bytes);
    }

    [Fact]
    public void Generic_Struct_MemoryPack_IntoReusedWriter_AllocatesNothing()
    {
        ISerializerDispatcher dispatcher = new SerializerDispatcher();
        var buffer = new ArrayBufferWriter<byte>(1024);

        var bytes = AllocatedPerCall(() =>
        {
            buffer.ResetWrittenCount();
            dispatcher.Serialize(buffer, s_mp);
        });

        Assert.Equal(0, bytes);
    }

    [Fact]
    public void Generic_Struct_MessagePack_IntoReusedWriter_AllocatesNothing()
    {
        ISerializerDispatcher dispatcher = new SerializerDispatcher();
        var buffer = new ArrayBufferWriter<byte>(1024);

        var bytes = AllocatedPerCall(() =>
        {
            buffer.ResetWrittenCount();
            dispatcher.Serialize(buffer, s_msgp);
        });

        Assert.Equal(0, bytes);
    }

    [Fact]
    public void Generic_Class_IntoReusedWriter_AllocatesNothing()
    {
        ISerializerDispatcher dispatcher = new SerializerDispatcher();
        var buffer = new ArrayBufferWriter<byte>(1024);
        var order = SampleStjOrder();

        var bytes = AllocatedPerCall(() =>
        {
            buffer.ResetWrittenCount();
            dispatcher.Serialize(buffer, order);
        });

        Assert.Equal(0, bytes);
    }

    [Fact]
    public void Generic_WritesExactlyWhatTheTypesSerializerWrites()
    {
        ISerializerDispatcher dispatcher = new SerializerDispatcher();

        var stj = new ArrayBufferWriter<byte>();
        dispatcher.Serialize(stj, s_stj);
        Assert.Equal(SerializerBytes(new DispatchStjStructSerializer(), s_stj), stj.WrittenSpan.ToArray());

        var mp = new ArrayBufferWriter<byte>();
        dispatcher.Serialize(mp, s_mp);
        Assert.Equal(SerializerBytes(new DispatchMpStructSerializer(), s_mp), mp.WrittenSpan.ToArray());

        var msgp = new ArrayBufferWriter<byte>();
        dispatcher.Serialize(msgp, s_msgp);
        Assert.Equal(SerializerBytes(new DispatchMsgpStructSerializer(), s_msgp), msgp.WrittenSpan.ToArray());
    }

    [Fact]
    public void Generic_AppendsToWhatTheWriterAlreadyHolds()
    {
        ISerializerDispatcher dispatcher = new SerializerDispatcher();
        var buffer = new ArrayBufferWriter<byte>();
        buffer.Write("prefix"u8);

        dispatcher.Serialize(buffer, s_stj);

        Assert.Equal(
            "prefix"u8.ToArray().Concat(SerializerBytes(new DispatchStjStructSerializer(), s_stj)).ToArray(),
            buffer.WrittenSpan.ToArray());
    }

    [Fact]
    public void Generic_WithNonRegisteredStaticType_DispatchesOnTheRuntimeType()
    {
        ISerializerDispatcher dispatcher = new SerializerDispatcher();
        object boxed = s_stj;
        var buffer = new ArrayBufferWriter<byte>();

        dispatcher.Serialize(buffer, boxed);

        Assert.Equal(SerializerBytes(new DispatchStjStructSerializer(), s_stj), buffer.WrittenSpan.ToArray());
    }

    [Fact]
    public void Generic_UnregisteredType_ThrowsNotSupported_AndWritesNothing()
    {
        ISerializerDispatcher dispatcher = new SerializerDispatcher();
        var buffer = new ArrayBufferWriter<byte>();

        var ex = Assert.Throws<NotSupportedException>(() => dispatcher.Serialize(buffer, 42));

        Assert.Contains(typeof(int).FullName!, ex.Message, StringComparison.Ordinal);
        Assert.Equal(0, buffer.WrittenCount);
    }

    [Fact]
    public void Generic_NullWriter_Throws()
    {
        ISerializerDispatcher dispatcher = new SerializerDispatcher();

        Assert.Throws<ArgumentNullException>(() => dispatcher.Serialize(null!, s_stj));
    }

    // ── Non-generic overload: the caller's writer, no intermediate buffer ────

    [Fact]
    public void ByType_IntoReusedWriter_AllocatesNothing()
    {
        ISerializerDispatcher dispatcher = new SerializerDispatcher();
        var buffer = new ArrayBufferWriter<byte>(1024);
        object order = SampleStjOrder();

        var bytes = AllocatedPerCall(() =>
        {
            buffer.ResetWrittenCount();
            dispatcher.Serialize(buffer, order, typeof(StjOrder));
        });

        Assert.Equal(0, bytes);
    }

    [Fact]
    public void ByType_WritesTheSameBytesAsTheMemoryReturningOverload()
    {
        ISerializerDispatcher dispatcher = new SerializerDispatcher();
        var buffer = new ArrayBufferWriter<byte>();

        dispatcher.Serialize(buffer, s_mp, typeof(DispatchMpStruct));

        Assert.Equal(dispatcher.Serialize(s_mp, typeof(DispatchMpStruct)).ToArray(), buffer.WrittenSpan.ToArray());
    }

    [Fact]
    public void ByType_UnregisteredType_ThrowsNotSupported()
    {
        ISerializerDispatcher dispatcher = new SerializerDispatcher();

        var ex = Assert.Throws<NotSupportedException>(() =>
            dispatcher.Serialize(new ArrayBufferWriter<byte>(), "text", typeof(string)));

        Assert.Contains(typeof(string).FullName!, ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void ByType_NullWriter_Throws()
    {
        ISerializerDispatcher dispatcher = new SerializerDispatcher();

        Assert.Throws<ArgumentNullException>(() => dispatcher.Serialize(null!, SampleStjOrder(), typeof(StjOrder)));
    }

    // ── Default interface implementation, for dispatchers written by hand ───

    /// <summary>A dispatcher that implements only the members ISerializerDispatcher shipped with.</summary>
    private sealed class LegacyDispatcher : ISerializerDispatcher
    {
        public Type? LastType { get; private set; }

        public ReadOnlyMemory<byte> Serialize(object value, Type type)
        {
            LastType = type;
            return new byte[] { 1, 2, 3 };
        }

        public object? Deserialize(ReadOnlyMemory<byte> data, Type type) => throw new NotSupportedException();
    }

    [Fact]
    public void DefaultImplementation_ByType_CopiesTheReturnedBytesIntoTheWriter()
    {
        var legacy = new LegacyDispatcher();
        ISerializerDispatcher dispatcher = legacy;
        var buffer = new ArrayBufferWriter<byte>();
        buffer.Write(new byte[] { 0 });

        dispatcher.Serialize(buffer, SampleStjOrder(), typeof(StjOrder));

        Assert.Equal(new byte[] { 0, 1, 2, 3 }, buffer.WrittenSpan.ToArray());
        Assert.Equal(typeof(StjOrder), legacy.LastType);
    }

    [Fact]
    public void DefaultImplementation_Generic_PassesTheStaticTypeArgument()
    {
        var legacy = new LegacyDispatcher();
        ISerializerDispatcher dispatcher = legacy;
        var buffer = new ArrayBufferWriter<byte>();

        dispatcher.Serialize(buffer, s_stj);

        Assert.Equal(new byte[] { 1, 2, 3 }, buffer.WrittenSpan.ToArray());
        Assert.Equal(typeof(DispatchStjStruct), legacy.LastType);
    }

    [Fact]
    public void DefaultImplementation_NullWriter_Throws()
    {
        ISerializerDispatcher dispatcher = new LegacyDispatcher();

        Assert.Throws<ArgumentNullException>(() => dispatcher.Serialize(null!, s_stj));
        Assert.Throws<ArgumentNullException>(() => dispatcher.Serialize(null!, s_stj, typeof(DispatchStjStruct)));
    }
}
