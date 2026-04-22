using System.Buffers;
using System.Text.Json.Serialization;
using System.Threading.Tasks;
using MemoryPack;
using MessagePack;
using Xunit;
using ZeroAlloc.Serialisation.MemoryPack;
using ZeroAlloc.Serialisation.MessagePack;
using ZeroAlloc.Serialisation.SystemTextJson;

namespace ZeroAlloc.Serialisation.Tests;

// Anchor types for edge-case coverage. Each is scoped to a single format so round-trip
// behaviour can be asserted independently. Basic Deserialize(empty-span) and minimal
// round-trip assertions are already covered in the per-format *SerializerTests.cs files;
// tests here exercise cases those files do not: richer shapes, nulls, unicode, buffer
// growth and concurrency.

[ZeroAllocSerializable(SerializationFormat.SystemTextJson)]
public sealed class StjOrder
{
    public string Id { get; set; } = "";
    public decimal Total { get; set; }
    public DateTimeOffset PlacedAt { get; set; }
    public string[]? Tags { get; set; }
}

[JsonSerializable(typeof(StjOrder))]
internal partial class StjOrderContext : JsonSerializerContext { }

[MemoryPackable]
[ZeroAllocSerializable(SerializationFormat.MemoryPack)]
public partial class MpOrder
{
    public string Id { get; set; } = "";
    public decimal Total { get; set; }
    public string[]? Tags { get; set; }
}

[MessagePackObject]
[ZeroAllocSerializable(SerializationFormat.MessagePack)]
public class MsgpOrder
{
    [Key(0)] public string Id { get; set; } = "";
    [Key(1)] public decimal Total { get; set; }
    [Key(2)] public string[]? Tags { get; set; }
}

public class SerializerEdgeCaseTests
{
    // ── Round-trip with richer shape (decimal, DateTimeOffset, list) ──────────

    [Fact]
    public void Stj_RichShape_RoundTrip()
    {
        var serializer = new SystemTextJsonSerializer<StjOrder>(StjOrderContext.Default.StjOrder);
        var original = new StjOrder
        {
            Id = "ord-1",
            Total = 12345.6789m,
            PlacedAt = new DateTimeOffset(2026, 4, 22, 12, 0, 0, TimeSpan.FromHours(2)),
            Tags = new[] { "a", "b" },
        };
        var buffer = new ArrayBufferWriter<byte>();

        serializer.Serialize(buffer, original);
        var result = serializer.Deserialize(buffer.WrittenSpan);

        Assert.NotNull(result);
        Assert.Equal(original.Id, result.Id);
        Assert.Equal(original.Total, result.Total);
        Assert.Equal(original.PlacedAt, result.PlacedAt);
        Assert.Equal(original.Tags, result.Tags);
    }

    [Fact]
    public void MemoryPack_RichShape_RoundTrip()
    {
        var serializer = new MemoryPackSerializer<MpOrder>();
        var original = new MpOrder
        {
            Id = "ord-2",
            Total = 999.99m,
            Tags = new[] { "x", "y", "z" },
        };
        var buffer = new ArrayBufferWriter<byte>();

        serializer.Serialize(buffer, original);
        var result = serializer.Deserialize(buffer.WrittenSpan);

        Assert.NotNull(result);
        Assert.Equal(original.Id, result.Id);
        Assert.Equal(original.Total, result.Total);
        Assert.Equal(original.Tags, result.Tags);
    }

    [Fact]
    public void MessagePack_RichShape_RoundTrip()
    {
        var serializer = new MessagePackSerializer<MsgpOrder>();
        var original = new MsgpOrder
        {
            Id = "ord-3",
            Total = 42.5m,
            Tags = new[] { "one", "two" },
        };
        var buffer = new ArrayBufferWriter<byte>();

        serializer.Serialize(buffer, original);
        var result = serializer.Deserialize(buffer.WrittenSpan);

        Assert.NotNull(result);
        Assert.Equal(original.Id, result.Id);
        Assert.Equal(original.Total, result.Total);
        Assert.Equal(original.Tags, result.Tags);
    }

    // ── Null value serialize/deserialize per format ───────────────────────────

    [Fact]
    public void Stj_NullValue_RoundTripsAsNull()
    {
        var serializer = new SystemTextJsonSerializer<StjOrder>(StjOrderContext.Default.StjOrder);
        var buffer = new ArrayBufferWriter<byte>();

        serializer.Serialize(buffer, null!);
        var result = serializer.Deserialize(buffer.WrittenSpan);

        Assert.Null(result);
    }

    [Fact]
    public void MemoryPack_NullValue_RoundTripsAsNull()
    {
        var serializer = new MemoryPackSerializer<MpOrder>();
        var buffer = new ArrayBufferWriter<byte>();

        serializer.Serialize(buffer, null!);
        var result = serializer.Deserialize(buffer.WrittenSpan);

        Assert.Null(result);
    }

    [Fact]
    public void MessagePack_NullValue_RoundTripsAsNull()
    {
        var serializer = new MessagePackSerializer<MsgpOrder>();
        var buffer = new ArrayBufferWriter<byte>();

        serializer.Serialize(buffer, null!);
        var result = serializer.Deserialize(buffer.WrittenSpan);

        Assert.Null(result);
    }

    // ── Unicode + control chars (STJ-focused) ─────────────────────────────────

    [Fact]
    public void Stj_UnicodeAndControlChars_RoundTrip()
    {
        var serializer = new SystemTextJsonSerializer<StjOrder>(StjOrderContext.Default.StjOrder);
        const string TrickyId = "rocket hello\tworld\n\"quoted\" /slash/ éè 中文";
        var original = new StjOrder
        {
            Id = TrickyId,
            Total = 1m,
            PlacedAt = DateTimeOffset.UnixEpoch,
        };
        var buffer = new ArrayBufferWriter<byte>();

        serializer.Serialize(buffer, original);
        var result = serializer.Deserialize(buffer.WrittenSpan);

        Assert.NotNull(result);
        Assert.Equal(TrickyId, result.Id);
    }

    // ── Very large payload (buffer growth) ────────────────────────────────────

    [Fact]
    public void Stj_LargePayload_RoundTrip()
    {
        var serializer = new SystemTextJsonSerializer<StjOrder>(StjOrderContext.Default.StjOrder);
        var original = new StjOrder { Id = new string('x', 10_000), Total = 1m, PlacedAt = DateTimeOffset.UnixEpoch };
        var buffer = new ArrayBufferWriter<byte>();

        serializer.Serialize(buffer, original);
        var result = serializer.Deserialize(buffer.WrittenSpan);

        Assert.NotNull(result);
        Assert.Equal(10_000, result.Id.Length);
    }

    [Fact]
    public void MemoryPack_LargePayload_RoundTrip()
    {
        var serializer = new MemoryPackSerializer<MpOrder>();
        var tags = new string[5_000];
        for (var i = 0; i < tags.Length; i++) tags[i] = $"tag-{i}";
        var original = new MpOrder { Id = "big", Total = 0m, Tags = tags };
        var buffer = new ArrayBufferWriter<byte>();

        serializer.Serialize(buffer, original);
        var result = serializer.Deserialize(buffer.WrittenSpan);

        Assert.NotNull(result);
        Assert.NotNull(result.Tags);
        Assert.Equal(5_000, result.Tags!.Length);
        Assert.Equal("tag-4999", result.Tags![4_999]);
    }

    [Fact]
    public void MessagePack_LargePayload_RoundTrip()
    {
        var serializer = new MessagePackSerializer<MsgpOrder>();
        var tags = new string[5_000];
        for (var i = 0; i < tags.Length; i++) tags[i] = $"tag-{i}";
        var original = new MsgpOrder { Id = "big", Total = 0m, Tags = tags };
        var buffer = new ArrayBufferWriter<byte>();

        serializer.Serialize(buffer, original);
        var result = serializer.Deserialize(buffer.WrittenSpan);

        Assert.NotNull(result);
        Assert.NotNull(result.Tags);
        Assert.Equal(5_000, result.Tags!.Length);
        Assert.Equal("tag-4999", result.Tags![4_999]);
    }

    // ── Thread safety: 16 tasks x 100 iterations per format ───────────────────

    [Fact]
    public async Task Stj_ConcurrentRoundTrips_Succeed()
    {
        var serializer = new SystemTextJsonSerializer<StjOrder>(StjOrderContext.Default.StjOrder);
        await RunConcurrentRoundTrips(i =>
        {
            var original = new StjOrder { Id = $"ord-{i}", Total = i, PlacedAt = DateTimeOffset.UnixEpoch };
            var buffer = new ArrayBufferWriter<byte>();
            serializer.Serialize(buffer, original);
            var result = serializer.Deserialize(buffer.WrittenSpan);
            Assert.NotNull(result);
            Assert.Equal($"ord-{i}", result!.Id);
        });
    }

    [Fact]
    public async Task MemoryPack_ConcurrentRoundTrips_Succeed()
    {
        var serializer = new MemoryPackSerializer<MpOrder>();
        await RunConcurrentRoundTrips(i =>
        {
            var original = new MpOrder { Id = $"ord-{i}", Total = i };
            var buffer = new ArrayBufferWriter<byte>();
            serializer.Serialize(buffer, original);
            var result = serializer.Deserialize(buffer.WrittenSpan);
            Assert.NotNull(result);
            Assert.Equal($"ord-{i}", result!.Id);
        });
    }

    [Fact]
    public async Task MessagePack_ConcurrentRoundTrips_Succeed()
    {
        var serializer = new MessagePackSerializer<MsgpOrder>();
        await RunConcurrentRoundTrips(i =>
        {
            var original = new MsgpOrder { Id = $"ord-{i}", Total = i };
            var buffer = new ArrayBufferWriter<byte>();
            serializer.Serialize(buffer, original);
            var result = serializer.Deserialize(buffer.WrittenSpan);
            Assert.NotNull(result);
            Assert.Equal($"ord-{i}", result!.Id);
        });
    }

    private static async Task RunConcurrentRoundTrips(Action<int> action)
    {
        const int TaskCount = 16;
        const int PerTask = 100;
        var tasks = new Task[TaskCount];
        for (var t = 0; t < TaskCount; t++)
        {
            var taskIndex = t;
            tasks[t] = Task.Run(() =>
            {
                for (var i = 0; i < PerTask; i++)
                {
                    action((taskIndex * PerTask) + i);
                }
            });
        }
        await Task.WhenAll(tasks).ConfigureAwait(false);
    }
}
