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

`MemoryPackSerializer<T>` accepted any `T`, but `MemoryPackableSerializer<T>` requires a `[MemoryPackable]` type. For a `T` that is not `[MemoryPackable]`, such as a collection like `List<Order>`, a primitive, an enum or an unmanaged struct, you have three options:

- Use the source generator. It does not require `[MemoryPackable]`: for a type without it the generator reports the `ZASZ003` warning and still emits a serializer that calls MemoryPack directly, which works as long as MemoryPack has a formatter for the type. Put `[ZeroAllocSerializable]` on an enum or struct you own, or declare a closed generic type on the assembly, for example `[assembly: ZeroAllocSerializable(typeof(List<Order>), SerializationFormat.MemoryPack)]`.
- Wrap the value in a `[MemoryPackable]` type.
- Implement `ISerializer<T>` yourself over `global::MemoryPack.MemoryPackSerializer`.

Where Native AOT matters, register the formatters first so MemoryPack does not look them up by reflection, for example `MemoryPackFormatterProvider.Register(new ListFormatter<Order>())`. Register one for every nested type that needs it, not only the outer one: for `List<List<int>>`, register both `new ListFormatter<List<int>>()` and `new ListFormatter<int>()`.

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
