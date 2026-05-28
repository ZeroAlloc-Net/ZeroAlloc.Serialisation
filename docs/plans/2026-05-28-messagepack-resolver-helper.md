# MessagePack Resolver + Helper Implementation Plan

> **For Claude:** REQUIRED SUB-SKILL: Use superpowers:executing-plans to implement this plan task-by-task.

**Goal:** Ship ZeroAlloc.Serialisation 2.3.3 — emit a per-assembly internal `IFormatterResolver` returning pre-configured formatters for every `[ValueObject]` type, plus a public extension method `AddZeroAllocValueObjectFormatters(this MessagePackSerializerOptions)` that prepends our resolver to the composite chain. Closes the MessagePack analog of the AOT/JsonContext gap that 2.3.1 + 2.3.2 closed for STJ.

**Architecture:** New `EmitMessagePackResolver` method on `ValueObjectEmitter` produces a single per-assembly file containing BOTH the resolver class AND the extension method (single-file because MessagePack has only one registration shape — the resolver chain — unlike STJ's split Converters list + TypeInfoResolverChain). Generator pipeline adds one gated `AddSource` call to the existing 2.3.1 value-object `RegisterSourceOutput` callback, symmetric with the STJ branch.

**Tech Stack:** .NET 10, Roslyn `IIncrementalGenerator`, xUnit, generator-snapshot tests (`CSharpCompilation.Create` + `CSharpGeneratorDriver`), integration round-trip + AOT smoke against `MessagePack.SourceGenerator`-emitted `GeneratedMessagePackResolver`.

**Design doc:** `docs/plans/2026-05-28-messagepack-resolver-helper-design.md` (committed at `c7d09a4`).

**Working branch:** `feat/messagepack-resolver-helper` (already created off `main` post-`f63d2c1` — the 2.3.2 release; design committed).

**Dependency:** [PR #38](https://github.com/ZeroAlloc-Net/ZeroAlloc.Serialisation/pull/38) (V1/STJ aot-smoke extension) introduces the shared `ValueObjectId` + `ValueObjectDto` fixtures in `samples/ZeroAlloc.Serialisation.AotSmoke/`. Phase 4 (smoke extension) reuses those fixtures. If PR #38 hasn't merged when starting Phase 4, two options:

1. **Rebase this branch onto `chore/aot-smoke-cover-value-objects`** (PR #38's branch) so the fixtures are present.
2. **Declare `ValueObjectId` inline** in this PR's smoke addition, then move it to the shared fixture file after #38 merges (small follow-up cleanup).

Phase 4 task copy assumes option (1) — rebased branch. Adjust if going with option (2).

---

## Phase 0 — Orient (5 min)

### Task 0.1: Read the 2.3.2 STJ resolver implementation as the model

**Files (read-only):**

- `src/ZeroAlloc.Serialisation.Generator/ValueObjectEmitter.cs` (~245 LOC current HEAD). Pay attention to:
  - `EmitSystemTextJsonResolver` (lines ~126-160) — the 2.3.2 STJ resolver pattern. New `EmitMessagePackResolver` mirrors its shape but emits BOTH the resolver class AND the extension method in one file.
  - `EmitMessagePackFormatter` (lines ~167-220) — the existing 2.3.0 per-type formatter emission. Your new code reads the FQN of these formatter classes and references them from the resolver.
  - `BuildConverterFqn` (helper). New `BuildFormatterFqn` sibling has the same shape but produces `<Name>MessagePackFormatter` instead of `<Name>SystemTextJsonConverter`.
- `src/ZeroAlloc.Serialisation.Generator/SerializerGenerator.cs` (~155 LOC). The 2.3.1 value-object `RegisterSourceOutput` callback is the insertion point — your new code adds a second gated `AddSource` block alongside the existing STJ branch.
- `tests/ZeroAlloc.Serialisation.Generator.Tests/ValueObjectResolverEmissionTests.cs` (~120 LOC). The 2.3.2 STJ snapshot test file. New `ValueObjectMessagePackResolverEmissionTests.cs` copies the `RunGenerator` helper shape.

### Task 0.2: Skim MessagePack-CSharp's resolver pattern

If unfamiliar with MessagePack-CSharp's resolver chain, quickly read:

- `MessagePack.Resolvers.CompositeResolver.Create(...)` — combines multiple resolvers; the first non-null `GetFormatter<T>` wins
- `MessagePackSerializerOptions.WithResolver(IFormatterResolver)` — returns NEW immutable options
- `IFormatterResolver.GetFormatter<T>()` — generic; canonical implementation uses a `FormatterCache<T>` static nested class for per-T JIT specialization

Reference implementation: `StandardResolver.cs` in MessagePack-CSharp source. Pattern is well-established; the new emitter just emits a specialized resolver that handles the value-object types it knows about and returns null for everything else.

---

## Phase 1 — `EmitMessagePackResolver` method + failing snapshot tests (45 min, 5 tasks)

### Task 1.1: Write the failing snapshot tests

**File (NEW):** `tests/ZeroAlloc.Serialisation.Generator.Tests/ValueObjectMessagePackResolverEmissionTests.cs`

```csharp
using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Xunit;
using ZeroAlloc.Serialisation.Generator;

namespace ZeroAlloc.Serialisation.Generator.Tests;

public class ValueObjectMessagePackResolverEmissionTests
{
    [Fact]
    public void Resolver_EmitsIFormatterResolverClass_ListingAllValueObjectsInAssembly()
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

        var result = RunGenerator(source, withMessagePack: true);

        Assert.Empty(result.Diagnostics);
        var emitted = result.GeneratedTrees
            .FirstOrDefault(t => t.FilePath.EndsWith("ValueObjectMessagePackResolverExtensions.g.cs", StringComparison.Ordinal));
        Assert.NotNull(emitted);
        var text = emitted!.ToString();

        // Namespace + resolver class shape
        Assert.Contains("namespace ZeroAlloc.Serialisation.MessagePack;", text, StringComparison.Ordinal);
        Assert.Contains("internal sealed class ValueObjectMessagePackResolver : global::MessagePack.IFormatterResolver", text, StringComparison.Ordinal);
        Assert.Contains("public static ValueObjectMessagePackResolver Default { get; } = new();", text, StringComparison.Ordinal);

        // FormatterCache<T> generic-static-cache nested class
        Assert.Contains("private static class FormatterCache<T>", text, StringComparison.Ordinal);
        Assert.Contains("public static readonly global::MessagePack.Formatters.IMessagePackFormatter<T>?", text, StringComparison.Ordinal);

        // GetFormatter<T> + untyped lookup
        Assert.Contains("public global::MessagePack.Formatters.IMessagePackFormatter<T>? GetFormatter<T>()", text, StringComparison.Ordinal);
        Assert.Contains("FormatterCache<T>.Formatter", text, StringComparison.Ordinal);
        Assert.Contains("private static object? GetFormatterUntyped(global::System.Type type)", text, StringComparison.Ordinal);

        // One switch arm per value-object, each instantiating the FQN'd formatter
        Assert.Contains("if (type == typeof(global::TestModels.CustomerId))", text, StringComparison.Ordinal);
        Assert.Contains("return new global::TestModels.CustomerIdMessagePackFormatter();", text, StringComparison.Ordinal);
        Assert.Contains("if (type == typeof(global::TestModels.OrderId))", text, StringComparison.Ordinal);
        Assert.Contains("return new global::TestModels.OrderIdMessagePackFormatter();", text, StringComparison.Ordinal);

        // Fallback null
        Assert.Contains("return null;", text, StringComparison.Ordinal);

        // Extension method
        Assert.Contains("public static class ValueObjectMessagePackFormattersExtensions", text, StringComparison.Ordinal);
        Assert.Contains("public static global::MessagePack.MessagePackSerializerOptions AddZeroAllocValueObjectFormatters(this global::MessagePack.MessagePackSerializerOptions options)", text, StringComparison.Ordinal);
        Assert.Contains("global::MessagePack.Resolvers.CompositeResolver.Create", text, StringComparison.Ordinal);
        Assert.Contains("ValueObjectMessagePackResolver.Default", text, StringComparison.Ordinal);
        Assert.Contains("options.Resolver", text, StringComparison.Ordinal);
        Assert.Contains("options.WithResolver(", text, StringComparison.Ordinal);
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

`RunGenerator` is duplicated from the existing sibling test files intentionally — keeps tests self-contained, matches the existing convention.

### Task 1.2: Run — expect FAIL

```bash
cd c:/Projects/Prive/ZeroAlloc/ZeroAlloc.Serialisation
dotnet test tests/ZeroAlloc.Serialisation.Generator.Tests -c Release --filter "FullyQualifiedName~ValueObjectMessagePackResolverEmissionTests"
```

Expected: 1 failed (the resolver file isn't emitted yet).

### Task 1.3: Implement `EmitMessagePackResolver` + `BuildFormatterFqn` helper

**File:** `src/ZeroAlloc.Serialisation.Generator/ValueObjectEmitter.cs`

Append a new method after `EmitMessagePackFormatter`:

```csharp
/// <summary>
/// Emits a single per-assembly file containing both an internal
/// IFormatterResolver listing every value-object's formatter AND a public
/// extension method AddZeroAllocValueObjectFormatters that prepends the
/// resolver to a MessagePackSerializerOptions chain. Required for
/// MessagePack.SourceGenerator (AOT) consumers — the AOT-generated resolver
/// doesn't have usable typeinfo for [ValueObject] types because Roslyn gens
/// can't see the [MessagePackFormatter] attribute our per-type emitter adds
/// via a partial-struct extension. Single file (unlike STJ's split) because
/// MessagePack has only one registration shape — the resolver chain.
/// </summary>
internal static string EmitMessagePackResolver(
    System.Collections.Generic.IReadOnlyList<(INamedTypeSymbol Type, IPropertySymbol UnderlyingProperty)> valueObjects)
{
    var sb = new System.Text.StringBuilder();
    sb.AppendLine("// <auto-generated/>");
    sb.AppendLine("#nullable enable");
    sb.AppendLine();
    sb.AppendLine("namespace ZeroAlloc.Serialisation.MessagePack;");
    sb.AppendLine();
    sb.AppendLine("/// <summary>");
    sb.AppendLine("/// Resolves IMessagePackFormatter&lt;T&gt; for every [ZeroAlloc.ValueObjects.ValueObject]");
    sb.AppendLine("/// partial struct in this assembly. Prepend via AddZeroAllocValueObjectFormatters.");
    sb.AppendLine("/// </summary>");
    sb.AppendLine("internal sealed class ValueObjectMessagePackResolver : global::MessagePack.IFormatterResolver");
    sb.AppendLine("{");
    sb.AppendLine("    public static ValueObjectMessagePackResolver Default { get; } = new();");
    sb.AppendLine();
    sb.AppendLine("    public global::MessagePack.Formatters.IMessagePackFormatter<T>? GetFormatter<T>()");
    sb.AppendLine("        => FormatterCache<T>.Formatter;");
    sb.AppendLine();
    sb.AppendLine("    private static class FormatterCache<T>");
    sb.AppendLine("    {");
    sb.AppendLine("        public static readonly global::MessagePack.Formatters.IMessagePackFormatter<T>? Formatter");
    sb.AppendLine("            = (global::MessagePack.Formatters.IMessagePackFormatter<T>?)GetFormatterUntyped(typeof(T));");
    sb.AppendLine("    }");
    sb.AppendLine();
    sb.AppendLine("    private static object? GetFormatterUntyped(global::System.Type type)");
    sb.AppendLine("    {");
    foreach (var (type, _) in valueObjects)
    {
        var typeFqn = BuildTypeFqn(type);
        var formatterFqn = BuildFormatterFqn(type);
        sb.AppendLine($"        if (type == typeof({typeFqn}))");
        sb.AppendLine($"            return new {formatterFqn}();");
    }
    sb.AppendLine("        return null;");
    sb.AppendLine("    }");
    sb.AppendLine("}");
    sb.AppendLine();
    sb.AppendLine("/// <summary>");
    sb.AppendLine("/// Registers every [ZeroAlloc.ValueObjects.ValueObject] partial struct's");
    sb.AppendLine("/// generator-emitted IMessagePackFormatter by prepending ValueObjectMessagePackResolver");
    sb.AppendLine("/// to a MessagePackSerializerOptions resolver chain. Call this AFTER setting your");
    sb.AppendLine("/// primary resolver (e.g. GeneratedMessagePackResolver.Instance) — call order");
    sb.AppendLine("/// matters; this method assumes the caller's options.Resolver is the fallback.");
    sb.AppendLine("/// </summary>");
    sb.AppendLine("public static class ValueObjectMessagePackFormattersExtensions");
    sb.AppendLine("{");
    sb.AppendLine("    public static global::MessagePack.MessagePackSerializerOptions AddZeroAllocValueObjectFormatters(this global::MessagePack.MessagePackSerializerOptions options)");
    sb.AppendLine("    {");
    sb.AppendLine("        var composite = global::MessagePack.Resolvers.CompositeResolver.Create(");
    sb.AppendLine("            ValueObjectMessagePackResolver.Default,");
    sb.AppendLine("            options.Resolver);");
    sb.AppendLine("        return options.WithResolver(composite);");
    sb.AppendLine("    }");
    sb.AppendLine("}");
    return sb.ToString();
}

private static string BuildFormatterFqn(INamedTypeSymbol type)
{
    var ns = type.ContainingNamespace.IsGlobalNamespace ? "" : type.ContainingNamespace.ToDisplayString();
    var formatterName = $"{type.Name}MessagePackFormatter";
    return string.IsNullOrEmpty(ns) ? $"global::{formatterName}" : $"global::{ns}.{formatterName}";
}
```

`BuildTypeFqn` already exists from 2.3.2 — reuse it. `BuildFormatterFqn` is the new sibling of `BuildConverterFqn`.

### Task 1.4: Run — expect resolver test still FAIL

```bash
dotnet test tests/ZeroAlloc.Serialisation.Generator.Tests -c Release --filter "FullyQualifiedName~ValueObjectMessagePackResolverEmissionTests"
```

Expected: still 1 failed — emitter exists but `SerializerGenerator` doesn't invoke it yet. That's Phase 2.

Confirm failure reason is `Assert.NotNull(emitted)` (file not in `GeneratedTrees`), NOT a compile error.

### Task 1.5: Commit

```bash
git add src/ZeroAlloc.Serialisation.Generator/ValueObjectEmitter.cs \
        tests/ZeroAlloc.Serialisation.Generator.Tests/ValueObjectMessagePackResolverEmissionTests.cs
git commit -m "feat(generator): EmitMessagePackResolver method + failing snapshot

Per-assembly MessagePack IFormatterResolver returning the generator-emitted
IMessagePackFormatter for each [ValueObject]. Single-file emission combining
the resolver class with the AddZeroAllocValueObjectFormatters extension
method on MessagePackSerializerOptions. Generator wire-in lands in the
next commit."
```

---

## Phase 2 — Wire MessagePack emission into `SerializerGenerator.Initialize` (15 min, 3 tasks)

### Task 2.1: Locate the existing 2.3.1 value-object callback

**File:** `src/ZeroAlloc.Serialisation.Generator/SerializerGenerator.cs`

Find the `RegisterSourceOutput` block that emits `ValueObjectJsonConvertersExtensions.g.cs` + `ValueObjectJsonTypeInfoResolver.g.cs`. The new MessagePack emission lands in the SAME callback as a sibling, gated on `ReferencesMessagePack(compilation)`.

### Task 2.2: Add the MessagePack emission

In the existing callback, after the two STJ `AddSource` calls (still inside the `detected.Count > 0` conditional), append:

```csharp
// 2.3.3: MessagePack equivalent of the STJ resolver — closes the
// MessagePack.SourceGenerator interop gap. Same shape, single-file.
if (ValueObjectEmitter.ReferencesMessagePack(compilation))
{
    var mpResolverSource = ValueObjectEmitter.EmitMessagePackResolver(detected);
    sourceCtx.AddSource("ValueObjectMessagePackResolverExtensions.g.cs", mpResolverSource);
}
```

Same `detected` list, same callback. The `ReferencesMessagePack(compilation)` gate is the existing 2.3.0 helper — reuse, don't redeclare.

### Task 2.3: Run + commit

```bash
dotnet test -c Release
```

Expected: full suite green — every existing test plus the Phase 1 resolver test now passes (red → green). Test count: 98 + 1 = 99.

```bash
git add src/ZeroAlloc.Serialisation.Generator/SerializerGenerator.cs
git commit -m "feat(generator): emit MessagePack resolver alongside STJ emissions

Single additional gated AddSource in the existing 2.3.1 value-object
RegisterSourceOutput callback. Same batched candidates, same shape as
the STJ branch. MessagePack.SourceGenerator consumers now have value-object
formatters available via the resolver chain."
```

---

## Phase 3 — Regression nets for resolver gating (15 min, 3 tasks)

### Task 3.1: Append "no value-objects → no resolver" test

**File:** `tests/ZeroAlloc.Serialisation.Generator.Tests/ValueObjectMessagePackResolverEmissionTests.cs`

```csharp
[Fact]
public void Resolver_NotEmitted_WhenNoValueObjectsPresent()
{
    var source = """
        namespace TestModels;

        public class Plain { public int Value { get; set; } }
        """;

    var result = RunGenerator(source, withMessagePack: true);

    Assert.DoesNotContain(result.GeneratedTrees,
        t => t.FilePath.EndsWith("ValueObjectMessagePackResolverExtensions.g.cs", StringComparison.Ordinal));
}
```

### Task 3.2: Append "no MessagePack backend → no resolver" test

```csharp
[Fact]
public void Resolver_NotEmitted_WhenMessagePackBackendNotReferenced()
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
        t => t.FilePath.EndsWith("ValueObjectMessagePackResolverExtensions.g.cs", StringComparison.Ordinal));
}
```

### Task 3.3: Run + commit

```bash
dotnet test tests/ZeroAlloc.Serialisation.Generator.Tests -c Release --filter "FullyQualifiedName~ValueObjectMessagePackResolverEmissionTests"
```

Expected: 3/3 pass (Phase 1 + Phase 3).

```bash
git add tests/ZeroAlloc.Serialisation.Generator.Tests/ValueObjectMessagePackResolverEmissionTests.cs
git commit -m "test(generator): regression nets for MessagePack resolver gating

Two regression tests: no resolver emission when no [ValueObject] types
present and no resolver when MessagePack backend not referenced. Mirrors
the STJ resolver's gating tests from 2.3.2."
```

---

## Phase 4 — Integration test + aot-smoke fixture (45 min, 5 tasks)

This phase exercises the full source-gen path end-to-end and adds an integration test for direct API verification.

### Task 4.1: Verify the shared `ValueObjectId` fixture is present (PR #38 dependency)

```bash
ls samples/ZeroAlloc.Serialisation.AotSmoke/ValueObjectId.cs
```

Expected: file exists (from PR #38). If missing:
- Option A: rebase this branch onto `chore/aot-smoke-cover-value-objects` (PR #38's branch)
- Option B: declare `ValueObjectId` inline in this PR's smoke addition (small follow-up cleanup after #38 merges)

Pick option A unless there's a specific reason to keep branches independent. If A, run:

```bash
git fetch origin chore/aot-smoke-cover-value-objects
git rebase origin/chore/aot-smoke-cover-value-objects
# resolve any conflicts (unlikely — different files)
```

### Task 4.2: Add `MessagePack.SourceGenerator` reference + the DTO

**File (MOD):** `samples/ZeroAlloc.Serialisation.AotSmoke/ZeroAlloc.Serialisation.AotSmoke.csproj`

Find the `<ItemGroup>` containing `<PackageReference Include="MessagePack" />`. Add immediately after:

```xml
<PackageReference Include="MessagePack.SourceGenerator">
  <PrivateAssets>all</PrivateAssets>
  <IncludeAssets>runtime; build; native; contentfiles; analyzers; buildtransitive</IncludeAssets>
</PackageReference>
```

The analyzer flow mirrors `Microsoft.EntityFrameworkCore.Design` (analyzer-only, no runtime). This triggers `GeneratedMessagePackResolver` emission against the AOT smoke compilation.

**File (NEW):** `samples/ZeroAlloc.Serialisation.AotSmoke/ValueObjectMpDto.cs`

```csharp
using MessagePack;

namespace ZeroAlloc.Serialisation.AotSmoke;

// [MessagePackObject] DTO containing a [ValueObject]-typed property. Under
// MessagePack.SourceGenerator, the AOT-generated resolver emits typeinfo for
// this DTO at build time — including a property descriptor for ValueObjectId
// that knows nothing about the [MessagePackFormatter] attribute ZA's generator
// adds via a partial (gens-can't-see-each-other). Without 2.3.3's resolver
// inserted into the chain, the wire format collapses to {"Value":42}.
[MessagePackObject]
public sealed partial class ValueObjectMpDto
{
    [Key(0)] public ValueObjectId Id { get; set; }
    [Key(1)] public string Label { get; set; } = "";
}
```

### Task 4.3: Extend Program.cs assertions

**File (MOD):** `samples/ZeroAlloc.Serialisation.AotSmoke/Program.cs`

Find the V1 (value-object + JsonContext) block from PR #38. Immediately after its `v1Ok` assertion (and before the final `ok = v0Ok && v1Ok` line), append:

```csharp
// V2 (2.3.3): MessagePack source-gen + [ValueObject] field on a [MessagePackObject]
// DTO. Without 2.3.3's resolver, the GeneratedMessagePackResolver returns a
// default-shape formatter for ValueObjectId, producing wrong wire format.
// AddZeroAllocValueObjectFormatters prepends our resolver to the chain,
// intercepting the value-object lookup before the source-gen resolver sees it.
var mpOptions = global::MessagePack.MessagePackSerializerOptions.Standard
    .AddZeroAllocValueObjectFormatters();

var mpDto = new ValueObjectMpDto { Id = new ValueObjectId(42), Label = "alpha" };
byte[] mpBytes;
ValueObjectMpDto? mpDtoBack;
string mpJson;
try
{
    mpBytes = global::MessagePack.MessagePackSerializer.Serialize(mpDto, mpOptions);
    mpJson = global::MessagePack.MessagePackSerializer.ConvertToJson(mpBytes);
    mpDtoBack = global::MessagePack.MessagePackSerializer.Deserialize<ValueObjectMpDto>(mpBytes, mpOptions);
}
catch (Exception ex)
{
    Console.WriteLine($"AOT smoke: FAIL (MessagePack source-gen DTO threw {ex.GetType().Name}: {ex.Message})");
    return 1;
}

// Wire format invariant: ValueObjectId.Value (42) appears as a bare integer
// in the MessagePack-to-JSON conversion, NOT as a {"Value":42} object wrapper.
// MessagePack's ConvertToJson emits keys as numeric strings (e.g. "0","1") for
// [Key]-marked properties — assert "0":42 (the Id key) is present in the JSON.
var mpBareInteger = mpJson.Contains("\"0\":42", StringComparison.Ordinal);
var mpRoundTrip = mpDtoBack is not null
    && mpDtoBack.Id.Value == mpDto.Id.Value
    && string.Equals(mpDtoBack.Label, mpDto.Label, StringComparison.Ordinal);

var v2Ok = mpBareInteger && mpRoundTrip;
```

Update the final exit-code computation to include `v2Ok`:

```csharp
var ok = v0Ok && v1Ok && v2Ok;
if (!ok)
{
    Console.WriteLine($"AOT smoke: FAIL (v0={v0Ok}, v1.resolver={resolverWired}, v1.wire={bareIntegerWire}, v1.roundTrip={roundTrip}, v2.bareInt={mpBareInteger}, v2.roundTrip={mpRoundTrip})");
    Console.WriteLine($"  dtoJson={dtoJson}");
    Console.WriteLine($"  mpJson={mpJson}");
    return 1;
}
```

The `mpJson` substring assertion `"\"0\":42"` is the canonical bare-integer-at-key-0 shape MessagePack emits for `[Key(0)]` integer fields. A regression that wrote `{"0":{"Value":42}}` (the wrapped form) would fail this substring check.

### Task 4.4: Add an in-process integration test mirroring the smoke

**File (NEW):** `tests/ZeroAlloc.Serialisation.Tests/ValueObjectMessagePackSourceGenRoundTripTests.cs`

```csharp
using MessagePack;
using Xunit;
using ZeroAlloc.Serialisation.MessagePack;

namespace ZeroAlloc.Serialisation.Tests;

public class ValueObjectMessagePackSourceGenRoundTripTests
{
    [Fact]
    public void Dto_WithValueObjectField_RoundTrips_AsBareInteger_AfterFormattersHelperApplied()
    {
        // The load-bearing test for the 2.3.3 fix: when AddZeroAllocValueObjectFormatters
        // is called, the resolver chain serves our value-object formatter for
        // ValueObjectMpId, NOT the default object-shape formatter that the
        // source-gen resolver would otherwise emit.
        var options = MessagePackSerializerOptions.Standard
            .AddZeroAllocValueObjectFormatters();

        var dto = new TestMpDto { Id = new ValueObjectMpId(42), Label = "alpha" };
        var bytes = MessagePackSerializer.Serialize(dto, options);
        var json = MessagePackSerializer.ConvertToJson(bytes);

        Assert.Contains("\"0\":42", json);
        Assert.DoesNotContain("\"value\"", json, System.StringComparison.OrdinalIgnoreCase);

        var roundTripped = MessagePackSerializer.Deserialize<TestMpDto>(bytes, options);
        Assert.Equal(dto.Id.Value, roundTripped.Id.Value);
        Assert.Equal(dto.Label, roundTripped.Label);
    }
}

[global::ZeroAlloc.ValueObjects.ValueObject]
public readonly partial struct ValueObjectMpId
{
    public int Value { get; }
    public ValueObjectMpId(int value) => Value = value;
}

[MessagePackObject]
public sealed partial class TestMpDto
{
    [Key(0)] public ValueObjectMpId Id { get; set; }
    [Key(1)] public string Label { get; set; } = "";
}
```

Note: the test does NOT reference `MessagePack.SourceGenerator` directly — the test project's compilation doesn't have it. The test asserts the runtime invariant (the resolver chain produces bare-integer wire format), which is the contract V2 ships. The full source-gen path is validated by the aot-smoke job, not in-process tests.

If the test compilation needs `[MessagePackObject]` properly handled, the test project's csproj may need a runtime-only `MessagePack` reference (which it already has for V0 round-trip tests). Verify by running and adjusting.

### Task 4.5: Run + commit

```bash
dotnet test -c Release
```

Expected: full suite green. Test count: previous + 1 new integration test.

Also run a local AOT publish to verify the smoke binary builds + passes:

```bash
dotnet publish samples/ZeroAlloc.Serialisation.AotSmoke/ZeroAlloc.Serialisation.AotSmoke.csproj -c Release -o ./aot-out
./aot-out/ZeroAlloc.Serialisation.AotSmoke
```

Expected: output is `AOT smoke: PASS`, exit code 0.

If AOT publish fails locally with toolchain errors, that's a workstation issue (Windows might need additional native toolchain setup). Skip the local AOT publish step — CI will verify it on the ubuntu-latest runner with the existing aot-smoke job's clang+zlib install.

```bash
git add tests/ZeroAlloc.Serialisation.Tests/ValueObjectMessagePackSourceGenRoundTripTests.cs \
        samples/ZeroAlloc.Serialisation.AotSmoke/ZeroAlloc.Serialisation.AotSmoke.csproj \
        samples/ZeroAlloc.Serialisation.AotSmoke/ValueObjectMpDto.cs \
        samples/ZeroAlloc.Serialisation.AotSmoke/Program.cs
git commit -m "test(integration+smoke): MessagePack source-gen + value-object DTO round-trip

In-process integration test asserts options.AddZeroAllocValueObjectFormatters
makes a [MessagePackObject] DTO containing a [ValueObject] field round-trip
with bare-integer wire format. aot-smoke fixture adds MessagePack.SourceGenerator
analyzer + a [MessagePackObject] DTO, exercising the full AOT source-gen path
end-to-end under PublishAot=true. A regression in V2's emission or in the
resolver chain ordering now fails CI."
```

---

## Phase 5 — Docs + backlog + ship (20 min, 4 tasks)

### Task 5.1: Update `docs/backlog.md`

Read first. Find the existing V2 entry (line 56 `## V2 — MessagePack registrar helper`). Replace its content with a struck-through shipped marker:

```markdown
## ~~V2 — MessagePack registrar + IFormatterResolver~~ — ✅ shipped 2.3.3 (2026-05-28)

**Shipped:** Generator emits a per-assembly internal `ValueObjectMessagePackResolver : IFormatterResolver` plus a public `AddZeroAllocValueObjectFormatters(this MessagePackSerializerOptions)` extension method (single-file emission — MessagePack has only one registration shape, no need for STJ's two-file split). Consumers using `MessagePack.SourceGenerator` (AOT) call `options.WithResolver(GeneratedMessagePackResolver.Instance).AddZeroAllocValueObjectFormatters()` and value-object properties on `[MessagePackObject]` DTOs round-trip with bare-primitive wire format.

**Design + plan:** [`docs/plans/2026-05-28-messagepack-resolver-helper-design.md`](plans/2026-05-28-messagepack-resolver-helper-design.md) + [`docs/plans/2026-05-28-messagepack-resolver-helper.md`](plans/2026-05-28-messagepack-resolver-helper.md).
```

(The V2 entry for "Extend MessagePack underlying-type table to cover Guid/DateTime" is a separate item and stays unchanged.)

### Task 5.2: Update `docs/getting-started.md`

Read first. Find the "Using value-objects with `JsonSerializerContext`" subsection from 2.3.2. Add a sibling subsection immediately after:

```markdown
### Using value-objects with `MessagePack.SourceGenerator`

When the consuming project uses MessagePack-CSharp's AOT source generator for trimmer-friendly typeinfo, register the value-object formatters explicitly during options setup:

```csharp
var options = MessagePackSerializerOptions.Standard
    .WithResolver(GeneratedMessagePackResolver.Instance)
    .AddZeroAllocValueObjectFormatters();
```

`AddZeroAllocValueObjectFormatters` is generated per assembly that declares `[ValueObject]` types — it prepends a `ValueObjectMessagePackResolver` to the composite chain. The resolver returns our generator-emitted formatter for value-object types; for all other types it returns null and `CompositeResolver` falls through to the user's resolver (typically `GeneratedMessagePackResolver` or `StandardResolver`).

**Call order matters.** `AddZeroAllocValueObjectFormatters` prepends our resolver to whatever `options.Resolver` was set to. Call it AFTER setting your primary resolver via `WithResolver`. Calling in the wrong order leaves the user's resolver wrapping ours, which inverts precedence — value-object lookups hit the source-gen resolver first and produce the wrong wire format.

For reflection-based MessagePack consumers (no `MessagePack.SourceGenerator`), nothing changes — the `[MessagePackFormatter]` attribute the generator emits on the partial-struct extension is picked up by MessagePack-CSharp's reflection-based attribute resolver automatically.
```

### Task 5.3: Run full suite + final smoke check

```bash
cd c:/Projects/Prive/ZeroAlloc/ZeroAlloc.Serialisation
dotnet test -c Release
```

Expected: full suite green.

### Task 5.4: Commit + push + open PR + STOP

```bash
git add docs/backlog.md docs/getting-started.md
git commit -m "docs: V2 shipped backlog entry + getting-started MessagePack subsection"

git push -u origin feat/messagepack-resolver-helper

gh pr create \
  --title "fix(generator): emit MessagePack IFormatterResolver for AOT source-gen interop" \
  --body "$(cat <<'EOF'
## Summary

Closes V2 from the 2.3.x backlog. MessagePack analog of 2.3.1+2.3.2 — emits a per-assembly IFormatterResolver + AddZeroAllocValueObjectFormatters extension method so consumers using MessagePack.SourceGenerator (AOT) can have [ValueObject] properties on [MessagePackObject] DTOs round-trip with bare-primitive wire format.

## What changed

- EmitMessagePackResolver — new emitter method producing a single per-assembly file with both the internal IFormatterResolver class (FormatterCache<T> generic-static-cache pattern + GetFormatterUntyped switch) AND the public extension method (CompositeResolver.Create + WithResolver).
- SerializerGenerator.Initialize — single additional gated AddSource call in the existing 2.3.1 value-object RegisterSourceOutput callback, alongside the STJ emissions.
- 3 new generator-snapshot tests (resolver shape, no-value-objects regression, no-backend regression).
- 1 new integration test asserting bare-integer wire format on a [MessagePackObject] DTO containing a [ValueObject] property after AddZeroAllocValueObjectFormatters is applied.
- aot-smoke fixture extended with MessagePack.SourceGenerator analyzer reference + [MessagePackObject] DTO + assertions for the full AOT source-gen path.
- Docs: backlog V2 struck shipped; getting-started gains a MessagePack source-gen subsection.

## Decisions ([design doc](docs/plans/2026-05-28-messagepack-resolver-helper-design.md))

- Single-file emission (resolver + extension method together) — MessagePack has only one registration shape, no need for the two-file split STJ uses.
- FormatterCache<T> generic-static-cache pattern — canonical, JIT specialises per T, zero-allocation hot path.
- CompositeResolver.Create(ours, options.Resolver) prepends our resolver to the chain — mirrors STJ's TypeInfoResolverChain.Insert(0, ours).
- Resolver stays internal sealed — extension method is the only public surface.

## SemVer

2.3.2 → 2.3.3 (patch). Strictly additive: one new emitted file + one gated AddSource block. Reflection-based MessagePack consumers see no observable change.

## Test plan

- [x] dotnet test -c Release locally
- [ ] CI green
- [ ] aot-smoke job validates the full source-gen path end-to-end

🤖 Generated with [Claude Code](https://claude.com/claude-code)
EOF
)"
```

**STOP** after PR open. Do NOT admin-merge.

---

## Verification checklist

- [ ] **Phase 1:** `EmitMessagePackResolver` emits the resolver class + extension method with correct shape (substring assertions pin the load-bearing pieces).
- [ ] **Phase 2:** Generator emits `ValueObjectMessagePackResolverExtensions.g.cs` when MessagePack backend referenced + value-objects present.
- [ ] **Phase 3:** Empty-source and no-backend correctly suppress emission.
- [ ] **Phase 4:** Integration test proves the resolver makes a value-object property on a [MessagePackObject] DTO round-trip with bare-integer wire format; aot-smoke proves the same end-to-end under PublishAot=true with MessagePack.SourceGenerator in play.
- [ ] **Phase 5:** Docs filed; CI green; release-please cuts 2.3.3; NuGet propagates.

## Out of scope (deferred to backlog)

- **MemoryPack equivalent** — already self-registers via [ModuleInitializer] (2.3.0); no AOT startup gap.
- **V3 Bebop backend** — separate backlog item; no change.
- **MessagePack Guid/DateTime underlying-type table extension** — separate backlog item from 2.3.0 review; no change.
- **A diagnostic when call order is wrong** — same documentation-only approach STJ takes; too noisy to detect reliably.
