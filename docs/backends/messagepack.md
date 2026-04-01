# MessagePack Backend

## Package

```bash
dotnet add package ZeroAlloc.Serialisation.MessagePack
```

## Base Class

`MessagePackSerializer<T>` implements `ISerializer<T>`:

```csharp
public class MessagePackSerializer<T> : ISerializer<T>
{
    private readonly MessagePackSerializerOptions _options;

    public MessagePackSerializer() : this(MessagePackSerializerOptions.Standard) { }
    public MessagePackSerializer(MessagePackSerializerOptions options) => _options = options;

    public virtual void Serialize(IBufferWriter<byte> writer, T value)
        => global::MessagePack.MessagePackSerializer.Serialize(writer, value, _options);

    public virtual T? Deserialize(ReadOnlySpan<byte> buffer)
    {
        if (buffer.IsEmpty) return default;
        // MessagePack 3.x has no Deserialize(ReadOnlySpan<byte>) overload.
        // ReadOnlySequence<byte> wrapping requires an array copy — unavoidable.
        var sequence = new ReadOnlySequence<byte>(buffer.ToArray());
        return global::MessagePack.MessagePackSerializer.Deserialize<T>(sequence, _options);
    }
}
```

## Source Generator Usage

```csharp
[MessagePackObject]
[ZeroAllocSerializable(SerializationFormat.MessagePack)]
public class MyEvent
{
    [Key(0)] public string Name { get; set; } = "";
    [Key(1)] public int Value { get; set; }
}
```

## Known Limitation: Deserialization Copy

MessagePack 3.x does not expose a `Deserialize(ReadOnlySpan<byte>)` overload — it requires `ReadOnlySequence<byte>`. The `buffer.ToArray()` call inside `Deserialize` is an unavoidable allocation on the deserialization path.

Serialization remains zero-copy: `MessagePackSerializer.Serialize(IBufferWriter<byte>, T)` writes directly to the buffer.

## Options

Pass custom `MessagePackSerializerOptions` for resolver customization:

```csharp
services.AddSingleton<ISerializer<MyEvent>>(
    new MessagePackSerializer<MyEvent>(MessagePackSerializerOptions.Standard
        .WithResolver(CompositeResolver.Create(NativeGuidResolver.Instance, StandardResolver.Instance))));
```
