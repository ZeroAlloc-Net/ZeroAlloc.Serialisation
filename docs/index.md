---
id: index
title: ZeroAlloc.Serialisation
slug: /
description: Source-generated, zero-allocation serialization for .NET 8+.
sidebar_position: 1
---

# ZeroAlloc.Serialisation

Source-generated, zero-allocation serialization for .NET 8+. Annotate a type — the Roslyn generator emits a sealed `ISerializer<T>` implementation and a DI extension. No reflection, no boxing, fully Native AOT safe.

```bash
dotnet add package ZeroAlloc.Serialisation
dotnet add package ZeroAlloc.Serialisation.Generator
dotnet add package ZeroAlloc.Serialisation.SystemTextJson  # or MemoryPack / MessagePack
```

## Quick Start

```csharp
// 1. Annotate your type
[ZeroAllocSerializable(SerializationFormat.SystemTextJson)]
[JsonSerializable(typeof(OrderPlacedEvent))]
public record OrderPlacedEvent(string OrderId, decimal Total);

// 2. Register
services.AddOrderPlacedEventSerializer();
// or register all types in the assembly at once:
services.AddSerializerDispatcher();

// 3. Inject and use
public class OrderStore(ISerializer<OrderPlacedEvent> serializer)
{
    public void Write(IBufferWriter<byte> writer, OrderPlacedEvent evt)
        => serializer.Serialize(writer, evt);

    public OrderPlacedEvent? Read(ReadOnlySpan<byte> bytes)
        => serializer.Deserialize(bytes);
}
```

## What Gets Generated

Given `[ZeroAllocSerializable(SerializationFormat.MemoryPack)]` on a type, the generator emits:

- `OrderPlacedEventSerializer : ISerializer<OrderPlacedEvent>` — sealed, no reflection
- `AddOrderPlacedEventSerializer()` DI extension — `TryAddSingleton` registration
- `SerializerDispatcher` — single dispatcher covering all annotated types in the assembly

## Backends

| Package | Format |
|---|---|
| `ZeroAlloc.Serialisation.MemoryPack` | MemoryPack (fastest binary) |
| `ZeroAlloc.Serialisation.MessagePack` | MessagePack |
| `ZeroAlloc.Serialisation.SystemTextJson` | System.Text.Json (AOT-safe via `JsonSerializerContext`) |

## Documentation

| Section | Description |
|---|---|
| [Getting Started](getting-started) | Install, annotate, register, and use |
| [Source Generator](source-generator) | What the generator emits — per-type and dispatcher output |
| [Backends](backends/memorypack) | Backend-specific setup for MemoryPack, MessagePack, and STJ |
| [Dependency Injection](dependency-injection) | Registration patterns and dispatcher usage |
| [AOT & Trimming](aot) | Native AOT and IL trimming compatibility |
| [REST Integration](rest-integration) | Use with ZeroAlloc.Rest |
