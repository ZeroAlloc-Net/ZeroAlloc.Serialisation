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

## Deserializing from a Span

MessagePack 3.x reads from `ReadOnlyMemory<byte>` or `ReadOnlySequence<byte>`, never from a `ReadOnlySpan<byte>`: its reader is built on `SequenceReader<byte>`. `ISerializer<T>.Deserialize` takes a span, so it copies the span into an array rented from `ArrayPool<byte>.Shared`, deserializes from that, and returns the array. The copy allocates nothing; the only allocations left are the ones MessagePack itself makes for the result.

The span is not pinned and wrapped in a `MemoryManager<byte>` to skip the copy. A formatter that keeps a slice of its input after `Deserialize` returns would then hold a pointer into memory the garbage collector is free to move. None of MessagePack's built-in formatters keep their input; they copy what they return.

The generated `SerializerDispatcher` already has the payload as `ReadOnlyMemory<byte>`, so for a MessagePack type it hands that memory to MessagePack directly, with no copy at all.

Serialization is zero-copy: `MessagePackSerializer.Serialize(IBufferWriter<byte>, T)` writes directly to the buffer.

## Options

Pass custom `MessagePackSerializerOptions` for resolver customization:

```csharp
services.AddSingleton<ISerializer<MyEvent>>(
    new MessagePackSerializer<MyEvent>(MessagePackSerializerOptions.Standard
        .WithResolver(CompositeResolver.Create(NativeGuidResolver.Instance, StandardResolver.Instance))));
```
