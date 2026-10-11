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
/// <para>
/// <see cref="Deserialize(ReadOnlyMemory{byte}, Type)"/> returns <see cref="object"/>, so a struct read back
/// through it is boxed. <see cref="Deserialize{T}(ReadOnlyMemory{byte})"/> returns the value as
/// <c>T</c> without boxing it.
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

    /// <summary>
    /// Deserializes <paramref name="data"/> to a <typeparamref name="T"/>, using the serializer registered
    /// for <typeparamref name="T"/>, without boxing a value type.
    /// </summary>
    /// <typeparam name="T">The target type. Must be registered as for <see cref="Deserialize(ReadOnlyMemory{byte}, Type)"/>.
    /// Reading has no runtime type to dispatch on, so a type that is not itself registered, such as
    /// <see cref="object"/>, an interface or <see cref="Nullable{T}"/> of a registered struct, throws
    /// <see cref="NotSupportedException"/>, as <see cref="Deserialize(ReadOnlyMemory{byte}, Type)"/> does with
    /// <c>typeof(T)</c>.</typeparam>
    /// <param name="data">The raw bytes to deserialize.</param>
    /// <returns>The deserialized value. For empty <paramref name="data"/>, the generated
    /// <c>SerializerDispatcher</c> returns <see langword="default"/>: <see langword="null"/> for a reference type,
    /// the zero value for a value type, as <see cref="Deserialize(ReadOnlyMemory{byte}, Type)"/> does. When
    /// <see cref="Deserialize(ReadOnlyMemory{byte}, Type)"/> returns <see langword="null"/>, the default
    /// implementation returns <see langword="null"/> for a reference type or a <see cref="Nullable{T}"/>, and
    /// throws for a non-nullable value type.</returns>
    /// <remarks>
    /// <para>
    /// The generated <c>SerializerDispatcher</c> compares <c>typeof(T)</c> with each registered type and returns
    /// that type's serializer result directly, so a struct is not boxed. Reading a registered type allocates
    /// nothing beyond what the format's own deserializer allocates for the value. The data is read where it is:
    /// a MessagePack payload is passed to MessagePack as memory, without the copy that the span-based
    /// <see cref="ISerializer{T}.Deserialize(ReadOnlySpan{byte})"/> makes for that format.
    /// </para>
    /// <para>
    /// The default implementation, used by a dispatcher that does not override it, calls
    /// <see cref="Deserialize(ReadOnlyMemory{byte}, Type)"/> with <c>typeof(T)</c> and unboxes the result.
    /// A <see langword="null"/> result has no value to unbox into a non-nullable value type, so it throws
    /// rather than return a zero value the data never held.
    /// </para>
    /// </remarks>
    /// <exception cref="NotSupportedException">Thrown when no serializer is registered for <typeparamref name="T"/>.</exception>
    /// <exception cref="InvalidCastException">Thrown by the default implementation when
    /// <see cref="Deserialize(ReadOnlyMemory{byte}, Type)"/> returns an object that is not a <typeparamref name="T"/>,
    /// or returns <see langword="null"/> when <typeparamref name="T"/> is a non-nullable value type.</exception>
    T? Deserialize<T>(ReadOnlyMemory<byte> data)
    {
        if (Deserialize(data, typeof(T)) is { } value) return (T)value;

        // default(T) is null exactly for a reference type or a Nullable<>, and is a constant in each
        // compiled instantiation, so this check costs nothing on the success path.
        if (default(T) is null) return default;

        throw new InvalidCastException(
            $"Deserialize(ReadOnlyMemory<byte>, Type) returned null for {typeof(T).FullName}, " +
            "a non-nullable value type. A null result cannot be returned as a value of that type.");
    }
}
