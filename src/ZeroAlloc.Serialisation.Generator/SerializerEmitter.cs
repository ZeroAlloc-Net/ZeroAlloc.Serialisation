using System.Linq;
using Microsoft.CodeAnalysis;
using ZeroAlloc.Serialisation.Generator.Models;

namespace ZeroAlloc.Serialisation.Generator;

internal static class SerializerEmitter
{
    private const string UnconditionalSuppressMessageAttribute =
        "System.Diagnostics.CodeAnalysis.UnconditionalSuppressMessageAttribute";

    /// <summary>
    /// Whether code in <paramref name="compilation"/> can apply <c>[UnconditionalSuppressMessage]</c>:
    /// some type of that name is accessible from it, the BCL's from .NET 5 on, or the project's own
    /// polyfill. Before .NET 5 the only copies are usually internal ones inside referenced packages.
    /// </summary>
    public static bool CanEmitTrimSuppressions(Compilation compilation) =>
        compilation.GetTypesByMetadataName(UnconditionalSuppressMessageAttribute)
            .Any(type => compilation.IsSymbolAccessibleWithin(type, compilation.Assembly));

    public static void Emit(SourceProductionContext ctx, SerializerModel model, bool trimSuppressions)
    {
        // ISerializer<T>.Deserialize returns an unconstrained T?, which for a value type is T itself,
        // not Nullable<T>. Emitting "X?" for a struct would declare Nullable<X> and fail to implement
        // the interface, so only reference types carry the nullable annotation.
        var deserializeReturnType = model.IsValueType
            ? model.TypeRef
            : model.TypeRef + "?";

        // MemoryPack/MessagePack still route through APIs annotated with [RequiresUnreferencedCode] /
        // [RequiresDynamicCode] even though their own source generators emit the formatter; the
        // attributes below silence publish-time IL analysis on the bindings we own. STJ no longer
        // needs them — the JsonSerializerContext path is first-class AOT-safe. A project that cannot
        // see an accessible copy of the attribute, such as one targeting netstandard2.1 without a
        // polyfill, gets none: applying an inaccessible one fails its build with CS0122.
        string suppressAttrs = model.FormatName == "SystemTextJson" || !trimSuppressions
            ? ""
            : "    [global::System.Diagnostics.CodeAnalysis.UnconditionalSuppressMessage(\"Trimming\", \"IL2026\", Justification = \"T is a closed concrete type annotated [ZeroAllocSerializable]; the generator only emits bindings the caller already references.\")]\n    [global::System.Diagnostics.CodeAnalysis.UnconditionalSuppressMessage(\"AOT\", \"IL3050\", Justification = \"T is a closed concrete type annotated [ZeroAllocSerializable]; no open generics or runtime code generation is introduced.\")]\n";

        var serializerCall = model.FormatName switch
        {
            "MemoryPack" =>
                "global::MemoryPack.MemoryPackSerializer.Serialize(writer, value);",
            "MessagePack" =>
                "global::MessagePack.MessagePackSerializer.Serialize(writer, value);",
            "SystemTextJson" => Body(
                "var _jw = t_jsonWriter;",
                "if (_jw is null)",
                "{",
                "    _jw = new global::System.Text.Json.Utf8JsonWriter(writer);",
                "}",
                "else",
                "{",
                "    t_jsonWriter = null;",
                "    _jw.Reset(writer);",
                "}",
                "try",
                "{",
                $"    global::System.Text.Json.JsonSerializer.Serialize(_jw, value, global::{model.StjContext!.ContextFullName}.Default.{model.StjContext.PropertyName});",
                "    _jw.Flush();",
                "}",
                "finally",
                "{",
                "    _jw.Reset(DetachedOutput.Instance);",
                "    t_jsonWriter = _jw;",
                "}"),
            _ => throw new System.InvalidOperationException(
                $"Unknown format '{model.FormatName}'; a ZASZ002 diagnostic should have suppressed emission. This indicates a generator bug."),
        };

        var deserializeCall = model.FormatName switch
        {
            "MemoryPack" =>
                $"return global::MemoryPack.MemoryPackSerializer.Deserialize<{model.TypeRef}>(buffer);",
            "MessagePack" => Body(
                "// MessagePack reads ReadOnlyMemory<byte> or ReadOnlySequence<byte>, never a span, so the span",
                "// is copied: into a pooled array, not a new one.",
                "var _rented = global::System.Buffers.ArrayPool<byte>.Shared.Rent(buffer.Length);",
                "try",
                "{",
                "    buffer.CopyTo(_rented);",
                "    return DeserializeMemory(new global::System.ReadOnlyMemory<byte>(_rented, 0, buffer.Length));",
                "}",
                "finally",
                "{",
                "    global::System.Buffers.ArrayPool<byte>.Shared.Return(_rented);",
                "}"),
            "SystemTextJson" =>
                $"return global::System.Text.Json.JsonSerializer.Deserialize(buffer, global::{model.StjContext!.ContextFullName}.Default.{model.StjContext.PropertyName});",
            _ => throw new System.InvalidOperationException(
                $"Unknown format '{model.FormatName}'; a ZASZ002 diagnostic should have suppressed emission. This indicates a generator bug."),
        };

        // Members after Deserialize. Each block starts with a blank line and ends with a newline.
        var extraMembers = model.FormatName switch
        {
            // A [ThreadStatic] field and a nested class rather than a shared helper type: the code
            // compiles into the consumer's assembly, which may not reference the adapter package, and
            // a per-assembly helper type would clash between assemblies that see each other's internals.
            "SystemTextJson" => """

                    // One Utf8JsonWriter per thread, reset to each caller's buffer writer. Serialize empties the
                    // slot while it writes, so a reentrant call on the same thread creates a writer of its own.
                    [global::System.ThreadStatic]
                    private static global::System.Text.Json.Utf8JsonWriter? t_jsonWriter;

                    // What a cached writer points at between calls, so that it keeps no caller's buffer writer alive.
                    private sealed class DetachedOutput : IBufferWriter<byte>
                    {
                        public static readonly DetachedOutput Instance = new DetachedOutput();

                        public void Advance(int count) => throw new global::System.InvalidOperationException();

                        public global::System.Memory<byte> GetMemory(int sizeHint = 0) => throw new global::System.InvalidOperationException();

                        public global::System.Span<byte> GetSpan(int sizeHint = 0) => throw new global::System.InvalidOperationException();
                    }

                """,
            "MessagePack" => $$"""

                    // Deserializes memory the caller already holds, without copying it. SerializerDispatcher calls this.
                {{suppressAttrs}}    internal static {{deserializeReturnType}} DeserializeMemory(global::System.ReadOnlyMemory<byte> buffer)
                    {
                        if (buffer.IsEmpty) return default;
                        return global::MessagePack.MessagePackSerializer.Deserialize<{{model.TypeRef}}>(buffer);
                    }

                """,
            _ => "",
        };

        var ns = string.IsNullOrEmpty(model.Namespace)
            ? ""
            : $"namespace {model.Namespace};\n\n";

        var source = $$"""
            // <auto-generated/>
            // NOTE: This generated class is internal to the assembly where [ZeroAllocSerializable] is applied.
            // It is not accessible from other assemblies. This is by design — apply [ZeroAllocSerializable]
            // to types in the assembly that will use the serializer.
            #nullable enable
            using System.Buffers;
            using ZeroAlloc.Serialisation;

            {{ns}}internal sealed class {{model.TypeName}}Serializer : ISerializer<{{model.TypeRef}}>
            {
            {{suppressAttrs}}    public void Serialize(IBufferWriter<byte> writer, {{model.TypeRef}} value)
                {
                    {{serializerCall}}
                }

            {{suppressAttrs}}    public {{deserializeReturnType}} Deserialize(global::System.ReadOnlySpan<byte> buffer)
                {
                    if (buffer.IsEmpty) return default;
                    {{deserializeCall}}
                }
            {{extraMembers}}}
            """;

        // The hint name keeps same-named types in different namespaces, and every closed
        // construction of a generic type, apart; see ModelExtractor.HintNameOf.
        ctx.AddSource($"{model.HintName}Serializer.g.cs", source);
    }

    /// <summary>Joins statement lines into a method body indented by eight spaces.</summary>
    private static string Body(params string[] lines) => string.Join("\n        ", lines);
}
