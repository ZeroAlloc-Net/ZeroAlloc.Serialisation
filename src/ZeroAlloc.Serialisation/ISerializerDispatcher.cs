namespace ZeroAlloc.Serialisation;

/// <summary>
/// Runtime-type dispatch surface for source-generated serializers.
/// Implement via the generated <c>SerializerDispatcher</c> class (emitted per assembly
/// by the ZeroAlloc.Serialisation source generator).
/// </summary>
/// <remarks>
/// This interface is intentionally allocation-tolerant: <see cref="Serialize"/> allocates
/// an intermediate buffer and returns <see cref="ReadOnlyMemory{T}">ReadOnlyMemory&lt;byte&gt;</see>
/// rather than writing to an <c>IBufferWriter&lt;byte&gt;</c>. This allows callers (e.g.,
/// <c>ZeroAllocEventSerializer</c>) to hold the result across async boundaries and hand it
/// to storage adapters without pinning a span.
/// </remarks>
public interface ISerializerDispatcher
{
    /// <summary>Serializes <paramref name="value"/> using the serializer registered for <paramref name="type"/>.</summary>
    /// <param name="value">The object to serialize. Must be an instance of <paramref name="type"/>.</param>
    /// <param name="type">The exact runtime type to dispatch to. Must have been annotated with
    /// <c>[ZeroAllocSerializable]</c> in the assembly where this dispatcher was generated.</param>
    /// <returns>A <see cref="ReadOnlyMemory{T}">ReadOnlyMemory&lt;byte&gt;</see> containing the serialized bytes.</returns>
    /// <exception cref="NotSupportedException">Thrown when no serializer is registered for <paramref name="type"/>.</exception>
    ReadOnlyMemory<byte> Serialize(object value, Type type);

    /// <summary>Deserializes <paramref name="data"/> to an instance of <paramref name="type"/>.</summary>
    /// <param name="data">The raw bytes to deserialize.</param>
    /// <param name="type">The target type. Must have been annotated with <c>[ZeroAllocSerializable]</c>
    /// in the assembly where this dispatcher was generated.</param>
    /// <returns>The deserialized object, or <see langword="null"/> if the underlying serializer returns null.</returns>
    /// <exception cref="NotSupportedException">Thrown when no serializer is registered for <paramref name="type"/>.</exception>
    object? Deserialize(ReadOnlyMemory<byte> data, Type type);
}
