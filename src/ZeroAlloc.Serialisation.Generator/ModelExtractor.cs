using System;
using System.Collections.Immutable;
using System.Linq;
using System.Runtime.CompilerServices;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using ZeroAlloc.Serialisation.Generator.Models;

[assembly: InternalsVisibleTo("ZeroAlloc.Serialisation.Generator.Tests")]

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

        // Every ZASZ diagnostic is about the [ZeroAllocSerializable] application.
        var attrSyntax = attrData.ApplicationSyntaxReference?.GetSyntax(ct);
        var attrLocation = attrSyntax is not null
            ? LocationInfo.From(attrSyntax)
            : ctx.TargetNode is TypeDeclarationSyntax typeDecl
                ? LocationInfo.From(typeDecl.Identifier)
                : LocationInfo.From(ctx.TargetNode);

        // ZASZ001: open generic type
        if (typeSymbol.IsGenericType && typeSymbol.TypeParameters.Length > 0)
        {
            diagnostics.Add(new DiagnosticInfo(
                SerializerDiagnostics.OpenGeneric,
                attrLocation,
                new EquatableArray<string>(new[] { typeSymbol.ToDisplayString() })));
            return Result(null, attrLocation, diagnostics);
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
            return Result(null, attrLocation, diagnostics);
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
            FormatName: formatName,
            IsValueType: typeSymbol.IsValueType);

        return Result(model, attrLocation, diagnostics);
    }

    private static SerializerExtractionResult Result(
        SerializerModel? model, LocationInfo attrLocation, ImmutableArray<DiagnosticInfo>.Builder diagnostics)
        => new(model, attrLocation, new EquatableArray<DiagnosticInfo>(diagnostics.ToArray()));

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
    public static EquatableArray<StjContextEntry> ExtractContextEntries(
        GeneratorAttributeSyntaxContext ctx,
        CancellationToken ct)
    {
        if (ctx.TargetSymbol is not INamedTypeSymbol contextSymbol)
            return default;

        if (!DerivesFromJsonSerializerContext(contextSymbol))
            return default;

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
        return new EquatableArray<StjContextEntry>(builder.ToArray());
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

    private const string ValueObjectAttributeFqn = "ZeroAlloc.ValueObjects.ValueObjectAttribute";

    /// <summary>
    /// Inspects a <c>[ValueObject]</c> application. A transparent value object gets a model,
    /// unless it is generic, nested in a generic type or file-local, which ZASZ005 reports. One
    /// inside a private or protected containing type carries ZASZ006 for the MemoryPack backend.
    /// </summary>
    internal static ValueObjectExtractionResult? ExtractValueObject(
        GeneratorAttributeSyntaxContext ctx,
        CancellationToken ct)
    {
        if (ctx.TargetSymbol is not INamedTypeSymbol candidate) return null;

        var model = TryGetTransparentValueObject(candidate);
        if (model is null) return null;

        var displayName = candidate.ToDisplayString();

        var blocker = ValueObjectBlocker(candidate);
        if (blocker is not null)
        {
            var attrSyntax = ctx.Attributes.Length > 0
                ? ctx.Attributes[0].ApplicationSyntaxReference?.GetSyntax(ct)
                : null;
            var attrLocation = attrSyntax is not null
                ? LocationInfo.From(attrSyntax)
                : TypeIdentifierLocation(ctx.TargetNode);
            return new ValueObjectExtractionResult(
                Model: null,
                Diagnostics: new EquatableArray<DiagnosticInfo>(new[]
                {
                    new DiagnosticInfo(
                        SerializerDiagnostics.ValueObjectCannotBeGenerated,
                        attrLocation,
                        new EquatableArray<string>(new[] { displayName, blocker })),
                }),
                MemoryPackDiagnostic: null);
        }

        DiagnosticInfo? memoryPackDiagnostic = null;
        for (var containing = candidate.ContainingType; containing is not null; containing = containing.ContainingType)
        {
            var hidden = containing.DeclaredAccessibility switch
            {
                Accessibility.Private => "private",
                Accessibility.Protected => "protected",
                Accessibility.ProtectedAndInternal => "private protected",
                _ => null,
            };
            if (hidden is null) continue;

            memoryPackDiagnostic = new DiagnosticInfo(
                SerializerDiagnostics.ValueObjectMemoryPackNotRegistered,
                TypeIdentifierLocation(ctx.TargetNode),
                new EquatableArray<string>(new[] { displayName, containing.ToDisplayString(), hidden }));
            break;
        }

        return new ValueObjectExtractionResult(
            model,
            new EquatableArray<DiagnosticInfo>(Array.Empty<DiagnosticInfo>()),
            memoryPackDiagnostic);
    }

    /// <summary>
    /// Why no serializers can be generated for <paramref name="type"/>, completing
    /// "gets no generated serializers because ...", or null when they can.
    /// </summary>
    private static string? ValueObjectBlocker(INamedTypeSymbol type)
    {
        for (var current = type; current is not null; current = current.ContainingType)
        {
            if (current.IsFileLocal)
            {
                return SymbolEqualityComparer.Default.Equals(current, type)
                    ? "it is file-local, so a partial declaration in a generated file would be a different type"
                    : $"it is nested in file-local type '{current.ToDisplayString()}', so a partial declaration in a generated file would be a different type";
            }
        }

        for (var current = type; current is not null; current = current.ContainingType)
        {
            if (current.TypeParameters.Length > 0)
            {
                return SymbolEqualityComparer.Default.Equals(current, type)
                    ? "it is generic, and its converters could only be created by reflection, which is not NativeAOT-safe"
                    : $"it is nested in generic type '{current.ToDisplayString()}', and its converters could only be created by reflection, which is not NativeAOT-safe";
            }
        }

        return null;
    }

    private static LocationInfo TypeIdentifierLocation(SyntaxNode node) =>
        node is BaseTypeDeclarationSyntax typeDecl ? LocationInfo.From(typeDecl.Identifier) : LocationInfo.From(node);

    /// <summary>
    /// If <paramref name="candidate"/> is decorated with
    /// <c>[ZeroAlloc.ValueObjects.ValueObject]</c> (FQN match — no runtime
    /// reference to ZA.ValueObjects required) and declares exactly one public
    /// instance property, returns the equatable model the emitters work from.
    /// Returns null for everything else — multi-property value-objects, or
    /// types without the marker attribute.
    /// </summary>
    internal static ValueObjectModel? TryGetTransparentValueObject(INamedTypeSymbol candidate)
    {
        var hasMarker = candidate.GetAttributes()
            .Any(a => string.Equals(
                a.AttributeClass?.ToDisplayString(),
                ValueObjectAttributeFqn,
                StringComparison.Ordinal));
        if (!hasMarker) return null;

        var properties = candidate.GetMembers()
            .OfType<IPropertySymbol>()
            .Where(p => !p.IsStatic && p.DeclaredAccessibility == Accessibility.Public)
            .ToArray();

        return properties.Length == 1 ? ValueObjectModel.From(candidate, properties[0]) : null;
    }

    /// <summary>
    /// Joins a STJ extraction result with the flattened set of context entries in the compilation,
    /// binding the model to its matching <c>JsonSerializerContext.Default.&lt;Prop&gt;</c> or adding
    /// ZASZ004 when none is found. Non-STJ results pass through unchanged.
    /// </summary>
    public static SerializerExtractionResult JoinWithContextMap(
        SerializerExtractionResult raw,
        EquatableArray<StjContextEntry> allEntries)
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
                raw.AttributeLocation,
                new EquatableArray<string>(new[] { raw.Model.FullTypeName })));
            return raw with { Model = null, Diagnostics = diag };
        }

        var boundModel = raw.Model with
        {
            StjContext = new StjContextBinding(match.ContextFullName, match.PropertyName),
        };
        return raw with { Model = boundModel };
    }
}
