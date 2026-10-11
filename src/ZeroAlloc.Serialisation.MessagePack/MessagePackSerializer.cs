using System.Buffers;
using System.Diagnostics.CodeAnalysis;
using MessagePack;
using ZeroAlloc.Serialisation;

namespace ZeroAlloc.Serialisation.MessagePack;

/// <summary>An <see cref="ISerializer{T}"/> for MessagePack.</summary>
public class MessagePackSerializer<T> : ISerializer<T>
{
    private const string ReflectionMessage =
        "MessagePackSerializerOptions.Standard, and options whose resolver falls back to it, build formatters "
        + "with reflection and dynamic code, which need members the trimmer may remove and code Native AOT "
        + "cannot generate. For Native AOT, pass a resolver built from a [GeneratedMessagePackResolver] class "
        + "and the built-in formatters to the MessagePackSerializer<T>(IFormatterResolver) constructor.";

    private readonly MessagePackSerializerOptions _options;

    /// <summary>Serializes with <see cref="MessagePackSerializerOptions.Standard"/>, which uses reflection.</summary>
    [RequiresUnreferencedCode(ReflectionMessage)]
    [RequiresDynamicCode(ReflectionMessage)]
    public MessagePackSerializer()
        : this(MessagePackSerializerOptions.Standard) { }

    /// <summary>
    /// Serializes with these options. Their resolver may use reflection, which this constructor cannot
    /// rule out, so it carries the trim and AOT annotations whatever the resolver is. To pass options with a
    /// resolver that does not, use <see cref="MessagePackSerializer(IFormatterResolver, MessagePackSerializerOptions?)"/>.
    /// </summary>
    [RequiresUnreferencedCode(ReflectionMessage)]
    [RequiresDynamicCode(ReflectionMessage)]
    public MessagePackSerializer(MessagePackSerializerOptions options)
        => _options = options;

    /// <summary>
    /// Serializes through <paramref name="resolver"/> only, with a copy of <paramref name="options"/>, or of
    /// default options, whose resolver is replaced by it.
    /// </summary>
    /// <remarks>
    /// For Native AOT, build the resolver from a <c>[GeneratedMessagePackResolver]</c> class and
    /// <c>BuiltinResolver.Instance</c>, for example with <c>CompositeResolver.Create</c>. This constructor adds
    /// no reflection-based resolver of its own, so a type the resolver has no formatter for throws
    /// <see cref="MessagePackSerializationException"/>.
    /// </remarks>
    /// <param name="resolver">The resolver that supplies every formatter.</param>
    /// <param name="options">Options to copy, or <see langword="null"/> for the defaults.</param>
    /// <exception cref="ArgumentNullException"><paramref name="resolver"/> is <see langword="null"/>.</exception>
    public MessagePackSerializer(IFormatterResolver resolver, MessagePackSerializerOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(resolver);
        // Not options ?? Standard: that would reach StandardResolver only to replace it.
        _options = options is null ? new MessagePackSerializerOptions(resolver) : options.WithResolver(resolver);
    }

    public virtual void Serialize(IBufferWriter<byte> writer, T value)
        => global::MessagePack.MessagePackSerializer.Serialize(writer, value, _options);

    public virtual T? Deserialize(ReadOnlySpan<byte> buffer)
    {
        if (buffer.IsEmpty) return default;
        // MessagePack reads ReadOnlyMemory<byte> or ReadOnlySequence<byte>, never a span: its reader is
        // built on SequenceReader<byte>. Copy the span into a pooled array rather than a new one. Pinning
        // the span behind a MemoryManager would skip the copy, but would turn a formatter that keeps a
        // slice of its input into a dangling pointer once the span is unpinned.
        var rented = ArrayPool<byte>.Shared.Rent(buffer.Length);
        try
        {
            buffer.CopyTo(rented);
            return global::MessagePack.MessagePackSerializer.Deserialize<T>(
                new ReadOnlyMemory<byte>(rented, 0, buffer.Length), _options);
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(rented);
        }
    }
}
