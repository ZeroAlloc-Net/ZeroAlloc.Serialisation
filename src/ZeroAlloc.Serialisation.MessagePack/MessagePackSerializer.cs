using System.Buffers;
using System.Diagnostics.CodeAnalysis;
using MessagePack;
using ZeroAlloc.Serialisation;

namespace ZeroAlloc.Serialisation.MessagePack;

public class MessagePackSerializer<T> : ISerializer<T>
{
    private readonly MessagePackSerializerOptions _options;

    public MessagePackSerializer()
        : this(MessagePackSerializerOptions.Standard) { }

    public MessagePackSerializer(MessagePackSerializerOptions options)
        => _options = options;

    [RequiresDynamicCode("MessagePack serialization of arbitrary types may require dynamic code.")]
    [RequiresUnreferencedCode("MessagePack serialization of arbitrary types may require unreferenced code.")]
    public virtual void Serialize(IBufferWriter<byte> writer, T value)
        => global::MessagePack.MessagePackSerializer.Serialize(writer, value, _options);

    [RequiresDynamicCode("MessagePack serialization of arbitrary types may require dynamic code.")]
    [RequiresUnreferencedCode("MessagePack serialization of arbitrary types may require unreferenced code.")]
    public virtual T? Deserialize(ReadOnlySpan<byte> buffer)
    {
        if (buffer.IsEmpty) return default;
        // MessagePack 3.x has no Deserialize(ReadOnlySpan<byte>) overload — it requires ReadOnlySequence<byte>.
        // Converting from ReadOnlySpan<byte> requires a buffer copy; this allocation is unavoidable with this API.
        var sequence = new ReadOnlySequence<byte>(buffer.ToArray());
        return global::MessagePack.MessagePackSerializer.Deserialize<T>(sequence, _options);
    }
}
