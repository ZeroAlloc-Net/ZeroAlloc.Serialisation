# JsonSerializerContext Interop Helper — Design

**Date:** 2026-05-27
**Scope:** ZeroAlloc.Serialisation 2.3.1 — emit a per-assembly public extension method that registers all generator-emitted `[ValueObject]` `JsonConverter<T>` instances into a `JsonSerializerOptions`. Closes the gap surfaced during the template-migration follow-up to 2.3.0.

## Background

2.3.0 ships transparent System.Text.Json converters for single-property `[ZeroAlloc.ValueObjects.ValueObjectAttribute]` partial structs. The generator emits two things per detected type:

1. An `internal sealed class XSystemTextJsonConverter : JsonConverter<X>` in the declaring assembly.
2. A `[JsonConverter(typeof(XSystemTextJsonConverter))]` attribute on the partial-struct extension.

The attribute path works for **reflection-based** STJ — `JsonSerializer.Serialize<T>(T)` consults the type's `[JsonConverter]` attribute via reflection at runtime and finds the generated converter.

It does **not** work for **source-generated** `JsonSerializerContext` consumers. STJ's `JsonSerializerContext` source generator runs in the same compilation pass as the ZA.Serialisation generator. Roslyn incremental generators do not see each other's output in the same pass — so when STJ's gen scans `CustomerDto.Id` (type `CustomerId`), it sees the user-written `public int Value { get; }` property only. It emits typeinfo that serializes `CustomerId` as an object (`{"value":42}`), not as the underlying primitive (`42`).

Surfaced 2026-05-27 in the [ZeroAlloc.Templates](https://github.com/ZeroAlloc-Net/ZeroAlloc.Templates) migration PR #127. Both templates (`za-clean`, `za-vertical-slice`) use `JsonSerializerContext` for AOT readiness. Integration tests failed with `Expected: Created, Actual: BadRequest` because POST bodies contained `{"customerId": 42}` which deserialized via the (correctly emitted) converter — but responses contained `{"id":{"value":42}}` because the response path went through `JsonContext.Default` typeinfo that didn't honor the attribute. PR #127 was closed; this work unblocks the re-author.

The 2.3.0 design implicitly assumed reflection-based STJ. Templates surface a use case the design didn't cover.

## Goal

A consumer using `JsonSerializerContext` can wire up transparent serialization for every `[ValueObject]` in their assembly with one call:

```csharp
builder.Services.ConfigureHttpJsonOptions(o =>
{
    o.SerializerOptions.TypeInfoResolverChain.Insert(0, JsonContext.Default);
    o.SerializerOptions.AddZeroAllocValueObjectConverters();  // NEW
});
```

The call site must not reference generator-emitted class names directly, must not require `InternalsVisibleTo`, and must work in AOT. Existing 2.3.0 reflection-based consumers see no behaviour change — the attribute-based emission stays.

## Decisions

### D-1: registration mechanism — `Converters.Add`

The helper adds entries to `JsonSerializerOptions.Converters`. STJ consults that list **before** any `JsonTypeInfo`-resolved converter, including ones emitted by `JsonSerializerContext`. So a converter added via the helper wins over the typeinfo's default object-shape serialization for the type.

**Considered and rejected:**

- **JsonTypeInfoModifier-chained resolver** (`JsonContext.Default.WithValueObjectConverters()`). More compositional, but harder for users to discover, and modifier semantics have shifted between STJ versions. `Converters.Add` is the well-worn pattern STJ users already know.
- **Make converter classes `public` + provide a convenience helper.** Locks in `XSystemTextJsonConverter` naming as a public API contract; future generator changes break consumers who took the documented escape hatch. Internal converters with a public extension method is the smaller surface.

### D-2: emission topology — one extension class per assembly

A single `ValueObjectJsonConvertersExtensions` class with one extension method (`AddZeroAllocValueObjectConverters`) lists every `[ValueObject]` in the assembly. Not per-type registration; not per-namespace.

**Why:** Consumers in a Domain or Common folder typically declare a handful of value-objects together. One method registers them all. Per-type registration would expand the call-site noise without benefit.

### D-3: location — `ZeroAlloc.Serialisation.SystemTextJson` namespace

The generated class lives in the `ZeroAlloc.Serialisation.SystemTextJson` namespace, even though it's emitted *into* the consuming compilation. Consumers already have `using ZeroAlloc.Serialisation.SystemTextJson;` in scope (they reference the package). The extension method shows up in IntelliSense as soon as they type `options.Add` — discoverable without doc-spelunking.

**Considered and rejected:**

- **Consumer's root namespace** (e.g. `MyApp.JsonConvertersExtensions`). Less discoverable; clutters the consumer's own namespace.
- **A fixed `ZeroAlloc.Serialisation.Generated` namespace.** Less obviously connected to the SystemTextJson backend.

### D-4: gating — STJ only for 2.3.1

MessagePack with AOT source-gen has the analogous gap in principle (Roslyn gens can't see attribute-added formatters). MemoryPack does not — it uses `[ModuleInitializer]` for self-registration, no consumer call needed.

MessagePack with the runtime composite resolver (what current ZA consumers use) doesn't hit the gap. So no current consumer needs a MessagePack helper. YAGNI — file as 2.3.x backlog, ship when a real consumer surfaces.

### D-5: SemVer + commit framing

`2.3.0 → 2.3.1` (patch). Conventional commit: `fix(generator): JsonSerializerContext-friendly converter registration helper`. The framing is "2.3.0 was incomplete for `JsonSerializerContext` users; this completes it." The new public extension method is small additive surface, not a feature in its own right.

## Design

### Emitter changes

`ValueObjectEmitter.cs` gains a new static method:

```csharp
internal static string EmitSystemTextJsonRegistrar(IReadOnlyList<(INamedTypeSymbol Type, IPropertySymbol UnderlyingProperty)> valueObjects)
{
    // Returns the source for a single per-assembly file:
    //
    //   // <auto-generated/>
    //   #nullable enable
    //
    //   using System.Text.Json;
    //
    //   namespace ZeroAlloc.Serialisation.SystemTextJson;
    //
    //   public static class ValueObjectJsonConvertersExtensions
    //   {
    //       public static JsonSerializerOptions AddZeroAllocValueObjectConverters(this JsonSerializerOptions options)
    //       {
    //           options.Converters.Add(new global::MyApp.Domain.CustomerIdSystemTextJsonConverter());
    //           options.Converters.Add(new global::MyApp.Domain.OrderIdSystemTextJsonConverter());
    //           return options;
    //       }
    //   }
    //
    // (Method body lists every value-object via its converter class's fully-qualified name.)
}
```

Each `Converters.Add(new global::Ns.XSystemTextJsonConverter())` references the converter via its FQN. The converter remains `internal sealed`, but the emitted extension class lives in the same assembly as the converters (the consumer's assembly), so it can `new` them without crossing visibility.

### Generator pipeline changes

`SerializerGenerator.Initialize` gains a second value-object pipeline:

```csharp
// Existing: per-type converter emission (2.3.0 — unchanged).
var valueObjectCandidates = context.SyntaxProvider
    .ForAttributeWithMetadataName("ZeroAlloc.ValueObjects.ValueObjectAttribute", ...)
    .Select(...);

var voPerType = valueObjectCandidates.Combine(context.CompilationProvider);
context.RegisterSourceOutput(voPerType, EmitPerTypeConverters);

// NEW: registrar emission. Batches all value-objects in the assembly.
var voCollected = valueObjectCandidates.Collect();
var voRegistrar = voCollected.Combine(context.CompilationProvider);

context.RegisterSourceOutput(voRegistrar, static (ctx, pair) =>
{
    var (allValueObjects, compilation) = pair;
    if (!ValueObjectEmitter.ReferencesSystemTextJson(compilation)) return;

    var detected = allValueObjects
        .Select(t => ModelExtractor.TryGetTransparentValueObject(t))
        .Where(d => d is not null)
        .Select(d => d!.Value)
        .ToArray();
    if (detected.Length == 0) return;

    var source = ValueObjectEmitter.EmitSystemTextJsonRegistrar(detected);
    ctx.AddSource("ValueObjectJsonConvertersExtensions.g.cs", source);
});
```

`ModelExtractor` is reused unchanged.

### Backwards compatibility

Strictly additive. Every `[ValueObject]` type in a 2.3.0-compiled assembly produces byte-identical converter + attribute output in 2.3.1. The NEW output is the registrar file. Consumers who don't call the new method see no behaviour change.

Existing reflection-based STJ users (no `JsonSerializerContext`) still work via the `[JsonConverter]` attribute path. Existing source-gen consumers — who previously hit the gap — now call the new method and get transparent serialization.

### Test surface

Four new tests in `ZeroAlloc.Serialisation.Generator.Tests` and `ZeroAlloc.Serialisation.Tests`:

| # | Project | Source / scenario | Expected |
|---|---|---|---|
| 1 | Generator.Tests (snapshot) | 2 single-property `[ValueObject]` structs + STJ backend reference | One `ValueObjectJsonConvertersExtensions.g.cs` file emitted; contains `public static JsonSerializerOptions AddZeroAllocValueObjectConverters` + 2 `Converters.Add(new ...)` lines with FQNs |
| 2 | Generator.Tests (regression) | No `[ValueObject]` types | No registrar file emitted (empty-class avoidance) |
| 3 | Generator.Tests (regression) | `[ValueObject]` present, no STJ backend reference | No registrar (matches per-type gating) |
| 4 | Serialisation.Tests (integration) | DTO with typed-ID field, `JsonSerializerContext` source-gen, `AddZeroAllocValueObjectConverters()` called | Round-trip produces bare `{"id":42}` on the wire, deserialize matches input |

Test #4 is the load-bearing one. Had it existed in the 2.3.0 suite, it would have caught the gap. Lands now as the regression net.

### Files touched

- **MOD:** `src/ZeroAlloc.Serialisation.Generator/ValueObjectEmitter.cs` — add `EmitSystemTextJsonRegistrar`.
- **MOD:** `src/ZeroAlloc.Serialisation.Generator/SerializerGenerator.cs` — add the registrar pipeline alongside the existing per-type pipeline.
- **NEW:** `tests/ZeroAlloc.Serialisation.Generator.Tests/ValueObjectRegistrarEmissionTests.cs` — tests 1-3.
- **NEW:** `tests/ZeroAlloc.Serialisation.Tests/ValueObjectJsonContextRoundTripTests.cs` — test 4.
- **MOD:** `docs/backlog.md` — file V2 (MessagePack/MemoryPack registrars, deferred) + V3 (Bebop backend, future direction). Strike V1.5 (this work) on release-PR merge.

Total commit footprint: ~250 LOC including tests.

## Out of scope

- **V2 — MessagePack registrar helper.** MessagePack-CSharp with the AOT source generator has the analogous gap (Roslyn-gens-can't-see-each-other), though no current consumer hits it. Same pattern: emit an extension method that registers each formatter into a `CompositeResolver`. Ships when a consumer surfaces. File V2 in backlog.
- **V3 — Bebop backend support.** Future direction recorded; not a 2.3.x item. The 2.3.0 per-backend-emission architecture (gating on `compilation.ReferencedAssemblyNames`) scales to a fourth backend as a copy-and-adapt of the MessagePack pattern.
- **A diagnostic when JsonContext is present without the helper called.** Tempting but noisy — the user might be intentionally using reflection-based STJ alongside an unrelated `JsonSerializerContext`. Skip until misuse is concrete.
