using System.Buffers;
using System.Text.Json;
using System.Text.Json.Serialization.Metadata;
using ZeroAlloc.Serialisation;

namespace ZeroAlloc.Serialisation.SystemTextJson;

// AOT-safe: caller supplies JsonTypeInfo<T> from a JsonSerializerContext.
public sealed class SystemTextJsonSerializer<T> : ISerializer<T>
{
    private readonly JsonTypeInfo<T> _typeInfo;

    public SystemTextJsonSerializer(JsonTypeInfo<T> typeInfo)
        => _typeInfo = typeInfo;

    public void Serialize(IBufferWriter<byte> writer, T value)
    {
        using var jsonWriter = new Utf8JsonWriter(writer);
        JsonSerializer.Serialize(jsonWriter, value, _typeInfo);
    }

    public T? Deserialize(ReadOnlySpan<byte> buffer)
    {
        if (buffer.IsEmpty) return default;
        return JsonSerializer.Deserialize(buffer, _typeInfo);
    }
}
