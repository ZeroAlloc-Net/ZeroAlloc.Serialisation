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
            .Select(static (r, _) => r!)
            .WithTrackingName(TrackingNames.ExtractionResults);

        // Separate pipeline: collect every [JsonSerializable(typeof(T))] on a
        // JsonSerializerContext-derived class. Flattened to a single array so
        // each raw result can look up its target type without quadratic scans.
        var flattenedContextEntries = context.SyntaxProvider
            .ForAttributeWithMetadataName(
                JsonSerializableAttrFullName,
                predicate: static (node, _) => node is ClassDeclarationSyntax,
                transform: static (ctx, ct) => ModelExtractor.ExtractContextEntries(ctx, ct))
            .Where(static entries => !entries.IsEmpty)
            .Collect()
            .Select(static (perClass, _) =>
            {
                var builder = ImmutableArray.CreateBuilder<Models.StjContextEntry>();
                foreach (var arr in perClass)
                {
                    builder.AddRange(arr.ToArray());
                }
                return new Models.EquatableArray<Models.StjContextEntry>(builder.ToArray());
            })
            .WithTrackingName(TrackingNames.StjContextEntries);

        var results = rawResults
            .Combine(flattenedContextEntries)
            .Select(static (pair, _) => ModelExtractor.JoinWithContextMap(pair.Left, pair.Right))
            .WithTrackingName(TrackingNames.BoundResults);

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
            .Select(static (r, _) => r.Model!)
            .WithTrackingName(TrackingNames.Models);

        // Emit one serializer + DI extension per annotated type
        context.RegisterSourceOutput(models, static (ctx, model) =>
        {
            SerializerEmitter.Emit(ctx, model);
            DiEmitter.Emit(ctx, model);
        });

        // Emit one dispatcher covering ALL annotated types in the assembly
        var allModels = models.Collect().WithTrackingName(TrackingNames.AllModels);
        context.RegisterSourceOutput(allModels, static (ctx, all) =>
        {
            DispatcherEmitter.Emit(ctx, all);
        });

        // V1: parallel discovery pass for [ZeroAlloc.ValueObjects.ValueObject]
        // types, structs and classes. Emits transparent serializers for whichever
        // backend assemblies the consuming compilation references.
        //
        // Every step carries value-only data: a ValueObjectModel per type and three backend
        // flags. Symbols or the Compilation would compare unequal on every edit and rerun
        // every output below, and would keep old compilations alive.
        var valueObjectModels = context.SyntaxProvider
            .ForAttributeWithMetadataName(
                "ZeroAlloc.ValueObjects.ValueObjectAttribute",
                // ZeroAlloc.ValueObjects allows the attribute on classes and structs, records
                // included; the emitters repeat whichever declaration kind the type has.
                predicate: static (node, _) =>
                    node is StructDeclarationSyntax || node is ClassDeclarationSyntax || node is RecordDeclarationSyntax,
                transform: static (ctx, _) => ctx.TargetSymbol is INamedTypeSymbol candidate
                    ? ModelExtractor.TryGetTransparentValueObject(candidate)
                    : null)
            .Where(static m => m is not null)
            .Select(static (m, _) => m!)
            .WithTrackingName(TrackingNames.ValueObjectModels);

        // Reruns on every edit, but produces three bools that compare equal until the
        // references change, so nothing downstream reruns.
        var backends = context.CompilationProvider
            .Select(static (compilation, _) => ValueObjectEmitter.DetectBackends(compilation))
            .WithTrackingName(TrackingNames.ValueObjectBackends);

        var perTypeInput = valueObjectModels
            .Combine(backends)
            .WithTrackingName(TrackingNames.ValueObjectInput);

        context.RegisterSourceOutput(perTypeInput, static (sourceCtx, pair) =>
        {
            var (model, backendFlags) = pair;

            if (backendFlags.SystemTextJson)
            {
                var stjSource = ValueObjectEmitter.EmitSystemTextJsonConverter(model);
                sourceCtx.AddSource(ValueObjectEmitter.HintName(model, "SystemTextJsonConverter"), stjSource);
            }

            if (backendFlags.MessagePack)
            {
                var mpSource = ValueObjectEmitter.EmitMessagePackFormatter(model);
                sourceCtx.AddSource(ValueObjectEmitter.HintName(model, "MessagePackFormatter"), mpSource);
            }

            if (backendFlags.MemoryPack)
            {
                var mpkSource = ValueObjectEmitter.EmitMemoryPackFormatter(model);
                sourceCtx.AddSource(ValueObjectEmitter.HintName(model, "MemoryPackFormatter"), mpkSource);
            }
        });

        // 2.3.1: per-assembly registrar emission. Batches every [ValueObject]
        // candidate found in the compilation and emits a single
        // ValueObjectJsonConvertersExtensions class with one entry per type.
        // Required for JsonSerializerContext consumers — STJ's source generator
        // doesn't see the [JsonConverter] attribute the per-type pipeline emits,
        // so without an explicit Converters.Add call the context-driven typeinfo
        // wins and the value-object serializes as {"value":N} instead of bare N.
        var allValueObjectModels = valueObjectModels
            .Collect()
            .Select(static (all, _) => new Models.EquatableArray<Models.ValueObjectModel>(all.ToArray()))
            .WithTrackingName(TrackingNames.AllValueObjectModels);

        var registrarInput = allValueObjectModels
            .Combine(backends)
            .WithTrackingName(TrackingNames.ValueObjectRegistrarInput);

        context.RegisterSourceOutput(registrarInput, static (sourceCtx, pair) =>
        {
            var (all, backendFlags) = pair;
            if (all.IsEmpty) return;

            var detected = all.ToArray();

            if (backendFlags.SystemTextJson)
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
            if (backendFlags.MessagePack)
            {
                var mpResolverSource = ValueObjectEmitter.EmitMessagePackResolver(detected);
                sourceCtx.AddSource("ValueObjectMessagePackResolverExtensions.g.cs", mpResolverSource);
            }
        });
    }
}
