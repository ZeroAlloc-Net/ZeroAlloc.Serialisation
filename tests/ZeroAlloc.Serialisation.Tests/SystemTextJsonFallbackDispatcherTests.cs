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

    // ── helper: dispatcher that throws a configurable exception ──────────────

    private sealed class ThrowingDispatcher(Exception ex) : ISerializerDispatcher
    {
        public ReadOnlyMemory<byte> Serialize(object value, Type type) => throw ex;
        public object?              Deserialize(ReadOnlyMemory<byte> data, Type type) => throw ex;
    }
}
