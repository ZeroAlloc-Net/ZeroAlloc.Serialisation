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

        // V1: parallel discovery pass for [ZeroAlloc.ValueObjects.ValueObject]
        // partial structs. Emits transparent serializers for whichever
        // backend assemblies the consuming compilation references.
        var valueObjectCandidates = context.SyntaxProvider
            .ForAttributeWithMetadataName(
                "ZeroAlloc.ValueObjects.ValueObjectAttribute",
                predicate: static (node, _) =>
                    node is StructDeclarationSyntax || node is RecordDeclarationSyntax,
                transform: static (ctx, _) => (INamedTypeSymbol)ctx.TargetSymbol);

        var withCompilation = valueObjectCandidates.Combine(context.CompilationProvider);

        context.RegisterSourceOutput(withCompilation, static (sourceCtx, pair) =>
        {
            var (candidate, compilation) = pair;
            var detected = ModelExtractor.TryGetTransparentValueObject(candidate);
            if (detected is null) return;

            var (type, underlyingProperty) = detected.Value;

            if (ValueObjectEmitter.ReferencesSystemTextJson(compilation))
            {
                var stjSource = ValueObjectEmitter.EmitSystemTextJsonConverter(type, underlyingProperty);
                sourceCtx.AddSource($"{type.Name}SystemTextJsonConverter.g.cs", stjSource);
            }

            if (ValueObjectEmitter.ReferencesMessagePack(compilation))
            {
                var mpSource = ValueObjectEmitter.EmitMessagePackFormatter(type, underlyingProperty);
                sourceCtx.AddSource($"{type.Name}MessagePackFormatter.g.cs", mpSource);
            }

            if (ValueObjectEmitter.ReferencesMemoryPack(compilation))
            {
                var mpkSource = ValueObjectEmitter.EmitMemoryPackFormatter(type, underlyingProperty);
                sourceCtx.AddSource($"{type.Name}MemoryPackFormatter.g.cs", mpkSource);
            }
        });

        // 2.3.1: per-assembly registrar emission. Batches every [ValueObject]
        // candidate found in the compilation and emits a single
        // ValueObjectJsonConvertersExtensions class with one entry per type.
        // Required for JsonSerializerContext consumers — STJ's source generator
        // doesn't see the [JsonConverter] attribute the per-type pipeline emits,
        // so without an explicit Converters.Add call the context-driven typeinfo
        // wins and the value-object serializes as {"value":N} instead of bare N.
        var valueObjectsCollected = valueObjectCandidates.Collect();
        var registrarInput = valueObjectsCollected.Combine(context.CompilationProvider);

        context.RegisterSourceOutput(registrarInput, static (sourceCtx, pair) =>
        {
            var (allCandidates, compilation) = pair;

            var detected = new System.Collections.Generic.List<(INamedTypeSymbol Type, IPropertySymbol UnderlyingProperty)>(allCandidates.Length);
            foreach (var candidate in allCandidates)
            {
                var d = ModelExtractor.TryGetTransparentValueObject(candidate);
                if (d is not null) detected.Add(d.Value);
            }

            if (detected.Count == 0) return;

            if (ValueObjectEmitter.ReferencesSystemTextJson(compilation))
            {
                var source = ValueObjectEmitter.EmitSystemTextJsonRegistrar(detected);
                sourceCtx.AddSource("ValueObjectJsonConvertersExtensions.g.cs", source);

                // 2.3.2: alongside the registrar, emit an IJsonTypeInfoResolver
                // so JsonSerializerContext consumers can resolve value-object
                // typeinfo at startup (the registrar's Converters.Add only wins
                // at serialize/deserialize time — startup property configuration
                // hits the resolver chain directly).
                var resolverSource = ValueObjectEmitter.EmitSystemTextJsonResolver(detected);
                sourceCtx.AddSource("ValueObjectJsonTypeInfoResolver.g.cs", resolverSource);
            }

            // 2.3.3: MessagePack equivalent of the STJ resolver — closes the
            // MessagePack.SourceGenerator interop gap. Same shape, single-file.
            if (ValueObjectEmitter.ReferencesMessagePack(compilation))
            {
                var mpResolverSource = ValueObjectEmitter.EmitMessagePackResolver(detected);
                sourceCtx.AddSource("ValueObjectMessagePackResolverExtensions.g.cs", mpResolverSource);
            }
        });
    }
}
