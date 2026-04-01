# ZeroAlloc.Serialisation

[![NuGet](https://img.shields.io/nuget/v/ZeroAlloc.Serialisation.svg)](https://www.nuget.org/packages/ZeroAlloc.Serialisation)
[![Build](https://github.com/ZeroAlloc-Net/ZeroAlloc.Serialisation/actions/workflows/ci.yml/badge.svg)](https://github.com/ZeroAlloc-Net/ZeroAlloc.Serialisation/actions/workflows/ci.yml)
[![License: MIT](https://img.shields.io/badge/License-MIT-yellow.svg)](LICENSE)

ZeroAlloc.Serialisation is a shared `IBufferWriter<byte>`-based serialisation library for the ZeroAlloc ecosystem. It provides a typed `ISerializer<T>` interface with a Roslyn source generator that emits AOT-safe, closed-generic implementations per annotated type — no reflection, no `[RequiresDynamicCode]` on hot paths.

Works with [MemoryPack](https://github.com/Cysharp/MemoryPack), [MessagePack](https://github.com/MessagePack-CSharp/MessagePack-CSharp), and `System.Text.Json`.

## Install

```bash
# Core interface
dotnet add package ZeroAlloc.Serialisation

# Source generator (add as analyzer — no runtime dependency)
dotnet add package ZeroAlloc.Serialisation.Generator

# Pick your backend(s)
dotnet add package ZeroAlloc.Serialisation.MemoryPack
dotnet add package ZeroAlloc.Serialisation.MessagePack
dotnet add package ZeroAlloc.Serialisation.SystemTextJson
```

Add the generator as an analyzer in your `.csproj`:

```xml
<PackageReference Include="ZeroAlloc.Serialisation.Generator" Version="*"
                  OutputItemType="Analyzer" ReferenceOutputAssembly="false" />
```

## Quick Start

```csharp
// 1. Annotate your type
[MemoryPackable]
[ZeroAllocSerializable(SerializationFormat.MemoryPack)]
public partial class OrderCreated
{
    public Guid OrderId { get; set; }
    public string Product { get; set; } = "";
}

// 2. The generator emits OrderCreatedSerializer : ISerializer<OrderCreated>
//    Register it with DI:
services.AddOrderCreatedSerializer();

// 3. Use it — writes directly to an IBufferWriter<byte>, zero intermediate allocation
public class OrderEventStore(ISerializer<OrderCreated> serializer)
{
    public void Append(IBufferWriter<byte> writer, OrderCreated evt)
        => serializer.Serialize(writer, evt);

    public OrderCreated? Read(ReadOnlySpan<byte> bytes)
        => serializer.Deserialize(bytes);
}
```

## Packages

| Package | Description | TFM |
|---|---|---|
| `ZeroAlloc.Serialisation` | `ISerializer<T>`, `ZeroAllocSerializableAttribute` | netstandard2.1, net8–10 |
| `ZeroAlloc.Serialisation.Generator` | Roslyn source generator | netstandard2.0 |
| `ZeroAlloc.Serialisation.MemoryPack` | MemoryPack backend | net8–10 |
| `ZeroAlloc.Serialisation.MessagePack` | MessagePack backend | net8–10 |
| `ZeroAlloc.Serialisation.SystemTextJson` | System.Text.Json backend | net8–10 |

## AOT Safety

Generated serializers suppress `[RequiresDynamicCode]` and `[RequiresUnreferencedCode]` because `T` is resolved at generation time. The backend's own source generator (MemoryPack / MessagePack) has already emitted the formatter, so no dynamic code is required at runtime.

Base classes (`MemoryPackSerializer<T>`, etc.) carry the attributes for ad-hoc / non-AOT use.

## REST Integration

Use `RestSerializerAdapter<T>` from `ZeroAlloc.Rest` to bridge `ISerializer<T>` to `IRestSerializer`:

```csharp
services.AddSingleton<IRestSerializer>(sp =>
    new RestSerializerAdapter<OrderCreated>(sp.GetRequiredService<ISerializer<OrderCreated>>()));
```

## License

MIT
