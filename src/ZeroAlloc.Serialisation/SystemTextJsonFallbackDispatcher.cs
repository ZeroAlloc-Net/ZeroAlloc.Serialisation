using System.Buffers;
using System.Diagnostics.CodeAnalysis;
using System.Text.Json;

namespace ZeroAlloc.Serialisation;

/// <summary>
/// An <see cref="ISerializerDispatcher"/> decorator that falls back to
/// <see cref="System.Text.Json.JsonSerializer"/> for types not registered in the inner dispatcher.
/// </summary>
/// <remarks>
/// <para>
/// <strong>Opt-in only.</strong> Register via
/// <c>services.WithSystemTextJsonFallback()</c> (from
/// <c>SerializerDispatcherFallbackExtensions</c>) after calling
/// <c>AddSerializerDispatcher()</c>. When the fallback is not registered the inner
/// dispatcher retains its strict behaviour and throws <see cref="NotSupportedException"/>
/// for unregistered types — ensuring missing <c>[ZeroAllocSerializable]</c> annotations are
/// caught early.
/// </para>
/// <para>
/// Only <see cref="NotSupportedException"/> is caught and re-routed; all other exceptions
/// propagate unchanged.
/// </para>
/// </remarks>
[RequiresUnreferencedCode("JSON fallback uses reflection-based System.Text.Json serialization which is not trim-safe. Under Native AOT or trimming, use SystemTextJsonSerializer<T> with a source-generated JsonTypeInfo<T> instead.")]
[RequiresDynamicCode("JSON fallback uses reflection-based System.Text.Json serialization which requires dynamic code generation. Under Native AOT, use SystemTextJsonSerializer<T> with a source-generated JsonTypeInfo<T> instead.")]
public sealed class SystemTextJsonFallbackDispatcher : ISerializerDispatcher
{
    private readonly ISerializerDispatcher _inner;
    private readonly JsonSerializerOptions? _options;

    /// <summary>
    /// Initializes a new <see cref="SystemTextJsonFallbackDispatcher"/>.
    /// </summary>
    /// <param name="inner">The inner dispatcher. Called first for every type.</param>
    /// <param name="options">
    /// Optional <see cref="JsonSerializerOptions"/> used when falling back to
    /// <see cref="System.Text.Json.JsonSerializer"/>. <see langword="null"/> uses the default options.
    /// </param>
    public SystemTextJsonFallbackDispatcher(
        ISerializerDispatcher inner,
        JsonSerializerOptions? options = null)
    {
        _inner   = inner ?? throw new ArgumentNullException(nameof(inner));
        _options = options;
    }

    /// <inheritdoc/>
    public ReadOnlyMemory<byte> Serialize(object value, Type type)
    {
        try
        {
            return _inner.Serialize(value, type);
        }
        catch (NotSupportedException)
        {
            return JsonSerializer.SerializeToUtf8Bytes(value, type, _options);
        }
    }

    /// <inheritdoc/>
    /// <remarks>
    /// Writes through the inner dispatcher's <see cref="ISerializerDispatcher.Serialize(IBufferWriter{byte}, object, Type)"/>,
    /// so a registered type is written straight into <paramref name="writer"/>. An unregistered type is serialized
    /// with <see cref="JsonSerializer"/>, as the memory-returning overload does, and copied in. The generated dispatcher
    /// throws <see cref="NotSupportedException"/> for an unregistered type before it writes anything.
    /// </remarks>
    public void Serialize(IBufferWriter<byte> writer, object value, Type type)
    {
        if (writer is null) throw new ArgumentNullException(nameof(writer));

        try
        {
            _inner.Serialize(writer, value, type);
        }
        catch (NotSupportedException)
        {
            WriteJson(writer, value, type);
        }
    }

    /// <inheritdoc/>
    /// <remarks>
    /// Writes through the inner dispatcher's <see cref="ISerializerDispatcher.Serialize{T}(IBufferWriter{byte}, T)"/>,
    /// so a registered type is written straight into <paramref name="writer"/> without boxing. An unregistered
    /// type is written with <see cref="JsonSerializer"/> as <typeparamref name="T"/>.
    /// </remarks>
    public void Serialize<T>(IBufferWriter<byte> writer, T value)
    {
        if (writer is null) throw new ArgumentNullException(nameof(writer));

        try
        {
            _inner.Serialize(writer, value);
        }
        catch (NotSupportedException)
        {
            WriteJson(writer, value, typeof(T));
        }
    }

    // The same bytes as the memory-returning Serialize: SerializeToUtf8Bytes applies every
    // JsonSerializerOptions setting, including the formatting ones a caller-built Utf8JsonWriter would
    // take from JsonWriterOptions instead. This path is the reflection fallback, not the hot path.
    private void WriteJson(IBufferWriter<byte> writer, object? value, Type type)
        => writer.Write(JsonSerializer.SerializeToUtf8Bytes(value, type, _options));

    /// <inheritdoc/>
    public object? Deserialize(ReadOnlyMemory<byte> data, Type type)
    {
        try
        {
            return _inner.Deserialize(data, type);
        }
        catch (NotSupportedException)
        {
            return JsonSerializer.Deserialize(data.Span, type, _options);
        }
    }

    /// <inheritdoc/>
    /// <remarks>
    /// Reads through the inner dispatcher's <see cref="ISerializerDispatcher.Deserialize{T}(ReadOnlyMemory{byte})"/>,
    /// so a registered struct is returned without boxing. An unregistered type is read with
    /// <see cref="JsonSerializer"/> as <typeparamref name="T"/>, with the same options as the object-returning overload.
    /// </remarks>
    public T? Deserialize<T>(ReadOnlyMemory<byte> data)
    {
        try
        {
            return _inner.Deserialize<T>(data);
        }
        catch (NotSupportedException)
        {
            return JsonSerializer.Deserialize<T>(data.Span, _options);
        }
    }
}
