using System.Collections.Immutable;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace ZeroAlloc.Serialisation.Generator;

[Generator]
public sealed class SerializerGenerator : IIncrementalGenerator
{
    private const string AttributeFullName =
        "ZeroAlloc.Serialisation.ZeroAllocSerializableAttribute";

    private const string JsonSerializableAttrFullName =
        "System.Text.Json.Serialization.JsonSerializableAttribute";

    public void Initialize(IncrementalGeneratorInitializationContext context)
    {
        var rawResults = context.SyntaxProvider
            .ForAttributeWithMetadataName(
                AttributeFullName,
                predicate: static (node, _) =>
                    node is TypeDeclarationSyntax,
                transform: static (ctx, ct) => ModelExtractor.Extract(ctx, ct))
            .Where(static r => r is not null)
            .Select(static (r, _) => r!);

        // Separate pipeline: collect every [JsonSerializable(typeof(T))] on a
        // JsonSerializerContext-derived class. Flattened to a single array so
        // each raw result can look up its target type without quadratic scans.
        var flattenedContextEntries = context.SyntaxProvider
            .ForAttributeWithMetadataName(
                JsonSerializableAttrFullName,
                predicate: static (node, _) => node is ClassDeclarationSyntax,
                transform: static (ctx, ct) => ModelExtractor.ExtractContextEntries(ctx, ct))
            .Where(static entries => !entries.IsDefaultOrEmpty)
            .Collect()
            .Select(static (perClass, _) =>
            {
                var builder = ImmutableArray.CreateBuilder<Models.StjContextEntry>();
                foreach (var arr in perClass)
                {
                    builder.AddRange(arr);
                }
                return builder.ToImmutable();
            });

        var results = rawResults
            .Combine(flattenedContextEntries)
            .Select(static (pair, _) => ModelExtractor.JoinWithContextMap(pair.Left, pair.Right));

        // Report diagnostics (errors + warnings) for every extraction result.
        context.RegisterSourceOutput(results, static (ctx, result) =>
        {
            foreach (var info in result.Diagnostics)
            {
                ctx.ReportDiagnostic(info.ToDiagnostic());
            }
        });

        // Only emit code for results that produced a valid model (i.e. no blocking errors).
        var models = results
            .Where(static r => r.Model is not null)
            .Select(static (r, _) => r.Model!);

        // Emit one serializer + DI extension per annotated type
        context.RegisterSourceOutput(models, static (ctx, model) =>
        {
            SerializerEmitter.Emit(ctx, model);
            DiEmitter.Emit(ctx, model);
        });

        // Emit one dispatcher covering ALL annotated types in the assembly
        var allModels = models.Collect();
        context.RegisterSourceOutput(allModels, static (ctx, allModels) =>
        {
            DispatcherEmitter.Emit(ctx, allModels);
        });
    }
}
