using System.Collections.Immutable;
using System.Linq;
using Microsoft.CodeAnalysis;
using ZeroAlloc.Serialisation.Generator.Models;

namespace ZeroAlloc.Serialisation.Generator;

internal static class ModelExtractor
{
    private const string AttributeDisplayName =
        "ZeroAlloc.Serialisation.ZeroAllocSerializableAttribute";

    private const string MemoryPackableAttr = "MemoryPack.MemoryPackableAttribute";
    private const string MessagePackObjectAttr = "MessagePack.MessagePackObjectAttribute";

    public static SerializerExtractionResult? Extract(
        GeneratorAttributeSyntaxContext ctx,
        CancellationToken ct)
    {
        if (ctx.TargetSymbol is not INamedTypeSymbol typeSymbol)
            return null;

        AttributeData? attrData = null;
        foreach (var candidate in typeSymbol.GetAttributes())
        {
            ct.ThrowIfCancellationRequested();
            if (candidate.AttributeClass?.ToDisplayString() == AttributeDisplayName)
            {
                attrData = candidate;
                break;
            }
        }

        if (attrData is null) return null;
        if (attrData.ConstructorArguments.Length != 1) return null;

        var diagnostics = ImmutableArray.CreateBuilder<DiagnosticInfo>();
        var attrLocation = LocationInfo.From(attrData.ApplicationSyntaxReference);

        // ZASZ001: open generic type
        if (typeSymbol.IsGenericType && typeSymbol.TypeParameters.Length > 0)
        {
            diagnostics.Add(new DiagnosticInfo(
                SerializerDiagnostics.OpenGeneric,
                attrLocation ?? LocationInfo.From(typeSymbol.Locations.FirstOrDefault()),
                new EquatableArray<string>(new[] { typeSymbol.ToDisplayString() })));
            return new SerializerExtractionResult(null, diagnostics.ToImmutable());
        }

        var formatValueObj = attrData.ConstructorArguments[0].Value;
        if (formatValueObj is not int formatValue)
            return null;

        var formatName = formatValue switch
        {
            0 => "MemoryPack",
            1 => "MessagePack",
            2 => "SystemTextJson",
            _ => null,
        };

        // ZASZ002: unknown format value
        if (formatName is null)
        {
            diagnostics.Add(new DiagnosticInfo(
                SerializerDiagnostics.UnknownFormat,
                attrLocation,
                new EquatableArray<string>(new[] { formatValue.ToString(System.Globalization.CultureInfo.InvariantCulture) })));
            return new SerializerExtractionResult(null, diagnostics.ToImmutable());
        }

        // ZASZ003: missing per-format attribute (warning, does not block emission)
        var requiredAttr = formatName switch
        {
            "MemoryPack" => MemoryPackableAttr,
            "MessagePack" => MessagePackObjectAttr,
            _ => null,
        };

        if (requiredAttr is not null && !HasAttributeByName(typeSymbol, requiredAttr))
        {
            diagnostics.Add(new DiagnosticInfo(
                SerializerDiagnostics.MissingFormatAttribute,
                attrLocation,
                new EquatableArray<string>(new[] { formatName, requiredAttr })));
        }

        var ns = typeSymbol.ContainingNamespace.IsGlobalNamespace
            ? string.Empty
            : typeSymbol.ContainingNamespace.ToDisplayString();

        var model = new SerializerModel(
            Namespace: ns,
            TypeName: typeSymbol.Name,
            FullTypeName: typeSymbol.ToDisplayString(),
            FormatName: formatName);

        return new SerializerExtractionResult(model, diagnostics.ToImmutable());
    }

    private static bool HasAttributeByName(INamedTypeSymbol typeSymbol, string fullyQualifiedName)
    {
        foreach (var attr in typeSymbol.GetAttributes())
        {
            if (attr.AttributeClass?.ToDisplayString() == fullyQualifiedName)
                return true;
        }
        return false;
    }

    private const string JsonSerializableAttr = "System.Text.Json.Serialization.JsonSerializableAttribute";
    private const string JsonSerializerContextType = "System.Text.Json.Serialization.JsonSerializerContext";

    /// <summary>
    /// Scans a class carrying one or more <c>[JsonSerializable(typeof(T))]</c> attributes and,
    /// if it derives from <c>JsonSerializerContext</c>, emits one <see cref="StjContextEntry"/>
    /// per attribute application. The <see cref="StjContextEntry.PropertyName"/> matches STJ's
    /// source-generator naming: the attribute's <c>TypeInfoPropertyName</c> named argument if
    /// supplied, otherwise the target type's unqualified <c>Name</c>.
    /// </summary>
    public static ImmutableArray<StjContextEntry> ExtractContextEntries(
        GeneratorAttributeSyntaxContext ctx,
        CancellationToken ct)
    {
        if (ctx.TargetSymbol is not INamedTypeSymbol contextSymbol)
            return ImmutableArray<StjContextEntry>.Empty;

        if (!DerivesFromJsonSerializerContext(contextSymbol))
            return ImmutableArray<StjContextEntry>.Empty;

        var contextFullName = contextSymbol.ToDisplayString();
        var builder = ImmutableArray.CreateBuilder<StjContextEntry>();
        foreach (var attr in contextSymbol.GetAttributes())
        {
            ct.ThrowIfCancellationRequested();
            if (attr.AttributeClass?.ToDisplayString() != JsonSerializableAttr) continue;
            if (attr.ConstructorArguments.Length < 1) continue;
            if (attr.ConstructorArguments[0].Value is not INamedTypeSymbol targetType) continue;

            string? customName = null;
            foreach (var named in attr.NamedArguments)
            {
                if (named.Key == "TypeInfoPropertyName" && named.Value.Value is string s)
                {
                    customName = s;
                    break;
                }
            }

            var propName = customName ?? targetType.Name;
            builder.Add(new StjContextEntry(
                TargetFullName: targetType.ToDisplayString(),
                ContextFullName: contextFullName,
                PropertyName: propName));
        }
        return builder.ToImmutable();
    }

    private static bool DerivesFromJsonSerializerContext(INamedTypeSymbol typeSymbol)
    {
        for (var cur = typeSymbol.BaseType; cur is not null; cur = cur.BaseType)
        {
            if (cur.ToDisplayString() == JsonSerializerContextType)
                return true;
        }
        return false;
    }

    /// <summary>
    /// Joins a STJ extraction result with the flattened set of context entries in the compilation,
    /// binding the model to its matching <c>JsonSerializerContext.Default.&lt;Prop&gt;</c> or adding
    /// ZASZ004 when none is found. Non-STJ results pass through unchanged.
    /// </summary>
    public static SerializerExtractionResult JoinWithContextMap(
        SerializerExtractionResult raw,
        ImmutableArray<StjContextEntry> allEntries)
    {
        if (raw.Model is null) return raw;
        if (raw.Model.FormatName != "SystemTextJson") return raw;

        // Deterministic selection when multiple contexts register the same type:
        // pick the one whose ContextFullName sorts first (ordinal). Users should avoid
        // this but we shouldn't flap between builds when they don't.
        StjContextEntry? match = null;
        foreach (var entry in allEntries)
        {
            if (entry.TargetFullName != raw.Model.FullTypeName) continue;
            if (match is null || string.CompareOrdinal(entry.ContextFullName, match.ContextFullName) < 0)
            {
                match = entry;
            }
        }

        if (match is null)
        {
            var diag = raw.Diagnostics.Add(new DiagnosticInfo(
                SerializerDiagnostics.MissingJsonSerializerContext,
                Location: null,
                new EquatableArray<string>(new[] { raw.Model.FullTypeName })));
            return new SerializerExtractionResult(Model: null, Diagnostics: diag);
        }

        var boundModel = raw.Model with
        {
            StjContext = new StjContextBinding(match.ContextFullName, match.PropertyName),
        };
        return new SerializerExtractionResult(boundModel, raw.Diagnostics);
    }
}
