# Value-Object Transparent Serializers Implementation Plan

> **For Claude:** REQUIRED SUB-SKILL: Use superpowers:executing-plans to implement this plan task-by-task.

**Goal:** Ship ZeroAlloc.Serialisation 2.3.0 — the generator detects single-property `[ZeroAlloc.ValueObjects.ValueObjectAttribute]` partial structs and emits transparent serializers across all three backends (System.Text.Json, MessagePack, MemoryPack). Wire format becomes the underlying primitive.

**Architecture:** Parallel discovery pipeline in `SerializerGenerator.Initialize` that runs alongside the existing `[ZeroAllocSerializable]` pass. New `ValueObjectEmitter` produces per-backend converter classes + partial-class attribute extensions, gated by which backend assembly the compilation references. FQN-match on the attribute keeps ZA.Serialisation runtime-decoupled from ZA.ValueObjects.

**Tech Stack:** .NET 10, Roslyn `IIncrementalGenerator`, xUnit, generator-snapshot tests (`CSharpCompilation.Create` + `CSharpGeneratorDriver`), integration round-trip tests against each backend.

**Design doc:** `docs/plans/2026-05-27-value-object-transparent-serializers-design.md` (committed at `6ad72f6`).

**Working branch:** `feat/value-object-transparent-serializers` (already created off `main`; design committed).

---

## Phase 0 — Orient (5 min)

Read four files before touching code.

### Task 0.1: Read the generator entry point + extractor

**Files (read-only):**

- `src/ZeroAlloc.Serialisation.Generator/SerializerGenerator.cs` (80 LOC) — the `Initialize` method registers the existing `[ZeroAllocSerializable]` pipeline via `ForAttributeWithMetadataName`. The new V1 discovery pipeline lands alongside it.
- `src/ZeroAlloc.Serialisation.Generator/ModelExtractor.cs` (209 LOC) — see how `Extract` walks an `INamedTypeSymbol`. The new `TryGetTransparentValueObject` helper has the same shape but simpler scope (only single-property partial structs).
- `src/ZeroAlloc.Serialisation.Generator/SerializerEmitter.cs` (72 LOC) — emission pattern for the existing pass. The new `ValueObjectEmitter` mirrors the file shape: static helpers that build a string and contribute it to the source-output context.

### Task 0.2: Read a sibling test to copy convention

**File (read-only):** `tests/ZeroAlloc.Serialisation.Generator.Tests/SerializerEmissionSnapshotTests.cs` — see the existing `CSharpCompilation.Create` + `CSharpGeneratorDriver.RunGenerators` test pattern. The new value-object emission tests follow the same shape, but with the additional dimension of "which backend assembly is referenced." Pay attention to how the test loads `MetadataReferences` — for V1 tests, you'll need to selectively include / exclude the backend assemblies to exercise each emission branch.

---

## Phase 1 — `TryGetTransparentValueObject` helper + standalone unit test (25 min, 5 tasks)

### Task 1.1: Write a focused unit test for the helper

**File (NEW):** `tests/ZeroAlloc.Serialisation.Generator.Tests/ValueObjectDetectionTests.cs`

```csharp
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Xunit;
using ZeroAlloc.Serialisation.Generator;

namespace ZeroAlloc.Serialisation.Generator.Tests;

public class ValueObjectDetectionTests
{
    [Fact]
    public void SinglePropertyValueObject_IsDetected_AndReturnsUnderlyingProperty()
    {
        var source = """
            namespace ZeroAlloc.ValueObjects
            {
                [System.AttributeUsage(System.AttributeTargets.Class | System.AttributeTargets.Struct)]
                public sealed class ValueObjectAttribute : System.Attribute { }
            }

            namespace TestModels;

            [ZeroAlloc.ValueObjects.ValueObject]
            public readonly partial struct CustomerId
            {
                public int Value { get; }
                public CustomerId(int value) => Value = value;
            }
            """;

        var compilation = Compile(source);
        var candidate = compilation.GetTypeByMetadataName("TestModels.CustomerId")!;

        var result = ModelExtractor.TryGetTransparentValueObject(candidate);

        Assert.NotNull(result);
        Assert.Equal("CustomerId", result.Value.Type.Name);
        Assert.Equal("Value", result.Value.UnderlyingProperty.Name);
        Assert.Equal(SpecialType.System_Int32, result.Value.UnderlyingProperty.Type.SpecialType);
    }

    [Fact]
    public void MultiPropertyValueObject_ReturnsNull()
    {
        var source = """
            namespace ZeroAlloc.ValueObjects
            {
                [System.AttributeUsage(System.AttributeTargets.Class | System.AttributeTargets.Struct)]
                public sealed class ValueObjectAttribute : System.Attribute { }
            }

            namespace TestModels;

            [ZeroAlloc.ValueObjects.ValueObject]
            public readonly partial struct Money
            {
                public decimal Amount { get; }
                public string Currency { get; }
                public Money(decimal amount, string currency)
                {
                    Amount = amount;
                    Currency = currency;
                }
            }
            """;

        var compilation = Compile(source);
        var candidate = compilation.GetTypeByMetadataName("TestModels.Money")!;

        var result = ModelExtractor.TryGetTransparentValueObject(candidate);

        Assert.Null(result);
    }

    [Fact]
    public void NonValueObject_PartialStruct_ReturnsNull()
    {
        var source = """
            namespace TestModels;

            public readonly partial struct Wrap
            {
                public int Value { get; }
                public Wrap(int value) => Value = value;
            }
            """;

        var compilation = Compile(source);
        var candidate = compilation.GetTypeByMetadataName("TestModels.Wrap")!;

        var result = ModelExtractor.TryGetTransparentValueObject(candidate);

        Assert.Null(result);
    }

    private static CSharpCompilation Compile(string source) =>
        CSharpCompilation.Create(
            "TestAssembly",
            new[] { CSharpSyntaxTree.ParseText(source) },
            new[] { MetadataReference.CreateFromFile(typeof(object).Assembly.Location) },
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
}
```

If the existing sibling tests use collection-expression `[...]` syntax for arrays, match — the assignment above uses `new[] { ... }`. Read `SerializerEmissionSnapshotTests.cs` first and use whatever the convention is.

The test's stub `ValueObjectAttribute` matches the FQN `ZeroAlloc.ValueObjects.ValueObjectAttribute` — that's what the helper looks up.

### Task 1.2: Run — expect BUILD FAIL (helper doesn't exist yet)

```bash
cd c:/Projects/Prive/ZeroAlloc/ZeroAlloc.Serialisation
dotnet build tests/ZeroAlloc.Serialisation.Generator.Tests -c Release
```

Expected: `error CS0117: 'ModelExtractor' does not contain a definition for 'TryGetTransparentValueObject'`.

### Task 1.3: Implement `TryGetTransparentValueObject` in `ModelExtractor.cs`

**File:** `src/ZeroAlloc.Serialisation.Generator/ModelExtractor.cs`

Add at the bottom of the class (after the existing methods):

```csharp
    private const string ValueObjectAttributeFqn = "ZeroAlloc.ValueObjects.ValueObjectAttribute";

    /// <summary>
    /// If <paramref name="candidate"/> is decorated with
    /// <c>[ZeroAlloc.ValueObjects.ValueObject]</c> (FQN match — no runtime
    /// reference to ZA.ValueObjects required) and declares exactly one public
    /// instance property, returns the type + its underlying property. Returns
    /// null for everything else — class types, multi-property value-objects,
    /// or types without the marker attribute.
    /// </summary>
    internal static (INamedTypeSymbol Type, IPropertySymbol UnderlyingProperty)? TryGetTransparentValueObject(INamedTypeSymbol candidate)
    {
        var hasMarker = candidate.GetAttributes()
            .Any(a => string.Equals(
                a.AttributeClass?.ToDisplayString(),
                ValueObjectAttributeFqn,
                StringComparison.Ordinal));
        if (!hasMarker) return null;

        var properties = candidate.GetMembers()
            .OfType<IPropertySymbol>()
            .Where(p => !p.IsStatic && p.DeclaredAccessibility == Accessibility.Public)
            .ToArray();

        return properties.Length == 1 ? (candidate, properties[0]) : null;
    }
```

Add `using System;` to the file if `StringComparison` isn't already in scope (check the top of `ModelExtractor.cs` first — it likely is, given the existing FQN comparisons in the file).

### Task 1.4: Run — expect 3/3 pass

```bash
dotnet test tests/ZeroAlloc.Serialisation.Generator.Tests -c Release --filter "FullyQualifiedName~ValueObjectDetectionTests"
```

Expected: 3/3 pass.

### Task 1.5: Commit

```bash
git add src/ZeroAlloc.Serialisation.Generator/ModelExtractor.cs \
        tests/ZeroAlloc.Serialisation.Generator.Tests/ValueObjectDetectionTests.cs
git commit -m "feat(generator): ModelExtractor.TryGetTransparentValueObject helper

Detects single-property [ZeroAlloc.ValueObjects.ValueObject] partial
structs by FQN-matching the attribute and returning the type + its
underlying property symbol. Returns null for multi-property
value-objects, unmarked types, or types without exactly one public
instance property. No runtime reference to ZA.ValueObjects — only
the attribute's metadata name matters.

Helper lands first; the emission pipeline + per-backend emitters
land in subsequent commits in the same release cycle."
```

---

## Phase 2 — Backend reference detection helpers (10 min, 3 tasks)

### Task 2.1: Create `ValueObjectEmitter` skeleton + reference-check helpers

**File (NEW):** `src/ZeroAlloc.Serialisation.Generator/ValueObjectEmitter.cs`

```csharp
using System;
using System.Linq;
using Microsoft.CodeAnalysis;

namespace ZeroAlloc.Serialisation.Generator;

/// <summary>
/// Emits transparent serializers for <c>[ValueObject]</c>-decorated
/// single-property partial structs across the three supported backends
/// (System.Text.Json, MessagePack, MemoryPack). Each emission method is
/// gated by a check on the consuming compilation's assembly references —
/// adopters who don't reference a given backend package pay zero code-gen
/// for it.
/// </summary>
internal static class ValueObjectEmitter
{
    private const string SystemTextJsonBackendAssembly = "ZeroAlloc.Serialisation.SystemTextJson";
    private const string MessagePackBackendAssembly = "ZeroAlloc.Serialisation.MessagePack";
    private const string MemoryPackBackendAssembly = "ZeroAlloc.Serialisation.MemoryPack";

    internal static bool ReferencesSystemTextJson(Compilation compilation) =>
        ReferencesAssembly(compilation, SystemTextJsonBackendAssembly);

    internal static bool ReferencesMessagePack(Compilation compilation) =>
        ReferencesAssembly(compilation, MessagePackBackendAssembly);

    internal static bool ReferencesMemoryPack(Compilation compilation) =>
        ReferencesAssembly(compilation, MemoryPackBackendAssembly);

    private static bool ReferencesAssembly(Compilation compilation, string assemblyName) =>
        compilation.ReferencedAssemblyNames.Any(a =>
            string.Equals(a.Name, assemblyName, StringComparison.Ordinal));
}
```

### Task 2.2: Write tests for the reference checks

**File:** `tests/ZeroAlloc.Serialisation.Generator.Tests/ValueObjectDetectionTests.cs` — append:

```csharp
    [Fact]
    public void ReferencesSystemTextJson_TrueWhenAssemblyReferenced()
    {
        var compilation = CSharpCompilation.Create(
            "TestAssembly",
            new[] { CSharpSyntaxTree.ParseText("class C { }") },
            new[]
            {
                MetadataReference.CreateFromFile(typeof(object).Assembly.Location),
                MetadataReference.CreateFromFile(typeof(ZeroAlloc.Serialisation.SystemTextJson.SystemTextJsonSerializer).Assembly.Location),
            });

        Assert.True(ValueObjectEmitter.ReferencesSystemTextJson(compilation));
        Assert.False(ValueObjectEmitter.ReferencesMessagePack(compilation));
        Assert.False(ValueObjectEmitter.ReferencesMemoryPack(compilation));
    }

    [Fact]
    public void ReferencesSystemTextJson_FalseWhenAssemblyNotReferenced()
    {
        var compilation = CSharpCompilation.Create(
            "TestAssembly",
            new[] { CSharpSyntaxTree.ParseText("class C { }") },
            new[] { MetadataReference.CreateFromFile(typeof(object).Assembly.Location) });

        Assert.False(ValueObjectEmitter.ReferencesSystemTextJson(compilation));
        Assert.False(ValueObjectEmitter.ReferencesMessagePack(compilation));
        Assert.False(ValueObjectEmitter.ReferencesMemoryPack(compilation));
    }
```

The test references `ZeroAlloc.Serialisation.SystemTextJson` — the test csproj already references this package (sibling tests use it). If the type name `SystemTextJsonSerializer` doesn't compile, search `tests/ZeroAlloc.Serialisation.Tests/SystemTextJsonSerializerTests.cs` for the actual public type name and use whichever one is correct.

### Task 2.3: Run — expect 5/5 pass + commit

```bash
dotnet test tests/ZeroAlloc.Serialisation.Generator.Tests -c Release --filter "FullyQualifiedName~ValueObjectDetectionTests"
```

Expected: 5/5 pass (3 from Phase 1 + 2 from this phase).

```bash
git add src/ZeroAlloc.Serialisation.Generator/ValueObjectEmitter.cs \
        tests/ZeroAlloc.Serialisation.Generator.Tests/ValueObjectDetectionTests.cs
git commit -m "feat(generator): ValueObjectEmitter backend-reference helpers

Three static References* checks on Compilation — true when the
consuming project references the corresponding ZA.Serialisation
backend assembly. Each per-backend emission method (landing in
subsequent commits) gates on its matching check."
```

---

## Phase 3 — System.Text.Json transparent emitter (50 min, 6 tasks)

### Task 3.1: Write the failing snapshot test

**File (NEW):** `tests/ZeroAlloc.Serialisation.Generator.Tests/ValueObjectEmissionSnapshotTests.cs`

```csharp
using System;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Xunit;
using ZeroAlloc.Serialisation.Generator;

namespace ZeroAlloc.Serialisation.Generator.Tests;

public class ValueObjectEmissionSnapshotTests
{
    [Fact]
    public void SystemTextJson_EmitsConverter_ForSinglePropertyValueObject()
    {
        var source = """
            using ZeroAlloc.Validation;
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
        var emitted = result.GeneratedTrees
            .FirstOrDefault(t => t.FilePath.EndsWith("CustomerIdSystemTextJsonConverter.g.cs", StringComparison.Ordinal));
        Assert.NotNull(emitted);
        var text = emitted!.ToString();
        Assert.Contains("JsonConverter<CustomerId>", text, StringComparison.Ordinal);
        Assert.Contains("reader.GetInt32()", text, StringComparison.Ordinal);
        Assert.Contains("writer.WriteNumberValue(value.Value)", text, StringComparison.Ordinal);
        Assert.Contains("[JsonConverter(typeof(CustomerIdSystemTextJsonConverter))]", text, StringComparison.Ordinal);
    }

    private static GeneratorDriverRunResult RunGenerator(string source, bool withSystemTextJson = false)
    {
        // Stub the [ValueObject] attribute so the generator's FQN lookup matches.
        var valueObjectStub = """
            namespace ZeroAlloc.ValueObjects
            {
                [System.AttributeUsage(System.AttributeTargets.Class | System.AttributeTargets.Struct)]
                public sealed class ValueObjectAttribute : System.Attribute { }
            }
            """;

        var references = new System.Collections.Generic.List<MetadataReference>
        {
            MetadataReference.CreateFromFile(typeof(object).Assembly.Location),
        };
        if (withSystemTextJson)
            references.Add(MetadataReference.CreateFromFile(typeof(ZeroAlloc.Serialisation.SystemTextJson.SystemTextJsonSerializer).Assembly.Location));

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

### Task 3.2: Run — expect FAIL (no emission yet)

```bash
dotnet test tests/ZeroAlloc.Serialisation.Generator.Tests -c Release --filter "FullyQualifiedName~ValueObjectEmissionSnapshotTests"
```

Expected: 1 test fails — `result.GeneratedTrees` doesn't contain `CustomerIdSystemTextJsonConverter.g.cs` because the new emission code path doesn't exist yet.

### Task 3.3: Add the STJ emission method

**File:** `src/ZeroAlloc.Serialisation.Generator/ValueObjectEmitter.cs`

Append to the `ValueObjectEmitter` class:

```csharp
    /// <summary>
    /// Emits a JsonConverter&lt;T&gt; for the value-object and a partial-struct
    /// extension carrying [JsonConverter(typeof(...))] so System.Text.Json picks
    /// it up automatically without explicit registration.
    /// </summary>
    internal static string EmitSystemTextJsonConverter(INamedTypeSymbol type, IPropertySymbol underlying)
    {
        var typeName = type.Name;
        var ns = type.ContainingNamespace.IsGlobalNamespace ? "" : type.ContainingNamespace.ToDisplayString();
        var typeKindKeyword = type.IsRecord ? "record struct" : "struct"; // V1 scope: structs only
        var readonlyKeyword = type.IsReadOnly ? "readonly " : "";
        var underlyingFqn = underlying.Type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
        var (readMethod, writeMethod) = SystemTextJsonReadWriteForType(underlying.Type);
        var converterName = $"{typeName}SystemTextJsonConverter";

        var nsOpen = string.IsNullOrEmpty(ns) ? "" : $"namespace {ns};\n\n";

        return $$"""
            // <auto-generated/>
            #nullable enable

            using System;
            using System.Text.Json;
            using System.Text.Json.Serialization;

            {{nsOpen}}[JsonConverter(typeof({{converterName}}))]
            public {{readonlyKeyword}}partial {{typeKindKeyword}} {{typeName}}
            {
            }

            internal sealed class {{converterName}} : JsonConverter<{{typeName}}>
            {
                public override {{typeName}} Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
                    => new {{typeName}}({{readMethod}});

                public override void Write(Utf8JsonWriter writer, {{typeName}} value, JsonSerializerOptions options)
                    => writer.{{writeMethod}}(value.{{underlying.Name}});
            }

            """;
    }

    private static (string ReadCall, string WriteMethod) SystemTextJsonReadWriteForType(ITypeSymbol underlyingType)
    {
        return underlyingType.SpecialType switch
        {
            SpecialType.System_Int32   => ("reader.GetInt32()",   "WriteNumberValue"),
            SpecialType.System_Int64   => ("reader.GetInt64()",   "WriteNumberValue"),
            SpecialType.System_Int16   => ("reader.GetInt16()",   "WriteNumberValue"),
            SpecialType.System_Decimal => ("reader.GetDecimal()", "WriteNumberValue"),
            SpecialType.System_Double  => ("reader.GetDouble()",  "WriteNumberValue"),
            SpecialType.System_Single  => ("reader.GetSingle()",  "WriteNumberValue"),
            SpecialType.System_String  => ("reader.GetString()!", "WriteStringValue"),
            SpecialType.System_Boolean => ("reader.GetBoolean()", "WriteBooleanValue"),
            _ when string.Equals(underlyingType.ToDisplayString(), "System.Guid", StringComparison.Ordinal)
                => ("reader.GetGuid()", "WriteStringValue"),
            _ when string.Equals(underlyingType.ToDisplayString(), "System.DateTime", StringComparison.Ordinal)
                => ("reader.GetDateTime()", "WriteStringValue"),
            _ => ("reader.GetString()!", "WriteStringValue"), // fallback — let the compiler complain if the user's underlying type doesn't accept this
        };
    }
```

### Task 3.4: Wire the emission into `SerializerGenerator.Initialize`

**File:** `src/ZeroAlloc.Serialisation.Generator/SerializerGenerator.cs`

Add a parallel pipeline after the existing `[ZeroAllocSerializable]` discovery. Inside `Initialize`:

```csharp
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

            // MessagePack + MemoryPack emissions land in Phases 4 + 5.
        });
```

Note: this discovery accepts both struct and record-struct declarations. The `[ValueObject]` attribute targets both per `ZA.ValueObjects/ValueObjectAttribute.cs`.

### Task 3.5: Run — expect PASS, then add integration round-trip test

```bash
dotnet test tests/ZeroAlloc.Serialisation.Generator.Tests -c Release --filter "FullyQualifiedName~ValueObjectEmissionSnapshotTests"
```

Expected: 1/1 pass.

**File (NEW):** `tests/ZeroAlloc.Serialisation.Tests/ValueObjectSystemTextJsonRoundTripTests.cs`

```csharp
using System.Text.Json;
using Xunit;

namespace ZeroAlloc.Serialisation.Tests;

public class ValueObjectSystemTextJsonRoundTripTests
{
    [Fact]
    public void CustomerId_Roundtrips_Through_SystemTextJson_AsBareInteger()
    {
        var original = new CustomerId(42);
        var json = JsonSerializer.Serialize(original);
        Assert.Equal("42", json);
        var deserialized = JsonSerializer.Deserialize<CustomerId>(json);
        Assert.Equal(original, deserialized);
    }
}

namespace ZeroAlloc.ValueObjects
{
    [System.AttributeUsage(System.AttributeTargets.Class | System.AttributeTargets.Struct)]
    public sealed class ValueObjectAttribute : System.Attribute { }
}

namespace ZeroAlloc.Serialisation.Tests
{
    [global::ZeroAlloc.ValueObjects.ValueObject]
    public readonly partial struct CustomerId
    {
        public int Value { get; }
        public CustomerId(int value) => Value = value;
    }
}
```

This test verifies the **end-to-end behaviour**: the test project references `ZeroAlloc.Serialisation.SystemTextJson` (check the csproj — it likely does already, since sibling tests use it). The generator runs over the test compilation, emits `CustomerIdSystemTextJsonConverter`, and the `[JsonConverter]` attribute extension lets `JsonSerializer.Serialize/Deserialize` route through it.

If `ZeroAlloc.Serialisation.Tests.csproj` does NOT reference the SystemTextJson backend, add the reference (or add the test to a sibling project that does).

### Task 3.6: Run + commit

```bash
dotnet test -c Release
```

Expected: every test passes — Phase 1's 3 detection tests, Phase 2's 2 reference-check tests, Phase 3's 1 snapshot test + 1 round-trip test, plus every existing test.

```bash
git add src/ZeroAlloc.Serialisation.Generator/ValueObjectEmitter.cs \
        src/ZeroAlloc.Serialisation.Generator/SerializerGenerator.cs \
        tests/ZeroAlloc.Serialisation.Generator.Tests/ValueObjectEmissionSnapshotTests.cs \
        tests/ZeroAlloc.Serialisation.Tests/ValueObjectSystemTextJsonRoundTripTests.cs
git commit -m "feat(generator): transparent System.Text.Json converter for value-objects

ValueObjectEmitter gains EmitSystemTextJsonConverter, gated by
ReferencesSystemTextJson on the consuming compilation. Single-property
[ValueObject] partial structs get a JsonConverter<T> emitted +
[JsonConverter(typeof(...))] applied via partial-class extension —
zero-config attribute-driven registration.

Wire format becomes the underlying primitive: bare 42 for
CustomerId(42), not {\"value\": 42}. Integration round-trip test
confirms serialize-then-deserialize fidelity through the actual STJ
pipeline.

MessagePack + MemoryPack emissions land in subsequent commits in
the same release cycle."
```

---

## Phase 4 — MessagePack transparent emitter (30 min, 4 tasks)

### Task 4.1: Add the MessagePack snapshot test

**File:** `tests/ZeroAlloc.Serialisation.Generator.Tests/ValueObjectEmissionSnapshotTests.cs`

Append:

```csharp
    [Fact]
    public void MessagePack_EmitsFormatter_ForSinglePropertyValueObject()
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

        var result = RunGenerator(source, withMessagePack: true);

        Assert.Empty(result.Diagnostics);
        var emitted = result.GeneratedTrees
            .FirstOrDefault(t => t.FilePath.EndsWith("CustomerIdMessagePackFormatter.g.cs", StringComparison.Ordinal));
        Assert.NotNull(emitted);
        var text = emitted!.ToString();
        Assert.Contains("IMessagePackFormatter<CustomerId>", text, StringComparison.Ordinal);
        Assert.Contains("reader.ReadInt32()", text, StringComparison.Ordinal);
        Assert.Contains("writer.Write(value.Value)", text, StringComparison.Ordinal);
        Assert.Contains("[MessagePackFormatter(typeof(CustomerIdMessagePackFormatter))]", text, StringComparison.Ordinal);
    }

    [Fact]
    public void MessagePack_EmitsNothing_WhenBackendNotReferenced()
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

        var result = RunGenerator(source, withSystemTextJson: false, withMessagePack: false, withMemoryPack: false);

        Assert.DoesNotContain(result.GeneratedTrees,
            t => t.FilePath.EndsWith("CustomerIdMessagePackFormatter.g.cs", StringComparison.Ordinal));
    }
```

Extend the `RunGenerator` helper signature to accept `withMessagePack: bool` and `withMemoryPack: bool` parameters; add the corresponding `MetadataReference.CreateFromFile` lines (referencing the public type from each backend's runtime adapter — `MessagePackSerializer` / `MemoryPackSerializer`).

### Task 4.2: Implement `EmitMessagePackFormatter`

**File:** `src/ZeroAlloc.Serialisation.Generator/ValueObjectEmitter.cs`

Append a sibling method to `EmitSystemTextJsonConverter`:

```csharp
    internal static string EmitMessagePackFormatter(INamedTypeSymbol type, IPropertySymbol underlying)
    {
        var typeName = type.Name;
        var ns = type.ContainingNamespace.IsGlobalNamespace ? "" : type.ContainingNamespace.ToDisplayString();
        var typeKindKeyword = type.IsRecord ? "record struct" : "struct";
        var readonlyKeyword = type.IsReadOnly ? "readonly " : "";
        var (readMethod, writeArgFormat) = MessagePackReadWriteForType(underlying.Type);
        var formatterName = $"{typeName}MessagePackFormatter";

        var nsOpen = string.IsNullOrEmpty(ns) ? "" : $"namespace {ns};\n\n";

        return $$"""
            // <auto-generated/>
            #nullable enable

            using MessagePack;
            using MessagePack.Formatters;

            {{nsOpen}}[MessagePackFormatter(typeof({{formatterName}}))]
            public {{readonlyKeyword}}partial {{typeKindKeyword}} {{typeName}}
            {
            }

            internal sealed class {{formatterName}} : IMessagePackFormatter<{{typeName}}>
            {
                public void Serialize(ref MessagePackWriter writer, {{typeName}} value, MessagePackSerializerOptions options)
                    => writer.Write(value.{{underlying.Name}});

                public {{typeName}} Deserialize(ref MessagePackReader reader, MessagePackSerializerOptions options)
                    => new {{typeName}}({{readMethod}});
            }

            """;
    }

    private static (string ReadCall, string WriteArgFormat) MessagePackReadWriteForType(ITypeSymbol underlyingType)
    {
        return underlyingType.SpecialType switch
        {
            SpecialType.System_Int32   => ("reader.ReadInt32()",   "{0}"),
            SpecialType.System_Int64   => ("reader.ReadInt64()",   "{0}"),
            SpecialType.System_Int16   => ("reader.ReadInt16()",   "{0}"),
            SpecialType.System_Single  => ("reader.ReadSingle()",  "{0}"),
            SpecialType.System_Double  => ("reader.ReadDouble()",  "{0}"),
            SpecialType.System_Boolean => ("reader.ReadBoolean()", "{0}"),
            SpecialType.System_String  => ("reader.ReadString()!", "{0}"),
            _ => ("reader.ReadString()!", "{0}"),
        };
    }
```

### Task 4.3: Wire MessagePack emission into the generator

**File:** `src/ZeroAlloc.Serialisation.Generator/SerializerGenerator.cs`

Inside the `RegisterSourceOutput` callback added in Phase 3, alongside the STJ branch:

```csharp
            if (ValueObjectEmitter.ReferencesMessagePack(compilation))
            {
                var mpSource = ValueObjectEmitter.EmitMessagePackFormatter(type, underlyingProperty);
                sourceCtx.AddSource($"{type.Name}MessagePackFormatter.g.cs", mpSource);
            }
```

### Task 4.4: Run + add round-trip + commit

```bash
dotnet test -c Release --filter "FullyQualifiedName~ValueObjectEmissionSnapshotTests"
```

Expected: 3/3 pass (STJ + MessagePack + the new "not emitted when backend not referenced" test).

**File (NEW):** `tests/ZeroAlloc.Serialisation.Tests/ValueObjectMessagePackRoundTripTests.cs` — parallel to the STJ round-trip:

```csharp
using MessagePack;
using Xunit;

namespace ZeroAlloc.Serialisation.Tests;

public class ValueObjectMessagePackRoundTripTests
{
    [Fact]
    public void CustomerId_Roundtrips_Through_MessagePack_AsBareInteger()
    {
        var original = new CustomerId(42);
        var bytes = MessagePackSerializer.Serialize(original);
        var json = MessagePackSerializer.ConvertToJson(bytes);
        Assert.Equal("42", json);
        var deserialized = MessagePackSerializer.Deserialize<CustomerId>(bytes);
        Assert.Equal(original, deserialized);
    }
}
```

(Reuse the same `CustomerId` declaration from the STJ test if it's in a shared namespace — if both test files declare `CustomerId`, that conflicts. Pull the value-object into a single shared `_ValueObjectModels.cs` file.)

```bash
dotnet test -c Release
```

Expected: full suite green.

```bash
git add src/ZeroAlloc.Serialisation.Generator/ValueObjectEmitter.cs \
        src/ZeroAlloc.Serialisation.Generator/SerializerGenerator.cs \
        tests/ZeroAlloc.Serialisation.Generator.Tests/ValueObjectEmissionSnapshotTests.cs \
        tests/ZeroAlloc.Serialisation.Tests/ValueObjectMessagePackRoundTripTests.cs
git commit -m "feat(generator): transparent MessagePack formatter for value-objects

EmitMessagePackFormatter parallel to the STJ emitter, gated by
ReferencesMessagePack. Wire format is the MessagePack primitive
encoding of the underlying value (single byte for small ints,
fixint/uint8 etc.). [MessagePackFormatter(typeof(...))] applied via
partial-class extension so MessagePack-CSharp picks it up via its
attribute-resolution mechanism."
```

---

## Phase 5 — MemoryPack transparent emitter (30 min, 4 tasks)

Same shape as Phase 4. MemoryPack has the extra `[MemoryPackable(GenerateType.NoGenerate)]` attribute requirement on the partial struct to disable MemoryPack's default object-shape generation.

### Task 5.1: Snapshot test

Append to `ValueObjectEmissionSnapshotTests.cs`:

```csharp
    [Fact]
    public void MemoryPack_EmitsFormatter_ForSinglePropertyValueObject()
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

        var result = RunGenerator(source, withMemoryPack: true);

        Assert.Empty(result.Diagnostics);
        var emitted = result.GeneratedTrees
            .FirstOrDefault(t => t.FilePath.EndsWith("CustomerIdMemoryPackFormatter.g.cs", StringComparison.Ordinal));
        Assert.NotNull(emitted);
        var text = emitted!.ToString();
        Assert.Contains("MemoryPackFormatter<CustomerId>", text, StringComparison.Ordinal);
        Assert.Contains("[MemoryPackable(GenerateType.NoGenerate)]", text, StringComparison.Ordinal);
        Assert.Contains("[MemoryPackCustomFormatter", text, StringComparison.Ordinal);
        Assert.Contains("writer.WriteValue(value.Value)", text, StringComparison.Ordinal);
        Assert.Contains("reader.ReadValue<int>()", text, StringComparison.Ordinal);
    }
```

### Task 5.2: Implement `EmitMemoryPackFormatter`

**File:** `src/ZeroAlloc.Serialisation.Generator/ValueObjectEmitter.cs`

```csharp
    internal static string EmitMemoryPackFormatter(INamedTypeSymbol type, IPropertySymbol underlying)
    {
        var typeName = type.Name;
        var ns = type.ContainingNamespace.IsGlobalNamespace ? "" : type.ContainingNamespace.ToDisplayString();
        var typeKindKeyword = type.IsRecord ? "record struct" : "struct";
        var readonlyKeyword = type.IsReadOnly ? "readonly " : "";
        var underlyingFqn = underlying.Type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
        var formatterName = $"{typeName}MemoryPackFormatter";

        var nsOpen = string.IsNullOrEmpty(ns) ? "" : $"namespace {ns};\n\n";

        return $$"""
            // <auto-generated/>
            #nullable enable

            using MemoryPack;

            {{nsOpen}}[MemoryPackable(GenerateType.NoGenerate)]
            [MemoryPackCustomFormatter<{{formatterName}}, {{typeName}}>]
            public {{readonlyKeyword}}partial {{typeKindKeyword}} {{typeName}}
            {
            }

            internal sealed class {{formatterName}} : MemoryPackFormatter<{{typeName}}>
            {
                public override void Serialize<TBufferWriter>(ref MemoryPackWriter<TBufferWriter> writer, scoped ref {{typeName}} value)
                    => writer.WriteValue<{{underlyingFqn}}>(value.{{underlying.Name}});

                public override void Deserialize(ref MemoryPackReader reader, scoped ref {{typeName}} value)
                    => value = new {{typeName}}(reader.ReadValue<{{underlyingFqn}}>()!);
            }

            """;
    }
```

### Task 5.3: Wire MemoryPack emission into the generator + run snapshot

**File:** `src/ZeroAlloc.Serialisation.Generator/SerializerGenerator.cs`

Inside the `RegisterSourceOutput` callback:

```csharp
            if (ValueObjectEmitter.ReferencesMemoryPack(compilation))
            {
                var mpkSource = ValueObjectEmitter.EmitMemoryPackFormatter(type, underlyingProperty);
                sourceCtx.AddSource($"{type.Name}MemoryPackFormatter.g.cs", mpkSource);
            }
```

```bash
dotnet test -c Release --filter "FullyQualifiedName~ValueObjectEmissionSnapshotTests"
```

Expected: 4/4 pass.

### Task 5.4: MemoryPack round-trip + commit

**File (NEW):** `tests/ZeroAlloc.Serialisation.Tests/ValueObjectMemoryPackRoundTripTests.cs`

```csharp
using MemoryPack;
using Xunit;

namespace ZeroAlloc.Serialisation.Tests;

public class ValueObjectMemoryPackRoundTripTests
{
    [Fact]
    public void CustomerId_Roundtrips_Through_MemoryPack()
    {
        var original = new CustomerId(42);
        var bytes = MemoryPackSerializer.Serialize(original);
        var deserialized = MemoryPackSerializer.Deserialize<CustomerId>(bytes);
        Assert.Equal(original, deserialized);
    }
}
```

Wire-format byte assertion is omitted — MemoryPack's binary frame for an int has a fixed shape (5 bytes: tag + 4-byte little-endian) but asserting the exact bytes is brittle across MemoryPack versions. Round-trip fidelity is the meaningful invariant.

If the shared `CustomerId` model conflict surfaces (Phase 4 mentioned consolidating), confirm `_ValueObjectModels.cs` was the consolidation target and reference from here. Otherwise extract.

```bash
dotnet test -c Release
```

Expected: full suite green.

```bash
git add src/ZeroAlloc.Serialisation.Generator/ValueObjectEmitter.cs \
        src/ZeroAlloc.Serialisation.Generator/SerializerGenerator.cs \
        tests/ZeroAlloc.Serialisation.Generator.Tests/ValueObjectEmissionSnapshotTests.cs \
        tests/ZeroAlloc.Serialisation.Tests/ValueObjectMemoryPackRoundTripTests.cs
git commit -m "feat(generator): transparent MemoryPack formatter for value-objects

EmitMemoryPackFormatter parallel to STJ + MessagePack, gated by
ReferencesMemoryPack. Partial-struct gains both
[MemoryPackable(GenerateType.NoGenerate)] (to disable MemoryPack's
default object-shape generation) and [MemoryPackCustomFormatter] —
both attributes are needed; either alone is insufficient."
```

---

## Phase 6 — Multi-property fallback + non-value-object regression nets (15 min, 2 tasks)

### Task 6.1: Add regression tests

**File:** `tests/ZeroAlloc.Serialisation.Generator.Tests/ValueObjectEmissionSnapshotTests.cs`

Append:

```csharp
    [Fact]
    public void MultiProperty_ValueObject_NoEmission_AllBackends()
    {
        var source = """
            using ZeroAlloc.ValueObjects;
            namespace TestModels;

            [ValueObject]
            public readonly partial struct Money
            {
                public decimal Amount { get; }
                public string Currency { get; }
                public Money(decimal amount, string currency)
                {
                    Amount = amount;
                    Currency = currency;
                }
            }
            """;

        var result = RunGenerator(source, withSystemTextJson: true, withMessagePack: true, withMemoryPack: true);

        Assert.DoesNotContain(result.GeneratedTrees,
            t => t.FilePath.EndsWith("MoneySystemTextJsonConverter.g.cs", StringComparison.Ordinal));
        Assert.DoesNotContain(result.GeneratedTrees,
            t => t.FilePath.EndsWith("MoneyMessagePackFormatter.g.cs", StringComparison.Ordinal));
        Assert.DoesNotContain(result.GeneratedTrees,
            t => t.FilePath.EndsWith("MoneyMemoryPackFormatter.g.cs", StringComparison.Ordinal));
    }

    [Fact]
    public void NonValueObject_PartialStruct_NoEmission()
    {
        var source = """
            namespace TestModels;

            // Note: no [ValueObject] attribute.
            public readonly partial struct Wrap
            {
                public int Value { get; }
                public Wrap(int value) => Value = value;
            }
            """;

        var result = RunGenerator(source, withSystemTextJson: true);

        Assert.Empty(result.GeneratedTrees.Where(t =>
            t.FilePath.EndsWith("WrapSystemTextJsonConverter.g.cs", StringComparison.Ordinal)));
    }
```

### Task 6.2: Run + commit

```bash
dotnet test -c Release
```

Expected: full suite green — 6 snapshot cases (4 happy + 2 regression net) + 3 round-trip tests + every existing test.

```bash
git add tests/ZeroAlloc.Serialisation.Generator.Tests/ValueObjectEmissionSnapshotTests.cs
git commit -m "test(generator): regression net for multi-property + non-attribute cases"
```

---

## Phase 7 — Docs + backlog + ship (25 min, 5 tasks)

### Task 7.1: Add `docs/backlog.md` (new file for this repo)

**File (NEW):** `docs/backlog.md`

```markdown
# ZeroAlloc.Serialisation — Backlog

Candidate enhancements identified during real-world usage. Each item is independent and can be implemented in any order. Order is rough priority, not commitment. Items graduate from this backlog when the friction or value is concrete enough to justify the work.

---

## ~~V1 — Transparent serializers for single-property `[ValueObject]` types~~ — ✅ shipped 2.3.0 (2026-05-27)

**Shipped:** `ZeroAlloc.Serialisation.Generator` gains a parallel discovery pass that detects single-property `[ZeroAlloc.ValueObjects.ValueObjectAttribute]` partial structs (FQN match — no runtime reference to ZA.ValueObjects). Per-backend emission gated by `Compilation.ReferencedAssemblyNames`:

- References `ZeroAlloc.Serialisation.SystemTextJson` → emit `JsonConverter<T>` + `[JsonConverter(typeof(...))]`
- References `ZeroAlloc.Serialisation.MessagePack` → emit `IMessagePackFormatter<T>` + `[MessagePackFormatter(typeof(...))]`
- References `ZeroAlloc.Serialisation.MemoryPack` → emit `MemoryPackFormatter<T>` + `[MemoryPackable(GenerateType.NoGenerate)] [MemoryPackCustomFormatter(...)]`

Multi-property value-objects (`Money { Amount, Currency }`) fall through silently to the backend's default object-shape serialization. Existing `[ZeroAllocSerializable]`-marked types see byte-identical generator output.

**Design + plan:** [`docs/plans/2026-05-27-value-object-transparent-serializers-design.md`](plans/2026-05-27-value-object-transparent-serializers-design.md) + [`docs/plans/2026-05-27-value-object-transparent-serializers.md`](plans/2026-05-27-value-object-transparent-serializers.md).

**Decisions worth flagging** (durable record):

- **FQN-match on `[ValueObject]`.** Same pattern ZA.Validation 1.5.0 (B1) established. Symmetric move; both packages now consume ZA.ValueObjects via attribute name only.
- **Per-backend emission gated by assembly reference**, not by an explicit per-backend opt-in attribute on the user side. Adopters who reference multiple backends get parallel emission for the same `[ValueObject]`.
- **Single-property only.** Multi-property value-objects fall through to backend default (which is sensible). No diagnostic — the case isn't a bug.
- **Zero-config registration.** Generator emits the backend's native attribute on a partial-struct extension; STJ / MessagePack / MemoryPack each pick the converter up via their normal attribute-resolution. No `services.AddZeroAllocValueObjectConverters()` call needed.
```

### Task 7.2: Update `docs/getting-started.md`

**File:** `docs/getting-started.md`

Read the file first to find a sensible insertion point — probably near the backend-specific subsections or in a new "Recipes" section. Add:

```markdown
## Value-object transparent serialization

If a property's type is decorated with `[ZeroAlloc.ValueObjects.ValueObject]` and declares exactly one public property (typical TypedId shape), the generator emits a transparent serializer that reads/writes only the underlying value.

```csharp
[ValueObject]
public readonly partial struct CustomerId
{
    public int Value { get; }
    public CustomerId(int value) => Value = value;
}
```

Reference `ZeroAlloc.Serialisation.SystemTextJson` (or `.MessagePack` / `.MemoryPack`), and JSON serialization becomes:

```csharp
JsonSerializer.Serialize(new CustomerId(42))    // → "42"   (bare integer, not {"value": 42})
JsonSerializer.Deserialize<CustomerId>("42")    // → CustomerId(42)
```

Same transparency holds across MessagePack and MemoryPack. Adopters who reference multiple backends get the converter emitted for each.

**Multi-property value-objects** (e.g. `Money { Amount, Currency }`) fall through to the backend's default object-shape serialization — no transparent emission, no diagnostic. If you need a specific wire format for them, declare an explicit converter the usual way (`[JsonConverter(typeof(...))]`).
```

### Task 7.3: Run full suite one final time

```bash
cd c:/Projects/Prive/ZeroAlloc/ZeroAlloc.Serialisation
dotnet test -c Release
```

Expected: every test passes — 8 generator-snapshot tests + 5 detection/reference tests + 3 round-trip tests + every existing test.

### Task 7.4: Commit + push + open PR

```bash
git add docs/backlog.md docs/getting-started.md
git commit -m "docs: file V1 in backlog + getting-started subsection"

git push -u origin feat/value-object-transparent-serializers

gh pr create \
  --title "feat(generator): transparent serializers for [ValueObject] types" \
  --body "$(cat <<'EOF'
## Summary

Closes new backlog item V1. ``ZeroAlloc.Serialisation.Generator`` gains a parallel discovery pass that detects single-property ``[ZeroAlloc.ValueObjects.ValueObjectAttribute]`` partial structs and emits transparent serializers/converters/formatters across all three backends (System.Text.Json, MessagePack, MemoryPack). Wire format becomes the underlying primitive.

Prerequisite for the ``za-clean`` + ``za-vertical-slice`` template migration that adopts typed-id properties (``CustomerId`` / ``OrderId`` instead of raw ``int``).

## What changed

- **Detection** (``ModelExtractor.TryGetTransparentValueObject``) — FQN-match on ``[ZeroAlloc.ValueObjects.ValueObjectAttribute]``; returns the type + its single underlying property symbol when applicable. No runtime reference to ZA.ValueObjects required.
- **Per-backend emission** (``ValueObjectEmitter``) — three methods (``EmitSystemTextJsonConverter``, ``EmitMessagePackFormatter``, ``EmitMemoryPackFormatter``), each gated by a ``Compilation.ReferencedAssemblyNames`` check on the matching backend assembly.
- **Generator pipeline** (``SerializerGenerator.Initialize``) — new ``ForAttributeWithMetadataName`` pipeline for ``[ValueObject]`` partial structs, combined with the compilation provider so per-backend gates can run.
- **6 snapshot tests** covering the matrix of backend × shape (single-property happy path × 3 backends, multi-property fallback, non-value-object regression net, backend-not-referenced regression net).
- **3 round-trip integration tests** — one per backend, verifying ``CustomerId(42)`` serializes to the underlying primitive shape and deserializes back faithfully.
- **5 detection / reference-check unit tests**.
- **New ``docs/backlog.md``** (first backlog file in this repo) + ``docs/getting-started.md`` subsection.

## Decisions ([design doc](docs/plans/2026-05-27-value-object-transparent-serializers-design.md))

- **FQN-match attribute detection** — same pattern ZA.Validation 1.5.0 established for B1. No runtime ZA.ValueObjects coupling.
- **Single-property only.** Multi-property value-objects fall through silently to backend default. No diagnostic — the case isn't a bug.
- **Per-backend emission gated by assembly reference**, not by explicit per-backend opt-in attribute. Adopters reference whichever backend(s) they need; emission follows.
- **Zero-config registration** via partial-class attribute extension. Each backend's native attribute (``[JsonConverter]`` / ``[MessagePackFormatter]`` / ``[MemoryPackCustomFormatter]``) is auto-applied on the value-object type via a generated partial-struct fragment.

## SemVer

``2.2.0`` → ``2.3.0`` (additive minor — new emission code path; existing ``[ZeroAllocSerializable]``-marked types see byte-identical output).

## Test plan

- [x] ``dotnet test -c Release`` — all green locally (existing suite + 14 new tests)
- [ ] CI — green on this PR
- [ ] Follow-up after 2.3.0 propagates: ``ZeroAlloc.Templates`` migrates ``za-clean`` + ``za-vertical-slice`` request types from raw ``int`` to typed ``CustomerId`` / ``OrderId``.

🤖 Generated with [Claude Code](https://claude.com/claude-code)
EOF
)"
```

### Task 7.5: Watch CI + admin-merge + mark V1 shipped

```bash
gh pr checks --watch
```

If green, admin-merge. release-please opens ``chore(main): release 2.3.0``. Admin-merge that too. Verify NuGet:

```bash
curl -s "https://api.nuget.org/v3-flatcontainer/zeroalloc.serialisation/index.json" \
  | python -c "import sys, json; v = json.load(sys.stdin)['versions']; print('latest:', v[-1])"
```

Expected: ``latest: 2.3.0``.

The ``docs/backlog.md`` entry already strikes V1 as shipped — no follow-up commit needed for backlog hygiene since it was written shipped-form upfront.

---

## Verification checklist

- [ ] **Phase 1:** `TryGetTransparentValueObject` helper detects single-property value-objects, returns null for multi-property and non-marked types.
- [ ] **Phase 2:** Three backend-reference helpers correctly read `Compilation.ReferencedAssemblyNames`.
- [ ] **Phase 3:** STJ converter emitted; partial-struct gains `[JsonConverter]`; bare-integer wire format proven via round-trip.
- [ ] **Phase 4:** MessagePack formatter emitted; partial-struct gains `[MessagePackFormatter]`; bare-primitive wire format proven via round-trip.
- [ ] **Phase 5:** MemoryPack formatter emitted; partial-struct gains both `[MemoryPackable(GenerateType.NoGenerate)]` + `[MemoryPackCustomFormatter]`; round-trip fidelity proven.
- [ ] **Phase 6:** Multi-property value-objects emit nothing across all 3 backends; non-value-object partial structs emit nothing.
- [ ] **Phase 7:** V1 filed + struck in `docs/backlog.md` shipped-form; CI green; release-please cuts 2.3.0; NuGet propagates.

## Out of scope (deferred)

- **Custom underlying-type-to-backend-method mappings** beyond the fixed table inside the emitter (`int / long / decimal / string / Guid / DateTime`). Falls through to the backend's generic `Read<T>` / `Write<T>` for other underlying types.
- **Multi-property value-objects with member-of hint.** No load-bearing consumer; backend default is correct for multi-property cases.
- **A ZS00NN Info diagnostic** announcing transparent emission. Skip until a real consumer asks.
- **Template migration follow-through** — ``za-clean`` + ``za-vertical-slice`` request-type migration lives in `ZeroAlloc.Templates`, lands as a separate PR after 2.3.0 propagates.
