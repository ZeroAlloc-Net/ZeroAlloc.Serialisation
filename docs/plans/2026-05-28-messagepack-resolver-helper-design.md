# MessagePack IFormatterResolver + Helper — Design

**Date:** 2026-05-28
**Scope:** ZeroAlloc.Serialisation 2.3.3 — emit a per-assembly internal `IFormatterResolver` that provides formatters for every `[ValueObject]` type, plus a public extension method `AddZeroAllocValueObjectFormatters(this MessagePackSerializerOptions)` that prepends the resolver to the composite chain. Closes the MessagePack analog of the AOT/JsonContext gap that 2.3.1 + 2.3.2 closed for STJ.

## Background

2.3.0 emits `[MessagePackFormatter(typeof(<Name>MessagePackFormatter))]` on the `[ValueObject]` partial-struct extension plus an `internal sealed class <Name>MessagePackFormatter : IMessagePackFormatter<T>`. MessagePack-CSharp's reflection-based runtime resolver consults the attribute and uses the formatter — works for runtime composite-resolver consumers.

It does **not** work for consumers using `MessagePack.SourceGenerator` (the AOT source-gen). MessagePack-CSharp's source generator emits a `GeneratedMessagePackResolver` at build time that has formatter entries for every `[MessagePackObject]`-decorated type. When the consumer's source-gen processes a DTO containing a value-object property, it sees the value-object's source declaration (just `int Value`) but NOT the `[MessagePackFormatter]` attribute that ZA's generator adds via a partial — Roslyn incremental generators don't see each other's output in the same compilation pass. The source-generated resolver returns a default-shape formatter for the value-object instead of the transparent one. Wire format collapses to `{"Value":42}` shape instead of bare integer.

This is the same root cause as the STJ gap closed by 2.3.1 + 2.3.2. The MessagePack equivalent has been on the backlog as V2 since 2.3.2 shipped — deferred because no current ZA consumer hits it (templates use the reflection-based runtime resolver). Surfaced 2026-05-28 as a proactive symmetry move: shipping V2 now means future migrations don't rediscover the gap reactively.

## Goal

A consumer using MessagePack with `MessagePack.SourceGenerator` (AOT-friendly typeinfo) wires up transparent value-object serialization with one call:

```csharp
var options = MessagePackSerializerOptions.Standard
    .WithResolver(GeneratedMessagePackResolver.Instance)
    .AddZeroAllocValueObjectFormatters();
```

The resulting resolver chain is `[ValueObjectMessagePackResolver, GeneratedMessagePackResolver]` — value-objects served by our resolver, everything else by the user's source-gen resolver. The call site doesn't reference any generator-emitted class names. Existing 2.3.0 reflection-based consumers see no behaviour change — the `[MessagePackFormatter]` attribute path stays intact.

## Decisions

### D-1: single-file emission (resolver + extension method together)

The STJ work split the V1.5 (registrar) and V1.6 (resolver) into two separate emission files because STJ has TWO registration mechanisms: `options.Converters` AND `TypeInfoResolverChain`. MessagePack has only ONE: the resolver. So the parallel "two-file split" would be artificial. V2 emits a single file `ValueObjectMessagePackResolverExtensions.g.cs` containing both the resolver class and the extension method.

**Considered and rejected:**

- **Two-file split for symmetry with STJ.** Conceptually parallel but increases the number of generated files for no functional benefit. The reader has more to track without a corresponding gain.

### D-2: `FormatterCache<T>` static-class lookup pattern

MessagePack-CSharp's `IFormatterResolver.GetFormatter<T>()` is generic and must return strongly-typed `IMessagePackFormatter<T>?` per call. The canonical pattern (used by `StandardResolver`, `CompositeResolver`, etc.) is a private generic-static-cache nested class:

```csharp
public IMessagePackFormatter<T>? GetFormatter<T>() => FormatterCache<T>.Formatter;

private static class FormatterCache<T>
{
    public static readonly IMessagePackFormatter<T>? Formatter
        = (IMessagePackFormatter<T>?)GetFormatterUntyped(typeof(T));
}
```

JIT specialises the static field per closed-generic type `T`; the cast runs once per T and is amortised over all subsequent lookups. Zero-allocation hot path.

**Considered and rejected:**

- **`Dictionary<Type, object>` lookup.** Allocates a dictionary entry once; subsequent lookups do a hash; allocates if any closure captures. Slower than the generic-static-cache pattern and not what idiomatic MessagePack resolvers do.

### D-3: `CompositeResolver.Create(ours, options.Resolver)` to prepend

The extension method returns new options with our resolver at chain position 0:

```csharp
public static MessagePackSerializerOptions AddZeroAllocValueObjectFormatters(this MessagePackSerializerOptions options)
{
    var composite = CompositeResolver.Create(
        ValueObjectMessagePackResolver.Default,
        options.Resolver);
    return options.WithResolver(composite);
}
```

For value-object types our resolver returns the formatter. For all other types it returns null, and `CompositeResolver` falls through to the user's existing resolver (typically `StandardResolver` or `GeneratedMessagePackResolver`). Mirrors STJ's `TypeInfoResolverChain.Insert(0, ours)` semantics.

### D-4: call-order matters

Same constraint as STJ's 2.3.2: consumers should call `AddZeroAllocValueObjectFormatters()` AFTER setting their primary resolver (typically `WithResolver(GeneratedMessagePackResolver.Instance)`). Calling in the wrong order produces a chain where the user's resolver wins for value-objects and our transparent formatters are bypassed.

Documented in the extension method's XML doc + `docs/getting-started.md`. No diagnostic — same call-order pitfall STJ has, same documentation-only approach.

### D-5: resolver visibility

`internal sealed`. Mirrors 2.3.2's STJ resolver visibility. The extension method is the only public surface; the resolver class name is implementation detail.

### D-6: aot-smoke fixture bundled in the same PR

The PR includes a fixture extension to `samples/ZeroAlloc.Serialisation.AotSmoke/` that exercises the actual AOT source-gen gap end-to-end. Without this, a regression in V2's emission or in the gating logic would slip past CI (lesson from 2.3.1 + 2.3.2 shipping reactively because the existing smoke didn't cover their surface).

**Fixture shape:**

- Add `<PackageReference Include="MessagePack.SourceGenerator" PrivateAssets="all" />` to the smoke csproj (analyzer, not runtime)
- New `ValueObjectMpDto.cs`: `[MessagePackObject] public sealed partial class ValueObjectMpDto { [Key(0)] public ValueObjectId Id { get; set; } [Key(1)] public string Label { get; set; } = ""; }` (reuses the `ValueObjectId` declared by PR #38's smoke extension)
- Program.cs assertions:
  - `options = MessagePackSerializerOptions.Standard.AddZeroAllocValueObjectFormatters()`
  - Round-trip `new ValueObjectMpDto { Id = new ValueObjectId(42), Label = "alpha" }` through `MessagePackSerializer.Serialize` + `Deserialize`
  - Inspect the wire shape via `MessagePackSerializer.ConvertToJson(bytes)`; assert the JSON contains `42` (bare integer for `Id`, not a wrapped `{"Value":42}` object)
  - Track `ok` flag and exit non-zero on failure

### D-7: dependency on PR #38

V2's smoke extension assumes the `ValueObjectId` fixture declared by [PR #38](https://github.com/ZeroAlloc-Net/ZeroAlloc.Serialisation/pull/38) (the V1/STJ smoke extension). Implementation will base off `chore/aot-smoke-cover-value-objects` (the #38 branch) rather than `main`, and rebase onto `main` after #38 merges. If V2 lands first by accident, the smoke extension will need to also declare `ValueObjectId` inline.

### D-8: SemVer + commit framing

`2.3.2 → 2.3.3` (patch). Strictly additive: one new emitted file + one new conditional block in the existing `RegisterSourceOutput` callback. Reflection-based MessagePack consumers see no observable change. Conventional commit: `fix(generator): emit MessagePack IFormatterResolver for AOT source-gen interop`.

## Design

### Emitter changes

`ValueObjectEmitter.cs` gains a new static method:

```csharp
internal static string EmitMessagePackResolver(
    IReadOnlyList<(INamedTypeSymbol Type, IPropertySymbol UnderlyingProperty)> valueObjects)
{
    // Emits a single per-assembly file with:
    //   - internal sealed ValueObjectMessagePackResolver : IFormatterResolver
    //     - FormatterCache<T> generic-static-cache nested class
    //     - GetFormatterUntyped(Type) switch over typeof comparisons
    //   - public static ValueObjectMessagePackFormattersExtensions class
    //     - AddZeroAllocValueObjectFormatters extension on MessagePackSerializerOptions
}
```

Reuses `BuildTypeFqn` and `BuildFormatterFqn` helpers (the latter is new — sibling to the existing `BuildConverterFqn` from STJ).

### Generator pipeline change

`SerializerGenerator.Initialize`'s value-object `RegisterSourceOutput` callback (the one that already emits STJ registrar + resolver) gains an additional gated block:

```csharp
if (ValueObjectEmitter.ReferencesMessagePack(compilation))
{
    var mpResolverSource = ValueObjectEmitter.EmitMessagePackResolver(detected);
    sourceCtx.AddSource("ValueObjectMessagePackResolverExtensions.g.cs", mpResolverSource);
}
```

Same `detected` list, same callback shape — symmetric with the STJ branch.

### Test surface

| # | Project | Scenario | Expected |
|---|---|---|---|
| 1 | Generator.Tests (snapshot) | 2 value-objects + MessagePack ref | `ValueObjectMessagePackResolverExtensions.g.cs` emitted; contains the resolver class, `Default` singleton, `FormatterCache<T>` nested class, `GetFormatterUntyped` with one arm per type, the extension method calling `CompositeResolver.Create` + `WithResolver` |
| 2 | Generator.Tests (regression) | `[ValueObject]` present, no MessagePack ref | No resolver/extension emission (gating consistency) |
| 3 | Generator.Tests (regression) | No `[ValueObject]` types | No resolver/extension emission (empty-class avoidance) |
| 4 | Serialisation.Tests (integration) | `MessagePackSerializerOptions.Standard.AddZeroAllocValueObjectFormatters()` + round-trip a DTO containing a value-object | Bytes round-trip; `MessagePackSerializer.ConvertToJson(bytes)` shows bare integer at the value-object field |
| 5 | AotSmoke (end-to-end) | `MessagePack.SourceGenerator`-marked DTO with value-object property | Native AOT publish + run succeeds; round-trip + wire-format invariants hold |

### Files touched

- **MOD:** `src/ZeroAlloc.Serialisation.Generator/ValueObjectEmitter.cs` — add `EmitMessagePackResolver` + `BuildFormatterFqn` helper.
- **MOD:** `src/ZeroAlloc.Serialisation.Generator/SerializerGenerator.cs` — add the gated `AddSource` block alongside the existing STJ emissions.
- **NEW:** `tests/ZeroAlloc.Serialisation.Generator.Tests/ValueObjectMessagePackResolverEmissionTests.cs` — tests 1-3.
- **MOD:** `tests/ZeroAlloc.Serialisation.Tests/ValueObjectMessagePackRoundTripTests.cs` — append test 4 (DTO round-trip with the new helper).
- **MOD:** `samples/ZeroAlloc.Serialisation.AotSmoke/ZeroAlloc.Serialisation.AotSmoke.csproj` — add `MessagePack.SourceGenerator` analyzer reference.
- **NEW:** `samples/ZeroAlloc.Serialisation.AotSmoke/ValueObjectMpDto.cs` — `[MessagePackObject]` DTO.
- **MOD:** `samples/ZeroAlloc.Serialisation.AotSmoke/Program.cs` — append MessagePack source-gen DTO round-trip + wire-format assertions.
- **MOD:** `docs/backlog.md` — strike V2 shipped; existing V2 (MessagePack underlying-type Guid/DateTime) entry stays open.
- **MOD:** `docs/getting-started.md` — MessagePack-with-AOT subsection mirroring the existing JsonSerializerContext subsection from 2.3.2.

Total commit footprint: ~250 LOC including tests + smoke fixture.

## Out of scope

- **MemoryPack** — already self-registers via `[ModuleInitializer]` (2.3.0); no AOT startup gap. No change.
- **V3 Bebop backend** — separate backlog item; no change.
- **MessagePack Guid/DateTime underlying-type table extension** — separate backlog item from 2.3.0 review; no change.
- **A diagnostic when call order is wrong** — same call-order pitfall STJ has; same documentation-only approach; too noisy to detect reliably.
