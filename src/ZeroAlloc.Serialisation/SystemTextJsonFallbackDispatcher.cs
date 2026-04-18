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
[RequiresUnreferencedCode("JSON fallback uses reflection-based System.Text.Json serialization which is not trim-safe.")]
[RequiresDynamicCode("JSON fallback uses reflection-based System.Text.Json serialization which requires dynamic code generation.")]
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
#pragma warning disable IL2026, IL3050
            return JsonSerializer.SerializeToUtf8Bytes(value, type, _options);
#pragma warning restore IL2026, IL3050
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
#pragma warning disable IL2026, IL3050
            return JsonSerializer.Deserialize(data.Span, type, _options);
#pragma warning restore IL2026, IL3050
        }
    }
}
