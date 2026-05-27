# Value-Object Transparent Serializers — Design

**Date:** 2026-05-27
**Scope:** `ZeroAlloc.Serialisation.Generator` gains a parallel discovery pass that detects single-property `[ZeroAlloc.ValueObjects.ValueObjectAttribute]` partial structs and emits transparent serializers/converters/formatters across the three supported backends (System.Text.Json, MessagePack, MemoryPack). Wire format becomes the underlying primitive (e.g. `42` instead of `{"value": 42}` for `CustomerId { int Value }`). Closes a new backlog item V1. Ships as **ZeroAlloc.Serialisation 2.3.0** (additive minor — existing `[ZeroAllocSerializable]`-marked types see byte-identical generator output).

## Background

ZeroAlloc.Validation 1.5.0 (shipped 2026-05-27) added value-object-aware property validators: a `[Validate]` request type can now carry `[ZeroAlloc.ValueObjects.ValueObject]`-typed properties and the built-in operand-taking validators (`[GreaterThan]`, `[NotEmpty]`, etc.) unwrap to the wrapper's underlying member.

The natural follow-up — adopting typed-id properties in the `za-clean` and `za-vertical-slice` templates — surfaced a missing piece. Wire-format JSON deserialization. With `[Validate] readonly record struct PlaceOrderCommand(CustomerId CustomerId, decimal Total)` and a wire body of `{"customerId": 42, "total": 99.99}`, System.Text.Json today expects `{"customerId": {"value": 42}, ...}` because `CustomerId` is a struct with a `Value` property. The wrapper-vs-underlying transparency that B1 delivered at the **validation** layer doesn't exist at the **serialization** layer.

The candidate fixes considered:

- **Hand-rolled converters in each template.** Per-type `JsonConverter<CustomerId>` classes shipped as template content. Works immediately but propagates maintenance tax to every new template and every adopter-defined typed-id.
- **New `ZeroAlloc.ValueObjects.SystemTextJson` sibling package.** Source-gen one converter per `[ValueObject]` partial struct. Right shape but only covers STJ; MessagePack and MemoryPack adopters would need parallel sibling packages.
- **Extend `ZeroAlloc.Serialisation` with `[ValueObject]` detection.** `ZeroAlloc.Serialisation` already has the source-gen plumbing across three backends (`ZeroAlloc.Serialisation.SystemTextJson`, `ZeroAlloc.Serialisation.MessagePack`, `ZeroAlloc.Serialisation.MemoryPack`) and ships AOT-safe by design. Adding a parallel detection pass for `[ValueObject]` types lets adopters get transparent serialization for free across whichever backend(s) they reference.

The third option is the design this doc captures.

## Goal

`[ValueObject]`-decorated single-property partial structs serialize transparently across all three backends — wire format is the underlying primitive, no per-adopter hand-rolling required. Adopters who reference only ZA.ValueObjects (without ZA.Serialisation) pay nothing. Adopters who reference a backend package get the converter/formatter auto-generated for any value-object in scope, with the backend's native attribute (`[JsonConverter]` / `[MessagePackFormatter]` / `[MemoryPackCustomFormatter]`) wired up automatically via partial-class extension.

## Decisions

### D-1: detect by `[ValueObject]` attribute FQN — same pattern as ZA.Validation B1

The generator matches `ZeroAlloc.ValueObjects.ValueObjectAttribute` by metadata name. No runtime reference to ZA.ValueObjects required — only the attribute's FQN matters at compile time. Adopters who don't pull in ZA.ValueObjects pay zero cost (no candidate types, no emission).

This is the same pattern ZA.Validation 1.5.0 (B1) established for property-validator unwrap detection. Symmetric move; both packages now consume `[ValueObject]` via FQN.

**Considered and rejected:**

- **Duck-typing on shape** (any partial struct with a single readable property of comparable underlying type). Too loose — quick `readonly struct Wrap(int n)` types adopters write for unrelated reasons would silently get serializers attached.
- **Require an explicit opt-in attribute** (`[TransparentValueObject(SerializationFormat.X)]`). Contradicts the brainstorm decision to make detection automatic when `[ValueObject]` + a backend package are both in scope.

### D-2: single-property only; multi-property falls through silently

`Money { decimal Amount, string Currency }` doesn't get transparent emission. The generator's existing per-backend defaults serialize multi-property structs sensibly (`{"amount": 100, "currency": "EUR"}` for STJ; equivalent compact frames for MessagePack/MemoryPack). The value-object author explicitly chose a multi-property shape; transparency isn't load-bearing.

No diagnostic. The case isn't a bug — the user's `Money` type works correctly when serialized as a multi-property object. A `ZS00NN` Info-level "transparent emission not applied" would be noise.

**Considered and rejected:**

- **`MemberOf` hint to pick one property for transparent emission.** API surface expansion only justified when multi-property value-objects with a "canonical wire field" become a load-bearing case. None today; YAGNI.
- **Loud diagnostic on multi-property + backend reference.** Paternalistic; the user explicitly designed a multi-property type expecting it to work.

### D-3: per-backend emission gated by `Compilation.ReferencedAssemblyNames`

The generator checks each consuming compilation's assembly references:

- References `ZeroAlloc.Serialisation.SystemTextJson` (or its FQN-matched assembly name) → emit `JsonConverter<T>` per detected value-object.
- References `ZeroAlloc.Serialisation.MessagePack` → emit `IMessagePackFormatter<T>`.
- References `ZeroAlloc.Serialisation.MemoryPack` → emit `MemoryPackFormatter<T>`.

Adopters who reference all three get three converters per value-object — same wire transparency expressed in each backend's native binary frame.

Adopters who reference none get no emission — the discovery pass produces an empty set.

The runtime adapter packages (`*.SystemTextJson` / `*.MessagePack` / `*.MemoryPack`) stay as the existing one-file shims; the generator picks them up via reference inspection. No new "tell the generator which backend to emit for" attribute on the value-object itself.

### D-4: registration via partial-class attribute extension

Each emitted converter/formatter class lives in the consuming compilation. The generator additionally emits a partial-class extension on the value-object that adds the backend's native attribute:

```csharp
// User declares:
[ValueObject]
public readonly partial struct CustomerId { public int Value { get; } public CustomerId(int v) => Value = v; }

// Generator emits (per backend, gated by reference):

// CustomerId.Generated.SystemTextJson.cs
[JsonConverter(typeof(CustomerIdSystemTextJsonConverter))]
public readonly partial struct CustomerId { }

public sealed class CustomerIdSystemTextJsonConverter : JsonConverter<CustomerId>
{
    public override CustomerId Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        => new CustomerId(reader.GetInt32());

    public override void Write(Utf8JsonWriter writer, CustomerId value, JsonSerializerOptions options)
        => writer.WriteNumberValue(value.Value);
}
```

System.Text.Json picks up the `[JsonConverter]` attribute automatically; no `JsonSerializerOptions.Converters.Add(...)` registration required. Same story for the other backends — each has an attribute-driven registration mechanism the generator wires up via the partial-class extension.

**Considered and rejected:**

- **Explicit DI/registration extension.** `services.AddZeroAllocValueObjectConverters()`. More moving parts; adopters would have to remember to call it. Attribute-driven registration is zero-config.

### D-5: no new attribute, no new diagnostic

The whole feature is silent transparent emission. No new attribute on the user side — `[ValueObject]` is the trigger. No new diagnostic — the case "this should have transparent emission but doesn't" doesn't exist (single-property + backend reference always emits; multi-property falls through to backend default which is correct behaviour).

A single `ZS00NN` Info-level "transparent emission emitted for X" could be useful for debugging adopter projects but is noise in normal use. Skip until a real consumer asks.

## Design

### Files touched

- **MOD:** `src/ZeroAlloc.Serialisation.Generator/SerializerGenerator.cs` (entry point) — add the parallel discovery pass that runs after the existing `[ZeroAllocSerializable]` pass. Both passes contribute to the same final emission.
- **MOD:** `src/ZeroAlloc.Serialisation.Generator/ModelExtractor.cs` — add a helper to find `[ValueObject]` partial structs by FQN match + extract the single underlying property symbol. Returns `null` for multi-property or unmarked types.
- **NEW:** `src/ZeroAlloc.Serialisation.Generator/ValueObjectEmitter.cs` — three emission methods, one per backend, each gated by a reference-check on the consuming compilation. Each method emits the converter class + the partial-class attribute extension.
- **NEW:** `tests/ZeroAlloc.Serialisation.Tests/Generator/ValueObjectEmissionTests.cs` (or matching the existing folder structure) — generator-snapshot tests, 4 cases per backend × 3 backends = 12 snapshot tests.
- **NEW:** `tests/ZeroAlloc.Serialisation.Tests/Integration/ValueObjectRoundTripTests.cs` — runtime round-trip tests, one per backend. `CustomerId(42)` → wire → `CustomerId(42)`.
- **MOD:** `docs/getting-started.md` — add "Value-object transparent serialization" subsection introducing the auto-detect behaviour.
- **NEW:** `docs/backlog.md` (this repo doesn't have one yet) — initial file with the V1 entry + a brief preamble matching the convention used in `ZeroAlloc.Validation/docs/backlog.md` and `ZeroAlloc.Mediator/docs/backlog.md`.

### Detection logic (in `ModelExtractor`)

```csharp
private const string ValueObjectAttributeFqn = "ZeroAlloc.ValueObjects.ValueObjectAttribute";

internal static (INamedTypeSymbol Type, IPropertySymbol UnderlyingProperty)? TryGetTransparentValueObject(INamedTypeSymbol candidate)
{
    var hasMarker = candidate.GetAttributes()
        .Any(a => string.Equals(a.AttributeClass?.ToDisplayString(), ValueObjectAttributeFqn, StringComparison.Ordinal));
    if (!hasMarker) return null;

    var properties = candidate.GetMembers()
        .OfType<IPropertySymbol>()
        .Where(p => !p.IsStatic && p.DeclaredAccessibility == Accessibility.Public)
        .ToArray();

    return properties.Length == 1 ? (candidate, properties[0]) : null;
}
```

Same shape as the ZA.Validation 1.5.0 helper `GetValueObjectUnwrapMember`. The two implementations don't share code (different repos) — convention is keep them in sync.

### Backend assembly references (in `ValueObjectEmitter`)

```csharp
internal static bool ReferencesSystemTextJsonBackend(Compilation compilation) =>
    compilation.ReferencedAssemblyNames.Any(a => string.Equals(a.Name, "ZeroAlloc.Serialisation.SystemTextJson", StringComparison.Ordinal));

// …parallel methods for MessagePack and MemoryPack…
```

Three independent checks; an adopter referencing all three triggers all three emissions.

### Per-backend emission shapes

**System.Text.Json:**

```csharp
[JsonConverter(typeof(CustomerIdSystemTextJsonConverter))]
public readonly partial struct CustomerId { }

public sealed class CustomerIdSystemTextJsonConverter : JsonConverter<CustomerId>
{
    public override CustomerId Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        => new CustomerId(reader.GetInt32());

    public override void Write(Utf8JsonWriter writer, CustomerId value, JsonSerializerOptions options)
        => writer.WriteNumberValue(value.Value);
}
```

The `reader.GetInt32()` and `writer.WriteNumberValue` calls are templated against the underlying property's type. For `Username { string Value }` the generator emits `reader.GetString()!` and `writer.WriteStringValue(value.Value)`. The underlying-type-to-STJ-method mapping is a small fixed table inside the emitter.

**MessagePack:**

```csharp
[MessagePackFormatter(typeof(CustomerIdMessagePackFormatter))]
public readonly partial struct CustomerId { }

public sealed class CustomerIdMessagePackFormatter : IMessagePackFormatter<CustomerId>
{
    public void Serialize(ref MessagePackWriter writer, CustomerId value, MessagePackSerializerOptions options)
        => writer.Write(value.Value);

    public CustomerId Deserialize(ref MessagePackReader reader, MessagePackSerializerOptions options)
        => new CustomerId(reader.ReadInt32());
}
```

**MemoryPack:**

```csharp
[MemoryPackable(GenerateType.NoGenerate)]
[MemoryPackCustomFormatter<CustomerIdMemoryPackFormatter, CustomerId>]
public readonly partial struct CustomerId { }

public sealed class CustomerIdMemoryPackFormatter : MemoryPackFormatter<CustomerId>
{
    public override void Serialize<TBufferWriter>(ref MemoryPackWriter<TBufferWriter> writer, scoped ref CustomerId value)
        => writer.WriteValue(value.Value);

    public override void Deserialize(ref MemoryPackReader reader, scoped ref CustomerId value)
        => value = new CustomerId(reader.ReadValue<int>()!);
}
```

The MemoryPack shape requires `partial struct` to also carry `[MemoryPackable(GenerateType.NoGenerate)]` to disable MemoryPack's default generation. This is the only meaningful per-backend nuance.

### Test surface

**Generator snapshots — 4 cases per backend × 3 backends:**

| Case | Source | Expected |
|---|---|---|
| 1 | Single-property `[ValueObject]` + backend reference | Converter/formatter emitted; partial-struct attribute extension added |
| 2 | Single-property `[ValueObject]` + **no** backend reference | No emission, no diagnostic |
| 3 | Multi-property `[ValueObject]` + backend reference | No emission, no diagnostic — falls through |
| 4 | Non-`[ValueObject]` partial struct (one property) + backend reference | No emission — attribute is the trigger, not shape |

**Integration round-trip — 1 per backend:**

```csharp
[Fact]
public void CustomerId_Roundtrips_Through_SystemTextJson()
{
    var original = new CustomerId(42);
    var json = JsonSerializer.Serialize(original);
    Assert.Equal("42", json);  // wire format is bare int
    var deserialized = JsonSerializer.Deserialize<CustomerId>(json);
    Assert.Equal(original, deserialized);
}
```

Same shape for MessagePack and MemoryPack with their respective wire-format assertions (`MessagePackSerializer.ConvertToJson(bytes)` returning `"42"`, etc.).

**Regression net** — existing `[ZeroAllocSerializable]` snapshot tests must produce byte-identical output to before. The new code path runs in parallel; if it perturbs existing emission, that's a generator bug.

### Backward compatibility

Strictly additive:

- Adopters not using `[ValueObject]` see byte-identical generator output.
- Adopters using `[ValueObject]` without any ZA.Serialisation backend reference also see byte-identical output (no emission triggered).
- Adopters using `[ValueObject]` + a backend reference get the new transparent emission — previously they'd hand-rolled converters or accepted the verbose object-shape. Either case is non-breaking: the hand-rolled converter's `[JsonConverter]` attribute takes precedence over the generator-emitted one (per System.Text.Json's attribute resolution — explicit declarations beat partial-class extensions), so adopters with existing hand-rolled converters keep them.

SemVer: minor bump (`2.2.0` → `2.3.0`).

## Out of scope (deferred)

- **Multi-property value-objects with explicit member-of hint.** No load-bearing consumer; backend default behaviour is correct. Backlog if it surfaces.
- **Custom underlying-type-to-backend-method mappings.** The fixed table inside the emitter covers `int / long / decimal / string / Guid / DateTime` plus their nullable variants. Other types fall through to the backend's generic `Read<T>()` / `Write<T>()` shape with no special-casing.
- **Async-only backends or buffer-writer customization.** All three current backends are ref-struct-based and synchronous; no async pattern to deconfound.
- **A ZS00NN Info diagnostic** announcing transparent emission. Skip until a real consumer asks.

## Files touched (final count)

- **MOD:** 2 generator files (`SerializerGenerator.cs`, `ModelExtractor.cs`)
- **NEW:** 1 generator file (`ValueObjectEmitter.cs`) — ~150 LOC including all three backends
- **NEW:** 2 test files (~300 LOC, 12 snapshot + 3 round-trip)
- **MOD:** 1 doc file (`getting-started.md`)
- **NEW:** 1 doc file (`docs/backlog.md` — first backlog file in this repo)

Total commit footprint: ~500 LOC including tests.

## Sequencing

This release is a prerequisite for the template migration PR:

1. **ZA.Serialisation 2.3.0** (this design) — transparent value-object emission.
2. **ZA.Templates follow-up PR** (separate) — bump `ZeroAlloc.Serialisation` reference, add the new `ZeroAlloc.Serialisation.SystemTextJson` reference, migrate the 5 typed-id request properties in `za-clean` + `za-vertical-slice` from raw `int` to typed `CustomerId` / `OrderId`, drop the manual handler wrapping.

Same handoff pattern that ZA.Validation 1.5.0 → templates 0.7.x established.
