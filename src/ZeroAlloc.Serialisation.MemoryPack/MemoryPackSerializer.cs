using System.Buffers;
using System.Diagnostics.CodeAnalysis;
using MemoryPack;
using ZeroAlloc.Serialisation;

namespace ZeroAlloc.Serialisation.MemoryPack;

public class MemoryPackSerializer<T> : ISerializer<T>
{
    [RequiresDynamicCode("MemoryPack serialization of arbitrary types may require dynamic code.")]
    [RequiresUnreferencedCode("MemoryPack serialization of arbitrary types may require unreferenced code.")]
    public virtual void Serialize(IBufferWriter<byte> writer, T value)
        => global::MemoryPack.MemoryPackSerializer.Serialize(writer, value);

    [RequiresDynamicCode("MemoryPack serialization of arbitrary types may require dynamic code.")]
    [RequiresUnreferencedCode("MemoryPack serialization of arbitrary types may require unreferenced code.")]
    public virtual T? Deserialize(ReadOnlySpan<byte> buffer)
    {
        if (buffer.IsEmpty) return default;
        return global::MemoryPack.MemoryPackSerializer.Deserialize<T>(buffer);
    }
}
