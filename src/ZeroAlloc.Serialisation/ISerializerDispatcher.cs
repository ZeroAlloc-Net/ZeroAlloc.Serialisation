namespace ZeroAlloc.Serialisation;

/// <summary>
/// Runtime-type dispatch surface for source-generated serializers.
/// Implement via the generated <c>SerializerDispatcher</c> class (emitted per assembly
/// by the ZeroAlloc.Serialisation source generator).
/// </summary>
public interface ISerializerDispatcher
{
    /// <summary>Serializes <paramref name="value"/> using the serializer registered for <paramref name="type"/>.</summary>
    ReadOnlyMemory<byte> Serialize(object value, Type type);

    /// <summary>Deserializes <paramref name="data"/> to an instance of <paramref name="type"/>.</summary>
    object? Deserialize(ReadOnlyMemory<byte> data, Type type);
}
