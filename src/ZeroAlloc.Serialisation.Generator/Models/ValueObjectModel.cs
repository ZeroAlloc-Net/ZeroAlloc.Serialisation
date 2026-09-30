using System.Collections.Generic;
using Microsoft.CodeAnalysis;

namespace ZeroAlloc.Serialisation.Generator.Models;

/// <summary>
/// A <c>[ValueObject]</c> type with exactly one public instance property, as the emitters need it.
/// </summary>
/// <remarks>
/// Only strings, bools, an enum and an equatable array of records made of strings, so the model
/// compares equal across compilations and the pipeline stays cached until the type itself
/// changes. A symbol never compares equal across compilations and would also keep the old
/// compilation alive.
/// </remarks>
/// <param name="Namespace">The containing namespace, or empty for the global namespace.</param>
/// <param name="ContainingTypes">
/// The types the value object is nested in, outermost first; empty for a top-level type.
/// </param>
/// <param name="TypeName">The type's simple name.</param>
/// <param name="Accessibility">The type's declared accessibility as C# modifiers, such as <c>public</c>.</param>
/// <param name="DeclarationKeyword">
/// The keyword a partial declaration of the type needs: <c>struct</c>, <c>record struct</c>,
/// <c>class</c> or <c>record</c>.
/// </param>
/// <param name="IsValueType">Whether the type is a struct.</param>
/// <param name="IsReadOnly">Whether the type is a readonly struct.</param>
/// <param name="IsReachableFromNamespace">
/// Whether code at namespace level of the same assembly can name the type as a closed type: the
/// type and every containing type are public, internal or protected internal, and no containing
/// type is generic. The per-assembly registrar and resolvers list only these.
/// </param>
/// <param name="UnderlyingPropertyName">The name of the single public instance property.</param>
/// <param name="UnderlyingSpecialType">The property type's <see cref="SpecialType"/>.</param>
/// <param name="UnderlyingTypeDisplayName">The property type in the default display format.</param>
/// <param name="UnderlyingTypeFullyQualifiedName">The property type with a <c>global::</c> prefix.</param>
internal sealed record ValueObjectModel(
    string Namespace,
    EquatableArray<ContainingTypeModel> ContainingTypes,
    string TypeName,
    string Accessibility,
    string DeclarationKeyword,
    bool IsValueType,
    bool IsReadOnly,
    bool IsReachableFromNamespace,
    string UnderlyingPropertyName,
    SpecialType UnderlyingSpecialType,
    string UnderlyingTypeDisplayName,
    string UnderlyingTypeFullyQualifiedName)
{
    /// <summary>Whether the type is nested in another type.</summary>
    public bool IsNested => !ContainingTypes.IsEmpty;

    /// <summary>
    /// The type's name qualified by its containing types, such as <c>Outer.Inner</c>, without
    /// the namespace.
    /// </summary>
    public string QualifiedTypeName
    {
        get
        {
            var sb = new System.Text.StringBuilder();
            foreach (var containing in ContainingTypes)
            {
                sb.Append(containing.Name).Append('.');
            }
            return sb.Append(TypeName).ToString();
        }
    }

    /// <summary>
    /// Builds the model from the value-object type and its single public instance property.
    /// </summary>
    public static ValueObjectModel From(INamedTypeSymbol type, IPropertySymbol underlying)
    {
        var containing = new List<ContainingTypeModel>();
        var reachableFromNamespace = IsAccessibleFromAssembly(type.DeclaredAccessibility);
        for (var current = type.ContainingType; current is not null; current = current.ContainingType)
        {
            containing.Insert(0, ContainingTypeModel.From(current));
            reachableFromNamespace &= IsAccessibleFromAssembly(current.DeclaredAccessibility)
                && current.TypeParameters.IsEmpty;
        }

        return new ValueObjectModel(
            Namespace: type.ContainingNamespace.IsGlobalNamespace ? "" : type.ContainingNamespace.ToDisplayString(),
            ContainingTypes: new EquatableArray<ContainingTypeModel>(containing.ToArray()),
            TypeName: type.Name,
            Accessibility: AccessibilityKeyword(type.DeclaredAccessibility),
            DeclarationKeyword: DeclarationKeywordOf(type),
            IsValueType: type.IsValueType,
            IsReadOnly: type.IsReadOnly,
            IsReachableFromNamespace: reachableFromNamespace,
            UnderlyingPropertyName: underlying.Name,
            UnderlyingSpecialType: underlying.Type.SpecialType,
            UnderlyingTypeDisplayName: underlying.Type.ToDisplayString(),
            UnderlyingTypeFullyQualifiedName: underlying.Type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat));
    }

    /// <summary>
    /// The keyword a partial declaration of <paramref name="type"/> must repeat. Every part of a
    /// partial type must agree on class, record class, struct, record struct or interface.
    /// </summary>
    internal static string DeclarationKeywordOf(INamedTypeSymbol type) => type switch
    {
        { TypeKind: TypeKind.Interface } => "interface",
        { IsValueType: true, IsRecord: true } => "record struct",
        { IsValueType: true } => "struct",
        { IsRecord: true } => "record",
        _ => "class",
    };

    internal static string AccessibilityKeyword(Accessibility accessibility) => accessibility switch
    {
        Microsoft.CodeAnalysis.Accessibility.Public => "public",
        Microsoft.CodeAnalysis.Accessibility.Internal => "internal",
        Microsoft.CodeAnalysis.Accessibility.Private => "private",
        Microsoft.CodeAnalysis.Accessibility.Protected => "protected",
        Microsoft.CodeAnalysis.Accessibility.ProtectedOrInternal => "protected internal",
        Microsoft.CodeAnalysis.Accessibility.ProtectedAndInternal => "private protected",
        _ => "internal",
    };

    private static bool IsAccessibleFromAssembly(Accessibility accessibility) =>
        accessibility is Microsoft.CodeAnalysis.Accessibility.Public
            or Microsoft.CodeAnalysis.Accessibility.Internal
            or Microsoft.CodeAnalysis.Accessibility.ProtectedOrInternal;
}

/// <summary>
/// A type that contains a nested <c>[ValueObject]</c>, as a partial declaration of it needs it.
/// </summary>
/// <param name="Accessibility">The declared accessibility as C# modifiers.</param>
/// <param name="Modifiers">
/// Modifiers every partial declaration must repeat, with a trailing space, such as
/// <c>readonly </c> or <c>ref </c>; empty when there are none.
/// </param>
/// <param name="DeclarationKeyword">
/// <c>class</c>, <c>record</c>, <c>struct</c>, <c>record struct</c> or <c>interface</c>.
/// </param>
/// <param name="Name">The type's simple name.</param>
/// <param name="TypeParameters">The type parameter list, such as <c>&lt;TKey, TValue&gt;</c>, or empty.</param>
internal sealed record ContainingTypeModel(
    string Accessibility,
    string Modifiers,
    string DeclarationKeyword,
    string Name,
    string TypeParameters)
{
    public static ContainingTypeModel From(INamedTypeSymbol type)
    {
        var modifiers = "";
        if (type.IsStatic) modifiers += "static ";
        if (type.IsValueType && type.IsReadOnly) modifiers += "readonly ";
        if (type.IsRefLikeType) modifiers += "ref ";

        var typeParameters = type.TypeParameters.IsEmpty
            ? ""
            : "<" + string.Join(", ", System.Linq.Enumerable.Select(type.TypeParameters, static p => p.Name)) + ">";

        return new ContainingTypeModel(
            Accessibility: ValueObjectModel.AccessibilityKeyword(type.DeclaredAccessibility),
            Modifiers: modifiers,
            DeclarationKeyword: ValueObjectModel.DeclarationKeywordOf(type),
            Name: type.Name,
            TypeParameters: typeParameters);
    }
}

/// <summary>
/// Which serializer backends the compilation references. Three bools, so the value compares equal
/// across edits and the <c>[ValueObject]</c> outputs do not rerun for each new compilation.
/// </summary>
internal sealed record ValueObjectBackends(bool SystemTextJson, bool MessagePack, bool MemoryPack);
