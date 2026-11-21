# MemoryPack Backend

## Package

```bash
dotnet add package ZeroAlloc.Serialisation.MemoryPack
```

## Adapter

`MemoryPackableSerializer<T>` implements `ISerializer<T>` for any `T : IMemoryPackable<T>`, that is, a `[MemoryPackable]` type:

```csharp
ISerializer<MyEvent> serializer = new MemoryPackableSerializer<MyEvent>();
```

The constructor registers the type's formatter through its generated static `RegisterFormatter`, so MemoryPack never looks it up by reflection. The class carries no trim or AOT annotations and is safe under Native AOT.

Before 3.0 this package also had `MemoryPackSerializer<T>`, whose parameterless constructor let MemoryPack find the formatter through reflection. It is removed; use `MemoryPackableSerializer<T>`, or the source generator below.

`MemoryPackSerializer<T>` accepted any `T`, but `MemoryPackableSerializer<T>` and the source generator both require a `[MemoryPackable]` type. For a `T` that is not `[MemoryPackable]`, such as a collection like `List<Order>`, a primitive, an enum or an unmanaged struct, either wrap the value in a `[MemoryPackable]` type, or implement `ISerializer<T>` yourself over `global::MemoryPack.MemoryPackSerializer`. Where Native AOT matters, register the formatter first, for example `MemoryPackFormatterProvider.Register(new ListFormatter<Order>())`, so MemoryPack does not look it up by reflection.

## Source Generator Usage

```csharp
[MemoryPackable]
[ZeroAllocSerializable(SerializationFormat.MemoryPack)]
public partial class MyEvent
{
    public string Name { get; set; } = "";
}
```

The `[MemoryPackable]` attribute (from `MemoryPack`) is required alongside `[ZeroAllocSerializable]`. MemoryPack's own generator emits the formatter; ZeroAlloc's generator emits the `ISerializer<T>` wrapper.

## Performance Notes

MemoryPack writes directly to `IBufferWriter<byte>` — no intermediate `byte[]` is allocated. Deserialization reads from `ReadOnlySpan<byte>` directly. This makes it the most allocation-efficient backend in this library.
