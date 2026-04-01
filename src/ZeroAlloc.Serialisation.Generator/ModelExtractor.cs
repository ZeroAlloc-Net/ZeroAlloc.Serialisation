using Microsoft.CodeAnalysis;
using ZeroAlloc.Serialisation.Generator.Models;

namespace ZeroAlloc.Serialisation.Generator;

internal static class ModelExtractor
{
    public static SerializerModel? Extract(
        GeneratorAttributeSyntaxContext ctx,
        CancellationToken ct)
    {
        if (ctx.TargetSymbol is not INamedTypeSymbol typeSymbol)
            return null;

        foreach (var attrData in typeSymbol.GetAttributes())
        {
            ct.ThrowIfCancellationRequested();
            if (attrData.AttributeClass?.ToDisplayString() !=
                "ZeroAlloc.Serialisation.ZeroAllocSerializableAttribute")
                continue;

            if (attrData.ConstructorArguments.Length != 1)
                return null;

            var formatValue = (int)attrData.ConstructorArguments[0].Value!;
            var formatName = formatValue switch
            {
                0 => "MemoryPack",
                1 => "MessagePack",
                2 => "SystemTextJson",
                _ => null,
            };

            if (formatName is null) return null;

            var ns = typeSymbol.ContainingNamespace.IsGlobalNamespace
                ? string.Empty
                : typeSymbol.ContainingNamespace.ToDisplayString();

            return new SerializerModel(
                Namespace: ns,
                TypeName: typeSymbol.Name,
                FullTypeName: typeSymbol.ToDisplayString(),
                FormatName: formatName);
        }

        return null;
    }
}
