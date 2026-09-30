using System;
using System.Linq;
using Microsoft.CodeAnalysis;
using ZeroAlloc.Serialisation.Generator.Models;

namespace ZeroAlloc.Serialisation.Generator;

/// <summary>
/// Emits transparent serializers for <c>[ValueObject]</c>-decorated
/// single-property partial types, structs and classes, top-level or nested,
/// across the three supported backends
/// (System.Text.Json, MessagePack, MemoryPack). Each emission method is
/// gated by a check on the consuming compilation's assembly references —
/// adopters who don't reference a given backend package pay zero code-gen
/// for it.
/// </summary>
internal static class ValueObjectEmitter
{
    private const string SystemTextJsonBackendAssembly = "ZeroAlloc.Serialisation.SystemTextJson";
    private const string MessagePackBackendAssembly = "ZeroAlloc.Serialisation.MessagePack";
    private const string MemoryPackBackendAssembly = "ZeroAlloc.Serialisation.MemoryPack";

    /// <summary>
    /// Which backends <paramref name="compilation"/> references, as a value that compares equal
    /// across edits that do not change the references.
    /// </summary>
    internal static ValueObjectBackends DetectBackends(Compilation compilation) => new(
        SystemTextJson: ReferencesSystemTextJson(compilation),
        MessagePack: ReferencesMessagePack(compilation),
        MemoryPack: ReferencesMemoryPack(compilation));

    internal static bool ReferencesSystemTextJson(Compilation compilation) =>
        ReferencesAssembly(compilation, SystemTextJsonBackendAssembly);

    internal static bool ReferencesMessagePack(Compilation compilation) =>
        ReferencesAssembly(compilation, MessagePackBackendAssembly);

    internal static bool ReferencesMemoryPack(Compilation compilation) =>
        ReferencesAssembly(compilation, MemoryPackBackendAssembly);

    private static bool ReferencesAssembly(Compilation compilation, string assemblyName) =>
        compilation.ReferencedAssemblyNames.Any(a =>
            string.Equals(a.Name, assemblyName, StringComparison.Ordinal));

    /// <summary>
    /// The file name the per-type output for <paramref name="model"/> is added under: the
    /// type's namespace, containing types and name, so same-named types in different
    /// namespaces or containers do not collide. Hint names are not a contract; a repeated one
    /// makes Roslyn throw and drop the generator's whole output.
    /// </summary>
    internal static string HintName(ValueObjectModel model, string suffix)
    {
        var sb = new System.Text.StringBuilder();
        if (!string.IsNullOrEmpty(model.Namespace)) sb.Append(model.Namespace).Append('.');
        return sb.Append(model.QualifiedTypeName).Append(suffix).Append(".g.cs").ToString();
    }

    /// <summary>
    /// The start of a partial declaration of the value object, such as
    /// <c>public readonly partial struct CustomerId</c>. Every part of a partial type must
    /// agree on class, record class, struct or record struct, and on accessibility when it
    /// states one.
    /// </summary>
    private static string PartialDeclaration(ValueObjectModel model)
    {
        var readonlyKeyword = model.IsReadOnly ? "readonly " : "";
        return $"{model.Accessibility} {readonlyKeyword}partial {model.DeclarationKeyword} {model.TypeName}";
    }

    /// <summary>
    /// The accessibility of the converter or formatter emitted next to the value object. It is
    /// internal, or narrower when the value object is: a converter must not be more accessible
    /// than the type in its signatures.
    /// </summary>
    private static string SiblingAccessibility(ValueObjectModel model) => model.Accessibility switch
    {
        "private" or "protected" or "private protected" => model.Accessibility,
        _ => "internal",
    };

    /// <summary>
    /// Wraps <paramref name="body"/> in partial declarations of the value object's containing
    /// types, outermost first, so the partial part and its sibling converter land inside the
    /// same containing type as the user's declaration. A top-level type's body is returned
    /// unchanged.
    /// </summary>
    private static string WrapInContainingTypes(ValueObjectModel model, string body)
    {
        var containing = model.ContainingTypes.ToArray();
        for (var i = containing.Length - 1; i >= 0; i--)
        {
            var type = containing[i];
            var sb = new System.Text.StringBuilder();
            sb.Append(type.Accessibility).Append(' ').Append(type.Modifiers).Append("partial ")
                .Append(type.DeclarationKeyword).Append(' ').Append(type.Name).Append('\n');
            sb.Append("{\n");
            // The templates carry the line endings of this source file, so a blank line may be
            // a lone '\r'. Indenting it would leave trailing whitespace.
            foreach (var line in body.TrimEnd('\n').Split('\n'))
            {
                if (line.Length > 0 && line != "\r") sb.Append("    ");
                sb.Append(line).Append('\n');
            }
            sb.Append("}\n");
            body = sb.ToString();
        }
        return body;
    }

    /// <summary>
    /// Emits a JsonConverter&lt;T&gt; for the value-object and a partial declaration of the
    /// type carrying [JsonConverter(typeof(...))] so System.Text.Json picks it up automatically
    /// without explicit registration. A nested type's converter is nested next to it, which
    /// keeps a private nested type reachable.
    /// </summary>
    /// <remarks>
    /// Reference types need no null handling here: <c>JsonConverter&lt;T&gt;.HandleNull</c> is
    /// false for them, so System.Text.Json writes and reads <c>null</c> itself and never calls
    /// the converter with it.
    /// </remarks>
    internal static string EmitSystemTextJsonConverter(ValueObjectModel model)
    {
        var typeName = model.TypeName;
        var ns = model.Namespace;
        var (readMethod, writeMethod) = SystemTextJsonReadWriteForType(model);
        var converterName = $"{typeName}SystemTextJsonConverter";

        // STJ's Utf8JsonWriter has no WriteStringValue(TimeSpan) overload — TimeSpan
        // round-trips through ToString() / Parse(). For all other underlying types
        // the native overload accepts the property type directly.
        var writeArgExpr = string.Equals(model.UnderlyingTypeDisplayName, "System.TimeSpan", StringComparison.Ordinal)
            ? $"value.{model.UnderlyingPropertyName}.ToString()"
            : $"value.{model.UnderlyingPropertyName}";

        var nsOpen = string.IsNullOrEmpty(ns) ? "" : $"namespace {ns};\n\n";

        var body = $$"""
            [JsonConverter(typeof({{converterName}}))]
            {{PartialDeclaration(model)}}
            {
            }

            {{SiblingAccessibility(model)}} sealed class {{converterName}} : JsonConverter<{{typeName}}>
            {
                public override {{typeName}} Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
                    => new {{typeName}}({{readMethod}});

                public override void Write(Utf8JsonWriter writer, {{typeName}} value, JsonSerializerOptions options)
                    => writer.{{writeMethod}}({{writeArgExpr}});
            }

            """;

        return $$"""
            // <auto-generated/>
            #nullable enable

            using System;
            using System.Text.Json;
            using System.Text.Json.Serialization;

            {{nsOpen}}{{WrapInContainingTypes(model, body)}}
            """;
    }

    /// <summary>
    /// Emits a single per-assembly extension method, public unless ZeroAllocGeneratedAccessibility
    /// is Internal, that registers every
    /// generator-emitted [ValueObject] JsonConverter into a JsonSerializerOptions
    /// instance. Closes the JsonSerializerContext interop gap — STJ's source
    /// generator doesn't see [JsonConverter] attributes added by other generators,
    /// so consumers using a context-based pipeline need an explicit registration
    /// path. The extension lives in the ZeroAlloc.Serialisation.SystemTextJson
    /// namespace for discoverability via the using consumers already have.
    /// </summary>
    internal static string EmitSystemTextJsonRegistrar(
        System.Collections.Generic.IReadOnlyList<ValueObjectModel> valueObjects,
        string accessibility)
    {
        var sb = new System.Text.StringBuilder();
        sb.AppendLine("// <auto-generated/>");
        sb.AppendLine("#nullable enable");
        sb.AppendLine();
        sb.AppendLine("namespace ZeroAlloc.Serialisation.SystemTextJson;");
        sb.AppendLine();
        sb.AppendLine("/// <summary>");
        sb.AppendLine("/// Registers every [ZeroAlloc.ValueObjects.ValueObject] partial struct's");
        sb.AppendLine("/// generator-emitted JsonConverter into a JsonSerializerOptions. Call this");
        sb.AppendLine("/// when using JsonSerializerContext (the source-generated typeinfo pipeline)");
        sb.AppendLine("/// — STJ resolves options.Converters before the context's typeinfo, so the");
        sb.AppendLine("/// underlying-primitive wire format takes precedence over default struct");
        sb.AppendLine("/// serialization.");
        sb.AppendLine("/// </summary>");
        sb.AppendLine($"{accessibility} static class ValueObjectJsonConvertersExtensions");
        sb.AppendLine("{");
        sb.AppendLine("    public static global::System.Text.Json.JsonSerializerOptions AddZeroAllocValueObjectConverters(this global::System.Text.Json.JsonSerializerOptions options)");
        sb.AppendLine("    {");
        foreach (var type in ReachableFromNamespace(valueObjects))
        {
            var converterFqn = BuildConverterFqn(type);
            sb.AppendLine($"        options.Converters.Add(new {converterFqn}());");
        }
        sb.AppendLine("        options.TypeInfoResolverChain.Insert(0, global::ZeroAlloc.Serialisation.SystemTextJson.ValueObjectJsonTypeInfoResolver.Default);");
        sb.AppendLine("        return options;");
        sb.AppendLine("    }");
        sb.AppendLine("}");
        return sb.ToString();
    }

    /// <summary>
    /// Emits a single per-assembly internal IJsonTypeInfoResolver that returns
    /// pre-configured JsonTypeInfo&lt;T&gt; for every value-object in the
    /// assembly. Required for JsonSerializerContext consumers — the startup
    /// JsonPropertyInfo.Configure walk asks the resolver chain for typeinfo,
    /// and JsonContext's source-gen-emitted resolver doesn't have usable
    /// metadata for [ValueObject] types (Roslyn gens can't see the
    /// [JsonConverter] attribute the per-type emitter adds via a partial).
    /// </summary>
    internal static string EmitSystemTextJsonResolver(
        System.Collections.Generic.IReadOnlyList<ValueObjectModel> valueObjects)
    {
        var sb = new System.Text.StringBuilder();
        sb.AppendLine("// <auto-generated/>");
        sb.AppendLine("#nullable enable");
        sb.AppendLine();
        sb.AppendLine("namespace ZeroAlloc.Serialisation.SystemTextJson;");
        sb.AppendLine();
        sb.AppendLine("internal sealed class ValueObjectJsonTypeInfoResolver : global::System.Text.Json.Serialization.Metadata.IJsonTypeInfoResolver");
        sb.AppendLine("{");
        sb.AppendLine("    public static ValueObjectJsonTypeInfoResolver Default { get; } = new();");
        sb.AppendLine();
        sb.AppendLine("    public global::System.Text.Json.Serialization.Metadata.JsonTypeInfo? GetTypeInfo(global::System.Type type, global::System.Text.Json.JsonSerializerOptions options)");
        sb.AppendLine("    {");
        foreach (var type in ReachableFromNamespace(valueObjects))
        {
            var typeFqn = BuildTypeFqn(type);
            var converterFqn = BuildConverterFqn(type);
            sb.AppendLine($"        if (type == typeof({typeFqn}))");
            sb.AppendLine($"            return global::System.Text.Json.Serialization.Metadata.JsonMetadataServices.CreateValueInfo<{typeFqn}>(options, new {converterFqn}());");
        }
        sb.AppendLine("        return null;");
        sb.AppendLine("    }");
        sb.AppendLine("}");
        return sb.ToString();
    }

    /// <summary>
    /// The value objects the per-assembly registrar and resolvers can list. They live at
    /// namespace level, so a type that is private or protected, or nested in such a type, cannot
    /// be named there; it is still served by the attribute on its partial declaration.
    /// </summary>
    private static System.Collections.Generic.IEnumerable<ValueObjectModel> ReachableFromNamespace(
        System.Collections.Generic.IReadOnlyList<ValueObjectModel> valueObjects) =>
        valueObjects.Where(static v => v.IsReachableFromNamespace);

    /// <summary>
    /// The <c>global::</c>-qualified name of <paramref name="memberName"/> declared where the
    /// value object is: in its namespace, or in its innermost containing type.
    /// </summary>
    private static string BuildSiblingFqn(ValueObjectModel type, string memberName)
    {
        var sb = new System.Text.StringBuilder("global::");
        if (!string.IsNullOrEmpty(type.Namespace)) sb.Append(type.Namespace).Append('.');
        foreach (var containing in type.ContainingTypes)
        {
            sb.Append(containing.Name).Append('.');
        }
        return sb.Append(memberName).ToString();
    }

    private static string BuildTypeFqn(ValueObjectModel type) => BuildSiblingFqn(type, type.TypeName);

    private static string BuildConverterFqn(ValueObjectModel type) =>
        BuildSiblingFqn(type, $"{type.TypeName}SystemTextJsonConverter");

    /// <summary>
    /// Emits an IMessagePackFormatter&lt;T&gt; for the value-object plus a partial declaration
    /// of the type carrying [MessagePackFormatter(typeof(...))] so MessagePack-CSharp's
    /// attribute-based resolver picks it up without explicit registration. A reference type's
    /// formatter writes nil for null and reads nil back as null.
    /// </summary>
    internal static string EmitMessagePackFormatter(ValueObjectModel model)
    {
        var typeName = model.TypeName;
        var ns = model.Namespace;
        var (readMethod, writeArgFormat) = MessagePackReadWriteForType(model);
        // WriteArgFormat now carries a full C# statement template (e.g. "writer.Write({0})"
        // or "MessagePackSerializer.Serialize<T>(ref writer, {0}, options)") — the {0}
        // placeholder is substituted with the property access. This keeps each switch
        // arm self-contained so the per-type write path (primitive write vs resolver
        // dispatch) lives in one place.
        var writeStatement = string.Format(System.Globalization.CultureInfo.InvariantCulture, writeArgFormat, $"value.{model.UnderlyingPropertyName}");
        var formatterName = $"{typeName}MessagePackFormatter";

        var nsOpen = string.IsNullOrEmpty(ns) ? "" : $"namespace {ns};\n\n";

        // MessagePack's own reference-type formatters implement IMessagePackFormatter<T?>,
        // since null is a value they write and read.
        var formatter = model.IsValueType
            ? $$"""
                {{SiblingAccessibility(model)}} sealed class {{formatterName}} : IMessagePackFormatter<{{typeName}}>
                {
                    public void Serialize(ref MessagePackWriter writer, {{typeName}} value, MessagePackSerializerOptions options)
                        => {{writeStatement}};

                    public {{typeName}} Deserialize(ref MessagePackReader reader, MessagePackSerializerOptions options)
                        => new {{typeName}}({{readMethod}});
                }

                """
            : $$"""
                {{SiblingAccessibility(model)}} sealed class {{formatterName}} : IMessagePackFormatter<{{typeName}}?>
                {
                    public void Serialize(ref MessagePackWriter writer, {{typeName}}? value, MessagePackSerializerOptions options)
                    {
                        if (value is null)
                        {
                            writer.WriteNil();
                            return;
                        }

                        {{writeStatement}};
                    }

                    public {{typeName}}? Deserialize(ref MessagePackReader reader, MessagePackSerializerOptions options)
                        => reader.TryReadNil() ? null : new {{typeName}}({{readMethod}});
                }

                """;

        var body = $$"""
            [MessagePackFormatter(typeof({{formatterName}}))]
            {{PartialDeclaration(model)}}
            {
            }

            {{formatter}}
            """;

        return $$"""
            // <auto-generated/>
            #nullable enable

            using MessagePack;
            using MessagePack.Formatters;

            {{nsOpen}}{{WrapInContainingTypes(model, body)}}
            """;
    }

    /// <summary>
    /// Emits a single per-assembly file containing both an internal
    /// IFormatterResolver listing every value-object's formatter AND a public
    /// extension method AddZeroAllocValueObjectFormatters that prepends the
    /// resolver to a MessagePackSerializerOptions chain. Required for
    /// MessagePack.SourceGenerator (AOT) consumers — the AOT-generated resolver
    /// doesn't have usable typeinfo for [ValueObject] types because Roslyn gens
    /// can't see the [MessagePackFormatter] attribute our per-type emitter adds
    /// via a partial-struct extension. Single file (unlike STJ's split) because
    /// MessagePack has only one registration shape — the resolver chain.
    /// </summary>
    internal static string EmitMessagePackResolver(
        System.Collections.Generic.IReadOnlyList<ValueObjectModel> valueObjects,
        string accessibility)
    {
        var sb = new System.Text.StringBuilder();
        sb.AppendLine("// <auto-generated/>");
        sb.AppendLine("#nullable enable");
        sb.AppendLine();
        sb.AppendLine("namespace ZeroAlloc.Serialisation.MessagePack;");
        sb.AppendLine();
        sb.AppendLine("/// <summary>");
        sb.AppendLine("/// Resolves IMessagePackFormatter&lt;T&gt; for every [ZeroAlloc.ValueObjects.ValueObject]");
        sb.AppendLine("/// partial struct in this assembly. Prepend via AddZeroAllocValueObjectFormatters.");
        sb.AppendLine("/// </summary>");
        sb.AppendLine("internal sealed class ValueObjectMessagePackResolver : global::MessagePack.IFormatterResolver");
        sb.AppendLine("{");
        sb.AppendLine("    public static ValueObjectMessagePackResolver Default { get; } = new();");
        sb.AppendLine();
        sb.AppendLine("    public global::MessagePack.Formatters.IMessagePackFormatter<T>? GetFormatter<T>()");
        sb.AppendLine("        => FormatterCache<T>.Formatter;");
        sb.AppendLine();
        sb.AppendLine("    private static class FormatterCache<T>");
        sb.AppendLine("    {");
        sb.AppendLine("        public static readonly global::MessagePack.Formatters.IMessagePackFormatter<T>? Formatter");
        sb.AppendLine("            = (global::MessagePack.Formatters.IMessagePackFormatter<T>?)GetFormatterUntyped(typeof(T));");
        sb.AppendLine("    }");
        sb.AppendLine();
        sb.AppendLine("    private static object? GetFormatterUntyped(global::System.Type type)");
        sb.AppendLine("    {");
        foreach (var type in ReachableFromNamespace(valueObjects))
        {
            var typeFqn = BuildTypeFqn(type);
            var formatterFqn = BuildFormatterFqn(type);
            sb.AppendLine($"        if (type == typeof({typeFqn}))");
            sb.AppendLine($"            return new {formatterFqn}();");
            // A reference type has no Nullable<T>; its formatter writes and reads nil itself.
            if (!type.IsValueType) continue;
            // Nullable<VO> members make MessagePack ask for a formatter of the nullable type.
            // Without this arm the lookup falls through to DynamicGenericResolver, which builds
            // NullableFormatter<VO> by reflection; NativeAOT never compiled that instantiation,
            // so it throws MissingMethodException. A closed, statically referenced wrapper does not.
            sb.AppendLine($"        if (type == typeof(global::System.Nullable<{typeFqn}>))");
            sb.AppendLine($"            return new global::MessagePack.Formatters.StaticNullableFormatter<{typeFqn}>(new {formatterFqn}());");
        }
        sb.AppendLine("        return null;");
        sb.AppendLine("    }");
        sb.AppendLine("}");
        sb.AppendLine();
        sb.AppendLine("/// <summary>");
        sb.AppendLine("/// Registers every [ZeroAlloc.ValueObjects.ValueObject] partial struct's");
        sb.AppendLine("/// generator-emitted IMessagePackFormatter by prepending ValueObjectMessagePackResolver");
        sb.AppendLine("/// to a MessagePackSerializerOptions resolver chain. Call this AFTER setting your");
        sb.AppendLine("/// primary resolver (e.g. GeneratedMessagePackResolver.Instance) — call order");
        sb.AppendLine("/// matters; this method assumes the caller's options.Resolver is the fallback.");
        sb.AppendLine("/// </summary>");
        sb.AppendLine($"{accessibility} static class ValueObjectMessagePackFormattersExtensions");
        sb.AppendLine("{");
        sb.AppendLine("    public static global::MessagePack.MessagePackSerializerOptions AddZeroAllocValueObjectFormatters(this global::MessagePack.MessagePackSerializerOptions options)");
        sb.AppendLine("    {");
        sb.AppendLine("        var composite = global::MessagePack.Resolvers.CompositeResolver.Create(");
        sb.AppendLine("            ValueObjectMessagePackResolver.Default,");
        sb.AppendLine("            options.Resolver);");
        sb.AppendLine("        return options.WithResolver(composite);");
        sb.AppendLine("    }");
        sb.AppendLine("}");
        return sb.ToString();
    }

    private static string BuildFormatterFqn(ValueObjectModel type) =>
        BuildSiblingFqn(type, $"{type.TypeName}MessagePackFormatter");

    /// <summary>
    /// Emits a MemoryPackFormatter&lt;T&gt; for the value-object plus a module
    /// initializer that registers it with <c>MemoryPackFormatterProvider</c>.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Deviation from the plan: the plan called for
    /// <c>[MemoryPackable(GenerateType.NoGenerate)] [MemoryPackCustomFormatter&lt;TFormatter, T&gt;]</c>
    /// on the partial struct. Reality (MemoryPack 1.21.x):
    /// <list type="bullet">
    /// <item><c>MemoryPackCustomFormatterAttribute&lt;TFormatter, T&gt;</c> is abstract
    ///   AND its <c>AttributeUsage</c> only targets properties + fields — it cannot be
    ///   applied to a type, and even if it could, the consumer would have to derive a
    ///   concrete subclass that overrides <c>GetFormatter()</c>.</item>
    /// <item>The canonical pattern for "I have a hand-rolled formatter for type T" is
    ///   <c>MemoryPackFormatterProvider.Register&lt;T&gt;(formatter)</c> at startup —
    ///   which we automate via a <c>[ModuleInitializer]</c>.</item>
    /// </list>
    /// No attribute is needed on the value-object struct itself; absence of
    /// <c>[MemoryPackable]</c> means MemoryPack's source generator generates nothing
    /// for the type, and the provider lookup at serialize-time finds our registered
    /// formatter instead.
    /// </para>
    /// </remarks>
    internal static string EmitMemoryPackFormatter(ValueObjectModel model)
    {
        var typeName = model.TypeName;
        var ns = model.Namespace;
        var underlyingFqn = model.UnderlyingTypeFullyQualifiedName;
        var formatterName = $"{typeName}MemoryPackFormatter";
        var registrarName = $"{typeName}MemoryPackFormatterRegistrar";

        var nsOpen = string.IsNullOrEmpty(ns) ? "" : $"namespace {ns};\n\n";

        // A struct keeps the bare underlying value on the wire. A reference type needs a
        // distinct encoding for null, so it uses MemoryPack's own object layout: the null
        // object header for null, otherwise a one-member object header and the value. That is
        // the layout MemoryPack itself writes for a [MemoryPackable] class with one member.
        var formatter = model.IsValueType
            ? $$"""
                {{SiblingAccessibility(model)}} sealed class {{formatterName}} : MemoryPackFormatter<{{typeName}}>
                {
                    public override void Serialize<TBufferWriter>(ref MemoryPackWriter<TBufferWriter> writer, scoped ref {{typeName}} value)
                        => writer.WriteValue<{{underlyingFqn}}>(value.{{model.UnderlyingPropertyName}});

                    public override void Deserialize(ref MemoryPackReader reader, scoped ref {{typeName}} value)
                        => value = new {{typeName}}(reader.ReadValue<{{underlyingFqn}}>()!);
                }

                """
            : $$"""
                {{SiblingAccessibility(model)}} sealed class {{formatterName}} : MemoryPackFormatter<{{typeName}}>
                {
                    public override void Serialize<TBufferWriter>(ref MemoryPackWriter<TBufferWriter> writer, scoped ref {{typeName}}? value)
                    {
                        if (value is null)
                        {
                            writer.WriteNullObjectHeader();
                            return;
                        }

                        writer.WriteObjectHeader(1);
                        writer.WriteValue<{{underlyingFqn}}>(value.{{model.UnderlyingPropertyName}});
                    }

                    public override void Deserialize(ref MemoryPackReader reader, scoped ref {{typeName}}? value)
                    {
                        if (!reader.TryReadObjectHeader(out var memberCount))
                        {
                            value = null;
                            return;
                        }

                        if (memberCount != 1)
                        {
                            MemoryPackSerializationException.ThrowInvalidPropertyCount(1, memberCount);
                        }

                        value = new {{typeName}}(reader.ReadValue<{{underlyingFqn}}>()!);
                    }
                }

                """;

        var body = $$"""
            internal static class {{registrarName}}
            {
                [ModuleInitializer]
                internal static void Register()
                {
                    if (!MemoryPackFormatterProvider.IsRegistered<{{typeName}}>())
                    {
                        MemoryPackFormatterProvider.Register<{{typeName}}>(new {{formatterName}}());
                    }
                }
            }

            {{formatter}}
            """;

        return $$"""
            // <auto-generated/>
            #nullable enable

            using System.Runtime.CompilerServices;
            using MemoryPack;

            {{nsOpen}}{{WrapInContainingTypes(model, body)}}
            """;
    }

    private static (string ReadCall, string WriteArgFormat) MessagePackReadWriteForType(ValueObjectModel model)
    {
        // WriteArgFormat is a full C# statement template — {0} is substituted with
        // the property access (e.g. "value.Value"). Primitives use the native
        // MessagePackWriter.Write(...) overloads; everything else routes through
        // MessagePackSerializer.Serialize<T>(ref writer, value, options) so the
        // active resolver's formatter (incl. adopter-registered custom formatters)
        // is honoured. Symmetric on the read side via Deserialize<T>.
        return model.UnderlyingSpecialType switch
        {
            SpecialType.System_Int32   => ("reader.ReadInt32()",   "writer.Write({0})"),
            SpecialType.System_Int64   => ("reader.ReadInt64()",   "writer.Write({0})"),
            SpecialType.System_Int16   => ("reader.ReadInt16()",   "writer.Write({0})"),
            SpecialType.System_Single  => ("reader.ReadSingle()",  "writer.Write({0})"),
            SpecialType.System_Double  => ("reader.ReadDouble()",  "writer.Write({0})"),
            SpecialType.System_Boolean => ("reader.ReadBoolean()", "writer.Write({0})"),
            SpecialType.System_String  => ("reader.ReadString()!", "writer.Write({0})"),
            SpecialType.System_Decimal
                => ("MessagePackSerializer.Deserialize<decimal>(ref reader, options)",
                    "MessagePackSerializer.Serialize<decimal>(ref writer, {0}, options)"),
            _ when string.Equals(model.UnderlyingTypeDisplayName, "System.Guid", StringComparison.Ordinal)
                => ("MessagePackSerializer.Deserialize<global::System.Guid>(ref reader, options)",
                    "MessagePackSerializer.Serialize<global::System.Guid>(ref writer, {0}, options)"),
            _ when string.Equals(model.UnderlyingTypeDisplayName, "System.DateTime", StringComparison.Ordinal)
                => ("MessagePackSerializer.Deserialize<global::System.DateTime>(ref reader, options)",
                    "MessagePackSerializer.Serialize<global::System.DateTime>(ref writer, {0}, options)"),
            _ when string.Equals(model.UnderlyingTypeDisplayName, "System.DateTimeOffset", StringComparison.Ordinal)
                => ("MessagePackSerializer.Deserialize<global::System.DateTimeOffset>(ref reader, options)",
                    "MessagePackSerializer.Serialize<global::System.DateTimeOffset>(ref writer, {0}, options)"),
            _ when string.Equals(model.UnderlyingTypeDisplayName, "System.TimeSpan", StringComparison.Ordinal)
                => ("MessagePackSerializer.Deserialize<global::System.TimeSpan>(ref reader, options)",
                    "MessagePackSerializer.Serialize<global::System.TimeSpan>(ref writer, {0}, options)"),
            _ when string.Equals(model.UnderlyingTypeDisplayName, "byte[]", StringComparison.Ordinal)
                => ("MessagePackSerializer.Deserialize<byte[]>(ref reader, options)",
                    "MessagePackSerializer.Serialize<byte[]>(ref writer, {0}, options)"),
            _ => ("reader.ReadString()!", "writer.Write({0})"),
        };
    }

    private static (string ReadCall, string WriteMethod) SystemTextJsonReadWriteForType(ValueObjectModel model)
    {
        return model.UnderlyingSpecialType switch
        {
            SpecialType.System_Int32   => ("reader.GetInt32()",   "WriteNumberValue"),
            SpecialType.System_Int64   => ("reader.GetInt64()",   "WriteNumberValue"),
            SpecialType.System_Int16   => ("reader.GetInt16()",   "WriteNumberValue"),
            SpecialType.System_Decimal => ("reader.GetDecimal()", "WriteNumberValue"),
            SpecialType.System_Double  => ("reader.GetDouble()",  "WriteNumberValue"),
            SpecialType.System_Single  => ("reader.GetSingle()",  "WriteNumberValue"),
            SpecialType.System_String  => ("reader.GetString()!", "WriteStringValue"),
            SpecialType.System_Boolean => ("reader.GetBoolean()", "WriteBooleanValue"),
            _ when string.Equals(model.UnderlyingTypeDisplayName, "System.Guid", StringComparison.Ordinal)
                => ("reader.GetGuid()", "WriteStringValue"),
            _ when string.Equals(model.UnderlyingTypeDisplayName, "System.DateTime", StringComparison.Ordinal)
                => ("reader.GetDateTime()", "WriteStringValue"),
            _ when string.Equals(model.UnderlyingTypeDisplayName, "System.DateTimeOffset", StringComparison.Ordinal)
                => ("reader.GetDateTimeOffset()", "WriteStringValue"),
            _ when string.Equals(model.UnderlyingTypeDisplayName, "System.TimeSpan", StringComparison.Ordinal)
                // Utf8JsonReader has no GetTimeSpan() pre-.NET 7; TimeSpan.Parse(reader.GetString()!)
                // is safe and round-trips ISO 8601-ish via value.ToString() on the write side
                // (handled in EmitSystemTextJsonConverter — there's no WriteStringValue(TimeSpan)
                // overload either, so we materialise the string explicitly).
                => ("global::System.TimeSpan.Parse(reader.GetString()!)", "WriteStringValue"),
            _ when string.Equals(model.UnderlyingTypeDisplayName, "byte[]", StringComparison.Ordinal)
                // Base64 string encoding matches STJ's default byte[] wire format.
                => ("reader.GetBytesFromBase64()", "WriteBase64StringValue"),
            _ => ("reader.GetString()!", "WriteStringValue"), // fallback — let the compiler complain if the user's underlying type doesn't accept this
        };
    }
}
