using System.Buffers;
using System.Text.Json;
using System.Text.Json.Serialization;
using Xunit;
using ZeroAlloc.Serialisation.SystemTextJson;

namespace ZeroAlloc.Serialisation.Tests;

// Fidelity checks for the SystemTextJson path:
//  • Deeply-nested objects round-trip without losing any depth level.
//  • The Utf8JsonWriter opened inside Serialize fully flushes into the
//    consumer's IBufferWriter<byte> by the time Serialize returns — no
//    pending bytes are stranded in the writer's internal buffers.
//
// These complement SerializerEdgeCaseTests which covers large payloads
// and unicode but not recursive structures or writer-flush semantics.

[ZeroAllocSerializable(SerializationFormat.SystemTextJson)]
public sealed class StjTreeNode
{
    public string Label { get; set; } = "";
    public StjTreeNode? Child { get; set; }
}

[JsonSerializable(typeof(StjTreeNode))]
internal sealed partial class StjTreeNodeContext : JsonSerializerContext { }

public sealed class SystemTextJsonFidelityTests
{
    [Fact]
    public void DeepNestedChain_20Levels_RoundTrips()
    {
        // Build a linear chain 20 deep — well under JsonSerializerOptions.MaxDepth (64)
        // but deep enough that any premature truncation in the writer/reader surfaces.
        const int Depth = 20;
        var root = new StjTreeNode { Label = "L0" };
        var tail = root;
        for (var i = 1; i < Depth; i++)
        {
            tail.Child = new StjTreeNode { Label = $"L{i}" };
            tail = tail.Child;
        }

        var serializer = new SystemTextJsonSerializer<StjTreeNode>(StjTreeNodeContext.Default.StjTreeNode);
        var buffer = new ArrayBufferWriter<byte>();
        serializer.Serialize(buffer, root);
        var result = serializer.Deserialize(buffer.WrittenSpan);

        var node = result;
        for (var i = 0; i < Depth; i++)
        {
            Assert.NotNull(node);
            Assert.Equal($"L{i}", node!.Label);
            node = node.Child;
        }
        Assert.Null(node);
    }

    [Fact]
    public void Serialize_FlushesFullPayload_IntoBufferWriter()
    {
        // The Serialize implementation opens a Utf8JsonWriter against the caller's
        // IBufferWriter<byte>, writes, and disposes. After Serialize returns the
        // writer's `WrittenSpan` must contain the complete JSON — no bytes stranded.
        //
        // Compare byte-for-byte against a direct JsonSerializer.SerializeToUtf8Bytes
        // call using the same JsonTypeInfo. Any divergence indicates a flush gap.
        var value = new StjTreeNode
        {
            Label = "root",
            Child = new StjTreeNode { Label = "leaf" },
        };

        var buffer = new ArrayBufferWriter<byte>();
        new SystemTextJsonSerializer<StjTreeNode>(StjTreeNodeContext.Default.StjTreeNode)
            .Serialize(buffer, value);
        var ours = buffer.WrittenSpan.ToArray();

        var direct = JsonSerializer.SerializeToUtf8Bytes(value, StjTreeNodeContext.Default.StjTreeNode);

        Assert.Equal(direct, ours);
    }
}
