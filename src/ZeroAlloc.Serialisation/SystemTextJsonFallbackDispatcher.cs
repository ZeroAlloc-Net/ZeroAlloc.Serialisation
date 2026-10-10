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
public sealed class SystemTextJsonFallbackDispatcher : ISerializerDispatcher
{
    private readonly ISerializerDispatcher _inner;
    private readonly JsonSerializerOptions? _options;
    private readonly Func<object, Type, JsonSerializerOptions?, ReadOnlyMemory<byte>> _serializeFallback;
    private readonly Func<ReadOnlyMemory<byte>, Type, JsonSerializerOptions?, object?> _deserializeFallback;

    /// <summary>
    /// Initializes a new <see cref="SystemTextJsonFallbackDispatcher"/>.
    /// </summary>
    /// <param name="inner">The inner dispatcher. Called first for every type.</param>
    /// <param name="options">
    /// Optional <see cref="JsonSerializerOptions"/> used when falling back to
    /// <see cref="System.Text.Json.JsonSerializer"/>. <see langword="null"/> uses the default options.
    /// </param>
    [RequiresUnreferencedCode("The fallback dispatcher serializes through reflection-based System.Text.Json, which is not trim-safe. Under Native AOT or trimming, use SystemTextJsonSerializer<T> with a source-generated JsonTypeInfo<T> instead.")]
    [RequiresDynamicCode("The fallback dispatcher serializes through reflection-based System.Text.Json, which needs runtime code generation. Under Native AOT, use SystemTextJsonSerializer<T> with a source-generated JsonTypeInfo<T> instead.")]
    public SystemTextJsonFallbackDispatcher(
        ISerializerDispatcher inner,
        JsonSerializerOptions? options = null)
    {
        _inner = inner ?? throw new ArgumentNullException(nameof(inner));
        // The reflection-based calls are bound here, in the annotated constructor, so they are
        // reachable only by callers that accepted the requirement.
        _options = options;
        _serializeFallback = SerializeReflective;
        _deserializeFallback = DeserializeReflective;
    }

    [RequiresUnreferencedCode("Reflection-based System.Text.Json serialization is not trim-safe.")]
    [RequiresDynamicCode("Reflection-based System.Text.Json serialization needs runtime code generation.")]
    private static ReadOnlyMemory<byte> SerializeReflective(object value, Type type, JsonSerializerOptions? options)
        => JsonSerializer.SerializeToUtf8Bytes(value, type, options);

    [RequiresUnreferencedCode("Reflection-based System.Text.Json deserialization is not trim-safe.")]
    [RequiresDynamicCode("Reflection-based System.Text.Json deserialization needs runtime code generation.")]
    private static object? DeserializeReflective(ReadOnlyMemory<byte> data, Type type, JsonSerializerOptions? options)
        => JsonSerializer.Deserialize(data.Span, type, options);

    /// <inheritdoc/>
    public ReadOnlyMemory<byte> Serialize(object value, Type type)
    {
        try
        {
            return _inner.Serialize(value, type);
        }
        catch (NotSupportedException)
        {
            return _serializeFallback(value, type, _options);
        }
    }

    /// <inheritdoc/>
    public object? Deserialize(ReadOnlyMemory<byte> data, Type type)
    {
        try
        {
            return _inner.Deserialize(data, type);
        }
        catch (NotSupportedException)
        {
            return _deserializeFallback(data, type, _options);
        }
    }
}
