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
