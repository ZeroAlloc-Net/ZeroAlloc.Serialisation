using System;
using System.Collections.Generic;
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

    /// <summary>
    /// The namespace-qualified name that identifies a type: in diagnostics, when looking up its
    /// <c>JsonSerializerContext</c> entry, and when finding duplicate declarations. Nullable value
    /// types are spelled out, so the name never contains <c>?</c>, and nullable reference annotations
    /// are left out, so <c>Envelope&lt;Order?&gt;</c> and <c>Envelope&lt;Order&gt;</c> are one type.
    /// </summary>
    internal static readonly SymbolDisplayFormat FullNameFormat = new(
        globalNamespaceStyle: SymbolDisplayGlobalNamespaceStyle.Omitted,
        typeQualificationStyle: SymbolDisplayTypeQualificationStyle.NameAndContainingTypesAndNamespaces,
        genericsOptions: SymbolDisplayGenericsOptions.IncludeTypeParameters,
        miscellaneousOptions: SymbolDisplayMiscellaneousOptions.UseSpecialTypes
            | SymbolDisplayMiscellaneousOptions.ExpandNullable);

    /// <summary>
    /// Inspects a class or struct carrying <c>[ZeroAllocSerializable]</c>. The first
    /// <c>[ZeroAllocSerializable(format)]</c> across all parts of the type generates; this callback
    /// emits it only when that application is on its own declaration, so a partial type is generated
    /// once. Every other application on this declaration gets ZASZ009 or ZASZ010.
    /// </summary>
    public static SerializerExtractionResult? Extract(
        GeneratorAttributeSyntaxContext ctx,
        CancellationToken ct)
    {
        if (ctx.TargetSymbol is not INamedTypeSymbol typeSymbol)
            return null;

        AttributeData? primary = null;
        foreach (var candidate in typeSymbol.GetAttributes())
        {
            ct.ThrowIfCancellationRequested();
            if (candidate.AttributeClass?.ToDisplayString() == AttributeDisplayName
                && FormOf(candidate) == DeclarationForm.Type)
            {
                primary = candidate;
                break;
            }
        }

        var diagnostics = ImmutableArray.CreateBuilder<DiagnosticInfo>();
        LocationInfo? primaryLocation = null;
        foreach (var attr in ctx.Attributes)
        {
            ct.ThrowIfCancellationRequested();
            var location = AttributeLocation(attr, ctx.TargetNode, ct);
            switch (FormOf(attr))
            {
                case DeclarationForm.Type when primary is not null && IsSameApplication(attr, primary):
                    primaryLocation = location;
                    break;
                case DeclarationForm.Type:
                    diagnostics.Add(new DiagnosticInfo(
                        SerializerDiagnostics.DuplicateDeclaration,
                        location,
                        new EquatableArray<string>(new[] { typeSymbol.ToDisplayString(FullNameFormat) })));
                    break;
                case DeclarationForm.Assembly:
                    diagnostics.Add(new DiagnosticInfo(
                        SerializerDiagnostics.FormDoesNotFitTarget,
                        location,
                        new EquatableArray<string>(new[]
                        {
                            "[ZeroAllocSerializable(typeof(...), format)] is only valid on the assembly; "
                            + $"apply [ZeroAllocSerializable(format)] to declare '{typeSymbol.ToDisplayString(FullNameFormat)}' serializable",
                        })));
                    break;
            }
        }

        if (primaryLocation is null)
        {
            // The generating application is on another part of this type, or there is none.
            return diagnostics.Count == 0 ? null : Result(null, diagnostics[0].Location, diagnostics);
        }

        // ZASZ001: open generic type
        if (typeSymbol.IsGenericType && typeSymbol.TypeParameters.Length > 0)
        {
            diagnostics.Add(new DiagnosticInfo(
                SerializerDiagnostics.OpenGeneric,
                primaryLocation,
                new EquatableArray<string>(new[] { typeSymbol.ToDisplayString() })));
            return Result(null, primaryLocation, diagnostics);
        }

        var ns = typeSymbol.ContainingNamespace.IsGlobalNamespace
            ? string.Empty
            : typeSymbol.ContainingNamespace.ToDisplayString();

        var model = BuildModel(typeSymbol, primary!.ConstructorArguments[0], ns, typeSymbol.Name, primaryLocation, diagnostics);
        return Result(model, primaryLocation, diagnostics);
    }

    /// <summary>
    /// Inspects the <c>[assembly: ZeroAllocSerializable]</c> applications in one compilation unit.
    /// Duplicates and generated-name collisions are only visible across the whole compilation, so
    /// <see cref="ResolveAssemblyDeclarations"/> settles them afterwards.
    /// </summary>
    public static EquatableArray<AssemblyDeclaration> ExtractAssemblyDeclarations(
        GeneratorAttributeSyntaxContext ctx,
        CancellationToken ct)
    {
        var declarations = ImmutableArray.CreateBuilder<AssemblyDeclaration>();
        foreach (var attr in ctx.Attributes)
        {
            ct.ThrowIfCancellationRequested();
            var declaration = ExtractAssemblyDeclaration(attr, ctx.TargetNode, ct);
            if (declaration is not null)
                declarations.Add(declaration);
        }
        return new EquatableArray<AssemblyDeclaration>(declarations.ToArray());
    }

    private static AssemblyDeclaration? ExtractAssemblyDeclaration(
        AttributeData attr, SyntaxNode targetNode, CancellationToken ct)
    {
        var location = AttributeLocation(attr, targetNode, ct);
        var diagnostics = ImmutableArray.CreateBuilder<DiagnosticInfo>();

        switch (FormOf(attr))
        {
            case DeclarationForm.Type:
                diagnostics.Add(new DiagnosticInfo(
                    SerializerDiagnostics.FormDoesNotFitTarget,
                    location,
                    new EquatableArray<string>(new[]
                    {
                        "[assembly: ZeroAllocSerializable(format)] names no type; declare a closed generic type with "
                        + "[assembly: ZeroAllocSerializable(typeof(Envelope<Order>), format)], or apply "
                        + "[ZeroAllocSerializable(format)] to a class or struct",
                    })));
                return new AssemblyDeclaration(Result(null, location, diagnostics), DeclaredType: null);
            case DeclarationForm.Assembly:
                break;
            default:
                return null;
        }

        if (attr.ConstructorArguments[0].Value is not ITypeSymbol declared)
            return null;

        // Checked before error types: the type arguments of typeof(Envelope<>) are error types.
        if (declared is INamedTypeSymbol unbound && IsUnboundGeneric(unbound))
        {
            diagnostics.Add(new DiagnosticInfo(
                SerializerDiagnostics.AssemblyFormOpenGeneric,
                location,
                new EquatableArray<string>(new[] { declared.ToDisplayString(FullNameFormat) })));
            return new AssemblyDeclaration(Result(null, location, diagnostics), DeclaredType: null);
        }

        // An unresolved typeof is already a compiler error; there is nothing to generate.
        if (ContainsErrorType(declared))
            return null;

        if (declared is not INamedTypeSymbol closed || TypeArgumentsInScope(closed).Count == 0)
        {
            diagnostics.Add(new DiagnosticInfo(
                SerializerDiagnostics.AssemblyFormNonGeneric,
                location,
                new EquatableArray<string>(new[]
                {
                    declared.ToDisplayString(FullNameFormat),
                    FormatArgumentText(attr.ConstructorArguments[1]),
                })));
            return new AssemblyDeclaration(Result(null, location, diagnostics), DeclaredType: null);
        }

        var definition = closed.OriginalDefinition;
        var ns = definition.ContainingNamespace.IsGlobalNamespace
            ? string.Empty
            : definition.ContainingNamespace.ToDisplayString();

        var model = BuildModel(closed, attr.ConstructorArguments[1], ns, GeneratedIdentifier(closed), location, diagnostics);
        return new AssemblyDeclaration(
            Result(model, location, diagnostics),
            DeclaredType: closed.ToDisplayString(FullNameFormat));
    }

    /// <summary>
    /// Settles the assembly-level declarations of the whole compilation, in source order: a
    /// repeated declaration of a type gets ZASZ009 and generates nothing, and a closed generic type
    /// whose generated names another serializable type already has gets ZASZ011.
    /// </summary>
    public static EquatableArray<SerializerExtractionResult> ResolveAssemblyDeclarations(
        ImmutableArray<EquatableArray<AssemblyDeclaration>> perFile,
        ImmutableArray<GeneratedName> typeLevelNames)
    {
        var declarations = perFile
            .SelectMany(static file => file.ToArray())
            .OrderBy(static d => d.Result.AttributeLocation.Tree.FilePath, StringComparer.Ordinal)
            .ThenBy(static d => d.Result.AttributeLocation.Span.Start)
            .ToArray();

        var declared = new HashSet<string>(StringComparer.Ordinal);
        var names = new Dictionary<(string Namespace, string TypeName), string>();
        foreach (var name in typeLevelNames)
        {
            names[(name.Namespace, name.TypeName)] = name.FullTypeName;
        }

        var results = ImmutableArray.CreateBuilder<SerializerExtractionResult>(declarations.Length);
        foreach (var declaration in declarations)
        {
            var result = declaration.Result;
            if (declaration.DeclaredType is null)
            {
                results.Add(result);
                continue;
            }

            if (!declared.Add(declaration.DeclaredType))
            {
                results.Add(new SerializerExtractionResult(
                    null,
                    result.AttributeLocation,
                    new EquatableArray<DiagnosticInfo>(new[]
                    {
                        new DiagnosticInfo(
                            SerializerDiagnostics.DuplicateDeclaration,
                            result.AttributeLocation,
                            new EquatableArray<string>(new[] { declaration.DeclaredType })),
                    })));
                continue;
            }

            if (result.Model is { } model)
            {
                var key = (model.Namespace, model.TypeName);
                if (names.TryGetValue(key, out var owner)
                    && !string.Equals(owner, model.FullTypeName, StringComparison.Ordinal))
                {
                    result = result with
                    {
                        Model = null,
                        Diagnostics = result.Diagnostics.Add(new DiagnosticInfo(
                            SerializerDiagnostics.GeneratedNameCollision,
                            result.AttributeLocation,
                            new EquatableArray<string>(new[]
                            {
                                model.FullTypeName,
                                model.TypeName,
                                owner,
                                model.Namespace.Length == 0 ? "<global namespace>" : model.Namespace,
                            }))),
                    };
                }
                else
                {
                    names[key] = model.FullTypeName;
                }
            }

            results.Add(result);
        }

        return new EquatableArray<SerializerExtractionResult>(results.ToArray());
    }

    /// <summary>
    /// Builds the model of a closed <paramref name="typeSymbol"/> declared with the format in
    /// <paramref name="formatArgument"/>. Reports ZASZ002 for an unknown format, and returns null,
    /// and ZASZ003 when the format's own attribute is missing.
    /// </summary>
    private static SerializerModel? BuildModel(
        INamedTypeSymbol typeSymbol,
        TypedConstant formatArgument,
        string ns,
        string typeName,
        LocationInfo attrLocation,
        ImmutableArray<DiagnosticInfo>.Builder diagnostics)
    {
        if (formatArgument.Value is not int formatValue)
            return null;

        var formatName = FormatName(formatValue);

        // ZASZ002: unknown format value
        if (formatName is null)
        {
            diagnostics.Add(new DiagnosticInfo(
                SerializerDiagnostics.UnknownFormat,
                attrLocation,
                new EquatableArray<string>(new[] { formatValue.ToString(System.Globalization.CultureInfo.InvariantCulture) })));
            return null;
        }

        // ZASZ003: missing per-format attribute (warning, does not block emission). A constructed
        // generic type carries the attributes of its generic definition.
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

        var fullTypeName = typeSymbol.ToDisplayString(FullNameFormat);
        return new SerializerModel(
            Namespace: ns,
            TypeName: typeName,
            FullTypeName: fullTypeName,
            TypeRef: typeSymbol.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat),
            HintName: HintNameOf(fullTypeName),
            FormatName: formatName,
            IsValueType: typeSymbol.IsValueType);
    }

    private static string? FormatName(int formatValue) => formatValue switch
    {
        0 => "MemoryPack",
        1 => "MessagePack",
        2 => "SystemTextJson",
        _ => null,
    };

    /// <summary>How a format argument reads in source, for a diagnostic that suggests another form.</summary>
    private static string FormatArgumentText(TypedConstant formatArgument) =>
        formatArgument.Value is int value && FormatName(value) is { } name
            ? "SerializationFormat." + name
            : "format";

    /// <summary>
    /// The file name stem of a type's generated sources: its namespace-qualified name, which keeps
    /// same-named types in different namespaces apart, with the angle brackets a hint name may not
    /// contain replaced by braces, which no type name contains. A repeated hint name makes Roslyn
    /// throw and drop the generator's whole output.
    /// </summary>
    internal static string HintNameOf(string fullTypeName) =>
        fullTypeName.Replace('<', '{').Replace('>', '}').Replace(" ", string.Empty);

    /// <summary>
    /// The identifier a closed generic type's generated names are built from: its name, then
    /// <c>Of</c> and its type arguments joined by <c>And</c>, each built the same way. So
    /// <c>Envelope&lt;Order&gt;</c> gives <c>EnvelopeOfOrder</c> and <c>Pair&lt;int, Order&gt;</c>
    /// gives <c>PairOfInt32AndOrder</c>. Type arguments of containing types come first.
    /// </summary>
    internal static string GeneratedIdentifier(ITypeSymbol type)
    {
        switch (type)
        {
            case IArrayTypeSymbol array:
                return GeneratedIdentifier(array.ElementType) + (array.Rank == 1 ? "Array" : $"Array{array.Rank}D");
            case INamedTypeSymbol named:
                var arguments = TypeArgumentsInScope(named);
                return arguments.Count == 0
                    ? named.Name
                    : named.Name + "Of" + string.Join("And", arguments.Select(GeneratedIdentifier));
            default:
                return type.Name;
        }
    }

    /// <summary>The type arguments of <paramref name="type"/> and its containing types, outermost first.</summary>
    private static List<ITypeSymbol> TypeArgumentsInScope(INamedTypeSymbol type)
    {
        var arguments = new List<ITypeSymbol>();
        for (var current = type; current is not null; current = current.ContainingType)
        {
            arguments.InsertRange(0, current.TypeArguments);
        }
        return arguments;
    }

    private static bool IsUnboundGeneric(INamedTypeSymbol type)
    {
        for (var current = type; current is not null; current = current.ContainingType)
        {
            if (current.IsUnboundGenericType)
                return true;
        }
        return false;
    }

    private static bool ContainsErrorType(ITypeSymbol type) => type switch
    {
        { TypeKind: TypeKind.Error } => true,
        IArrayTypeSymbol array => ContainsErrorType(array.ElementType),
        INamedTypeSymbol named => TypeArgumentsInScope(named).Exists(ContainsErrorType),
        _ => false,
    };

    private enum DeclarationForm
    {
        Unknown,

        /// <summary><c>[ZeroAllocSerializable(format)]</c>, for a class or struct declaration.</summary>
        Type,

        /// <summary><c>[assembly: ZeroAllocSerializable(typeof(...), format)]</c>, for a closed generic type.</summary>
        Assembly,
    }

    /// <summary>
    /// Which constructor an application uses. An application with errors in its arguments binds no
    /// constructor; the compiler reports it, and the generator leaves it alone.
    /// </summary>
    private static DeclarationForm FormOf(AttributeData attr) => attr.ConstructorArguments.Length switch
    {
        1 => DeclarationForm.Type,
        2 => DeclarationForm.Assembly,
        _ => DeclarationForm.Unknown,
    };

    private static bool IsSameApplication(AttributeData a, AttributeData b)
    {
        if (ReferenceEquals(a, b)) return true;
        var left = a.ApplicationSyntaxReference;
        var right = b.ApplicationSyntaxReference;
        return left is not null && right is not null
            && left.SyntaxTree == right.SyntaxTree
            && left.Span == right.Span;
    }

    /// <summary>Every ZASZ diagnostic about a declaration is reported on its attribute.</summary>
    private static LocationInfo AttributeLocation(AttributeData attr, SyntaxNode targetNode, CancellationToken ct)
    {
        var attrSyntax = attr.ApplicationSyntaxReference?.GetSyntax(ct);
        return attrSyntax is not null
            ? LocationInfo.From(attrSyntax)
            : targetNode is TypeDeclarationSyntax typeDecl
                ? LocationInfo.From(typeDecl.Identifier)
                : LocationInfo.From(targetNode);
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

            var propName = customName ?? StjTypeInfoPropertyName(targetType);
            builder.Add(new StjContextEntry(
                TargetFullName: targetType.ToDisplayString(FullNameFormat),
                ContextFullName: contextFullName,
                PropertyName: propName));
        }
        return new EquatableArray<StjContextEntry>(builder.ToArray());
    }

    /// <summary>
    /// The name System.Text.Json's source generator gives the <c>JsonTypeInfo</c> property of
    /// <paramref name="type"/> when no <c>TypeInfoPropertyName</c> is set: the type's name followed by
    /// the names of its type arguments, containing types' first, so <c>Envelope&lt;Order&gt;</c> is
    /// <c>EnvelopeOrder</c> and <c>Pair&lt;int, Order&gt;</c> is <c>PairInt32Order</c>. An array
    /// appends <c>Array</c>, or <c>Array2D</c> and up for a multi-dimensional one.
    /// </summary>
    internal static string StjTypeInfoPropertyName(ITypeSymbol type)
    {
        if (type is IArrayTypeSymbol array)
        {
            return StjTypeInfoPropertyName(array.ElementType)
                + (array.Rank == 1 ? "Array" : $"Array{array.Rank}D");
        }

        if (type is not INamedTypeSymbol named || !named.IsGenericType)
            return type.Name;

        return named.Name + string.Concat(TypeArgumentsInScope(named).Select(StjTypeInfoPropertyName));
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
