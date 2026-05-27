# JsonTypeInfoResolver for AOT/JsonContext Interop — Design

**Date:** 2026-05-27
**Scope:** ZeroAlloc.Serialisation 2.3.2 — emit a per-assembly `IJsonTypeInfoResolver` that returns pre-configured typeinfo for every `[ValueObject]` type, satisfying `JsonSerializerContext` startup property configuration under both JIT and AOT. Closes the second-order gap surfaced by the [ZeroAlloc.Templates PR #128](https://github.com/ZeroAlloc-Net/ZeroAlloc.Templates/pull/128) za-clean AOT smoke.

## Background

2.3.0 emitted a `[JsonConverter(typeof(...))]` attribute on a `[ValueObject]` partial-struct extension. Worked for reflection-based STJ but invisible to `JsonSerializerContext` source-gen (Roslyn incremental generators don't see each other's output in the same compilation pass).

2.3.1 added `AddZeroAllocValueObjectConverters()` — a per-assembly extension method that `Converters.Add(...)` every generator-emitted converter into a `JsonSerializerOptions`. STJ's `options.Converters` list takes precedence over the JsonContext's resolved typeinfo at serialize/deserialize time, so JSON wire format is bare integer as designed.

2.3.1's integration tests passed: round-trip via `JsonSerializer.Serialize/Deserialize` works correctly. The template re-author PR #128 also passes integration tests on both templates (`build` + `build-vs` green).

**But:** the za-clean template enables `PublishAot=true` plus uses `JsonSerializerContext`. Its smoke jobs (AOT publish + real-run JIT) both fail at app startup with:

> `System.NotSupportedException: JsonTypeInfo metadata for type 'CustomerId' was not provided by TypeInfoResolver of type '[JsonContext]'`

Root cause: ASP.NET Core's request-delegate factory pre-resolves typeinfo for every endpoint binding type at startup. When it walks `OrderRequest`'s typeinfo (emitted by JsonContext source-gen), it encounters the `CustomerId` property. `JsonPropertyInfo.Configure()` calls `options.GetTypeInfo(typeof(CustomerId))` — which iterates the resolver chain. The chain has only `JsonContext.Default`, which either silently failed to emit typeinfo for `CustomerId` (because STJ's source-gen couldn't see the `[JsonConverter]` attribute added via partial) or emitted a stub that returns null. Throw.

`options.Converters.Add` (2.3.1's registrar) is irrelevant here — that's a `serialize/deserialize` path, not a startup-typeinfo-resolution path.

This is the same architectural class of bug as the original 2.3.0 → 2.3.1 gap: Roslyn-gens-can't-see-each-other surfacing in a slightly different STJ pipeline corner. Templates use `JsonSerializerContext` for AOT readiness; both gaps surface there.

## Goal

`AddZeroAllocValueObjectConverters()` makes a `JsonSerializerOptions` work transparently with `JsonSerializerContext` source-gen, in both JIT and AOT, without the consumer needing to know about typeinfo resolvers or to manually register typeinfo for value-object types.

PR #128's za-clean AOT + real-run smoke jobs should pass on the 2.3.2 foundation with no changes beyond the package version bump.

## Decisions

### D-1: emit an `IJsonTypeInfoResolver` per assembly

The generator emits one additional file: `ValueObjectJsonTypeInfoResolver.g.cs`. It implements `IJsonTypeInfoResolver` and returns pre-configured `JsonTypeInfo<T>` for every value-object in the assembly. Typeinfo is built via `JsonMetadataServices.CreateValueInfo<T>(options, converter)` — STJ's canonical bridge from a custom converter to the source-gen-friendly typeinfo model.

**Considered and rejected:**

- **Make the existing converter classes `public` and rely on JsonContext source-gen to find the `[JsonConverter]` attribute.** STJ source-gen still doesn't see the attribute added by ZA's gen via a generated partial — same root cause as 2.3.0's gap. Visibility doesn't help.
- **Drop the `[JsonConverter]` attribute emission and rely solely on the resolver + Converters list.** Reflection-based STJ users (no JsonContext) lose the auto-discovery the 2.3.0 contract promised. Breaks 2.3.0 consumers.
- **Hand-write `[JsonConverter]` on the user's `[ValueObject]` declaration.** Visible to STJ source-gen, but requires the user to know the generator-emitted converter class name. Leaks generator internals into user code. Same anti-pattern that closed PR #127.

### D-2: extend the existing registrar (one new line)

`AddZeroAllocValueObjectConverters()` gains a call to `options.TypeInfoResolverChain.Insert(0, ValueObjectJsonTypeInfoResolver.Default)` after the existing `Converters.Add` lines. Consumers upgrading from 2.3.1 to 2.3.2 transparently gain AOT support — no API change, one extra method-body line.

**Considered and rejected:**

- **A separate `AddZeroAllocValueObjectTypeInfo()` method.** Doubles the API surface for no compositional gain. Every realistic consumer wants both behaviors together.

### D-3: resolver visibility

`ValueObjectJsonTypeInfoResolver` is `internal sealed`. It's referenced only from the same-assembly registrar; the consumer's contract is the extension method, not the resolver class. Mirrors the converter-class visibility from 2.3.0.

### D-4: call order documentation

`AddZeroAllocValueObjectConverters()` inserts the resolver at chain index 0. Consumers should call it **after** their `JsonContext.Default` insertion:

```csharp
o.SerializerOptions.TypeInfoResolverChain.Insert(0, JsonContext.Default);
o.SerializerOptions.AddZeroAllocValueObjectConverters();
```

This produces chain `[VO-resolver, JsonContext.Default]`. Value-objects resolve via our resolver; DTOs fall through to JsonContext. Reversing the calls produces `[JsonContext.Default, VO-resolver]` — wrong order, JsonContext's broken typeinfo for value-objects wins.

Will be flagged in `getting-started.md` and the registrar's XML doc comment.

### D-5: SemVer + commit framing

`2.3.1 → 2.3.2` (patch). Strictly additive: one new emitted file + one extra line in the existing method body; reflection-based and JIT/JsonContext consumers see no observable behavior change. Conventional commit: `fix(generator): emit JsonTypeInfoResolver for AOT-friendly value-object metadata`.

## Design

### Emitter changes

`ValueObjectEmitter.cs` gains a new static method:

```csharp
internal static string EmitSystemTextJsonTypeInfoResolver(
    IReadOnlyList<(INamedTypeSymbol Type, IPropertySymbol UnderlyingProperty)> valueObjects)
{
    // Emits a per-assembly internal sealed class implementing
    // IJsonTypeInfoResolver. Single switch-style GetTypeInfo method with one
    // arm per value-object, plus a Default singleton. Each arm returns
    // JsonMetadataServices.CreateValueInfo<T>(options, new XConverter()).
}
```

Shape of the emitted file (illustrative, with one value-object):

```csharp
// <auto-generated/>
#nullable enable

namespace ZeroAlloc.Serialisation.SystemTextJson;

internal sealed class ValueObjectJsonTypeInfoResolver : global::System.Text.Json.Serialization.Metadata.IJsonTypeInfoResolver
{
    public static ValueObjectJsonTypeInfoResolver Default { get; } = new();

    public global::System.Text.Json.Serialization.Metadata.JsonTypeInfo? GetTypeInfo(
        global::System.Type type,
        global::System.Text.Json.JsonSerializerOptions options)
    {
        if (type == typeof(global::MyApp.Domain.CustomerId))
            return global::System.Text.Json.Serialization.Metadata.JsonMetadataServices.CreateValueInfo<global::MyApp.Domain.CustomerId>(
                options,
                new global::MyApp.Domain.CustomerIdSystemTextJsonConverter());
        return null;
    }
}
```

`JsonMetadataServices.CreateValueInfo<T>` creates a `JsonTypeInfo<T>` whose serialization is fully delegated to the supplied converter. No property graph, no constructor binding, no allocation surprises — the converter is the contract.

### Registrar change

`EmitSystemTextJsonRegistrar` (from 2.3.1) gains one line in the emitted method body:

```csharp
public static JsonSerializerOptions AddZeroAllocValueObjectConverters(this JsonSerializerOptions options)
{
    options.Converters.Add(new global::Ns.XSystemTextJsonConverter());
    // ... one per value-object ...
    options.TypeInfoResolverChain.Insert(0, ValueObjectJsonTypeInfoResolver.Default);  // NEW
    return options;
}
```

### Generator pipeline change

`SerializerGenerator.Initialize`'s value-object registrar pipeline (added in 2.3.1) gains one additional `AddSource` call when the detection list is non-empty: alongside `ValueObjectJsonConvertersExtensions.g.cs`, it also emits `ValueObjectJsonTypeInfoResolver.g.cs`. Same gate (`ReferencesSystemTextJson` + `detected.Count > 0`).

### Backwards compatibility

Strictly additive at the generator-output level. Existing emitted files keep their shape; one new file is added. The registrar method body grows by one line — its public signature is unchanged.

- **2.3.0 consumers** (reflection STJ, no JsonContext): see no change. The `[JsonConverter]` attribute auto-discovery path is preserved.
- **2.3.1 consumers** (JsonContext + `AddZeroAllocValueObjectConverters`, JIT only): see no change for working scenarios. The new typeinfo resolver doesn't disturb JIT round-trips.
- **New 2.3.2 use case** (JsonContext + AOT): newly works. Was broken in 2.3.0 (no registrar) and 2.3.1 (registrar alone insufficient for startup typeinfo).

### Test surface

Four additions:

| # | Project | Scenario | Expected |
|---|---|---|---|
| 1 | Generator.Tests (snapshot) | 2 value-objects + STJ ref | `ValueObjectJsonTypeInfoResolver.g.cs` emitted; contains the resolver class, `Default` singleton, switch arms for both types, `return null;` fallback |
| 2 | Generator.Tests (snapshot) | Registrar shape | The 2.3.1 registrar's emitted method body contains the new `TypeInfoResolverChain.Insert(0, ...)` line |
| 3 | Generator.Tests (regression) | No value-objects / no STJ ref | No resolver emission (same gating as registrar) |
| 4 | Serialisation.Tests (integration) | `options.GetTypeInfo(typeof(CustomerId))` returns non-null after `AddZeroAllocValueObjectConverters()` | Directly verifies the chain insert worked + the typeinfo is queryable |

The existing 2.3.1 JsonContext round-trip tests (PascalCase + CamelCase) stay green and now ALSO transitively exercise the resolver (they call the same registrar; the chain insert happens whether or not they query typeinfo).

### Files touched

- **MOD:** `src/ZeroAlloc.Serialisation.Generator/ValueObjectEmitter.cs` — add `EmitSystemTextJsonTypeInfoResolver`; tweak `EmitSystemTextJsonRegistrar` to append the chain-insert line.
- **MOD:** `src/ZeroAlloc.Serialisation.Generator/SerializerGenerator.cs` — `RegisterSourceOutput` callback gains a second `AddSource("ValueObjectJsonTypeInfoResolver.g.cs", ...)` next to the existing registrar emission.
- **NEW:** `tests/ZeroAlloc.Serialisation.Generator.Tests/ValueObjectResolverEmissionTests.cs` — tests 1-3.
- **MOD:** `tests/ZeroAlloc.Serialisation.Tests/ValueObjectJsonContextRoundTripTests.cs` — append test 4 (queryable typeinfo).
- **MOD:** `docs/backlog.md` — strike V1.6 (this work) shipped; existing V2 (MessagePack/MemoryPack resolver+registrar parity) entry updated to reflect that MessagePack also has an analogous typeinfo gap under AOT source-gen.
- **MOD:** `docs/getting-started.md` — the JsonContext subsection gains a call-order note.

Total commit footprint: ~150 LOC including tests.

## Out of scope

- **MessagePack typeinfo-resolver equivalent.** MessagePack-CSharp's AOT source-generator has an analogous typeinfo concept (`IFormatterResolver`). Same architectural fix shape applies. Defer until a real consumer surfaces.
- **MemoryPack equivalent.** MemoryPack already self-registers via `[ModuleInitializer]` (per 2.3.0 design); no startup typeinfo issue.
- **A diagnostic when call order is wrong.** Tempting but noisy — we don't know what other resolvers the user might want in the chain.
- **Bebop backend** (V3 in backlog). No change.
