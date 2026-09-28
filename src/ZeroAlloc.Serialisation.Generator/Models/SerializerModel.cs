using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Text;

namespace ZeroAlloc.Serialisation.Generator.Models;

internal sealed record SerializerModel(
    string Namespace,
    string TypeName,
    string FullTypeName,
    string FormatName,  // "MemoryPack" | "MessagePack" | "SystemTextJson"
    bool IsValueType,
    StjContextBinding? StjContext = null
);

/// <summary>
/// For SystemTextJson models: the fully-qualified <c>JsonSerializerContext</c>-derived type and
/// the property name on its <c>Default</c> singleton that exposes <c>JsonTypeInfo&lt;T&gt;</c>.
/// When non-null, the emitter uses <c>ContextFullName.Default.PropertyName</c> instead of the
/// reflection-based overloads. Required for AOT safety; null for non-STJ formats.
/// </summary>
internal sealed record StjContextBinding(string ContextFullName, string PropertyName);

/// <summary>
/// Extracted from a <c>JsonSerializerContext</c>-derived class with one or more
/// <c>[JsonSerializable(typeof(T))]</c> attributes. One entry per (context class, target type) pair.
/// </summary>
internal sealed record StjContextEntry(string TargetFullName, string ContextFullName, string PropertyName);

/// <summary>
/// Result of inspecting a <c>[ZeroAllocSerializable]</c> application.
/// When diagnostics contain an error, <paramref name="Model"/> is null and no code should be emitted;
/// warnings may appear alongside a valid model.
/// </summary>
/// <param name="AttributeLocation">
/// The <c>[ZeroAllocSerializable]</c> attribute, where a diagnostic found after extraction, ZASZ004,
/// is reported.
/// </param>
internal sealed record SerializerExtractionResult(
    SerializerModel? Model,
    LocationInfo AttributeLocation,
    EquatableArray<DiagnosticInfo> Diagnostics);

/// <summary>
/// Equatable, location-describing diagnostic payload that can cross the incremental pipeline boundary
/// (Roslyn requires generator pipeline values to be equatable / cacheable).
/// </summary>
internal sealed record DiagnosticInfo(
    DiagnosticDescriptor Descriptor,
    LocationInfo Location,
    EquatableArray<string> MessageArgs)
{
    public Diagnostic ToDiagnostic()
        => Diagnostic.Create(Descriptor, Location.ToLocation(), MessageArgs.ToArray());
}

/// <summary>
/// A diagnostic location the pipeline can cache: the syntax tree and the span within it.
/// </summary>
/// <remarks>
/// <para>
/// The tree is kept, not just its file path, because the rebuilt diagnostic must be a source
/// location. <c>Location.Create(filePath, span, lineSpan)</c> gives an external-file location
/// with no <see cref="Location.SourceTree"/>, and the compiler then ignores
/// <c>#pragma warning disable</c> and per-file severity settings for it.
/// </para>
/// <para>
/// Keeping the tree does not defeat caching. <see cref="SyntaxTree"/> compares by reference, and
/// a compilation reuses the tree instance of every file that did not change, so the location
/// compares equal across runs until its own file is edited, when the model is rebuilt anyway. A
/// tree belongs to no one compilation, and only the tree of the latest run is held.
/// </para>
/// </remarks>
internal sealed record LocationInfo(SyntaxTree Tree, TextSpan Span)
{
    public Location ToLocation() => Location.Create(Tree, Span);

    public static LocationInfo From(SyntaxNode node) => new(node.SyntaxTree, node.Span);

    public static LocationInfo From(SyntaxToken token) => new(token.SyntaxTree!, token.Span);
}

/// <summary>
/// Minimal value-equatable wrapper around an array. Compares element-wise so records using it
/// participate in incremental generator caching.
/// </summary>
internal readonly struct EquatableArray<T> : System.IEquatable<EquatableArray<T>>
    where T : System.IEquatable<T>
{
    private readonly T[]? _array;

    public EquatableArray(T[] array) => _array = array;

    public T[] ToArray() => _array ?? System.Array.Empty<T>();

    public bool IsEmpty => _array is null || _array.Length == 0;

    /// <summary>A copy of this array with <paramref name="item"/> appended.</summary>
    public EquatableArray<T> Add(T item)
    {
        var a = ToArray();
        var copy = new T[a.Length + 1];
        System.Array.Copy(a, copy, a.Length);
        copy[a.Length] = item;
        return new EquatableArray<T>(copy);
    }

    public System.Collections.Generic.IEnumerator<T> GetEnumerator() =>
        ((System.Collections.Generic.IEnumerable<T>)ToArray()).GetEnumerator();

    public bool Equals(EquatableArray<T> other)
    {
        var a = _array ?? System.Array.Empty<T>();
        var b = other._array ?? System.Array.Empty<T>();
        if (a.Length != b.Length) return false;
        for (int i = 0; i < a.Length; i++)
        {
            if (!a[i].Equals(b[i])) return false;
        }
        return true;
    }

    public override bool Equals(object? obj) => obj is EquatableArray<T> other && Equals(other);

    public override int GetHashCode()
    {
        var a = _array;
        if (a is null) return 0;
        unchecked
        {
            int hash = 17;
            foreach (var item in a)
            {
                hash = (hash * 31) + (item?.GetHashCode() ?? 0);
            }
            return hash;
        }
    }

    public static bool operator ==(EquatableArray<T> left, EquatableArray<T> right) => left.Equals(right);

    public static bool operator !=(EquatableArray<T> left, EquatableArray<T> right) => !left.Equals(right);
}
