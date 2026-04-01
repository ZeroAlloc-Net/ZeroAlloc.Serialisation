# MemoryPack Backend

## Package

```bash
dotnet add package ZeroAlloc.Serialisation.MemoryPack
```

## Base Class

`MemoryPackSerializer<T>` implements `ISerializer<T>` for any `T : IMemoryPackable<T>`:

```csharp
public class MemoryPackSerializer<T> : ISerializer<T>
{
    [RequiresDynamicCode("...")]
    [RequiresUnreferencedCode("...")]
    public virtual void Serialize(IBufferWriter<byte> writer, T value)
        => global::MemoryPack.MemoryPackSerializer.Serialize(writer, value);

    [RequiresDynamicCode("...")]
    [RequiresUnreferencedCode("...")]
    public virtual T? Deserialize(ReadOnlySpan<byte> buffer)
    {
        if (buffer.IsEmpty) return default;
        return global::MemoryPack.MemoryPackSerializer.Deserialize<T>(buffer);
    }
}
```

The base class is open-generic and carries `[RequiresDynamicCode]`. For AOT use, use the source generator instead.

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
