# JsonTypeInfoResolver Implementation Plan

> **For Claude:** REQUIRED SUB-SKILL: Use superpowers:executing-plans to implement this plan task-by-task.

**Goal:** Ship ZeroAlloc.Serialisation 2.3.2 — emit a per-assembly internal `IJsonTypeInfoResolver` that returns pre-configured `JsonTypeInfo<T>` for every `[ValueObject]` type, so `JsonSerializerContext` consumers (including AOT) can resolve typeinfo at startup. Existing 2.3.1 `AddZeroAllocValueObjectConverters()` gains one body line inserting the resolver at chain index 0.

**Architecture:** New `EmitSystemTextJsonTypeInfoResolver` method on `ValueObjectEmitter` produces a single per-assembly file. The existing 2.3.1 registrar's emitted body gains `options.TypeInfoResolverChain.Insert(0, ValueObjectJsonTypeInfoResolver.Default)` after the `Converters.Add` calls. Same generator pipeline as 2.3.1's registrar — same `.Collect()` over `valueObjectCandidates`, same gating (STJ backend referenced + non-empty detection), additional `AddSource` call.

**Tech Stack:** .NET 10, Roslyn `IIncrementalGenerator`, xUnit, generator-snapshot tests, integration round-trip + typeinfo-resolution test against `JsonSerializerContext`.

**Design doc:** `docs/plans/2026-05-27-jsontypeinfo-resolver-design.md` (committed at `66a9952`).

**Working branch:** `feat/jsontypeinfo-resolver` (already created off `main` post-`c718159`; design committed).

---

## Phase 0 — Orient (5 min)

### Task 0.1: Read the three files you'll touch

**Files (read-only):**

- `src/ZeroAlloc.Serialisation.Generator/ValueObjectEmitter.cs` (~225 LOC, current HEAD includes 2.3.1's `EmitSystemTextJsonRegistrar`). The new emitter method lands as a sibling. Pay attention to the `EmitSystemTextJsonRegistrar` shape — yours mirrors it but emits a class instead of an extension method, and is called from the same `RegisterSourceOutput` callback.
- `src/ZeroAlloc.Serialisation.Generator/SerializerGenerator.cs` (~150 LOC, current HEAD includes 2.3.1's value-object registrar pipeline). The new emission lands inside the SAME `RegisterSourceOutput` callback as 2.3.1's registrar — just an additional `AddSource(...)` call. No new pipeline.
- `tests/ZeroAlloc.Serialisation.Generator.Tests/ValueObjectRegistrarEmissionTests.cs` (~70 LOC, the 2.3.1 snapshot test file). The new test file `ValueObjectResolverEmissionTests.cs` copies the `RunGenerator` helper shape exactly.

---

## Phase 1 — `EmitSystemTextJsonTypeInfoResolver` method + failing snapshot test (40 min, 5 tasks)

### Task 1.1: Write the failing snapshot test

**File (NEW):** `tests/ZeroAlloc.Serialisation.Generator.Tests/ValueObjectResolverEmissionTests.cs`

```csharp
using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Xunit;
using ZeroAlloc.Serialisation.Generator;

namespace ZeroAlloc.Serialisation.Generator.Tests;

public class ValueObjectResolverEmissionTests
{
    [Fact]
    public void Resolver_EmitsIJsonTypeInfoResolverClass_ListingAllValueObjectsInAssembly()
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
            .FirstOrDefault(t => t.FilePath.EndsWith("ValueObjectJsonTypeInfoResolver.g.cs", StringComparison.Ordinal));
        Assert.NotNull(emitted);
        var text = emitted!.ToString();

        // Namespace + class shape
        Assert.Contains("namespace ZeroAlloc.Serialisation.SystemTextJson;", text, StringComparison.Ordinal);
        Assert.Contains("internal sealed class ValueObjectJsonTypeInfoResolver : global::System.Text.Json.Serialization.Metadata.IJsonTypeInfoResolver", text, StringComparison.Ordinal);
        Assert.Contains("public static ValueObjectJsonTypeInfoResolver Default { get; } = new();", text, StringComparison.Ordinal);

        // GetTypeInfo signature
        Assert.Contains("public global::System.Text.Json.Serialization.Metadata.JsonTypeInfo? GetTypeInfo(", text, StringComparison.Ordinal);
        Assert.Contains("global::System.Type type", text, StringComparison.Ordinal);
        Assert.Contains("global::System.Text.Json.JsonSerializerOptions options", text, StringComparison.Ordinal);

        // One switch arm per value-object, each calling JsonMetadataServices.CreateValueInfo<T>
        Assert.Contains("if (type == typeof(global::TestModels.CustomerId))", text, StringComparison.Ordinal);
        Assert.Contains("return global::System.Text.Json.Serialization.Metadata.JsonMetadataServices.CreateValueInfo<global::TestModels.CustomerId>(options, new global::TestModels.CustomerIdSystemTextJsonConverter());", text, StringComparison.Ordinal);
        Assert.Contains("if (type == typeof(global::TestModels.OrderId))", text, StringComparison.Ordinal);
        Assert.Contains("return global::System.Text.Json.Serialization.Metadata.JsonMetadataServices.CreateValueInfo<global::TestModels.OrderId>(options, new global::TestModels.OrderIdSystemTextJsonConverter());", text, StringComparison.Ordinal);

        // Fallback null
        Assert.Contains("return null;", text, StringComparison.Ordinal);
    }

    [Fact]
    public void Registrar_AlsoInsertsResolverIntoChain()
    {
        // Verifies the 2.3.2 amplification of the 2.3.1 registrar: the method
        // body now contains the TypeInfoResolverChain.Insert call alongside
        // the existing Converters.Add lines.
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

        var result = RunGenerator(source, withSystemTextJson: true);

        Assert.Empty(result.Diagnostics);
        var registrar = result.GeneratedTrees
            .FirstOrDefault(t => t.FilePath.EndsWith("ValueObjectJsonConvertersExtensions.g.cs", StringComparison.Ordinal));
        Assert.NotNull(registrar);
        var text = registrar!.ToString();

        // 2.3.1 behaviour preserved
        Assert.Contains("options.Converters.Add(new global::TestModels.CustomerIdSystemTextJsonConverter());", text, StringComparison.Ordinal);

        // 2.3.2 addition
        Assert.Contains("options.TypeInfoResolverChain.Insert(0, global::ZeroAlloc.Serialisation.SystemTextJson.ValueObjectJsonTypeInfoResolver.Default);", text, StringComparison.Ordinal);
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

`RunGenerator` is duplicated from `ValueObjectRegistrarEmissionTests.cs` intentionally — tests stay self-contained. If a future refactor extracts the helper, both files migrate together.

### Task 1.2: Run — expect FAIL on both tests

```bash
cd c:/Projects/Prive/ZeroAlloc/ZeroAlloc.Serialisation
dotnet test tests/ZeroAlloc.Serialisation.Generator.Tests -c Release --filter "FullyQualifiedName~ValueObjectResolverEmissionTests"
```

Expected: 2/2 fail. First test fails because `ValueObjectJsonTypeInfoResolver.g.cs` isn't emitted yet; second fails because the existing 2.3.1 registrar doesn't contain the new `TypeInfoResolverChain.Insert` line.

### Task 1.3: Implement `EmitSystemTextJsonTypeInfoResolver` + amend the registrar

**File:** `src/ZeroAlloc.Serialisation.Generator/ValueObjectEmitter.cs`

Append a new method after `EmitSystemTextJsonRegistrar`:

```csharp
/// <summary>
/// Emits a single per-assembly internal IJsonTypeInfoResolver that returns
/// pre-configured JsonTypeInfo&lt;T&gt; for every value-object in the
/// assembly. Required for JsonSerializerContext consumers — the startup
/// JsonPropertyInfo.Configure walk asks the resolver chain for typeinfo,
/// and JsonContext's source-gen-emitted resolver doesn't have usable
/// metadata for [ValueObject] types (Roslyn gens can't see the
/// [JsonConverter] attribute the per-type emitter adds via a partial).
/// </summary>
internal static string EmitSystemTextJsonResolver(
    System.Collections.Generic.IReadOnlyList<(INamedTypeSymbol Type, IPropertySymbol UnderlyingProperty)> valueObjects)
{
    var sb = new System.Text.StringBuilder();
    sb.AppendLine("// <auto-generated/>");
    sb.AppendLine("#nullable enable");
    sb.AppendLine();
    sb.AppendLine("namespace ZeroAlloc.Serialisation.SystemTextJson;");
    sb.AppendLine();
    sb.AppendLine("internal sealed class ValueObjectJsonTypeInfoResolver : global::System.Text.Json.Serialization.Metadata.IJsonTypeInfoResolver");
    sb.AppendLine("{");
    sb.AppendLine("    public static ValueObjectJsonTypeInfoResolver Default { get; } = new();");
    sb.AppendLine();
    sb.AppendLine("    public global::System.Text.Json.Serialization.Metadata.JsonTypeInfo? GetTypeInfo(global::System.Type type, global::System.Text.Json.JsonSerializerOptions options)");
    sb.AppendLine("    {");
    foreach (var (type, _) in valueObjects)
    {
        var typeFqn = BuildTypeFqn(type);
        var converterFqn = BuildConverterFqn(type);
        sb.AppendLine($"        if (type == typeof({typeFqn}))");
        sb.AppendLine($"            return global::System.Text.Json.Serialization.Metadata.JsonMetadataServices.CreateValueInfo<{typeFqn}>(options, new {converterFqn}());");
    }
    sb.AppendLine("        return null;");
    sb.AppendLine("    }");
    sb.AppendLine("}");
    return sb.ToString();
}

private static string BuildTypeFqn(INamedTypeSymbol type)
{
    var ns = type.ContainingNamespace.IsGlobalNamespace ? "" : type.ContainingNamespace.ToDisplayString();
    return string.IsNullOrEmpty(ns) ? $"global::{type.Name}" : $"global::{ns}.{type.Name}";
}
```

`BuildConverterFqn` already exists from 2.3.1 — reuse it. Don't redeclare.

Then **modify `EmitSystemTextJsonRegistrar`** (also in this file). Find the line that appends `        return options;` and insert the chain-insert one line above:

```csharp
// Existing 2.3.1 body:
//   foreach (var (type, _) in valueObjects) {
//       var converterFqn = BuildConverterFqn(type);
//       sb.AppendLine($"        options.Converters.Add(new {converterFqn}());");
//   }
//   sb.AppendLine("        return options;");   ← BEFORE
//   sb.AppendLine("    }");
//   ...

// 2.3.2: insert chain-insert before `return options;`
sb.AppendLine("        options.TypeInfoResolverChain.Insert(0, global::ZeroAlloc.Serialisation.SystemTextJson.ValueObjectJsonTypeInfoResolver.Default);");
sb.AppendLine("        return options;");
```

Confirm the existing `EmitSystemTextJsonRegistrar` method's current shape before editing — if my reconstruction above is off, adjust the insertion point so the new line is the LAST statement before `return options;`. The test `Registrar_AlsoInsertsResolverIntoChain` will tell you if the substring is present.

### Task 1.4: Run — expect both tests STILL fail (emitter exists but generator not wired)

```bash
dotnet test tests/ZeroAlloc.Serialisation.Generator.Tests -c Release --filter "FullyQualifiedName~ValueObjectResolverEmissionTests"
```

Expected: still 2 failing. The new `EmitSystemTextJsonResolver` method exists but `SerializerGenerator` doesn't call it yet; the modified `EmitSystemTextJsonRegistrar` produces the new line, but the registrar test fails because the snapshot inspection needs the generator wire-in too (Phase 2). The first test fails with `Assert.NotNull(emitted)`; the second fails because the registrar text in the generated tree doesn't contain the new line yet (since the generator hasn't been re-invoked with the new emitter).

Actually: the second test SHOULD pass after this task — the registrar emitter is called from the same Phase-2 wire-in but the emitter method body was changed. Verify by inspecting:

```bash
dotnet test tests/ZeroAlloc.Serialisation.Generator.Tests -c Release --filter "FullyQualifiedName~Registrar_AlsoInsertsResolverIntoChain"
```

If it passes: great, the registrar's emitter change works and gets invoked by the existing 2.3.1 wire-in. If it still fails: the wire-in must be invoking the OLD emitter binary; rebuild and re-run.

Snapshot test #1 (`Resolver_EmitsIJsonTypeInfoResolverClass_...`) still fails because the generator doesn't call `EmitSystemTextJsonResolver` yet. Phase 2 wires it.

### Task 1.5: Commit

```bash
git add src/ZeroAlloc.Serialisation.Generator/ValueObjectEmitter.cs \
        tests/ZeroAlloc.Serialisation.Generator.Tests/ValueObjectResolverEmissionTests.cs
git commit -m "feat(generator): EmitSystemTextJsonResolver method + amended registrar

Per-assembly IJsonTypeInfoResolver that returns pre-configured
JsonTypeInfo<T> for each value-object via JsonMetadataServices.CreateValueInfo.
Registrar method body now also inserts the resolver at chain index 0
alongside the existing Converters.Add lines. Generator wire-in lands
in the next commit."
```

---

## Phase 2 — Wire resolver emission into `SerializerGenerator.Initialize` (15 min, 3 tasks)

### Task 2.1: Locate the 2.3.1 registrar pipeline

**File:** `src/ZeroAlloc.Serialisation.Generator/SerializerGenerator.cs`

Find the `RegisterSourceOutput` callback added in 2.3.1 that emits `ValueObjectJsonConvertersExtensions.g.cs`. It looks roughly like:

```csharp
context.RegisterSourceOutput(registrarInput, static (sourceCtx, pair) =>
{
    var (allCandidates, compilation) = pair;
    if (!ValueObjectEmitter.ReferencesSystemTextJson(compilation)) return;

    var detected = new List<(...)>();
    foreach (var c in allCandidates) { ... }

    if (detected.Count == 0) return;

    var source = ValueObjectEmitter.EmitSystemTextJsonRegistrar(detected);
    sourceCtx.AddSource("ValueObjectJsonConvertersExtensions.g.cs", source);
});
```

### Task 2.2: Add the resolver emission as a second `AddSource` in the SAME callback

**File:** `src/ZeroAlloc.Serialisation.Generator/SerializerGenerator.cs`

Immediately after the existing `sourceCtx.AddSource("ValueObjectJsonConvertersExtensions.g.cs", source)` line, append:

```csharp
            var resolverSource = ValueObjectEmitter.EmitSystemTextJsonResolver(detected);
            sourceCtx.AddSource("ValueObjectJsonTypeInfoResolver.g.cs", resolverSource);
```

Same gating (we're inside the same conditional block). Same input list (`detected`). No new pipeline — both the registrar and the resolver derive from the same batched candidates.

Add a brief comment above the new line explaining WHY there are two emissions:

```csharp
            // 2.3.2: alongside the registrar, emit an IJsonTypeInfoResolver
            // so JsonSerializerContext consumers can resolve value-object
            // typeinfo at startup (the registrar's Converters.Add only wins
            // at serialize/deserialize time — startup property configuration
            // hits the resolver chain directly).
```

### Task 2.3: Run, expect PASS + commit

```bash
dotnet test -c Release
```

Expected: full suite green — 92 from 2.3.1 + 2 new from this phase = 94.

```bash
git add src/ZeroAlloc.Serialisation.Generator/SerializerGenerator.cs
git commit -m "feat(generator): emit IJsonTypeInfoResolver alongside the registrar

Single additional AddSource in the existing per-assembly RegisterSourceOutput
callback. Same gating, same batched input. JsonSerializerContext consumers
now have value-object typeinfo available at startup."
```

---

## Phase 3 — Regression tests for resolver gating (15 min, 3 tasks)

The Phase 1+2 happy-path tests pass. Phase 3 locks in the negative cases — same shape as 2.3.1 Phase 3.

### Task 3.1: Append "no value-objects → no resolver" test

**File:** `tests/ZeroAlloc.Serialisation.Generator.Tests/ValueObjectResolverEmissionTests.cs`

```csharp
[Fact]
public void Resolver_NotEmitted_WhenNoValueObjectsPresent()
{
    var source = """
        namespace TestModels;

        public class Plain { public int Value { get; set; } }
        """;

    var result = RunGenerator(source, withSystemTextJson: true);

    Assert.DoesNotContain(result.GeneratedTrees,
        t => t.FilePath.EndsWith("ValueObjectJsonTypeInfoResolver.g.cs", StringComparison.Ordinal));
}
```

### Task 3.2: Append "no STJ backend → no resolver" test

```csharp
[Fact]
public void Resolver_NotEmitted_WhenSystemTextJsonBackendNotReferenced()
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

    var result = RunGenerator(source); // all backend flags default false

    Assert.DoesNotContain(result.GeneratedTrees,
        t => t.FilePath.EndsWith("ValueObjectJsonTypeInfoResolver.g.cs", StringComparison.Ordinal));
}
```

### Task 3.3: Run + commit

```bash
dotnet test tests/ZeroAlloc.Serialisation.Generator.Tests -c Release --filter "FullyQualifiedName~ValueObjectResolverEmissionTests"
```

Expected: 4/4 pass (Phase 1's 2 + Phase 3's 2).

```bash
git add tests/ZeroAlloc.Serialisation.Generator.Tests/ValueObjectResolverEmissionTests.cs
git commit -m "test(generator): regression nets for resolver gating

Two regression tests: no resolver emission when no [ValueObject] types
present (empty-class avoidance) and no resolver when SystemTextJson
backend not referenced (per-backend gating consistency)."
```

---

## Phase 4 — Integration test against `JsonSerializerContext` startup (20 min, 3 tasks)

This is the load-bearing test for 2.3.2. It directly verifies `options.GetTypeInfo(typeof(CustomerId))` returns non-null after `AddZeroAllocValueObjectConverters()` — which is exactly what ASP.NET Core's endpoint factory will call at startup.

### Task 4.1: Append the GetTypeInfo assertion test

**File:** `tests/ZeroAlloc.Serialisation.Tests/ValueObjectJsonContextRoundTripTests.cs`

The existing file (from 2.3.1) has two round-trip tests (`PascalCase` + `CamelCase`). Append:

```csharp
[Fact]
public void GetTypeInfo_ForValueObject_AfterRegistrar_ReturnsNonNull()
{
    // The load-bearing test for the 2.3.1 -> 2.3.2 fix: ASP.NET Core's
    // endpoint factory pre-resolves typeinfo for every binding type at
    // startup, hitting the resolver chain directly (not the Converters
    // list). Without the 2.3.2 resolver insert, this call throws
    // NotSupportedException("metadata not provided").
    var options = new JsonSerializerOptions
    {
        TypeInfoResolver = JsonContextRoundTripContext.Default,
    };
    options.AddZeroAllocValueObjectConverters();

    var typeInfo = options.GetTypeInfo(typeof(JsonContextCustomerId));

    Assert.NotNull(typeInfo);
    Assert.Equal(typeof(JsonContextCustomerId), typeInfo.Type);
}

[Fact]
public void GetTypeInfo_ForUnrelatedType_FallsThroughToJsonContext()
{
    // Confirms the resolver chain is composing correctly: our resolver
    // returns null for non-value-object types, falling through to
    // JsonContext.Default which DOES have typeinfo for DTOs.
    var options = new JsonSerializerOptions
    {
        TypeInfoResolver = JsonContextRoundTripContext.Default,
    };
    options.AddZeroAllocValueObjectConverters();

    var typeInfo = options.GetTypeInfo(typeof(JsonContextCustomerDto));

    Assert.NotNull(typeInfo);
    Assert.Equal(typeof(JsonContextCustomerDto), typeInfo.Type);
}
```

These use the same fixture types the 2.3.1 round-trip tests already declared in the file (`JsonContextCustomerId`, `JsonContextCustomerDto`, `JsonContextRoundTripContext`). Don't redeclare.

### Task 4.2: Run + verify

```bash
cd c:/Projects/Prive/ZeroAlloc/ZeroAlloc.Serialisation
dotnet test -c Release
```

Expected: 96/96 (94 + 2 new). Verify both new tests pass — if either fails, the resolver isn't being inserted into the chain, or the generator-emitted file isn't reaching this test compilation.

If `GetTypeInfo_ForValueObject_AfterRegistrar_ReturnsNonNull` fails with `NotSupportedException("metadata not provided")`: the resolver chain insert didn't happen. Check that Phase 1's amendment to `EmitSystemTextJsonRegistrar` actually wrote the `TypeInfoResolverChain.Insert` line; check the generated file in `tests/ZeroAlloc.Serialisation.Tests/obj/Release/.../ValueObjectJsonConvertersExtensions.g.cs`.

### Task 4.3: Commit

```bash
git add tests/ZeroAlloc.Serialisation.Tests/ValueObjectJsonContextRoundTripTests.cs
git commit -m "test(integration): GetTypeInfo resolves for value-objects after registrar

Two new tests against the actual ASP.NET Core startup path: GetTypeInfo
for a [ValueObject] type returns non-null typeinfo (would throw
NotSupportedException without 2.3.2's resolver insert), and GetTypeInfo
for a non-value-object DTO falls through correctly to JsonContext.

The PascalCase + CamelCase round-trip tests from 2.3.1 stay green and
transitively exercise the resolver via the same registrar call."
```

---

## Phase 5 — Docs + backlog + ship (20 min, 4 tasks)

### Task 5.1: Update `docs/backlog.md`

Read first. Append a new V1.6 entry struck-through as shipped, immediately after the existing V1.5 (2.3.1) entry:

```markdown

---

## ~~V1.6 — JsonTypeInfoResolver emission for AOT/JsonContext interop~~ — ✅ shipped 2.3.2 (2026-05-27)

**Shipped:** Generator emits a per-assembly internal `ValueObjectJsonTypeInfoResolver` that returns pre-configured `JsonTypeInfo<T>` for every `[ValueObject]` type via `JsonMetadataServices.CreateValueInfo<T>`. The existing `AddZeroAllocValueObjectConverters` registrar inserts this resolver at `TypeInfoResolverChain` index 0 alongside its existing `Converters.Add` calls.

**Why it shipped:** 2.3.1 closed the runtime serialize/deserialize gap but not the startup typeinfo gap. ASP.NET Core's request-delegate factory pre-resolves typeinfo at startup, hitting the resolver chain directly (not the Converters list). [ZeroAlloc.Templates PR #128](https://github.com/ZeroAlloc-Net/ZeroAlloc.Templates/pull/128) za-clean AOT smoke caught this — same Roslyn-gens-can't-see-each-other root cause as the original 2.3.0 gap, just surfacing in a different STJ pipeline corner.

**Design + plan:** [`docs/plans/2026-05-27-jsontypeinfo-resolver-design.md`](plans/2026-05-27-jsontypeinfo-resolver-design.md) + [`docs/plans/2026-05-27-jsontypeinfo-resolver.md`](plans/2026-05-27-jsontypeinfo-resolver.md).
```

Update the existing V2 entry (MessagePack helper) to mention that under AOT source-gen MessagePack has an analogous typeinfo gap to fix at the same time when V2 lands.

### Task 5.2: Update `docs/getting-started.md`

Read first. Find the "Using value-objects with `JsonSerializerContext`" subsection from 2.3.1. Add a call-order note:

```markdown
**Call order matters.** `AddZeroAllocValueObjectConverters()` inserts the value-object typeinfo resolver at chain index 0. Call it **after** your `Insert(0, JsonContext.Default)` so the resulting chain is `[VO-resolver, JsonContext.Default]` — value-objects resolved by us, DTOs by JsonContext. Reversing the order produces `[JsonContext.Default, VO-resolver]` and value-object typeinfo would be served by JsonContext's broken stub instead.
```

### Task 5.3: Run full suite one final time

```bash
cd c:/Projects/Prive/ZeroAlloc/ZeroAlloc.Serialisation
dotnet test -c Release
```

Expected: 96/96 green.

### Task 5.4: Commit docs + push + open PR

```bash
git add docs/backlog.md docs/getting-started.md
git commit -m "docs: file V1.6 shipped; getting-started call-order note"

git push -u origin feat/jsontypeinfo-resolver

gh pr create \
  --title "fix(generator): emit JsonTypeInfoResolver for AOT-friendly value-object metadata" \
  --body "$(cat <<'EOF'
## Summary

Closes the AOT/JsonContext startup-typeinfo gap surfaced by the [ZeroAlloc.Templates PR #128](https://github.com/ZeroAlloc-Net/ZeroAlloc.Templates/pull/128) za-clean smoke jobs. 2.3.1's converter registrar closed the runtime serialize/deserialize gap; this closes the startup property-configuration gap.

## What changed

- **`EmitSystemTextJsonResolver`** — new emitter method producing a single per-assembly `internal sealed class ValueObjectJsonTypeInfoResolver : IJsonTypeInfoResolver` with one switch arm per `[ValueObject]` type, each calling `JsonMetadataServices.CreateValueInfo<T>(options, converter)`.
- **Existing `EmitSystemTextJsonRegistrar` amended** to insert the resolver at `TypeInfoResolverChain` index 0 alongside its existing `Converters.Add` calls. Single new line in the emitted method body; no API change.
- **`SerializerGenerator.Initialize`** gains a second `AddSource` call inside the existing 2.3.1 value-object `RegisterSourceOutput` callback. Same gating, same batched input — no new pipeline.
- **4 new generator-snapshot tests** (resolver shape, registrar amendment, empty-source regression, no-backend regression) + **2 new integration tests** (`GetTypeInfo` returns non-null for value-objects, falls through to JsonContext for DTOs).
- **Docs:** backlog V1.6 filed as shipped; getting-started gains a call-order note.

## Decisions ([design doc](docs/plans/2026-05-27-jsontypeinfo-resolver-design.md))

- **Extend the existing registrar method, don't add a new one.** Consumers upgrading from 2.3.1 to 2.3.2 transparently gain AOT support — no API surface change.
- **Resolver class stays internal.** Public contract is the extension method, not the resolver class name. Mirrors converter visibility from 2.3.0.
- **Keep the `[JsonConverter]` attribute emission.** Reflection-based STJ users (no JsonContext) continue to work via attribute auto-discovery; the resolver is additive infrastructure for source-gen consumers.

## SemVer

`2.3.1` -> `2.3.2` (patch). Strictly additive: one new emitted file + one extra line in the existing registrar method body. Reflection-based and JIT/JsonContext-without-AOT consumers see no observable change.

## Test plan

- [x] `dotnet test -c Release` — 96/96 locally
- [ ] CI — green on this PR
- [ ] Follow-up after 2.3.2 propagates: rebase ZeroAlloc.Templates PR #128 onto 2.3.2 and re-run smoke jobs

🤖 Generated with [Claude Code](https://claude.com/claude-code)
EOF
)"
```

**Stop here** — do not admin-merge. User handles the merge + release-please + NuGet propagation.

---

## Verification checklist

- [ ] **Phase 1:** `EmitSystemTextJsonResolver` produces the right class shape; `EmitSystemTextJsonRegistrar` body has the chain-insert line.
- [ ] **Phase 2:** Generator emits both files (`ValueObjectJsonConvertersExtensions.g.cs` AND `ValueObjectJsonTypeInfoResolver.g.cs`) when conditions match.
- [ ] **Phase 3:** Empty-source / no-backend correctly suppress resolver emission.
- [ ] **Phase 4:** `GetTypeInfo(CustomerId)` returns non-null after registrar; `GetTypeInfo(CustomerDto)` falls through to JsonContext.
- [ ] **Phase 5:** Docs filed; CI green; release-please cuts 2.3.2; NuGet propagates.

## Out of scope (deferred to backlog)

- **MessagePack/MemoryPack typeinfo-resolver equivalents** — same architectural fix shape applies to MessagePack's AOT source-gen; defer until a real consumer surfaces (V2).
- **A diagnostic when call order is wrong** — too noisy, can't reliably detect.
- **Bebop backend** (V3 in backlog) — no change.
- **MessagePack Guid/DateTime underlying-type support** — pre-existing backlog item from 2.3.0 review.
