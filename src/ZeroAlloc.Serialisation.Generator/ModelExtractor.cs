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
}
