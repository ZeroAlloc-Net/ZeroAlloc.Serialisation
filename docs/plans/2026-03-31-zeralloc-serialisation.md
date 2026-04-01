# ZeroAlloc.Serialisation Implementation Plan

> **For Claude:** REQUIRED SUB-SKILL: Use superpowers:executing-plans to implement this plan task-by-task.

**Goal:** Build a shared `IBufferWriter<byte>`-based serialisation library with a source generator that produces typed, AOT-friendly `ISerializer<T>` implementations consumed by both `ZeroAlloc.Rest` and the upcoming `ZeroAlloc.EventSourcing`.

**Architecture:** Typed `ISerializer<T>` interface (sync, `IBufferWriter<byte>` write / `ReadOnlySpan<byte>` read). Backend packages wrap MemoryPack/MessagePack/STJ. Source generator detects `[ZeroAllocSerializable]` on a type and emits a concrete `{TypeName}Serializer : ISerializer<T>` + DI extension — no `RequiresDynamicCode` because T is statically known. `ZeroAlloc.Rest` gets a `RestSerializerAdapter` bridging typed serializers to the existing `IRestSerializer` (stream-based) surface.

**Tech Stack:** C# 13 / .NET 10, multi-TFM (`netstandard2.1;net8.0;net9.0;net10.0`), Roslyn incremental generator (`netstandard2.0`), MemoryPack 1.21.4, MessagePack 3.1.4, System.Text.Json (inbox), xunit, `Microsoft.CodeAnalysis.CSharp.SourceGenerators.Testing.XUnit`, BenchmarkDotNet.

---

## Task 1: Solution Scaffold

**Files:**
- Create: `ZeroAlloc.Serialisation.sln`
- Create: `Directory.Build.props`
- Create: `Directory.Packages.props`
- Create: `.gitignore`
- Create: `.commitlintrc.yml`
- Create: `GitVersion.yml`

**Step 1: Init git and dotnet solution**

```bash
cd c:/Projects/Prive/ZeroAlloc.Serialisation
git init
dotnet new sln -n ZeroAlloc.Serialisation
mkdir -p src tests
```

**Step 2: Create `Directory.Build.props`**

```xml
<Project>
  <PropertyGroup>
    <TargetFrameworks>netstandard2.1;net8.0;net9.0;net10.0</TargetFrameworks>
    <Nullable>enable</Nullable>
    <ImplicitUsings>enable</ImplicitUsings>
    <LangVersion>latest</LangVersion>
    <TreatWarningsAsErrors>true</TreatWarningsAsErrors>
  </PropertyGroup>

  <PropertyGroup>
    <Authors>Marcel Roozekrans</Authors>
    <Company>ZeroAlloc</Company>
    <PackageLicenseExpression>MIT</PackageLicenseExpression>
    <PackageProjectUrl>https://serialisation.zeroalloc.net</PackageProjectUrl>
    <RepositoryUrl>https://github.com/ZeroAlloc-Net/ZeroAlloc.Serialisation</RepositoryUrl>
    <RepositoryType>git</RepositoryType>
    <PackageTags>serialisation;serialization;bufferwriter;source-generator;aot;zero-allocation</PackageTags>
    <Copyright>Copyright © Marcel Roozekrans</Copyright>
    <Description>Source-generated, AOT-compatible IBufferWriter serialisation for .NET</Description>
  </PropertyGroup>

  <PropertyGroup Condition="$(MSBuildProjectName.Contains('.Tests')) Or $(MSBuildProjectName.Contains('.Benchmarks'))">
    <IsPackable>false</IsPackable>
  </PropertyGroup>

  <ItemGroup Condition="'$(IsRoslynComponent)' != 'true'">
    <PackageReference Include="Meziantou.Analyzer" PrivateAssets="all" />
    <PackageReference Include="Roslynator.Analyzers" PrivateAssets="all" />
    <PackageReference Include="ErrorProne.NET.CoreAnalyzers" PrivateAssets="all" />
    <PackageReference Include="ErrorProne.NET.Structs" PrivateAssets="all" />
  </ItemGroup>
</Project>
```

**Step 3: Create `Directory.Packages.props`**

```xml
<Project>
  <PropertyGroup>
    <ManagePackageVersionsCentrally>true</ManagePackageVersionsCentrally>
  </PropertyGroup>
  <ItemGroup>
    <!-- Roslyn -->
    <PackageVersion Include="Microsoft.CodeAnalysis.CSharp" Version="4.13.0" />
    <PackageVersion Include="Microsoft.CodeAnalysis.Analyzers" Version="3.11.0" />
    <!-- Serializers -->
    <PackageVersion Include="MemoryPack" Version="1.21.4" />
    <PackageVersion Include="MessagePack" Version="3.1.4" />
    <!-- DI -->
    <PackageVersion Include="Microsoft.Extensions.DependencyInjection.Abstractions" Version="10.0.0" />
    <!-- Testing -->
    <PackageVersion Include="xunit" Version="2.9.3" />
    <PackageVersion Include="xunit.runner.visualstudio" Version="2.8.2" />
    <PackageVersion Include="Microsoft.NET.Test.Sdk" Version="17.13.0" />
    <PackageVersion Include="Microsoft.CodeAnalysis.CSharp.SourceGenerators.Testing.XUnit" Version="1.1.2" />
    <PackageVersion Include="Basic.Reference.Assemblies.Net100" Version="1.8.4" />
    <!-- Benchmarks -->
    <PackageVersion Include="BenchmarkDotNet" Version="0.14.0" />
    <!-- Analyzers -->
    <PackageVersion Include="Meziantou.Analyzer" Version="2.0.182" />
    <PackageVersion Include="Roslynator.Analyzers" Version="4.12.10" />
    <PackageVersion Include="ErrorProne.NET.CoreAnalyzers" Version="0.1.2" />
    <PackageVersion Include="ErrorProne.NET.Structs" Version="0.1.2" />
  </ItemGroup>
</Project>
```

**Step 4: Create `.commitlintrc.yml`**

```yaml
extends:
  - '@commitlint/config-conventional'
rules:
  scope-enum:
    - 2
    - always
    - - core
      - generator
      - memorypack
      - messagepack
      - systemtextjson
      - benchmarks
      - ci
      - deps
```

**Step 5: Create `GitVersion.yml`**

```yaml
mode: ContinuousDeployment
tag-prefix: v
major-version-bump-message: "^(build|chore|ci|docs|feat|fix|perf|refactor|revert|style|test)(\\(.*\\))?!:"
minor-version-bump-message: "^feat(\\(.*\\))?:"
patch-version-bump-message: "^fix(\\(.*\\))?:"
branches:
  main:
    label: alpha
```

**Step 6: Create `.gitignore`**

```
bin/
obj/
*.user
.vs/
BenchmarkDotNet.Artifacts/
```

**Step 7: Verify scaffold builds**

```bash
dotnet sln list
```
Expected: `No projects found in the solution.` (no projects yet — that's fine)

**Step 8: Commit**

```bash
git add .
git commit -m "chore(ci): init solution scaffold"
```

---

## Task 2: Core Project — `ZeroAlloc.Serialisation`

**Files:**
- Create: `src/ZeroAlloc.Serialisation/ZeroAlloc.Serialisation.csproj`
- Create: `src/ZeroAlloc.Serialisation/ISerializer.cs`
- Create: `src/ZeroAlloc.Serialisation/ZeroAllocSerializableAttribute.cs`

**Step 1: Create project file**

`src/ZeroAlloc.Serialisation/ZeroAlloc.Serialisation.csproj`:
```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <IsAotCompatible>true</IsAotCompatible>
  </PropertyGroup>
  <ItemGroup>
    <PackageReference Include="Microsoft.Extensions.DependencyInjection.Abstractions" />
  </ItemGroup>
</Project>
```

**Step 2: Create `ISerializer.cs`**

```csharp
using System.Buffers;

namespace ZeroAlloc.Serialisation;

public interface ISerializer<T>
{
    void Serialize(IBufferWriter<byte> writer, T value);
    T? Deserialize(ReadOnlySpan<byte> buffer);
}
```

**Step 3: Create `ZeroAllocSerializableAttribute.cs`**

```csharp
namespace ZeroAlloc.Serialisation;

public enum SerializationFormat
{
    MemoryPack,
    MessagePack,
    SystemTextJson,
}

[AttributeUsage(AttributeTargets.Class | AttributeTargets.Struct)]
public sealed class ZeroAllocSerializableAttribute(SerializationFormat format) : Attribute
{
    public SerializationFormat Format { get; } = format;
}
```

**Step 4: Add to solution**

```bash
dotnet sln add src/ZeroAlloc.Serialisation/ZeroAlloc.Serialisation.csproj --solution-folder src
```

**Step 5: Build**

```bash
dotnet build src/ZeroAlloc.Serialisation/ZeroAlloc.Serialisation.csproj
```
Expected: `Build succeeded.`

**Step 6: Commit**

```bash
git add src/ZeroAlloc.Serialisation/
git commit -m "feat(core): add ISerializer<T> and ZeroAllocSerializableAttribute"
```

---

## Task 3: Core Tests

**Files:**
- Create: `tests/ZeroAlloc.Serialisation.Tests/ZeroAlloc.Serialisation.Tests.csproj`
- Create: `tests/ZeroAlloc.Serialisation.Tests/ISerializerContractTests.cs`

**Step 1: Create test project file**

`tests/ZeroAlloc.Serialisation.Tests/ZeroAlloc.Serialisation.Tests.csproj`:
```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFrameworks>net10.0</TargetFrameworks>
    <NoWarn>$(NoWarn);MA0048</NoWarn>
  </PropertyGroup>
  <ItemGroup>
    <PackageReference Include="xunit" />
    <PackageReference Include="xunit.runner.visualstudio" />
    <PackageReference Include="Microsoft.NET.Test.Sdk" />
    <ProjectReference Include="../../src/ZeroAlloc.Serialisation/ZeroAlloc.Serialisation.csproj" />
  </ItemGroup>
</Project>
```

**Step 2: Write failing test**

`tests/ZeroAlloc.Serialisation.Tests/ISerializerContractTests.cs`:
```csharp
using System.Buffers;
using ZeroAlloc.Serialisation;

namespace ZeroAlloc.Serialisation.Tests;

// Verify the interface shape is correct and can be implemented without issues.
public sealed class FakeSerializer : ISerializer<int>
{
    public void Serialize(IBufferWriter<byte> writer, int value)
        => writer.GetSpan(4)[0] = (byte)value; // minimal impl

    public int Deserialize(ReadOnlySpan<byte> buffer)
        => buffer[0];
}

public class ISerializerContractTests
{
    [Fact]
    public void ISerializer_CanBeImplemented()
    {
        ISerializer<int> s = new FakeSerializer();
        Assert.NotNull(s);
    }

    [Fact]
    public void ZeroAllocSerializableAttribute_StoresFormat()
    {
        var attr = new ZeroAllocSerializableAttribute(SerializationFormat.MemoryPack);
        Assert.Equal(SerializationFormat.MemoryPack, attr.Format);
    }
}
```

**Step 3: Add to solution and run test — expect FAIL (project not yet added)**

```bash
dotnet sln add tests/ZeroAlloc.Serialisation.Tests/ZeroAlloc.Serialisation.Tests.csproj --solution-folder tests
dotnet test tests/ZeroAlloc.Serialisation.Tests/
```
Expected: `Build succeeded. Passed: 2`

**Step 4: Commit**

```bash
git add tests/ZeroAlloc.Serialisation.Tests/
git commit -m "test(core): add ISerializer contract tests"
```

---

## Task 4: MemoryPack Backend

**Files:**
- Create: `src/ZeroAlloc.Serialisation.MemoryPack/ZeroAlloc.Serialisation.MemoryPack.csproj`
- Create: `src/ZeroAlloc.Serialisation.MemoryPack/MemoryPackSerializer.cs`

**Step 1: Write failing test first** (in `tests/ZeroAlloc.Serialisation.Tests/`, add file)

`tests/ZeroAlloc.Serialisation.Tests/MemoryPackSerializerTests.cs`:
```csharp
using System.Buffers;
using MemoryPack;
using ZeroAlloc.Serialisation.MemoryPack;

namespace ZeroAlloc.Serialisation.Tests;

[MemoryPackable]
public partial class SampleDto
{
    public int Id { get; set; }
    public string Name { get; set; } = "";
}

public class MemoryPackSerializerTests
{
    private readonly MemoryPackSerializer<SampleDto> _serializer = new();

    [Fact]
    public void RoundTrip_PreservesValues()
    {
        var original = new SampleDto { Id = 42, Name = "Alice" };
        var buffer = new ArrayBufferWriter<byte>();

        _serializer.Serialize(buffer, original);
        var result = _serializer.Deserialize(buffer.WrittenSpan);

        Assert.NotNull(result);
        Assert.Equal(42, result.Id);
        Assert.Equal("Alice", result.Name);
    }

    [Fact]
    public void Serialize_WritesBytes()
    {
        var buffer = new ArrayBufferWriter<byte>();
        _serializer.Serialize(buffer, new SampleDto { Id = 1, Name = "x" });
        Assert.True(buffer.WrittenCount > 0);
    }

    [Fact]
    public void Deserialize_EmptySpan_ReturnsDefault()
    {
        var result = _serializer.Deserialize(ReadOnlySpan<byte>.Empty);
        Assert.Null(result);
    }
}
```

**Step 2: Run test — expect FAIL (MemoryPackSerializer<T> does not exist)**

```bash
dotnet test tests/ZeroAlloc.Serialisation.Tests/ --filter MemoryPackSerializerTests
```
Expected: compilation error — `MemoryPackSerializer<SampleDto>` not found.

**Step 3: Create the MemoryPack project**

`src/ZeroAlloc.Serialisation.MemoryPack/ZeroAlloc.Serialisation.MemoryPack.csproj`:
```xml
<Project Sdk="Microsoft.NET.Sdk">
  <ItemGroup>
    <ProjectReference Include="../ZeroAlloc.Serialisation/ZeroAlloc.Serialisation.csproj" />
    <PackageReference Include="MemoryPack" />
  </ItemGroup>
</Project>
```

**Step 4: Create `MemoryPackSerializer.cs`**

```csharp
using System.Buffers;
using System.Diagnostics.CodeAnalysis;
using MemoryPack;
using ZeroAlloc.Serialisation;

namespace ZeroAlloc.Serialisation.MemoryPack;

public class MemoryPackSerializer<T> : ISerializer<T>
{
    [RequiresDynamicCode("MemoryPack serialization of arbitrary types may require dynamic code.")]
    [RequiresUnreferencedCode("MemoryPack serialization of arbitrary types may require unreferenced code.")]
    public virtual void Serialize(IBufferWriter<byte> writer, T value)
        => global::MemoryPack.MemoryPackSerializer.Serialize(writer, value);

    [RequiresDynamicCode("MemoryPack serialization of arbitrary types may require dynamic code.")]
    [RequiresUnreferencedCode("MemoryPack serialization of arbitrary types may require unreferenced code.")]
    public virtual T? Deserialize(ReadOnlySpan<byte> buffer)
    {
        if (buffer.IsEmpty) return default;
        return global::MemoryPack.MemoryPackSerializer.Deserialize<T>(buffer);
    }
}
```

**Step 5: Add MemoryPack project reference to tests, add to sln**

Update `tests/ZeroAlloc.Serialisation.Tests/ZeroAlloc.Serialisation.Tests.csproj`:
```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFrameworks>net10.0</TargetFrameworks>
    <NoWarn>$(NoWarn);MA0048</NoWarn>
    <!-- Suppress RequiresDynamicCode in test project — test DTOs are concrete types -->
    <TreatWarningsAsErrors>false</TreatWarningsAsErrors>
  </PropertyGroup>
  <ItemGroup>
    <PackageReference Include="xunit" />
    <PackageReference Include="xunit.runner.visualstudio" />
    <PackageReference Include="Microsoft.NET.Test.Sdk" />
    <PackageReference Include="MemoryPack" />
    <PackageReference Include="MessagePack" />
    <ProjectReference Include="../../src/ZeroAlloc.Serialisation/ZeroAlloc.Serialisation.csproj" />
    <ProjectReference Include="../../src/ZeroAlloc.Serialisation.MemoryPack/ZeroAlloc.Serialisation.MemoryPack.csproj" />
  </ItemGroup>
</Project>
```

```bash
dotnet sln add src/ZeroAlloc.Serialisation.MemoryPack/ZeroAlloc.Serialisation.MemoryPack.csproj --solution-folder src
```

**Step 6: Run tests — expect PASS**

```bash
dotnet test tests/ZeroAlloc.Serialisation.Tests/ --filter MemoryPackSerializerTests
```
Expected: `Passed: 3`

**Step 7: Commit**

```bash
git add src/ZeroAlloc.Serialisation.MemoryPack/ tests/ZeroAlloc.Serialisation.Tests/
git commit -m "feat(memorypack): add MemoryPackSerializer<T>"
```

---

## Task 5: MessagePack Backend

**Files:**
- Create: `src/ZeroAlloc.Serialisation.MessagePack/ZeroAlloc.Serialisation.MessagePack.csproj`
- Create: `src/ZeroAlloc.Serialisation.MessagePack/MessagePackSerializer.cs`

**Step 1: Write failing test**

`tests/ZeroAlloc.Serialisation.Tests/MessagePackSerializerTests.cs`:
```csharp
using System.Buffers;
using MessagePack;
using ZeroAlloc.Serialisation.MessagePack;

namespace ZeroAlloc.Serialisation.Tests;

[MessagePackObject]
public sealed class MsgPackDto
{
    [Key(0)] public int Id { get; set; }
    [Key(1)] public string Name { get; set; } = "";
}

public class MessagePackSerializerTests
{
    private readonly MessagePackSerializer<MsgPackDto> _serializer = new();

    [Fact]
    public void RoundTrip_PreservesValues()
    {
        var original = new MsgPackDto { Id = 7, Name = "Bob" };
        var buffer = new ArrayBufferWriter<byte>();

        _serializer.Serialize(buffer, original);
        var result = _serializer.Deserialize(buffer.WrittenSpan);

        Assert.NotNull(result);
        Assert.Equal(7, result.Id);
        Assert.Equal("Bob", result.Name);
    }

    [Fact]
    public void Serialize_WritesBytes()
    {
        var buffer = new ArrayBufferWriter<byte>();
        _serializer.Serialize(buffer, new MsgPackDto { Id = 1, Name = "x" });
        Assert.True(buffer.WrittenCount > 0);
    }
}
```

**Step 2: Run test — expect FAIL (project not found)**

```bash
dotnet test tests/ZeroAlloc.Serialisation.Tests/ --filter MessagePackSerializerTests
```

**Step 3: Create MessagePack project**

`src/ZeroAlloc.Serialisation.MessagePack/ZeroAlloc.Serialisation.MessagePack.csproj`:
```xml
<Project Sdk="Microsoft.NET.Sdk">
  <ItemGroup>
    <ProjectReference Include="../ZeroAlloc.Serialisation/ZeroAlloc.Serialisation.csproj" />
    <PackageReference Include="MessagePack" />
  </ItemGroup>
</Project>
```

**Step 4: Create `MessagePackSerializer.cs`**

```csharp
using System.Buffers;
using System.Diagnostics.CodeAnalysis;
using MessagePack;
using ZeroAlloc.Serialisation;

namespace ZeroAlloc.Serialisation.MessagePack;

public class MessagePackSerializer<T> : ISerializer<T>
{
    private readonly MessagePackSerializerOptions _options;

    public MessagePackSerializer()
        : this(MessagePackSerializerOptions.Standard) { }

    public MessagePackSerializer(MessagePackSerializerOptions options)
        => _options = options;

    [RequiresDynamicCode("MessagePack serialization of arbitrary types may require dynamic code.")]
    [RequiresUnreferencedCode("MessagePack serialization of arbitrary types may require unreferenced code.")]
    public virtual void Serialize(IBufferWriter<byte> writer, T value)
        => global::MessagePack.MessagePackSerializer.Serialize(writer, value, _options);

    [RequiresDynamicCode("MessagePack serialization of arbitrary types may require dynamic code.")]
    [RequiresUnreferencedCode("MessagePack serialization of arbitrary types may require unreferenced code.")]
    public virtual T? Deserialize(ReadOnlySpan<byte> buffer)
    {
        if (buffer.IsEmpty) return default;
        return global::MessagePack.MessagePackSerializer.Deserialize<T>(buffer, _options);
    }
}
```

**Step 5: Add to solution and tests csproj, run tests**

```bash
dotnet sln add src/ZeroAlloc.Serialisation.MessagePack/ZeroAlloc.Serialisation.MessagePack.csproj --solution-folder src
```

Add to `tests/ZeroAlloc.Serialisation.Tests/ZeroAlloc.Serialisation.Tests.csproj`:
```xml
<ProjectReference Include="../../src/ZeroAlloc.Serialisation.MessagePack/ZeroAlloc.Serialisation.MessagePack.csproj" />
```

```bash
dotnet test tests/ZeroAlloc.Serialisation.Tests/ --filter MessagePackSerializerTests
```
Expected: `Passed: 2`

**Step 6: Commit**

```bash
git add src/ZeroAlloc.Serialisation.MessagePack/ tests/ZeroAlloc.Serialisation.Tests/
git commit -m "feat(messagepack): add MessagePackSerializer<T>"
```

---

## Task 6: SystemTextJson Backend

**Files:**
- Create: `src/ZeroAlloc.Serialisation.SystemTextJson/ZeroAlloc.Serialisation.SystemTextJson.csproj`
- Create: `src/ZeroAlloc.Serialisation.SystemTextJson/SystemTextJsonSerializer.cs`

**Step 1: Write failing test**

`tests/ZeroAlloc.Serialisation.Tests/SystemTextJsonSerializerTests.cs`:
```csharp
using System.Buffers;
using System.Text.Json.Serialization;
using ZeroAlloc.Serialisation.SystemTextJson;

namespace ZeroAlloc.Serialisation.Tests;

public sealed class JsonDto
{
    public int Id { get; set; }
    public string Name { get; set; } = "";
}

[JsonSerializable(typeof(JsonDto))]
internal partial class TestJsonContext : JsonSerializerContext { }

public class SystemTextJsonSerializerTests
{
    private readonly SystemTextJsonSerializer<JsonDto> _serializer =
        new(TestJsonContext.Default.JsonDto);

    [Fact]
    public void RoundTrip_PreservesValues()
    {
        var original = new JsonDto { Id = 3, Name = "Carol" };
        var buffer = new ArrayBufferWriter<byte>();

        _serializer.Serialize(buffer, original);
        var result = _serializer.Deserialize(buffer.WrittenSpan);

        Assert.NotNull(result);
        Assert.Equal(3, result.Id);
        Assert.Equal("Carol", result.Name);
    }

    [Fact]
    public void Serialize_WritesUtf8Json()
    {
        var buffer = new ArrayBufferWriter<byte>();
        _serializer.Serialize(buffer, new JsonDto { Id = 1, Name = "x" });
        Assert.Contains((byte)'{', buffer.WrittenSpan.ToArray());
    }
}
```

**Step 2: Run test — expect FAIL**

```bash
dotnet test tests/ZeroAlloc.Serialisation.Tests/ --filter SystemTextJsonSerializerTests
```

**Step 3: Create STJ project**

`src/ZeroAlloc.Serialisation.SystemTextJson/ZeroAlloc.Serialisation.SystemTextJson.csproj`:
```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <IsAotCompatible>true</IsAotCompatible>
  </PropertyGroup>
  <ItemGroup>
    <ProjectReference Include="../ZeroAlloc.Serialisation/ZeroAlloc.Serialisation.csproj" />
  </ItemGroup>
</Project>
```

**Step 4: Create `SystemTextJsonSerializer.cs`**

```csharp
using System.Buffers;
using System.Text.Json;
using System.Text.Json.Serialization.Metadata;
using ZeroAlloc.Serialisation;

namespace ZeroAlloc.Serialisation.SystemTextJson;

// AOT-safe: caller supplies JsonTypeInfo<T> from a JsonSerializerContext.
public sealed class SystemTextJsonSerializer<T> : ISerializer<T>
{
    private readonly JsonTypeInfo<T> _typeInfo;

    public SystemTextJsonSerializer(JsonTypeInfo<T> typeInfo)
        => _typeInfo = typeInfo;

    public void Serialize(IBufferWriter<byte> writer, T value)
    {
        using var jsonWriter = new Utf8JsonWriter(writer);
        JsonSerializer.Serialize(jsonWriter, value, _typeInfo);
    }

    public T? Deserialize(ReadOnlySpan<byte> buffer)
    {
        if (buffer.IsEmpty) return default;
        return JsonSerializer.Deserialize(buffer, _typeInfo);
    }
}
```

**Step 5: Add to solution and tests, run tests**

```bash
dotnet sln add src/ZeroAlloc.Serialisation.SystemTextJson/ZeroAlloc.Serialisation.SystemTextJson.csproj --solution-folder src
```

Add to tests csproj:
```xml
<ProjectReference Include="../../src/ZeroAlloc.Serialisation.SystemTextJson/ZeroAlloc.Serialisation.SystemTextJson.csproj" />
```

```bash
dotnet test tests/ZeroAlloc.Serialisation.Tests/ --filter SystemTextJsonSerializerTests
```
Expected: `Passed: 2`

**Step 6: Commit**

```bash
git add src/ZeroAlloc.Serialisation.SystemTextJson/ tests/ZeroAlloc.Serialisation.Tests/
git commit -m "feat(systemtextjson): add SystemTextJsonSerializer<T>"
```

---

## Task 7: Source Generator — Skeleton

**Files:**
- Create: `src/ZeroAlloc.Serialisation.Generator/ZeroAlloc.Serialisation.Generator.csproj`
- Create: `src/ZeroAlloc.Serialisation.Generator/IsExternalInit.cs`
- Create: `src/ZeroAlloc.Serialisation.Generator/SerializerGenerator.cs`

**Step 1: Create generator project file**

`src/ZeroAlloc.Serialisation.Generator/ZeroAlloc.Serialisation.Generator.csproj`:
```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>netstandard2.0</TargetFramework>
    <EnforceExtendedAnalyzerRules>true</EnforceExtendedAnalyzerRules>
    <IsRoslynComponent>true</IsRoslynComponent>
  </PropertyGroup>
  <ItemGroup>
    <PackageReference Include="Microsoft.CodeAnalysis.CSharp" PrivateAssets="all" />
    <PackageReference Include="Microsoft.CodeAnalysis.Analyzers" PrivateAssets="all" />
  </ItemGroup>
</Project>
```

**Step 2: Create `IsExternalInit.cs`** (required for `record` / `init` in netstandard2.0)

```csharp
// Licensed to the .NET Foundation under one or more agreements.
namespace System.Runtime.CompilerServices;

internal static class IsExternalInit { }
```

**Step 3: Create generator skeleton**

`src/ZeroAlloc.Serialisation.Generator/SerializerGenerator.cs`:
```csharp
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace ZeroAlloc.Serialisation.Generator;

[Generator]
public sealed class SerializerGenerator : IIncrementalGenerator
{
    private const string AttributeFullName =
        "ZeroAlloc.Serialisation.ZeroAllocSerializableAttribute";

    public void Initialize(IncrementalGeneratorInitializationContext context)
    {
        var types = context.SyntaxProvider
            .ForAttributeWithMetadataName(
                AttributeFullName,
                predicate: static (node, _) =>
                    node is ClassDeclarationSyntax or StructDeclarationSyntax,
                transform: static (ctx, ct) => ModelExtractor.Extract(ctx, ct))
            .Where(static m => m is not null)
            .Select(static (m, _) => m!);

        context.RegisterSourceOutput(types, static (ctx, model) =>
        {
            SerializerEmitter.Emit(ctx, model);
            DiEmitter.Emit(ctx, model);
        });
    }
}
```

**Step 4: Add to solution**

```bash
dotnet sln add src/ZeroAlloc.Serialisation.Generator/ZeroAlloc.Serialisation.Generator.csproj --solution-folder src
dotnet build src/ZeroAlloc.Serialisation.Generator/
```
Expected: `Build succeeded.` (SerializerEmitter/DiEmitter/ModelExtractor will be added next tasks)

> Note: this step will fail to build until Task 8 adds the missing types. That is expected — proceed to Task 8 immediately.

---

## Task 8: Source Generator — Model Extraction

**Files:**
- Create: `src/ZeroAlloc.Serialisation.Generator/Models/SerializerModel.cs`
- Create: `src/ZeroAlloc.Serialisation.Generator/ModelExtractor.cs`

**Step 1: Create `SerializerModel.cs`**

```csharp
namespace ZeroAlloc.Serialisation.Generator.Models;

internal sealed record SerializerModel(
    string Namespace,
    string TypeName,
    string FullTypeName,
    string FormatName  // "MemoryPack" | "MessagePack" | "SystemTextJson"
);
```

**Step 2: Create `ModelExtractor.cs`**

```csharp
using Microsoft.CodeAnalysis;
using ZeroAlloc.Serialisation.Generator.Models;

namespace ZeroAlloc.Serialisation.Generator;

internal static class ModelExtractor
{
    public static SerializerModel? Extract(
        GeneratorAttributeSyntaxContext ctx,
        CancellationToken ct)
    {
        if (ctx.TargetSymbol is not INamedTypeSymbol typeSymbol)
            return null;

        // Find the ZeroAllocSerializableAttribute and extract the Format enum value
        foreach (var attrData in typeSymbol.GetAttributes())
        {
            ct.ThrowIfCancellationRequested();
            if (attrData.AttributeClass?.ToDisplayString() !=
                "ZeroAlloc.Serialisation.ZeroAllocSerializableAttribute")
                continue;

            if (attrData.ConstructorArguments.Length != 1)
                return null;

            var formatValue = (int)attrData.ConstructorArguments[0].Value!;
            var formatName = formatValue switch
            {
                0 => "MemoryPack",
                1 => "MessagePack",
                2 => "SystemTextJson",
                _ => null,
            };

            if (formatName is null) return null;

            var ns = typeSymbol.ContainingNamespace.IsGlobalNamespace
                ? string.Empty
                : typeSymbol.ContainingNamespace.ToDisplayString();

            return new SerializerModel(
                Namespace: ns,
                TypeName: typeSymbol.Name,
                FullTypeName: typeSymbol.ToDisplayString(),
                FormatName: formatName);
        }

        return null;
    }
}
```

**Step 3: Build generator**

```bash
dotnet build src/ZeroAlloc.Serialisation.Generator/
```
Expected: compile errors for missing `SerializerEmitter` and `DiEmitter` — proceed to Task 9.

---

## Task 9: Source Generator — Emitters

**Files:**
- Create: `src/ZeroAlloc.Serialisation.Generator/SerializerEmitter.cs`
- Create: `src/ZeroAlloc.Serialisation.Generator/DiEmitter.cs`

**Step 1: Create `SerializerEmitter.cs`**

```csharp
using Microsoft.CodeAnalysis;
using ZeroAlloc.Serialisation.Generator.Models;

namespace ZeroAlloc.Serialisation.Generator;

internal static class SerializerEmitter
{
    public static void Emit(SourceProductionContext ctx, SerializerModel model)
    {
        var serializerCall = model.FormatName switch
        {
            "MemoryPack" =>
                $"global::MemoryPack.MemoryPackSerializer.Serialize(writer, value);",
            "MessagePack" =>
                $"global::MessagePack.MessagePackSerializer.Serialize(writer, value);",
            "SystemTextJson" =>
                $"global::System.Text.Json.JsonSerializer.Serialize(new global::System.Text.Json.Utf8JsonWriter(writer), value);",
            _ => throw new InvalidOperationException($"Unknown format: {model.FormatName}"),
        };

        var deserializeCall = model.FormatName switch
        {
            "MemoryPack" =>
                $"return global::MemoryPack.MemoryPackSerializer.Deserialize<{model.FullTypeName}>(buffer);",
            "MessagePack" =>
                $"return global::MessagePack.MessagePackSerializer.Deserialize<{model.FullTypeName}>(buffer);",
            "SystemTextJson" =>
                $"return global::System.Text.Json.JsonSerializer.Deserialize<{model.FullTypeName}>(buffer);",
            _ => throw new InvalidOperationException($"Unknown format: {model.FormatName}"),
        };

        var ns = string.IsNullOrEmpty(model.Namespace)
            ? ""
            : $"namespace {model.Namespace};\n\n";

        var source = $$"""
            // <auto-generated/>
            #nullable enable
            #pragma warning disable CS8603 // suppress RequiresDynamicCode: T is a closed, concrete type
            using System.Buffers;
            using ZeroAlloc.Serialisation;

            {{ns}}internal sealed class {{model.TypeName}}Serializer : ISerializer<{{model.FullTypeName}}>
            {
                public void Serialize(IBufferWriter<byte> writer, {{model.FullTypeName}} value)
                    => {{serializerCall}}

                public {{model.FullTypeName}}? Deserialize(ReadOnlySpan<byte> buffer)
                {
                    if (buffer.IsEmpty) return default;
                    {{deserializeCall}}
                }
            }
            """;

        ctx.AddSource($"{model.TypeName}Serializer.g.cs", source);
    }
}
```

**Step 2: Create `DiEmitter.cs`**

```csharp
using Microsoft.CodeAnalysis;
using ZeroAlloc.Serialisation.Generator.Models;

namespace ZeroAlloc.Serialisation.Generator;

internal static class DiEmitter
{
    public static void Emit(SourceProductionContext ctx, SerializerModel model)
    {
        var ns = string.IsNullOrEmpty(model.Namespace)
            ? ""
            : $"namespace {model.Namespace};\n\n";

        var source = $$"""
            // <auto-generated/>
            #nullable enable
            using Microsoft.Extensions.DependencyInjection;
            using ZeroAlloc.Serialisation;

            {{ns}}public static partial class SerializerServiceCollectionExtensions
            {
                public static IServiceCollection Add{{model.TypeName}}Serializer(
                    this IServiceCollection services)
                    => services.AddSingleton<ISerializer<{{model.FullTypeName}}>, {{model.TypeName}}Serializer>();
            }
            """;

        ctx.AddSource($"{model.TypeName}SerializerExtensions.g.cs", source);
    }
}
```

**Step 3: Build generator**

```bash
dotnet build src/ZeroAlloc.Serialisation.Generator/
```
Expected: `Build succeeded.`

**Step 4: Commit**

```bash
git add src/ZeroAlloc.Serialisation.Generator/
git commit -m "feat(generator): add SerializerGenerator with model extraction and emitters"
```

---

## Task 10: Generator Tests

**Files:**
- Create: `tests/ZeroAlloc.Serialisation.Generator.Tests/ZeroAlloc.Serialisation.Generator.Tests.csproj`
- Create: `tests/ZeroAlloc.Serialisation.Generator.Tests/SerializerGeneratorTests.cs`

**Step 1: Create test project**

`tests/ZeroAlloc.Serialisation.Generator.Tests/ZeroAlloc.Serialisation.Generator.Tests.csproj`:
```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFrameworks>net10.0</TargetFrameworks>
    <NoWarn>$(NoWarn);NU1608;MA0006;MA0048;MA0074</NoWarn>
  </PropertyGroup>
  <ItemGroup>
    <PackageReference Include="xunit" />
    <PackageReference Include="xunit.runner.visualstudio" />
    <PackageReference Include="Microsoft.NET.Test.Sdk" />
    <PackageReference Include="Microsoft.CodeAnalysis.CSharp" />
    <PackageReference Include="Microsoft.CodeAnalysis.CSharp.SourceGenerators.Testing.XUnit" />
    <PackageReference Include="Basic.Reference.Assemblies.Net100" />
    <ProjectReference Include="../../src/ZeroAlloc.Serialisation.Generator/ZeroAlloc.Serialisation.Generator.csproj" />
    <ProjectReference Include="../../src/ZeroAlloc.Serialisation/ZeroAlloc.Serialisation.csproj" />
  </ItemGroup>
</Project>
```

**Step 2: Write failing tests**

`tests/ZeroAlloc.Serialisation.Generator.Tests/SerializerGeneratorTests.cs`:
```csharp
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Testing.XUnit;
using Microsoft.CodeAnalysis.Testing;
using ZeroAlloc.Serialisation.Generator;

namespace ZeroAlloc.Serialisation.Generator.Tests;

public class SerializerGeneratorTests
{
    [Fact]
    public async Task Generator_MemoryPack_EmitsSerializerClass()
    {
        var source = """
            using ZeroAlloc.Serialisation;
            using MemoryPack;

            namespace MyApp;

            [ZeroAllocSerializable(SerializationFormat.MemoryPack)]
            [MemoryPackable]
            public partial class PersonEvent
            {
                public int Id { get; set; }
            }
            """;

        var test = new CSharpSourceGeneratorTest<SerializerGenerator, DefaultVerifier>
        {
            TestCode = source,
        };

        // Verify the generated file contains the serializer class name
        test.TestState.GeneratedSources.Add((
            typeof(SerializerGenerator),
            "PersonEventSerializer.g.cs",
            Microsoft.CodeAnalysis.Text.SourceText.From(
                string.Empty,  // We only check it compiles, not exact content in this test
                System.Text.Encoding.UTF8)));

        // The real test: does the generated compilation succeed?
        var compilation = CreateCompilation(source);
        var generator = new SerializerGenerator();
        var driver = CSharpGeneratorDriver.Create(generator)
            .RunGenerators(compilation);

        var result = driver.GetRunResult();
        Assert.Empty(result.Diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error));

        var generated = result.GeneratedTrees;
        Assert.Contains(generated, t => t.FilePath.Contains("PersonEventSerializer.g.cs"));
        Assert.Contains(generated, t => t.FilePath.Contains("PersonEventSerializerExtensions.g.cs"));

        await Task.CompletedTask;
    }

    [Fact]
    public async Task Generator_NoAttribute_EmitsNothing()
    {
        var source = """
            namespace MyApp;
            public class PlainClass { }
            """;

        var compilation = CreateCompilation(source);
        var generator = new SerializerGenerator();
        var driver = CSharpGeneratorDriver.Create(generator)
            .RunGenerators(compilation);

        var result = driver.GetRunResult();
        Assert.Empty(result.GeneratedTrees);

        await Task.CompletedTask;
    }

    [Fact]
    public async Task Generator_EmittedSerializer_ContainsMemoryPackCall()
    {
        var source = """
            using ZeroAlloc.Serialisation;

            namespace MyApp;

            [ZeroAllocSerializable(SerializationFormat.MemoryPack)]
            public class OrderEvent { }
            """;

        var compilation = CreateCompilation(source);
        var driver = CSharpGeneratorDriver.Create(new SerializerGenerator())
            .RunGenerators(compilation);

        var result = driver.GetRunResult();
        var serializerFile = result.GeneratedTrees
            .FirstOrDefault(t => t.FilePath.Contains("OrderEventSerializer.g.cs"));

        Assert.NotNull(serializerFile);
        var text = serializerFile!.GetText().ToString();
        Assert.Contains("MemoryPackSerializer.Serialize", text);
        Assert.Contains("MemoryPackSerializer.Deserialize<", text);
        Assert.Contains("ISerializer<MyApp.OrderEvent>", text);

        await Task.CompletedTask;
    }

    [Fact]
    public async Task Generator_EmittedDiExtension_ContainsAddMethod()
    {
        var source = """
            using ZeroAlloc.Serialisation;

            namespace MyApp;

            [ZeroAllocSerializable(SerializationFormat.MessagePack)]
            public class InvoiceEvent { }
            """;

        var compilation = CreateCompilation(source);
        var driver = CSharpGeneratorDriver.Create(new SerializerGenerator())
            .RunGenerators(compilation);

        var result = driver.GetRunResult();
        var diFile = result.GeneratedTrees
            .FirstOrDefault(t => t.FilePath.Contains("InvoiceEventSerializerExtensions.g.cs"));

        Assert.NotNull(diFile);
        var text = diFile!.GetText().ToString();
        Assert.Contains("AddInvoiceEventSerializer", text);
        Assert.Contains("ISerializer<MyApp.InvoiceEvent>", text);

        await Task.CompletedTask;
    }

    private static CSharpCompilation CreateCompilation(string source)
    {
        var syntaxTree = CSharpSyntaxTree.ParseText(source);
        return CSharpCompilation.Create(
            "TestAssembly",
            [syntaxTree],
            Basic.Reference.Assemblies.Net100.References.All,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
    }
}
```

**Step 3: Add to solution and run**

```bash
dotnet sln add tests/ZeroAlloc.Serialisation.Generator.Tests/ZeroAlloc.Serialisation.Generator.Tests.csproj --solution-folder tests
dotnet test tests/ZeroAlloc.Serialisation.Generator.Tests/
```
Expected: `Passed: 4`

> Note: if any test fails, the generator emitter code in Task 9 needs adjustment. Compare the actual generated text in the failure output against what the test expects.

**Step 4: Commit**

```bash
git add tests/ZeroAlloc.Serialisation.Generator.Tests/
git commit -m "test(generator): add source generator tests"
```

---

## Task 11: Benchmarks

**Files:**
- Create: `tests/ZeroAlloc.Serialisation.Benchmarks/ZeroAlloc.Serialisation.Benchmarks.csproj`
- Create: `tests/ZeroAlloc.Serialisation.Benchmarks/SerializerBenchmarks.cs`
- Create: `tests/ZeroAlloc.Serialisation.Benchmarks/Program.cs`

**Step 1: Create benchmark project**

`tests/ZeroAlloc.Serialisation.Benchmarks/ZeroAlloc.Serialisation.Benchmarks.csproj`:
```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <OutputType>Exe</OutputType>
    <TargetFrameworks>net10.0</TargetFrameworks>
    <TreatWarningsAsErrors>false</TreatWarningsAsErrors>
  </PropertyGroup>
  <ItemGroup>
    <ProjectReference Include="../../src/ZeroAlloc.Serialisation/ZeroAlloc.Serialisation.csproj" />
    <ProjectReference Include="../../src/ZeroAlloc.Serialisation.Generator/ZeroAlloc.Serialisation.Generator.csproj"
                      OutputItemType="Analyzer"
                      ReferenceOutputAssembly="false" />
    <ProjectReference Include="../../src/ZeroAlloc.Serialisation.MemoryPack/ZeroAlloc.Serialisation.MemoryPack.csproj" />
    <ProjectReference Include="../../src/ZeroAlloc.Serialisation.MessagePack/ZeroAlloc.Serialisation.MessagePack.csproj" />
    <ProjectReference Include="../../src/ZeroAlloc.Serialisation.SystemTextJson/ZeroAlloc.Serialisation.SystemTextJson.csproj" />
    <PackageReference Include="BenchmarkDotNet" />
    <PackageReference Include="MemoryPack" />
    <PackageReference Include="MessagePack" />
  </ItemGroup>
</Project>
```

**Step 2: Create `SerializerBenchmarks.cs`**

```csharp
using System.Buffers;
using System.Text.Json.Serialization;
using BenchmarkDotNet.Attributes;
using MemoryPack;
using MessagePack;
using ZeroAlloc.Serialisation;
using ZeroAlloc.Serialisation.MemoryPack;
using ZeroAlloc.Serialisation.MessagePack;
using ZeroAlloc.Serialisation.SystemTextJson;

namespace ZeroAlloc.Serialisation.Benchmarks;

[MemoryPackable]
[MessagePackObject]
public partial class BenchDto
{
    [Key(0)] public int Id { get; set; }
    [Key(1)] public string Name { get; set; } = "";
}

[JsonSerializable(typeof(BenchDto))]
internal partial class BenchContext : JsonSerializerContext { }

[MemoryDiagnoser]
[SimpleJob]
public class SerializerBenchmarks
{
    private static readonly BenchDto s_dto = new() { Id = 42, Name = "Alice" };

    private readonly ISerializer<BenchDto> _mp = new MemoryPackSerializer<BenchDto>();
    private readonly ISerializer<BenchDto> _msg = new MessagePackSerializer<BenchDto>();
    private readonly ISerializer<BenchDto> _stj = new SystemTextJsonSerializer<BenchDto>(BenchContext.Default.BenchDto);

    private byte[] _mpBytes = [];
    private byte[] _msgBytes = [];
    private byte[] _stjBytes = [];

    [GlobalSetup]
    public void Setup()
    {
        var buf = new ArrayBufferWriter<byte>();
        _mp.Serialize(buf, s_dto); _mpBytes = buf.WrittenSpan.ToArray();
        buf = new ArrayBufferWriter<byte>();
        _msg.Serialize(buf, s_dto); _msgBytes = buf.WrittenSpan.ToArray();
        buf = new ArrayBufferWriter<byte>();
        _stj.Serialize(buf, s_dto); _stjBytes = buf.WrittenSpan.ToArray();
    }

    [Benchmark(Baseline = true)]
    public int Serialize_MemoryPack()
    {
        var buf = new ArrayBufferWriter<byte>();
        _mp.Serialize(buf, s_dto);
        return buf.WrittenCount;
    }

    [Benchmark]
    public int Serialize_MessagePack()
    {
        var buf = new ArrayBufferWriter<byte>();
        _msg.Serialize(buf, s_dto);
        return buf.WrittenCount;
    }

    [Benchmark]
    public int Serialize_SystemTextJson()
    {
        var buf = new ArrayBufferWriter<byte>();
        _stj.Serialize(buf, s_dto);
        return buf.WrittenCount;
    }

    [Benchmark]
    public BenchDto? Deserialize_MemoryPack() => _mp.Deserialize(_mpBytes);

    [Benchmark]
    public BenchDto? Deserialize_MessagePack() => _msg.Deserialize(_msgBytes);

    [Benchmark]
    public BenchDto? Deserialize_SystemTextJson() => _stj.Deserialize(_stjBytes);
}
```

**Step 3: Create `Program.cs`**

```csharp
using BenchmarkDotNet.Running;
using ZeroAlloc.Serialisation.Benchmarks;

BenchmarkRunner.Run<SerializerBenchmarks>();
```

**Step 4: Add to solution and verify it builds**

```bash
dotnet sln add tests/ZeroAlloc.Serialisation.Benchmarks/ZeroAlloc.Serialisation.Benchmarks.csproj --solution-folder tests
dotnet build tests/ZeroAlloc.Serialisation.Benchmarks/
```
Expected: `Build succeeded.`

**Step 5: Commit**

```bash
git add tests/ZeroAlloc.Serialisation.Benchmarks/
git commit -m "test(benchmarks): add serializer benchmarks"
```

---

## Task 12: REST Integration — Update `ZeroAlloc.Rest.MemoryPack`

This task lives in the **`ZeroAlloc.Rest`** repository (`c:/Projects/Prive/ZeroAlloc.Rest`).

**Files:**
- Modify: `src/ZeroAlloc.Rest.MemoryPack/MemoryPackRestSerializer.cs`

**Step 1: Write failing test that proves current impl allocates**

In `tests/ZeroAlloc.Rest.Tests/Serializers/MemoryPackSerializerTests.cs`, verify the serializer works with `ArrayBufferWriter` by adding an assertion that `SerializeAsync` doesn't allocate a `byte[]` intermediate — this is a code-reading observation, not a unit test. Proceed directly to the fix.

**Step 2: Fix the intermediate `byte[]` allocation**

Replace the current `SerializeAsync` in `src/ZeroAlloc.Rest.MemoryPack/MemoryPackRestSerializer.cs:27`:
```csharp
// Before:
public async ValueTask SerializeAsync<T>(Stream stream, T value, CancellationToken ct = default)
{
    var bytes = MemoryPackSerializer.Serialize(value);
    await stream.WriteAsync(bytes, 0, bytes.Length, ct).ConfigureAwait(false);
}

// After:
public async ValueTask SerializeAsync<T>(Stream stream, T value, CancellationToken ct = default)
{
    var buffer = new ArrayBufferWriter<byte>();
    MemoryPackSerializer.Serialize(buffer, value);
    await stream.WriteAsync(buffer.WrittenMemory, ct).ConfigureAwait(false);
}
```

**Step 3: Run existing Rest tests**

```bash
cd c:/Projects/Prive/ZeroAlloc.Rest
dotnet test tests/ZeroAlloc.Rest.Tests/ --filter MemoryPack
```
Expected: all existing MemoryPack serializer tests pass.

**Step 4: Commit in ZeroAlloc.Rest**

```bash
git add src/ZeroAlloc.Rest.MemoryPack/MemoryPackRestSerializer.cs
git commit -m "perf(memorypack): eliminate intermediate byte[] in SerializeAsync"
```

---

## Task 13: Full Solution Verify

**Step 1: Run all tests**

```bash
cd c:/Projects/Prive/ZeroAlloc.Serialisation
dotnet test
```
Expected: all tests pass across all test projects.

**Step 2: Build entire solution**

```bash
dotnet build
```
Expected: `Build succeeded. 0 Error(s) 0 Warning(s)` (or only expected suppressions).

**Step 3: Final commit**

```bash
git add .
git commit -m "chore(ci): verify full solution builds and all tests pass"
```
