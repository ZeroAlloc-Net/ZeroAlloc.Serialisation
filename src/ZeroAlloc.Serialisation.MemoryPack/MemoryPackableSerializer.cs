using System.Buffers;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using MemoryPack;
using ZeroAlloc.Serialisation;

namespace ZeroAlloc.Serialisation.MemoryPack;

/// <summary>
/// An <see cref="ISerializer{T}"/> for MemoryPack's binary format, for a <c>[MemoryPackable]</c> type. It is safe
/// under trimming and Native AOT.
/// </summary>
/// <remarks>
/// The constructor registers <typeparamref name="T"/>'s formatter through the type's generated static
/// <c>RegisterFormatter</c>, so MemoryPack never looks it up by reflection.
/// </remarks>
/// <typeparam name="T">A <c>[MemoryPackable]</c> type.</typeparam>
public sealed class MemoryPackableSerializer<T> : ISerializer<T>
    where T : IMemoryPackable<T>
{
    /// <summary>Creates a serializer and registers the formatter for <typeparamref name="T"/>.</summary>
    public MemoryPackableSerializer()
    {
        T.RegisterFormatter();
    }

    /// <inheritdoc />
    public void Serialize(IBufferWriter<byte> writer, T value)
        => global::MemoryPack.MemoryPackSerializer.Serialize(writer, value);

    /// <inheritdoc />
    public T? Deserialize(ReadOnlySpan<byte> buffer)
    {
        if (buffer.IsEmpty) return default;
        return Read(buffer);
    }

    // MemoryPackSerializer.Deserialize<T> is annotated DynamicallyAccessedMembers(All) on T, which would force that
    // requirement onto every caller of this class. This is the same read without the annotation: the unmanaged
    // fast path, then a MemoryPackReader over pooled optional state. The formatter comes from the registry, where
    // the constructor put it.
    private static T? Read(ReadOnlySpan<byte> buffer)
    {
        if (!RuntimeHelpers.IsReferenceOrContainsReferences<T>())
        {
            if (buffer.Length < Unsafe.SizeOf<T>())
                MemoryPackSerializationException.ThrowInvalidRange(Unsafe.SizeOf<T>(), buffer.Length);
            return Unsafe.ReadUnaligned<T>(in MemoryMarshal.GetReference(buffer));
        }

        using var state = MemoryPackReaderOptionalStatePool.Rent(options: null);
        var reader = new MemoryPackReader(buffer, state);
        try
        {
            T? value = default;
            reader.ReadValue(ref value);
            return value;
        }
        finally
        {
            reader.Dispose();
        }
    }
}
