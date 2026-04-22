using System.Collections.Immutable;
using Microsoft.CodeAnalysis;

namespace ZeroAlloc.Serialisation.Generator.Models;

internal sealed record SerializerModel(
    string Namespace,
    string TypeName,
    string FullTypeName,
    string FormatName  // "MemoryPack" | "MessagePack" | "SystemTextJson"
);

/// <summary>
/// Result of inspecting a <c>[ZeroAllocSerializable]</c> application.
/// When diagnostics contain an error, <paramref name="Model"/> is null and no code should be emitted;
/// warnings may appear alongside a valid model.
/// </summary>
internal sealed record SerializerExtractionResult(
    SerializerModel? Model,
    ImmutableArray<DiagnosticInfo> Diagnostics);

/// <summary>
/// Equatable, location-describing diagnostic payload that can cross the incremental pipeline boundary
/// (Roslyn requires generator pipeline values to be equatable / cacheable).
/// </summary>
internal sealed record DiagnosticInfo(
    DiagnosticDescriptor Descriptor,
    LocationInfo? Location,
    EquatableArray<string> MessageArgs)
{
    public Diagnostic ToDiagnostic()
        => Diagnostic.Create(Descriptor, Location?.ToLocation(), MessageArgs.ToArray());
}

/// <summary>
/// Serializable, value-equatable location reference. Avoids pulling <see cref="Location"/> directly
/// through the incremental pipeline (Location is not equatable in a way Roslyn caches well).
/// </summary>
internal sealed record LocationInfo(string FilePath, TextSpanInfo TextSpan, LinePositionSpanInfo LineSpan)
{
    public Location ToLocation()
        => Location.Create(
            FilePath,
            new Microsoft.CodeAnalysis.Text.TextSpan(TextSpan.Start, TextSpan.Length),
            new Microsoft.CodeAnalysis.Text.LinePositionSpan(
                new Microsoft.CodeAnalysis.Text.LinePosition(LineSpan.StartLine, LineSpan.StartCharacter),
                new Microsoft.CodeAnalysis.Text.LinePosition(LineSpan.EndLine, LineSpan.EndCharacter)));

    public static LocationInfo? From(Location? location)
    {
        if (location is null) return null;
        var span = location.SourceSpan;
        var line = location.GetLineSpan();
        return new LocationInfo(
            location.SourceTree?.FilePath ?? string.Empty,
            new TextSpanInfo(span.Start, span.Length),
            new LinePositionSpanInfo(
                line.StartLinePosition.Line,
                line.StartLinePosition.Character,
                line.EndLinePosition.Line,
                line.EndLinePosition.Character));
    }

    public static LocationInfo? From(SyntaxReference? syntaxReference)
    {
        if (syntaxReference is null) return null;
        return From(Location.Create(syntaxReference.SyntaxTree, syntaxReference.Span));
    }
}

internal readonly record struct TextSpanInfo(int Start, int Length);

internal readonly record struct LinePositionSpanInfo(int StartLine, int StartCharacter, int EndLine, int EndCharacter);

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
