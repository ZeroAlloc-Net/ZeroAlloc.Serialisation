# JsonSerializerContext Interop Helper Implementation Plan

> **For Claude:** REQUIRED SUB-SKILL: Use superpowers:executing-plans to implement this plan task-by-task.

**Goal:** Ship ZeroAlloc.Serialisation 2.3.1 — emit a per-assembly public extension method `JsonSerializerOptions.AddZeroAllocValueObjectConverters()` so `JsonSerializerContext` consumers can register every generator-emitted `[ValueObject]` converter in one call, without `InternalsVisibleTo` or class-name coupling.

**Architecture:** New `EmitSystemTextJsonRegistrar` method on `ValueObjectEmitter` produces a single per-assembly file. A second value-object pipeline in `SerializerGenerator.Initialize` collects all candidates via `.Collect()` and emits the registrar once per assembly. Gated by `ReferencesSystemTextJson(compilation)` and `valueObjects.Count > 0`.

**Tech Stack:** .NET 10, Roslyn `IIncrementalGenerator`, xUnit, generator-snapshot tests (`CSharpCompilation.Create` + `CSharpGeneratorDriver`), integration round-trip test against `JsonSerializerContext`.

**Design doc:** `docs/plans/2026-05-27-jsoncontext-interop-design.md` (committed at `b17cf58`).

**Working branch:** `feat/jsoncontext-interop-helper` (already created off `main` post-`67de979`; design committed).

---

## Phase 0 — Orient (5 min)

Read three files before touching code.

### Task 0.1: Read the existing emitter + the existing per-type pipeline

**Files (read-only):**

- `src/ZeroAlloc.Serialisation.Generator/ValueObjectEmitter.cs` (~215 LOC) — the existing 2.3.0 emitter. New method lands as a sibling to `EmitSystemTextJsonConverter`. Pay attention to: the `nsOpen` pattern, the `IsReadOnly`/`IsRecord` keyword construction, the `SystemTextJsonReadWriteForType` table.
- `src/ZeroAlloc.Serialisation.Generator/SerializerGenerator.cs` (~120 LOC) — the existing `Initialize` method registers the per-type value-object pipeline via `ForAttributeWithMetadataName` + `Combine(CompilationProvider)` + `RegisterSourceOutput`. The new registrar pipeline lands alongside it; same shape but with `.Collect()` between the candidates and the combine.
- `tests/ZeroAlloc.Serialisation.Generator.Tests/ValueObjectEmissionSnapshotTests.cs` (~210 LOC) — the existing snapshot tests for 2.3.0. New file `ValueObjectRegistrarEmissionTests.cs` copies the `RunGenerator` helper shape exactly so test conventions stay consistent across the suite.

---

## Phase 1 — `EmitSystemTextJsonRegistrar` method + snapshot test (40 min, 5 tasks)

### Task 1.1: Write the failing snapshot test

**File (NEW):** `tests/ZeroAlloc.Serialisation.Generator.Tests/ValueObjectRegistrarEmissionTests.cs`

```csharp
using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Xunit;
using ZeroAlloc.Serialisation.Generator;

namespace ZeroAlloc.Serialisation.Generator.Tests;

public class ValueObjectRegistrarEmissionTests
{
    [Fact]
    public void Registrar_EmitsExtensionMethod_ListingAllValueObjectsInAssembly()
    {
        var source = """
            using ZeroAlloc.ValueObjects;
            namespace TestModels;

            [ValueObject]
            public readonly partial struct CustomerId
            {
                public int Value { get; }
                public CustomerId(int value) => Value = value;
            }

            [ValueObject]
            public readonly partial struct OrderId
            {
                public int Value { get; }
                public OrderId(int value) => Value = value;
            }
            """;

        var result = RunGenerator(source, withSystemTextJson: true);

        Assert.Empty(result.Diagnostics);
        var emitted = result.GeneratedTrees
            .FirstOrDefault(t => t.FilePath.EndsWith("ValueObjectJsonConvertersExtensions.g.cs", StringComparison.Ordinal));
        Assert.NotNull(emitted);
        var text = emitted!.ToString();

        // Namespace + class shape
        Assert.Contains("namespace ZeroAlloc.Serialisation.SystemTextJson;", text, StringComparison.Ordinal);
        Assert.Contains("public static class ValueObjectJsonConvertersExtensions", text, StringComparison.Ordinal);
        Assert.Contains("public static global::System.Text.Json.JsonSerializerOptions AddZeroAllocValueObjectConverters(this global::System.Text.Json.JsonSerializerOptions options)", text, StringComparison.Ordinal);

        // Both value-objects registered via their FQN converter classes
        Assert.Contains("options.Converters.Add(new global::TestModels.CustomerIdSystemTextJsonConverter());", text, StringComparison.Ordinal);
        Assert.Contains("options.Converters.Add(new global::TestModels.OrderIdSystemTextJsonConverter());", text, StringComparison.Ordinal);

        // Returns options for chaining
        Assert.Contains("return options;", text, StringComparison.Ordinal);
    }

    private static GeneratorDriverRunResult RunGenerator(string source, bool withSystemTextJson = false, bool withMessagePack = false, bool withMemoryPack = false)
    {
        var valueObjectStub = """
            namespace ZeroAlloc.ValueObjects
            {
                [System.AttributeUsage(System.AttributeTargets.Class | System.AttributeTargets.Struct)]
                public sealed class ValueObjectAttribute : System.Attribute { }
            }
            """;

        var references = new List<MetadataReference>
        {
            MetadataReference.CreateFromFile(typeof(object).Assembly.Location),
        };
        if (withSystemTextJson)
            references.Add(MetadataReference.CreateFromFile(typeof(ZeroAlloc.Serialisation.SystemTextJson.SystemTextJsonSerializer<int>).Assembly.Location));
        if (withMessagePack)
            references.Add(MetadataReference.CreateFromFile(typeof(ZeroAlloc.Serialisation.MessagePack.MessagePackSerializer<int>).Assembly.Location));
        if (withMemoryPack)
            references.Add(MetadataReference.CreateFromFile(typeof(ZeroAlloc.Serialisation.MemoryPack.MemoryPackSerializer<int>).Assembly.Location));

        var compilation = CSharpCompilation.Create(
            "TestAssembly",
            new[] { CSharpSyntaxTree.ParseText(source), CSharpSyntaxTree.ParseText(valueObjectStub) },
            references,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

        var generator = new SerializerGenerator();
        var driver = CSharpGeneratorDriver.Create(generator).RunGenerators(compilation);
        return driver.GetRunResult();
    }
}
```

The `RunGenerator` helper is duplicated from `ValueObjectEmissionSnapshotTests.cs` intentionally — keeps tests self-contained. Match whatever the sibling test does for type names — if `SystemTextJsonSerializer<int>` doesn't compile, look at the sibling for the actual public type used.

### Task 1.2: Run — expect FAIL (no registrar emission yet)

```bash
cd c:/Projects/Prive/ZeroAlloc/ZeroAlloc.Serialisation
dotnet test tests/ZeroAlloc.Serialisation.Generator.Tests -c Release --filter "FullyQualifiedName~ValueObjectRegistrarEmissionTests"
```

Expected: 1 test fails — `result.GeneratedTrees` doesn't contain `ValueObjectJsonConvertersExtensions.g.cs`.

### Task 1.3: Implement `EmitSystemTextJsonRegistrar`

**File:** `src/ZeroAlloc.Serialisation.Generator/ValueObjectEmitter.cs`

Append to the `ValueObjectEmitter` class (after `EmitSystemTextJsonConverter`):

```csharp
/// <summary>
/// Emits a single per-assembly public extension method that registers every
/// generator-emitted [ValueObject] JsonConverter into a JsonSerializerOptions
/// instance. Closes the JsonSerializerContext interop gap — STJ's source
/// generator doesn't see [JsonConverter] attributes added by other generators,
/// so consumers using a context-based pipeline need an explicit registration
/// path. The extension lives in the ZeroAlloc.Serialisation.SystemTextJson
/// namespace for discoverability via the using consumers already have.
/// </summary>
internal static string EmitSystemTextJsonRegistrar(
    System.Collections.Generic.IReadOnlyList<(INamedTypeSymbol Type, IPropertySymbol UnderlyingProperty)> valueObjects)
{
    var sb = new System.Text.StringBuilder();
    sb.AppendLine("// <auto-generated/>");
    sb.AppendLine("#nullable enable");
    sb.AppendLine();
    sb.AppendLine("namespace ZeroAlloc.Serialisation.SystemTextJson;");
    sb.AppendLine();
    sb.AppendLine("/// <summary>");
    sb.AppendLine("/// Registers every [ZeroAlloc.ValueObjects.ValueObject] partial struct's");
    sb.AppendLine("/// generator-emitted JsonConverter into a JsonSerializerOptions. Call this");
    sb.AppendLine("/// when using JsonSerializerContext (the source-generated typeinfo pipeline)");
    sb.AppendLine("/// — STJ resolves options.Converters before the context's typeinfo, so the");
    sb.AppendLine("/// underlying-primitive wire format takes precedence over default struct");
    sb.AppendLine("/// serialization.");
    sb.AppendLine("/// </summary>");
    sb.AppendLine("public static class ValueObjectJsonConvertersExtensions");
    sb.AppendLine("{");
    sb.AppendLine("    public static global::System.Text.Json.JsonSerializerOptions AddZeroAllocValueObjectConverters(this global::System.Text.Json.JsonSerializerOptions options)");
    sb.AppendLine("    {");
    foreach (var (type, _) in valueObjects)
    {
        var converterFqn = BuildConverterFqn(type);
        sb.AppendLine($"        options.Converters.Add(new {converterFqn}());");
    }
    sb.AppendLine("        return options;");
    sb.AppendLine("    }");
    sb.AppendLine("}");
    return sb.ToString();
}

private static string BuildConverterFqn(INamedTypeSymbol type)
{
    var ns = type.ContainingNamespace.IsGlobalNamespace ? "" : type.ContainingNamespace.ToDisplayString();
    var converterName = $"{type.Name}SystemTextJsonConverter";
    return string.IsNullOrEmpty(ns) ? $"global::{converterName}" : $"global::{ns}.{converterName}";
}
```

The `global::` FQN ensures no ambiguity if the consumer's namespace ever collides with a generated type name.

### Task 1.4: Run — expect FAIL still (emitter exists but generator not wired)

```bash
dotnet test tests/ZeroAlloc.Serialisation.Generator.Tests -c Release --filter "FullyQualifiedName~ValueObjectRegistrarEmissionTests"
```

Expected: still 1/1 failing — the test invokes `SerializerGenerator` which doesn't know to call the new emitter yet. Phase 2 wires it.

### Task 1.5: Commit

```bash
git add src/ZeroAlloc.Serialisation.Generator/ValueObjectEmitter.cs \
        tests/ZeroAlloc.Serialisation.Generator.Tests/ValueObjectRegistrarEmissionTests.cs
git commit -m "feat(generator): EmitSystemTextJsonRegistrar method + failing snapshot

Per-assembly registrar that emits a public AddZeroAllocValueObjectConverters
extension method listing every generated converter. The method lives in the
ZeroAlloc.Serialisation.SystemTextJson namespace so consumers discover it
via the using they already have. Generator wire-in lands in the next commit."
```

---

## Phase 2 — Wire the registrar pipeline into `SerializerGenerator.Initialize` (25 min, 4 tasks)

### Task 2.1: Locate the existing per-type pipeline

**File:** `src/ZeroAlloc.Serialisation.Generator/SerializerGenerator.cs`

Find the existing `ForAttributeWithMetadataName("ZeroAlloc.ValueObjects.ValueObjectAttribute", ...)` pipeline added in 2.3.0 Phase 3. It looks roughly like:

```csharp
var valueObjectCandidates = context.SyntaxProvider
    .ForAttributeWithMetadataName(
        "ZeroAlloc.ValueObjects.ValueObjectAttribute",
        predicate: static (node, _) => node is StructDeclarationSyntax || node is RecordDeclarationSyntax,
        transform: static (ctx, _) => (INamedTypeSymbol)ctx.TargetSymbol);

var withCompilation = valueObjectCandidates.Combine(context.CompilationProvider);

context.RegisterSourceOutput(withCompilation, static (sourceCtx, pair) =>
{
    var (candidate, compilation) = pair;
    // ... per-type STJ + MessagePack + MemoryPack emission ...
});
```

This stays unchanged. The new registrar pipeline is a sibling.

### Task 2.2: Add the registrar pipeline

**File:** `src/ZeroAlloc.Serialisation.Generator/SerializerGenerator.cs`

Immediately after the existing per-type `RegisterSourceOutput` (still inside `Initialize`), append:

```csharp
// 2.3.1: per-assembly registrar emission. Batches every [ValueObject]
// candidate found in the compilation and emits a single
// ValueObjectJsonConvertersExtensions class with one entry per type.
// Required for JsonSerializerContext consumers — STJ's source generator
// doesn't see the [JsonConverter] attribute the per-type pipeline emits.
var valueObjectsCollected = valueObjectCandidates.Collect();
var registrarInput = valueObjectsCollected.Combine(context.CompilationProvider);

context.RegisterSourceOutput(registrarInput, static (sourceCtx, pair) =>
{
    var (allCandidates, compilation) = pair;

    if (!ValueObjectEmitter.ReferencesSystemTextJson(compilation)) return;

    var detected = new System.Collections.Generic.List<(INamedTypeSymbol Type, IPropertySymbol UnderlyingProperty)>(allCandidates.Length);
    foreach (var candidate in allCandidates)
    {
        var d = ModelExtractor.TryGetTransparentValueObject(candidate);
        if (d is not null) detected.Add(d.Value);
    }

    if (detected.Count == 0) return;

    var source = ValueObjectEmitter.EmitSystemTextJsonRegistrar(detected);
    sourceCtx.AddSource("ValueObjectJsonConvertersExtensions.g.cs", source);
});
```

`valueObjectCandidates` is the same `IncrementalValuesProvider<INamedTypeSymbol>` declared by the existing per-type pipeline — reuse it via `.Collect()` rather than building a parallel `ForAttributeWithMetadataName` pipeline. Cheaper, no redundant syntax-tree visits.

### Task 2.3: Run — expect PASS

```bash
dotnet test tests/ZeroAlloc.Serialisation.Generator.Tests -c Release --filter "FullyQualifiedName~ValueObjectRegistrarEmissionTests"
```

Expected: 1/1 pass.

### Task 2.4: Commit

```bash
git add src/ZeroAlloc.Serialisation.Generator/SerializerGenerator.cs
git commit -m "feat(generator): wire registrar pipeline into SerializerGenerator.Initialize

Sibling pipeline to the existing per-type value-object emission. Uses
.Collect() on the shared valueObjectCandidates provider so the syntax tree
is walked exactly once. Gated on ReferencesSystemTextJson(compilation) and
detected.Count > 0 to avoid emitting an empty extension class."
```

---

## Phase 3 — Regression nets (20 min, 4 tasks)

### Task 3.1: Add the no-value-objects regression test

**File:** `tests/ZeroAlloc.Serialisation.Generator.Tests/ValueObjectRegistrarEmissionTests.cs`

Append:

```csharp
[Fact]
public void Registrar_NotEmitted_WhenNoValueObjectsPresent()
{
    var source = """
        namespace TestModels;

        // No [ValueObject] attribute anywhere.
        public class Plain
        {
            public int Value { get; set; }
        }
        """;

    var result = RunGenerator(source, withSystemTextJson: true);

    Assert.DoesNotContain(result.GeneratedTrees,
        t => t.FilePath.EndsWith("ValueObjectJsonConvertersExtensions.g.cs", StringComparison.Ordinal));
}
```

### Task 3.2: Add the backend-not-referenced regression test

Append:

```csharp
[Fact]
public void Registrar_NotEmitted_WhenSystemTextJsonBackendNotReferenced()
{
    var source = """
        using ZeroAlloc.ValueObjects;
        namespace TestModels;

        [ValueObject]
        public readonly partial struct CustomerId
        {
            public int Value { get; }
            public CustomerId(int value) => Value = value;
        }
        """;

    // Note: withSystemTextJson = false (default).
    var result = RunGenerator(source);

    Assert.DoesNotContain(result.GeneratedTrees,
        t => t.FilePath.EndsWith("ValueObjectJsonConvertersExtensions.g.cs", StringComparison.Ordinal));
}
```

This mirrors the gating logic for per-type converters — assemblies that don't reference the SystemTextJson backend pay zero code-gen for the value-object pipeline.

### Task 3.3: Run — expect 3/3 pass (Phase 1 + 2 regressions)

```bash
dotnet test tests/ZeroAlloc.Serialisation.Generator.Tests -c Release --filter "FullyQualifiedName~ValueObjectRegistrarEmissionTests"
```

Expected: 3/3 pass.

### Task 3.4: Commit

```bash
git add tests/ZeroAlloc.Serialisation.Generator.Tests/ValueObjectRegistrarEmissionTests.cs
git commit -m "test(generator): regression nets for registrar gating

Two regression tests: no emission when no [ValueObject] types present in
the assembly (empty-class avoidance) and no emission when the SystemTextJson
backend assembly is not referenced (per-backend gating consistency)."
```

---

## Phase 4 — Integration test against `JsonSerializerContext` (35 min, 4 tasks)

This is the load-bearing test. It proves the gap that closed PR #127 is actually fixed.

### Task 4.1: Write the failing integration test

**File (NEW):** `tests/ZeroAlloc.Serialisation.Tests/ValueObjectJsonContextRoundTripTests.cs`

```csharp
using System.Text.Json;
using System.Text.Json.Serialization;
using Xunit;
using ZeroAlloc.Serialisation.SystemTextJson;

namespace ZeroAlloc.Serialisation.Tests;

public class ValueObjectJsonContextRoundTripTests
{
    [Fact]
    public void CustomerDto_WithJsonContext_RoundTrips_AsBareInteger()
    {
        var options = new JsonSerializerOptions
        {
            TypeInfoResolver = JsonContextRoundTripContext.Default,
        };
        options.AddZeroAllocValueObjectConverters();

        var dto = new JsonContextCustomerDto(new JsonContextCustomerId(42), "alice");
        var json = JsonSerializer.Serialize(dto, options);

        // Critical: the id field must be bare integer, NOT { "value": 42 }.
        // If the registrar isn't wired correctly, the JsonContext typeinfo
        // serializes JsonContextCustomerId as an object — and this assertion
        // catches it.
        Assert.Contains("\"id\":42", json);
        Assert.DoesNotContain("\"value\"", json);

        var roundTripped = JsonSerializer.Deserialize<JsonContextCustomerDto>(json, options);
        Assert.Equal(dto, roundTripped);
    }
}

// Test models live in the same file so the test is self-contained.
[ZeroAlloc.ValueObjects.ValueObject]
public readonly partial struct JsonContextCustomerId
{
    public int Value { get; }
    public JsonContextCustomerId(int value) => Value = value;
}

public sealed record JsonContextCustomerDto(JsonContextCustomerId Id, string Name);

[JsonSerializable(typeof(JsonContextCustomerDto))]
[JsonSerializable(typeof(JsonContextCustomerId))]
internal sealed partial class JsonContextRoundTripContext : JsonSerializerContext { }

// Stub the [ValueObject] attribute so the test source compiles even if the
// runtime ZA.ValueObjects package isn't a direct dependency. Keep it minimal —
// the FQN must match what the generator looks up.
namespace ZeroAlloc.ValueObjects
{
    [System.AttributeUsage(System.AttributeTargets.Class | System.AttributeTargets.Struct)]
    public sealed class ValueObjectAttribute : System.Attribute { }
}
```

The `[JsonSerializable(typeof(JsonContextCustomerId))]` declaration is intentional — it forces `JsonContextRoundTripContext`'s source-gen to emit typeinfo for the value-object. Without the registrar, that typeinfo serializes the struct as `{"value":42}`. With the registrar, `options.Converters` wins and the wire is bare `42`.

### Task 4.2: Run — expect FAIL or compile error

```bash
dotnet test tests/ZeroAlloc.Serialisation.Tests -c Release --filter "FullyQualifiedName~ValueObjectJsonContextRoundTripTests"
```

Expected: either compile error (`AddZeroAllocValueObjectConverters` not found — the registrar isn't being emitted into the test compilation) or test failure (`"value"` substring found in output).

The test project at `tests/ZeroAlloc.Serialisation.Tests/ZeroAlloc.Serialisation.Tests.csproj` already wires the generator as `OutputItemType="Analyzer" ReferenceOutputAssembly="false"` from the 2.3.0 work — so the generator should run. If `AddZeroAllocValueObjectConverters` resolves but the test still fails on the substring assertion, that's the gap being reproduced.

### Task 4.3: Verify the registrar is being emitted

If the test fails with "method not found", inspect the obj directory:

```bash
ls tests/ZeroAlloc.Serialisation.Tests/obj/Release/net*/ZeroAlloc.Serialisation.Generator/ZeroAlloc.Serialisation.Generator.SerializerGenerator/
```

Expected: `ValueObjectJsonConvertersExtensions.g.cs` is in the listing. If not, the Phase 2 wire-in isn't reaching the test compilation — return to Phase 2 and check the gating logic against the test's referenced assemblies.

If the file IS there but the test still fails on the substring, the registrar IS working — but the test's assertions or the JsonContext typeinfo precedence need adjusting. Read the actual emitted JSON via a `Debug.WriteLine` or breakpoint to see what shape is being produced.

### Task 4.4: Run — expect PASS + commit

```bash
dotnet test -c Release
```

Expected: full suite green — every existing test + the new integration test (89 total: 38 generator + 51 runtime).

```bash
git add tests/ZeroAlloc.Serialisation.Tests/ValueObjectJsonContextRoundTripTests.cs
git commit -m "test(integration): JsonSerializerContext round-trip through registrar

The load-bearing regression net for the 2.3.0 → 2.3.1 fix: a [ValueObject]
inside a JsonSerializerContext-managed DTO serializes to the underlying
primitive wire format only when AddZeroAllocValueObjectConverters() is
called. Asserts both the positive case (the JSON contains bare integer)
and the negative case (no 'value' key — i.e. the registrar takes precedence
over the JsonContext default object-shape typeinfo)."
```

---

## Phase 5 — Docs + backlog + ship (25 min, 5 tasks)

### Task 5.1: Update `docs/backlog.md`

**File:** `docs/backlog.md`

The 2.3.0 V1 entry already strikes V1 as shipped. Add a new struck-through V1.5 entry below it (or above the existing V2/V3 entries if any), file V2 (MessagePack/MemoryPack registrar helpers) and V3 (Bebop backend) as deferred.

Read the current file first to see the existing structure, then append:

```markdown

---

## ~~V1.5 — JsonSerializerContext interop helper~~ — ✅ shipped 2.3.1 (2026-05-27)

**Shipped:** Generator emits a per-assembly public `ValueObjectJsonConvertersExtensions` class in the `ZeroAlloc.Serialisation.SystemTextJson` namespace, with an `AddZeroAllocValueObjectConverters` extension method on `JsonSerializerOptions`. Consumers using `JsonSerializerContext` source-gen call this once during STJ options configuration; STJ's `options.Converters` list takes precedence over the context's typeinfo, so the transparent-primitive wire format wins.

**Why it shipped:** 2.3.0's `[JsonConverter]` attribute approach works for reflection-based STJ but not for `JsonSerializerContext` source-gen — Roslyn incremental generators don't see each other's output in the same compilation pass. Surfaced during the [ZeroAlloc.Templates PR #127](https://github.com/ZeroAlloc-Net/ZeroAlloc.Templates/pull/127) migration; PR closed, this work unblocks the re-author.

**Design + plan:** [`docs/plans/2026-05-27-jsoncontext-interop-design.md`](plans/2026-05-27-jsoncontext-interop-design.md) + [`docs/plans/2026-05-27-jsoncontext-interop-helper.md`](plans/2026-05-27-jsoncontext-interop-helper.md).

---

## V2 — MessagePack registrar helper

MessagePack-CSharp with the AOT source generator has the analogous Roslyn-gens-can't-see-each-other gap. No current ZA consumer uses MessagePack with the AOT source-gen pipeline (the runtime composite resolver picks up `[MessagePackFormatter]` via reflection), but symmetry argues for a parallel `AddZeroAllocValueObjectFormatters` extension method on `IFormatterResolver` (or whatever MessagePack's options-equivalent is). Defer until a consumer surfaces.

## V3 — Bebop backend support

Adding [Bebop](https://github.com/6over3/bebop) as a fourth supported backend. The 2.3.0 per-backend-emission architecture (gating on `compilation.ReferencedAssemblyNames`) scales cleanly — same shape as the MessagePack/MemoryPack additions, swapping in Bebop's serialization primitives. Recorded as a future direction; no concrete timeline.
```

### Task 5.2: Update `docs/getting-started.md`

Read the file first. Find the "Value-object transparent serialization" subsection added in 2.3.0, and add a paragraph addressing the JsonSerializerContext case:

```markdown
### Using value-objects with `JsonSerializerContext`

When the consuming project uses STJ's source-generated `JsonSerializerContext` for AOT readiness, register the value-object converters explicitly during options setup:

```csharp
builder.Services.ConfigureHttpJsonOptions(o =>
{
    o.SerializerOptions.TypeInfoResolverChain.Insert(0, JsonContext.Default);
    o.SerializerOptions.AddZeroAllocValueObjectConverters();  // adds every [ValueObject] converter
});
```

`AddZeroAllocValueObjectConverters` is generated per assembly that declares `[ValueObject]` types — it lists each one and adds the converter to `options.Converters`. STJ consults that list before the context's typeinfo, so the underlying-primitive wire format takes precedence over default struct serialization. No `InternalsVisibleTo` required; no class-name coupling.

For reflection-based STJ (no `JsonSerializerContext`), nothing changes — the `[JsonConverter]` attribute the generator emits on the partial-struct extension is picked up automatically via reflection.
```

### Task 5.3: Run full suite one final time

```bash
cd c:/Projects/Prive/ZeroAlloc/ZeroAlloc.Serialisation
dotnet test -c Release
```

Expected: full suite green. Should be 89 total tests (87 from 2.3.0 + 3 new snapshot tests + 1 new integration test = 91; if the count differs check whether any 2.3.0 test was incidentally renamed/moved).

### Task 5.4: Commit docs + push + open PR

```bash
git add docs/backlog.md docs/getting-started.md
git commit -m "docs: file V1.5 + V2 + V3 in backlog; getting-started subsection for JsonContext"

git push -u origin feat/jsoncontext-interop-helper

gh pr create \
  --title "fix(generator): JsonSerializerContext-friendly converter registration helper" \
  --body "$(cat <<'EOF'
## Summary

Closes the gap surfaced during [ZeroAlloc.Templates PR #127](https://github.com/ZeroAlloc-Net/ZeroAlloc.Templates/pull/127). 2.3.0's transparent JsonConverter for `[ValueObject]` types relied on a `[JsonConverter(typeof(...))]` attribute on a generator-emitted partial-struct extension — which `JsonSerializerContext`'s source generator does not see (Roslyn incremental generators can't see each other's output in the same compilation pass). Templates using `JsonSerializerContext` for AOT readiness hit the gap and serialized typed IDs as `{"value":42}` instead of bare `42`.

2.3.1 adds a per-assembly public extension method that registers every generated converter into `JsonSerializerOptions.Converters`. STJ resolves `options.Converters` before the context's typeinfo, so the underlying-primitive wire format takes precedence.

## What changed

- **`EmitSystemTextJsonRegistrar`** — new emitter method that produces a single per-assembly `ValueObjectJsonConvertersExtensions` class with `AddZeroAllocValueObjectConverters(this JsonSerializerOptions)`.
- **New value-object pipeline** in `SerializerGenerator.Initialize` — uses `.Collect()` on the existing candidates to batch every `[ValueObject]` from the compilation, gated by `ReferencesSystemTextJson(compilation)` and `detected.Count > 0` (avoid empty class).
- **3 snapshot tests** — happy path (2 value-objects, registrar lists both), no-value-objects regression, backend-not-referenced regression.
- **1 integration test** — `JsonSerializerContext` round-trip asserting the registrar makes the wire bare-integer. Would have caught the 2.3.0 gap; lands now as the regression net.
- **Docs** — `backlog.md` strikes V1.5; files V2 (MessagePack registrar) and V3 (Bebop) as deferred. `getting-started.md` gains a JsonSerializerContext usage subsection.

## Decisions ([design doc](docs/plans/2026-05-27-jsoncontext-interop-design.md))

- **Registration via `options.Converters.Add`** — well-worn STJ pattern; precedence over `JsonSerializerContext` typeinfo is documented + verified.
- **One extension method per assembly** — `[ValueObject]` types tend to cluster in Domain/Common folders; one call registers them all.
- **Extension lives in `ZeroAlloc.Serialisation.SystemTextJson` namespace** — discoverable via the `using` consumers already have.
- **Converter classes stay `internal sealed`** — extension method is the only public surface; `XSystemTextJsonConverter` naming stays implementation-detail.
- **STJ only for 2.3.1** — MessagePack/MemoryPack don't currently have a consumer that hits the analogous gap. V2 backlog entry covers it when needed.

## SemVer

`2.3.0` → `2.3.1` (patch). Framing: 2.3.0 was incomplete for `JsonSerializerContext` consumers; this completes it. The new public extension method is small additive surface but doesn't justify a minor bump given the corrective framing.

## Test plan

- [x] `dotnet test -c Release` — all green locally (existing suite + 4 new tests)
- [ ] CI — green on this PR
- [ ] Follow-up after 2.3.1 propagates: re-author [ZeroAlloc.Templates PR #127](https://github.com/ZeroAlloc-Net/ZeroAlloc.Templates/pull/127) on the clean 2.3.1 foundation — templates call `o.SerializerOptions.AddZeroAllocValueObjectConverters()` in their `ConfigureHttpJsonOptions` block.

🤖 Generated with [Claude Code](https://claude.com/claude-code)
EOF
)"
```

### Task 5.5: Watch CI + STOP

```bash
gh pr checks --watch
```

If green, **stop and report the PR URL to the user**. Do NOT admin-merge — the user merges. After merge, release-please cuts `chore(main): release 2.3.1`; user merges that too. Verify NuGet:

```bash
curl -s "https://api.nuget.org/v3-flatcontainer/zeroalloc.serialisation/index.json" \
  | python -c "import sys, json; v = json.load(sys.stdin)['versions']; print('latest:', v[-1])"
```

Expected (after propagation): `latest: 2.3.1`.

The `docs/backlog.md` entry already strikes V1.5 as shipped — no follow-up commit needed.

---

## Verification checklist

- [ ] **Phase 1:** `EmitSystemTextJsonRegistrar` produces a single-file class with the right namespace, signature, and `Converters.Add` lines.
- [ ] **Phase 2:** Registrar pipeline runs via `.Collect()` on the shared candidates provider; gated by backend reference + non-empty detection.
- [ ] **Phase 3:** Empty-source and backend-not-referenced both produce zero registrar emission.
- [ ] **Phase 4:** Integration test proves `JsonSerializerContext` + `AddZeroAllocValueObjectConverters` produces bare-integer wire format.
- [ ] **Phase 5:** Docs filed; CI green; release-please cuts 2.3.1; NuGet propagates.

## Out of scope (deferred to backlog)

- **MessagePack registrar helper** — same Roslyn-gens-can't-see-each-other issue applies to MessagePack AOT source-gen but no current consumer hits it (V2).
- **Bebop backend** — future direction (V3).
- **A diagnostic when `JsonSerializerContext` is present without the helper called** — too noisy; the user may be using both intentionally.
- **MessagePack Guid/DateTime underlying-type support** — separate 2.3.x backlog item from 2.3.0 code review.
