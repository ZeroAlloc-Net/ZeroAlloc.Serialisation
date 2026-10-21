# ZeroAlloc.Serialisation

[![NuGet](https://img.shields.io/nuget/v/ZeroAlloc.Serialisation.svg)](https://www.nuget.org/packages/ZeroAlloc.Serialisation)
[![Build](https://github.com/ZeroAlloc-Net/ZeroAlloc.Serialisation/actions/workflows/ci.yml/badge.svg)](https://github.com/ZeroAlloc-Net/ZeroAlloc.Serialisation/actions/workflows/ci.yml)
[![codecov](https://codecov.io/gh/ZeroAlloc-Net/ZeroAlloc.Serialisation/graph/badge.svg)](https://codecov.io/gh/ZeroAlloc-Net/ZeroAlloc.Serialisation)
[![License: MIT](https://img.shields.io/badge/License-MIT-yellow.svg)](LICENSE)
[![AOT](https://img.shields.io/badge/AOT--Compatible-passing-brightgreen)](https://learn.microsoft.com/dotnet/core/deploying/native-aot/)
[![GitHub Sponsors](https://img.shields.io/github/sponsors/MarcelRoozekrans?style=flat&logo=githubsponsors&color=ea4aaa&label=Sponsor)](https://github.com/sponsors/MarcelRoozekrans)

**One `ISerializer<T>` for MemoryPack, MessagePack and System.Text.Json, source-generated per type: swap formats without touching call sites, with no allocation added over the raw library and no reflection.**

## Why ZeroAlloc.Serialisation

- **Adds no allocation over the raw library.** Serializing into a reused `IBufferWriter<byte>` allocates 0 B in all three formats, asserted by tests. Deserializing allocates exactly what the raw library does.
- **A thin layer, measured.** In the same benchmark run the wrapper serializes in 0.96× to 1.44× the time of calling the library directly.
- **Works under NativeAOT and trimming.** Each serializer is generated for a closed type and calls the backend's own source-generated formatter. A NativeAOT smoke app is published and run in CI.
- **Mistakes fail the build.** A System.Text.Json type missing from your `JsonSerializerContext` is compile error ZASZ004, not a runtime exception on the first message.

## Install

```bash
dotnet add package ZeroAlloc.Serialisation
dotnet add package ZeroAlloc.Serialisation.MemoryPack   # or .MessagePack, or .SystemTextJson
```

The core package carries the source generator. Add the backend for each format you use.

## Quick start

```csharp
using System.Buffers;
using MemoryPack;
using ZeroAlloc.Serialisation;

ISerializer<OrderCreated> serializer = new OrderCreatedSerializer();   // generated

var buffer = new ArrayBufferWriter<byte>();
serializer.Serialize(buffer, new OrderCreated { OrderId = Guid.NewGuid(), Product = "Coffee" });
OrderCreated? copy = serializer.Deserialize(buffer.WrittenSpan);

Console.WriteLine($"{buffer.WrittenCount} bytes, {copy?.Product}");

[MemoryPackable]
[ZeroAllocSerializable(SerializationFormat.MemoryPack)]
public partial class OrderCreated
{
    public Guid OrderId { get; set; }
    public string Product { get; set; } = "";
}
```

In an app with DI, `services.AddOrderCreatedSerializer()` registers it and you inject `ISerializer<OrderCreated>`. Switching to MessagePack or JSON changes the attribute, not the code that uses it.

## What you get

- **A typed `ISerializer<T>`** that writes to any `IBufferWriter<byte>`, such as a pipe or a pooled buffer, and reads from a `ReadOnlySpan<byte>`.
- **Three backends**: [MemoryPack](docs/backends/memorypack.md), [MessagePack](docs/backends/messagepack.md) and [System.Text.Json](docs/backends/systemtextjson.md).
- **DI registration** generated per type, with an option to make it internal for libraries.
- **Runtime dispatch by `Type`** through `ISerializerDispatcher`, for event stores and message buses. It is a generated switch, with no reflection and no dictionary lookup.
- **Closed generic types** such as `Envelope<Order>`, declared once on the assembly.
- **Value objects serialized as their underlying value**: a [ZeroAlloc.ValueObjects](https://github.com/ZeroAlloc-Net/ZeroAlloc.ValueObjects) `CustomerId` writes `42`, not `{"value":42}`.
- **[ZeroAlloc.Rest](https://github.com/ZeroAlloc-Net/ZeroAlloc.Rest) integration** for non-JSON HTTP payloads, see [REST integration](docs/rest-integration.md).

## Performance

Serialize into a reused `IBufferWriter<byte>`, .NET 10, BenchmarkDotNet, raw library and wrapper in the same run.

| Backend | Raw library | ZeroAlloc wrapper | Wrapper time vs raw |
|---|---:|---:|---:|
| MemoryPack | 0 B | 0 B | 1.21× |
| MessagePack | 0 B | 0 B | 1.25× |
| System.Text.Json | 0 B | 0 B | 1.23× |

Allocations are exact; the time is a same-run ratio from three launches. The [performance page](docs/performance.md) has the deserialize numbers, the generated dispatcher's allocation profile, and when to call the raw library instead.

## Learn more

- Documentation: [serialisation.zeroalloc.net](https://serialisation.zeroalloc.net), sources in [docs/](docs/index.md). Start with [Getting started](docs/getting-started.md), or [AOT and trimming](docs/aot.md) and the [diagnostics reference](docs/source-generator.md#diagnostics).
- Upgrading from 1.x: [Migrating 1.x to 2.x](docs/migration-1x-to-2x.md). Release history: [CHANGELOG.md](CHANGELOG.md).
- Related packages: [ZeroAlloc.EventSourcing](https://github.com/ZeroAlloc-Net/ZeroAlloc.EventSourcing), [ZeroAlloc.Outbox](https://github.com/ZeroAlloc-Net/ZeroAlloc.Outbox), [ZeroAlloc.Rest](https://github.com/ZeroAlloc-Net/ZeroAlloc.Rest), [ZeroAlloc.ValueObjects](https://github.com/ZeroAlloc-Net/ZeroAlloc.ValueObjects).
- Issues and pull requests are welcome on [GitHub](https://github.com/ZeroAlloc-Net/ZeroAlloc.Serialisation/issues).
- MIT licensed. See [LICENSE](LICENSE).
