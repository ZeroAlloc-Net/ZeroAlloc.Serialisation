using System.Buffers;
using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using MemoryPack;
using ZeroAlloc.Serialisation;

namespace ZeroAlloc.Serialisation.MemoryPack;

/// <summary>An <see cref="ISerializer{T}"/> for MemoryPack's binary format.</summary>
/// <remarks>
/// MemoryPack finds the formatter for a type through reflection unless the type's formatter is already
/// registered, so the public constructor carries the trim and AOT annotations. For trimming and Native AOT use
/// <see cref="MemoryPackableSerializer{T}"/>, which registers the formatter itself.
/// </remarks>
public class MemoryPackSerializer<T> : ISerializer<T>
{
    private const string ReflectionMessage =
        "MemoryPack finds the formatter for a type through reflection unless the formatter is registered, "
        + "which needs members the trimmer may remove and code Native AOT cannot generate. "
        + "For Native AOT, use MemoryPackableSerializer<T> with a [MemoryPackable] type, "
        + "which registers the type's formatter without reflection.";

    /// <summary>Creates a serializer that lets MemoryPack find formatters through reflection.</summary>
    [RequiresUnreferencedCode(ReflectionMessage)]
    [RequiresDynamicCode(ReflectionMessage)]
    public MemoryPackSerializer()
    {
    }

    /// <summary>
    /// For subclasses that have registered the formatter for <typeparamref name="T"/> themselves, so no
    /// reflection is needed. It is not annotated: the subclass takes responsibility for the registration.
    /// </summary>
    /// <param name="formatterRegistered">
    /// Only distinguishes this constructor from the reflection-based one. Pass <see langword="true"/>.
    /// </param>
    protected MemoryPackSerializer(bool formatterRegistered)
    {
        _ = formatterRegistered;
    }

    public virtual void Serialize(IBufferWriter<byte> writer, T value)
        => global::MemoryPack.MemoryPackSerializer.Serialize(writer, value);

    public virtual T? Deserialize(ReadOnlySpan<byte> buffer)
    {
        if (buffer.IsEmpty) return default;
        return Read(buffer);
    }

    // MemoryPackSerializer.Deserialize<T> is annotated DynamicallyAccessedMembers(All) on T, which would force that
    // requirement onto every caller of this class. This is the same read without the annotation: the unmanaged
    // fast path, then a MemoryPackReader over pooled optional state. The formatter comes from the registry,
    // and only the annotated public constructor can leave a type to be found by reflection.
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
