using System.Buffers;

namespace ZeroAlloc.Serialisation;

/// <summary>
/// Runtime-type dispatch surface for source-generated serializers.
/// Implement via the generated <c>SerializerDispatcher</c> class (emitted per assembly
/// by the ZeroAlloc.Serialisation source generator).
/// </summary>
/// <remarks>
/// <para>
/// <see cref="Serialize(object, Type)"/> allocates its own buffer and returns
/// <see cref="ReadOnlyMemory{T}">ReadOnlyMemory&lt;byte&gt;</see>, so callers (e.g.,
/// <c>ZeroAllocEventSerializer</c>) can hold the result across async boundaries and hand it to
/// storage adapters without pinning a span. That buffer is allocated on every call.
/// </para>
/// <para>
/// To serialize without that buffer, write into an <see cref="IBufferWriter{T}">IBufferWriter&lt;byte&gt;</see>
/// you own, for example a pooled or reused one: <see cref="Serialize{T}(IBufferWriter{byte}, T)"/>, which
/// does not box a struct, or <see cref="Serialize(IBufferWriter{byte}, object, Type)"/> when the type is
/// only known at run time. The generated <c>SerializerDispatcher</c> implements both by writing straight
/// into your writer. A dispatcher written by hand that implements only the two original members inherits
/// default implementations that call <see cref="Serialize(object, Type)"/> and copy its bytes into the writer.
/// </para>
/// </remarks>
public interface ISerializerDispatcher
{
    /// <summary>Serializes <paramref name="value"/> using the serializer registered for <paramref name="type"/>.</summary>
    /// <param name="value">The object to serialize. Must be an instance of <paramref name="type"/>.</param>
    /// <param name="type">The exact runtime type to dispatch to. Must have been annotated with
    /// <c>[ZeroAllocSerializable]</c>, or for a closed generic type declared with
    /// <c>[assembly: ZeroAllocSerializable(typeof(...), format)]</c>, in the assembly where this
    /// dispatcher was generated.</param>
    /// <returns>A <see cref="ReadOnlyMemory{T}">ReadOnlyMemory&lt;byte&gt;</see> containing the serialized bytes.</returns>
    /// <exception cref="NotSupportedException">Thrown when no serializer is registered for <paramref name="type"/>.</exception>
    ReadOnlyMemory<byte> Serialize(object value, Type type);

    /// <summary>
    /// Serializes <paramref name="value"/> into <paramref name="writer"/>, using the serializer
    /// registered for <paramref name="type"/>.
    /// </summary>
    /// <param name="writer">The writer the serialized bytes are appended to. Bytes it already holds are kept.</param>
    /// <param name="value">The object to serialize. Must be an instance of <paramref name="type"/>.</param>
    /// <param name="type">The exact runtime type to dispatch to, as for <see cref="Serialize(object, Type)"/>.</param>
    /// <remarks>
    /// The generated <c>SerializerDispatcher</c> writes directly into <paramref name="writer"/>; nothing is
    /// allocated per call beyond what the format's own serializer allocates. The default implementation, used
    /// by a dispatcher that does not override it, calls <see cref="Serialize(object, Type)"/> and copies the
    /// returned bytes into <paramref name="writer"/>.
    /// </remarks>
    /// <exception cref="ArgumentNullException"><paramref name="writer"/> is <see langword="null"/>.</exception>
    /// <exception cref="NotSupportedException">Thrown when no serializer is registered for <paramref name="type"/>.</exception>
    void Serialize(IBufferWriter<byte> writer, object value, Type type)
    {
        if (writer is null) throw new ArgumentNullException(nameof(writer));

        var bytes = Serialize(value, type);
        bytes.Span.CopyTo(writer.GetSpan(bytes.Length));
        writer.Advance(bytes.Length);
    }

    /// <summary>
    /// Serializes <paramref name="value"/> into <paramref name="writer"/>, using the serializer registered
    /// for <typeparamref name="T"/>, without boxing a value type.
    /// </summary>
    /// <typeparam name="T">The type to serialize as. When it is not a registered type, for example
    /// <see cref="object"/> or an interface, the dispatcher falls back to
    /// <see cref="Serialize(IBufferWriter{byte}, object, Type)"/> with <c>typeof(T)</c>, which boxes a value
    /// type.</typeparam>
    /// <param name="writer">The writer the serialized bytes are appended to. Bytes it already holds are kept.</param>
    /// <param name="value">The value to serialize.</param>
    /// <remarks>
    /// The generated <c>SerializerDispatcher</c> compares <c>typeof(T)</c> with each registered type and calls
    /// that type's serializer directly: no box, no intermediate buffer. Serializing a registered type into a
    /// reused writer allocates nothing beyond what the format's own serializer allocates. The default
    /// implementation, used by a dispatcher that does not override it, forwards to
    /// <see cref="Serialize(IBufferWriter{byte}, object, Type)"/> with <c>typeof(T)</c>.
    /// </remarks>
    /// <exception cref="ArgumentNullException"><paramref name="writer"/> is <see langword="null"/>.</exception>
    /// <exception cref="NotSupportedException">Thrown when no serializer is registered for <typeparamref name="T"/>
    /// or for the runtime type of <paramref name="value"/>.</exception>
    void Serialize<T>(IBufferWriter<byte> writer, T value)
    {
        if (writer is null) throw new ArgumentNullException(nameof(writer));

        Serialize(writer, value!, typeof(T));
    }

    /// <summary>Deserializes <paramref name="data"/> to an instance of <paramref name="type"/>.</summary>
    /// <param name="data">The raw bytes to deserialize.</param>
    /// <param name="type">The target type. Must have been annotated with <c>[ZeroAllocSerializable]</c>,
    /// or for a closed generic type declared with <c>[assembly: ZeroAllocSerializable(typeof(...), format)]</c>,
    /// in the assembly where this dispatcher was generated.</param>
    /// <returns>The deserialized object, or <see langword="null"/> if the underlying serializer returns null.</returns>
    /// <exception cref="NotSupportedException">Thrown when no serializer is registered for <paramref name="type"/>.</exception>
    object? Deserialize(ReadOnlyMemory<byte> data, Type type);
}
