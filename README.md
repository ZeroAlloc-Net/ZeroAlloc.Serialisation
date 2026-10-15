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

A closed generic type such as `Envelope<Order>` is declared once per closed construction, on the assembly, and gets the same serializer, DI registration and dispatcher entry:

```csharp
[assembly: ZeroAllocSerializable(typeof(Envelope<Order>), SerializationFormat.MemoryPack)]

services.AddEnvelopeOfOrderSerializer();   // ISerializer<Envelope<Order>>
```

See [Closed Generic Types](docs/source-generator.md#closed-generic-types).

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

`Serialize(object, Type)` allocates the buffer it returns. To avoid that, write into an `IBufferWriter<byte>` you own; the generic overload also avoids boxing a struct, and both allocate 0 B into a reused writer:

```csharp
dispatcher.Serialize(writer, orderCreated);                 // generic, dispatches on typeof(T)
dispatcher.Serialize(writer, @event, @event.GetType());     // by runtime type
```

See [Serializing into your own `IBufferWriter<byte>`](docs/dependency-injection.md#serializing-into-your-own-ibufferwriterbyte).

`Deserialize(ReadOnlyMemory<byte>, Type)` returns `object`, so a struct read back through it is boxed. When the type is known at compile time, the generic overload returns it as `T` without the box:

```csharp
OrderCreated created = dispatcher.Deserialize<OrderCreated>(data);
```

See [Deserializing to a known type](docs/dependency-injection.md#deserializing-to-a-known-type).

## Packages

| Package | Description | TFM |
|---|---|---|
| `ZeroAlloc.Serialisation` | `ISerializer<T>`, `ISerializerDispatcher`, `ZeroAllocSerializableAttribute` | netstandard2.1, net8–10 |
| `ZeroAlloc.Serialisation.Generator` | Roslyn source generator — **legacy**, already bundled inside `ZeroAlloc.Serialisation`; referencing both loads it twice | netstandard2.0 |
| `ZeroAlloc.Serialisation.MemoryPack` | MemoryPack backend | net8–10 |
| `ZeroAlloc.Serialisation.MessagePack` | MessagePack backend | net8–10 |
| `ZeroAlloc.Serialisation.SystemTextJson` | System.Text.Json backend | net8–10 |

## Performance

ZA.Serialisation is an abstraction layer — not a competing serializer. The honest comparison is whether the wrapper adds measurable overhead vs calling the raw library directly. .NET 10, BenchmarkDotNet v0.15.8, ShortRun; both sides of each pair write into the same reused `IBufferWriter<byte>`.

**Deserialize:**

| Library | Raw | ZA wrapper | Extra allocation |
|---|---:|---:|---:|
| MemoryPack | 53.5 ns / 64 B | 87.0 ns / 64 B | **0 B** |
| MessagePack | 156.5 ns / 64 B | 204.4 ns / 64 B | **0 B** |
| System.Text.Json | 395.4 ns / 64 B | 270.6 ns / 64 B | **0 B** |

**Serialize:**

| Library | Raw | ZA wrapper | Extra allocation |
|---|---:|---:|---:|
| MemoryPack | 53.6 ns / 0 B | 51.2 ns / 0 B | **0 B** |
| MessagePack | 56.1 ns / 0 B | 74.0 ns / 0 B | **0 B** |
| System.Text.Json | 93.1 ns / 0 B | 134.0 ns / 0 B | **0 B** |

The 64 B on deserialize is the deserialized object itself; the wrapper adds no allocation on either path. The timings come from a short run on a shared machine and vary by tens of nanoseconds between runs, so read them as an order of magnitude; the allocation column is exact. If you would otherwise create a fresh `ArrayBufferWriter<byte>` per call, a raw API that returns a `byte[]` allocates less — pool or reuse the writer.

Full methodology, the generated-serializer and dispatcher allocation profile, and guidance on when to use the wrapper vs raw libraries: [docs/performance.md](https://github.com/ZeroAlloc-Net/ZeroAlloc.Serialisation/blob/main/docs/performance.md).

## AOT Safety

Generated serializers suppress `[RequiresDynamicCode]` and `[RequiresUnreferencedCode]` because `T` is resolved at generation time. The backend's own source generator (MemoryPack / MessagePack) has already emitted the formatter, so no dynamic code is required at runtime.

Base classes (`MemoryPackSerializer<T>`, etc.) have reflection-based constructors that carry the attributes, for ad-hoc / non-AOT use. For Native AOT use `MemoryPackableSerializer<T>` and `MessagePackSerializer<T>(IFormatterResolver)`.

## REST Integration

For JSON clients, prefer Rest's own `new SystemTextJsonSerializer(MyApiJsonContext.Default)`: it covers every request and response type in one serializer and is AOT-safe.

For a non-JSON backend, bridge `ISerializer<T>` into `IRestSerializer` with a short hand-written adapter (Rest 3.0 removed the built-in `RestSerializerAdapter<T>`; Rest 2.x users can keep using it). See [docs/rest-integration.md](docs/rest-integration.md) for the full adapter and setup.

## License

MIT
