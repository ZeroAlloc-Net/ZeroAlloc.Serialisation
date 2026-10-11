using Microsoft.Extensions.DependencyInjection;
using System.Text.Json;
using Xunit;
using ZeroAlloc.Serialisation;

namespace ZeroAlloc.Serialisation.Tests;

public sealed class SystemTextJsonFallbackDispatcherTests
{
    // ── helpers ──────────────────────────────────────────────────────────────

    private sealed record Point(int X, int Y);

    /// <summary>
    /// Inner dispatcher that only knows about <see cref="Point"/>.
    /// Any other type throws <see cref="NotSupportedException"/>.
    /// </summary>
    private sealed class StubDispatcher : ISerializerDispatcher
    {
        // Sentinel payload: a minimal hand-crafted JSON that the fallback would never produce
        // (it uses a deliberate structure the real STJ fallback won't generate for Point/Other).
        public static readonly byte[] PointSentinel = """{"sentinel":true}"""u8.ToArray();

        public ReadOnlyMemory<byte> Serialize(object value, Type type)
        {
            if (type != typeof(Point)) throw new NotSupportedException($"Unknown: {type}");
            return PointSentinel;
        }

        public object? Deserialize(ReadOnlyMemory<byte> data, Type type)
        {
            if (type != typeof(Point)) throw new NotSupportedException($"Unknown: {type}");
            // Return a known sentinel Point so the caller can distinguish inner vs fallback
            return new Point(99, 99);
        }
    }

    private sealed record Other(string Label);

    // ── constructor ───────────────────────────────────────────────────────────

    [Fact]
    public void Constructor_ThrowsOnNullInner()
    {
        Assert.Throws<ArgumentNullException>(() =>
            new SystemTextJsonFallbackDispatcher(null!));
    }

    // ── Serialize ─────────────────────────────────────────────────────────────

    [Fact]
    public void Serialize_UsesInnerDispatcher_WhenTypeIsKnown()
    {
        var sut = new SystemTextJsonFallbackDispatcher(new StubDispatcher());

        var bytes = sut.Serialize(new Point(1, 2), typeof(Point));

        // Inner was used: bytes must be the sentinel, not real STJ output
        Assert.Equal(StubDispatcher.PointSentinel, bytes.ToArray());
    }

    [Fact]
    public void Serialize_FallsBackToSystemTextJson_WhenTypeIsUnknown()
    {
        var sut = new SystemTextJsonFallbackDispatcher(new StubDispatcher());
        var o = new Other("hello");

        var bytes = sut.Serialize(o, typeof(Other));

        var json = System.Text.Encoding.UTF8.GetString(bytes.Span);
        Assert.Contains("\"Label\"", json);
        Assert.Contains("hello", json);
    }

    [Fact]
    public void Serialize_PropagatesNonNotSupportedException()
    {
        var throwing = new ThrowingDispatcher(new InvalidOperationException("boom"));
        var sut = new SystemTextJsonFallbackDispatcher(throwing);

        Assert.Throws<InvalidOperationException>(() =>
            sut.Serialize(new Other("x"), typeof(Other)));
    }

    // ── Deserialize ───────────────────────────────────────────────────────────

    [Fact]
    public void Deserialize_UsesInnerDispatcher_WhenTypeIsKnown()
    {
        var sut = new SystemTextJsonFallbackDispatcher(new StubDispatcher());
        // Payload content doesn't matter — StubDispatcher ignores it and returns sentinel Point
        var dummyBytes = """{"X":0,"Y":0}"""u8.ToArray();

        var result = sut.Deserialize(dummyBytes, typeof(Point));

        // Inner was used: returns sentinel Point(99,99), not whatever STJ would produce
        var p = Assert.IsType<Point>(result);
        Assert.Equal(99, p.X);
        Assert.Equal(99, p.Y);
    }

    [Fact]
    public void Deserialize_FallsBackToSystemTextJson_WhenTypeIsUnknown()
    {
        var sut = new SystemTextJsonFallbackDispatcher(new StubDispatcher());
        var bytes = System.Text.Json.JsonSerializer.SerializeToUtf8Bytes(new Other("world"));

        var result = sut.Deserialize(bytes, typeof(Other));

        var o = Assert.IsType<Other>(result);
        Assert.Equal("world", o.Label);
    }

    [Fact]
    public void Deserialize_PropagatesNonNotSupportedException()
    {
        var throwing = new ThrowingDispatcher(new InvalidOperationException("boom"));
        var sut = new SystemTextJsonFallbackDispatcher(throwing);
        var bytes = System.Text.Json.JsonSerializer.SerializeToUtf8Bytes(new Other("x"));

        Assert.Throws<InvalidOperationException>(() =>
            sut.Deserialize(bytes, typeof(Other)));
    }

    // ── JsonSerializerOptions passthrough ─────────────────────────────────────

    [Fact]
    public void Serialize_UsesProvidedOptions()
    {
        var options = new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };
        var sut = new SystemTextJsonFallbackDispatcher(new StubDispatcher(), options);
        var o = new Other("camel");

        var bytes = sut.Serialize(o, typeof(Other));
        var json = System.Text.Encoding.UTF8.GetString(bytes.Span);

        // camelCase policy => "label" not "Label"
        Assert.Contains("\"label\"", json);
    }

    // ── IBufferWriter overloads ───────────────────────────────────────────────

    [Fact]
    public void SerializeToWriter_UsesInnerDispatcher_WhenTypeIsKnown()
    {
        var sut = new SystemTextJsonFallbackDispatcher(new StubDispatcher());
        var byType = new System.Buffers.ArrayBufferWriter<byte>();
        var generic = new System.Buffers.ArrayBufferWriter<byte>();

        sut.Serialize(byType, new Point(1, 2), typeof(Point));
        sut.Serialize(generic, new Point(1, 2));

        Assert.Equal(StubDispatcher.PointSentinel, byType.WrittenSpan.ToArray());
        Assert.Equal(StubDispatcher.PointSentinel, generic.WrittenSpan.ToArray());
    }

    [Fact]
    public void SerializeToWriter_FallsBackToTheSameBytesAsTheMemoryOverload_WhenTypeIsUnknown()
    {
        var options = new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase, WriteIndented = true };
        var sut = new SystemTextJsonFallbackDispatcher(new StubDispatcher(), options);
        var o = new Other("hello");
        var expected = sut.Serialize(o, typeof(Other)).ToArray();
        var byType = new System.Buffers.ArrayBufferWriter<byte>();
        var generic = new System.Buffers.ArrayBufferWriter<byte>();

        sut.Serialize(byType, o, typeof(Other));
        sut.Serialize(generic, o);

        Assert.Equal(expected, byType.WrittenSpan.ToArray());
        Assert.Equal(expected, generic.WrittenSpan.ToArray());
    }

    [Fact]
    public void SerializeToWriter_PropagatesNonNotSupportedException()
    {
        var sut = new SystemTextJsonFallbackDispatcher(new ThrowingDispatcher(new InvalidOperationException("boom")));
        var writer = new System.Buffers.ArrayBufferWriter<byte>();

        Assert.Throws<InvalidOperationException>(() => sut.Serialize(writer, new Other("x"), typeof(Other)));
        Assert.Throws<InvalidOperationException>(() => sut.Serialize(writer, new Other("x")));
    }

    [Fact]
    public void SerializeToWriter_NullWriter_Throws()
    {
        var sut = new SystemTextJsonFallbackDispatcher(new StubDispatcher());

        Assert.Throws<ArgumentNullException>(() => sut.Serialize(null!, new Other("x"), typeof(Other)));
        Assert.Throws<ArgumentNullException>(() => sut.Serialize(null!, new Other("x")));
    }

    [Fact]
    public void SerializeToWriter_OverGeneratedDispatcher_RegisteredStruct_AllocatesNothing()
    {
        var sut = new SystemTextJsonFallbackDispatcher(new SerializerDispatcher());
        var value = new DispatchStjStruct(7, "seven");
        var writer = new System.Buffers.ArrayBufferWriter<byte>(1024);

        for (var i = 0; i < 20; i++) { writer.ResetWrittenCount(); sut.Serialize(writer, value); }
        var before = GC.GetAllocatedBytesForCurrentThread();
        for (var i = 0; i < 200; i++) { writer.ResetWrittenCount(); sut.Serialize(writer, value); }
        var perCall = (GC.GetAllocatedBytesForCurrentThread() - before) / 200;

        Assert.Equal(0, perCall);
    }

    // ── Generic Deserialize<T> ────────────────────────────────────────────────

    [Fact]
    public void DeserializeGeneric_UsesInnerDispatcher_WhenTypeIsKnown()
    {
        var sut = new SystemTextJsonFallbackDispatcher(new StubDispatcher());

        var p = sut.Deserialize<Point>("""{"X":0,"Y":0}"""u8.ToArray());

        Assert.Equal(new Point(99, 99), p);
    }

    [Fact]
    public void DeserializeGeneric_FallsBackToSystemTextJson_LikeTheObjectOverload_WhenTypeIsUnknown()
    {
        var options = new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };
        var sut = new SystemTextJsonFallbackDispatcher(new StubDispatcher(), options);
        var bytes = JsonSerializer.SerializeToUtf8Bytes(new Other("world"), options);

        var generic = sut.Deserialize<Other>(bytes);

        Assert.Equal(new Other("world"), generic);
        Assert.Equal(sut.Deserialize(bytes, typeof(Other)), generic);
    }

    [Fact]
    public void DeserializeGeneric_PropagatesNonNotSupportedException()
    {
        var sut = new SystemTextJsonFallbackDispatcher(new ThrowingDispatcher(new InvalidOperationException("boom")));
        var bytes = JsonSerializer.SerializeToUtf8Bytes(new Other("x"));

        Assert.Throws<InvalidOperationException>(() => sut.Deserialize<Other>(bytes));
    }

    [Fact]
    public void DeserializeGeneric_OverGeneratedDispatcher_RegisteredStruct_AllocatesNothing()
    {
        var sut = new SystemTextJsonFallbackDispatcher(new SerializerDispatcher());
        var value = new ReadMpStruct(8, 80);
        var data = sut.Serialize(value, typeof(ReadMpStruct));
        ReadMpStruct back = default;

        for (var i = 0; i < 20; i++) back = sut.Deserialize<ReadMpStruct>(data);
        var before = GC.GetAllocatedBytesForCurrentThread();
        for (var i = 0; i < 200; i++) back = sut.Deserialize<ReadMpStruct>(data);
        var perCall = (GC.GetAllocatedBytesForCurrentThread() - before) / 200;

        Assert.Equal(0, perCall);
        Assert.Equal(value, back);
    }

    // ── helper: dispatcher that throws a configurable exception ──────────────

    private sealed class ThrowingDispatcher(Exception ex) : ISerializerDispatcher
    {
        public ReadOnlyMemory<byte> Serialize(object value, Type type) => throw ex;
        public object?              Deserialize(ReadOnlyMemory<byte> data, Type type) => throw ex;
    }
}

public sealed class WithSystemTextJsonFallbackTests
{
    private sealed record Ping(string Message);

    // Simulates the generated SerializerDispatcher: only knows nothing, always throws.
    private sealed class StubDispatcher : ISerializerDispatcher
    {
        public ReadOnlyMemory<byte> Serialize(object value, Type type)
            => throw new NotSupportedException();
        public object? Deserialize(ReadOnlyMemory<byte> data, Type type)
            => throw new NotSupportedException();
    }

    [Fact]
    public void WithSystemTextJsonFallback_ThrowsWhenNoDispatcherRegistered()
    {
        var services = new ServiceCollection();

        Assert.Throws<InvalidOperationException>(() =>
            services.WithSystemTextJsonFallback());
    }

    [Fact]
    public void WithSystemTextJsonFallback_WrapsRegisteredDispatcher()
    {
        var services = new ServiceCollection();
        services.AddSingleton<ISerializerDispatcher, StubDispatcher>();
        services.WithSystemTextJsonFallback();

        var provider   = services.BuildServiceProvider();
        var dispatcher = provider.GetRequiredService<ISerializerDispatcher>();

        Assert.IsType<SystemTextJsonFallbackDispatcher>(dispatcher);
    }

    [Fact]
    public void WithSystemTextJsonFallback_FallbackWorksEndToEnd()
    {
        var services = new ServiceCollection();
        services.AddSingleton<ISerializerDispatcher, StubDispatcher>();
        services.WithSystemTextJsonFallback();

        var provider   = services.BuildServiceProvider();
        var dispatcher = provider.GetRequiredService<ISerializerDispatcher>();

        var bytes  = dispatcher.Serialize(new Ping("hi"), typeof(Ping));
        var result = dispatcher.Deserialize(bytes, typeof(Ping));

        var ping = Assert.IsType<Ping>(result);
        Assert.Equal("hi", ping.Message);
    }

    [Fact]
    public void WithSystemTextJsonFallback_WrapsInstanceDescriptor()
    {
        var services = new ServiceCollection();
        services.AddSingleton<ISerializerDispatcher>(new StubDispatcher());
        services.WithSystemTextJsonFallback();

        var dispatcher = services.BuildServiceProvider()
                                 .GetRequiredService<ISerializerDispatcher>();

        Assert.IsType<SystemTextJsonFallbackDispatcher>(dispatcher);
    }

    [Fact]
    public void WithSystemTextJsonFallback_WrapsFactoryDescriptor()
    {
        var services = new ServiceCollection();
        services.AddSingleton<ISerializerDispatcher>(_ => new StubDispatcher());
        services.WithSystemTextJsonFallback();

        var dispatcher = services.BuildServiceProvider()
                                 .GetRequiredService<ISerializerDispatcher>();

        Assert.IsType<SystemTextJsonFallbackDispatcher>(dispatcher);
    }

    [Fact]
    public void WithSystemTextJsonFallback_PassesOptionsThrough()
    {
        var options  = new JsonSerializerOptions
            { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };
        var services = new ServiceCollection();
        services.AddSingleton<ISerializerDispatcher, StubDispatcher>();
        services.WithSystemTextJsonFallback(options);

        var provider   = services.BuildServiceProvider();
        var dispatcher = provider.GetRequiredService<ISerializerDispatcher>();

        var bytes = dispatcher.Serialize(new Ping("camel"), typeof(Ping));
        var json  = System.Text.Encoding.UTF8.GetString(bytes.Span);

        Assert.Contains("\"message\"", json); // camelCase applied
    }
}
