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

### Using value-objects with `JsonSerializerContext`

When the consuming project uses STJ's source-generated `JsonSerializerContext` for AOT readiness, register the value-object converters explicitly during options setup:

```csharp
builder.Services.ConfigureHttpJsonOptions(o =>
{
    o.SerializerOptions.TypeInfoResolverChain.Insert(0, JsonContext.Default);
    o.SerializerOptions.AddZeroAllocValueObjectConverters();  // adds every [ValueObject] converter
});
```

`AddZeroAllocValueObjectConverters` is generated per assembly that declares `[ValueObject]` types — it lists each one and adds the converter to `options.Converters`. STJ consults that list before the context's typeinfo, so the underlying-primitive wire format takes precedence over default struct serialization. No `InternalsVisibleTo` required; no class-name coupling.

**Call order matters.** `AddZeroAllocValueObjectConverters()` inserts the value-object typeinfo resolver at chain index 0. Call it **after** your `Insert(0, JsonContext.Default)` so the resulting chain is `[VO-resolver, JsonContext.Default]` — value-objects resolved by us, DTOs by JsonContext. Reversing the order produces `[JsonContext.Default, VO-resolver]` and value-object typeinfo would be served by JsonContext's broken stub instead.

For reflection-based STJ (no `JsonSerializerContext`), nothing changes — the `[JsonConverter]` attribute the generator emits on the partial-struct extension is picked up automatically via reflection.

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
