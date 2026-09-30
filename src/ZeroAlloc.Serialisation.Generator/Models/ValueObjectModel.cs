using Microsoft.CodeAnalysis;

namespace ZeroAlloc.Serialisation.Generator.Models;

/// <summary>
/// A <c>[ValueObject]</c> type with exactly one public instance property, as the emitters need it.
/// </summary>
/// <remarks>
/// Only strings, bools and an enum, so the model compares equal across compilations and the
/// pipeline stays cached until the type itself changes. A symbol never compares equal across
/// compilations and would also keep the old compilation alive.
/// </remarks>
/// <param name="Namespace">The containing namespace, or empty for the global namespace.</param>
/// <param name="TypeName">The type's simple name.</param>
/// <param name="IsRecord">Whether the type is a record.</param>
/// <param name="IsReadOnly">Whether the type is a readonly struct.</param>
/// <param name="UnderlyingPropertyName">The name of the single public instance property.</param>
/// <param name="UnderlyingSpecialType">The property type's <see cref="SpecialType"/>.</param>
/// <param name="UnderlyingTypeDisplayName">The property type in the default display format.</param>
/// <param name="UnderlyingTypeFullyQualifiedName">The property type with a <c>global::</c> prefix.</param>
internal sealed record ValueObjectModel(
    string Namespace,
    string TypeName,
    bool IsRecord,
    bool IsReadOnly,
    string UnderlyingPropertyName,
    SpecialType UnderlyingSpecialType,
    string UnderlyingTypeDisplayName,
    string UnderlyingTypeFullyQualifiedName)
{
    /// <summary>
    /// Builds the model from the value-object type and its single public instance property.
    /// </summary>
    public static ValueObjectModel From(INamedTypeSymbol type, IPropertySymbol underlying) => new(
        Namespace: type.ContainingNamespace.IsGlobalNamespace ? "" : type.ContainingNamespace.ToDisplayString(),
        TypeName: type.Name,
        IsRecord: type.IsRecord,
        IsReadOnly: type.IsReadOnly,
        UnderlyingPropertyName: underlying.Name,
        UnderlyingSpecialType: underlying.Type.SpecialType,
        UnderlyingTypeDisplayName: underlying.Type.ToDisplayString(),
        UnderlyingTypeFullyQualifiedName: underlying.Type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat));
}

/// <summary>
/// Which serializer backends the compilation references. Three bools, so the value compares equal
/// across edits and the <c>[ValueObject]</c> outputs do not rerun for each new compilation.
/// </summary>
internal sealed record ValueObjectBackends(bool SystemTextJson, bool MessagePack, bool MemoryPack);
