# Getting Started

## Installation

Install the core interface and generator into your project:

```bash
dotnet add package ZeroAlloc.Serialisation
dotnet add package ZeroAlloc.Serialisation.Generator
```

Then add a backend for your chosen serialization format:

```bash
dotnet add package ZeroAlloc.Serialisation.MemoryPack
# or
dotnet add package ZeroAlloc.Serialisation.MessagePack
# or
dotnet add package ZeroAlloc.Serialisation.SystemTextJson
```

Add the generator as an analyzer so it produces no runtime dependency:

```xml
<PackageReference Include="ZeroAlloc.Serialisation.Generator" Version="*"
                  OutputItemType="Analyzer" ReferenceOutputAssembly="false" />
```

## Quick Start

### 1. Annotate your type

Apply `[ZeroAllocSerializable]` alongside the backend's own attribute:

```csharp
using MemoryPack;
using ZeroAlloc.Serialisation;

[MemoryPackable]
[ZeroAllocSerializable(SerializationFormat.MemoryPack)]
public partial class OrderCreated
{
    public Guid OrderId { get; set; }
    public string Product { get; set; } = "";
    public int Quantity { get; set; }
}
```

### 2. Register the generated serializer

The generator emits `OrderCreatedSerializer` and a DI extension method:

```csharp
// Program.cs / Startup.cs
services.AddOrderCreatedSerializer();
```

### 3. Inject and use

```csharp
public class OrderStore(ISerializer<OrderCreated> serializer)
{
    public void Write(IBufferWriter<byte> writer, OrderCreated evt)
        => serializer.Serialize(writer, evt);

    public OrderCreated? Read(ReadOnlySpan<byte> bytes)
        => serializer.Deserialize(bytes);
}
```

## The ISerializer&lt;T&gt; Interface

```csharp
public interface ISerializer<T>
{
    void Serialize(IBufferWriter<byte> writer, T value);
    T? Deserialize(ReadOnlySpan<byte> buffer);
}
```

`Serialize` writes directly to any `IBufferWriter<byte>` — pipe writers, array buffer writers, pooled buffers — with no intermediate `byte[]` allocation.

`Deserialize` reads from a `ReadOnlySpan<byte>`, which covers in-memory buffers, `Memory<byte>.Span`, and slices of pooled arrays.
