# ZeroAlloc.Serialisation

[![NuGet](https://img.shields.io/nuget/v/ZeroAlloc.Serialisation.svg)](https://www.nuget.org/packages/ZeroAlloc.Serialisation)
[![Build](https://github.com/ZeroAlloc-Net/ZeroAlloc.Serialisation/actions/workflows/ci.yml/badge.svg)](https://github.com/ZeroAlloc-Net/ZeroAlloc.Serialisation/actions/workflows/ci.yml)
[![License: MIT](https://img.shields.io/badge/License-MIT-yellow.svg)](LICENSE)
[![AOT](https://img.shields.io/badge/AOT--Compatible-passing-brightgreen)](https://learn.microsoft.com/dotnet/core/deploying/native-aot/)
[![GitHub Sponsors](https://img.shields.io/github/sponsors/MarcelRoozekrans?style=flat&logo=githubsponsors&color=ea4aaa&label=Sponsor)](https://github.com/sponsors/MarcelRoozekrans)

ZeroAlloc.Serialisation is a shared `IBufferWriter<byte>`-based serialisation library for the ZeroAlloc ecosystem. It provides a typed `ISerializer<T>` interface with a Roslyn source generator that emits AOT-safe, closed-generic implementations per annotated type — no reflection, no `[RequiresDynamicCode]` on hot paths.

Works with [MemoryPack](https://github.com/Cysharp/MemoryPack), [MessagePack](https://github.com/MessagePack-CSharp/MessagePack-CSharp), and `System.Text.Json`.

## Install

The source generator is bundled into the main package — a single `PackageReference` is all you need:

```bash
# Core interface + bundled source generator
dotnet add package ZeroAlloc.Serialisation

# Pick your backend(s)
dotnet add package ZeroAlloc.Serialisation.MemoryPack
dotnet add package ZeroAlloc.Serialisation.MessagePack
dotnet add package ZeroAlloc.Serialisation.SystemTextJson
```

> The standalone `ZeroAlloc.Serialisation.Generator` package is still published for backwards compatibility with existing direct PackageReferences, but new consumers should reference only `ZeroAlloc.Serialisation`.

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

## Runtime Dispatch

When you need to serialize/deserialize by `Type` at runtime (e.g. in event sourcing infrastructure), use `ISerializerDispatcher`. The generator emits one `SerializerDispatcher` per assembly covering all annotated types:

```csharp
// Register the dispatcher alongside per-type serializers:
services.AddOrderCreatedSerializer();
services.AddOrderShippedSerializer();
services.AddSerializerDispatcher();  // registers ISerializerDispatcher → SerializerDispatcher

// Inject and use:
public class EventStore(ISerializerDispatcher dispatcher)
{
    public ReadOnlyMemory<byte> Serialize(object @event)
        => dispatcher.Serialize(@event, @event.GetType());

    public object? Deserialize(ReadOnlyMemory<byte> data, Type eventType)
        => dispatcher.Deserialize(data, eventType);
}
```

The generated `SerializerDispatcher` uses a compile-time switch — no reflection, no dictionary lookup, AOT-safe.

## Packages

| Package | Description | TFM |
|---|---|---|
| `ZeroAlloc.Serialisation` | `ISerializer<T>`, `ISerializerDispatcher`, `ZeroAllocSerializableAttribute` | netstandard2.1, net8–10 |
| `ZeroAlloc.Serialisation.Generator` | Roslyn source generator | netstandard2.0 |
| `ZeroAlloc.Serialisation.MemoryPack` | MemoryPack backend | net8–10 |
| `ZeroAlloc.Serialisation.MessagePack` | MessagePack backend | net8–10 |
| `ZeroAlloc.Serialisation.SystemTextJson` | System.Text.Json backend | net8–10 |

## Performance

ZA.Serialisation is an abstraction layer — not a competing serializer. The honest comparison is whether the wrapper adds measurable overhead vs calling the raw library directly. .NET 10.0.7, BenchmarkDotNet v0.14.0.

**Deserialize (wrapper is thin):**

| Library | Raw | ZA wrapper | Overhead |
|---|---:|---:|---:|
| MemoryPack | 48 ns / 64 B | 55 ns / 64 B | +16%, 0 B |
| MessagePack | 124 ns / 64 B | 183 ns / 96 B | +47%, +32 B |
| System.Text.Json | 303 ns / 64 B | 375 ns / 64 B | +23%, 0 B |

**Serialize (IBufferWriter pattern adds measurable cost):**

| Library | Raw | ZA wrapper | Overhead |
|---|---:|---:|---:|
| MemoryPack | 75 ns / 48 B | 160 ns / 312 B | +114%, +264 B |
| MessagePack | 128 ns / 32 B | 215 ns / 312 B | +68%, +280 B |
| System.Text.Json | 226 ns / 48 B | 288 ns / 448 B | +27%, +400 B |

The serialize wrapper costs more because `ISerializer<T>.Serialize` takes an `IBufferWriter<byte>` — the buffer abstraction. The 264–400 B is the `ArrayBufferWriter<byte>` allocated fresh per call by the benchmark. **The wrapper is fastest when the caller pools the buffer writer**; a real application that pools writers amortises the overhead to ~0 per call.

Full methodology and guidance on when to use the wrapper vs raw libraries: [docs/performance.md](https://github.com/ZeroAlloc-Net/ZeroAlloc.Serialisation/blob/main/docs/performance.md).

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
