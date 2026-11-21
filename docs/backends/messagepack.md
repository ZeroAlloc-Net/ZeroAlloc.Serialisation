# MessagePack Backend

## Package

```bash
dotnet add package ZeroAlloc.Serialisation.MessagePack
```

## Adapter

`MessagePackSerializer<T>` implements `ISerializer<T>`. It takes the resolver that supplies every formatter, and optionally options whose resolver it replaces:

```csharp
public class MessagePackSerializer<T> : ISerializer<T>
{
    public MessagePackSerializer(IFormatterResolver resolver, MessagePackSerializerOptions? options = null);

    public virtual void Serialize(IBufferWriter<byte> writer, T value);
    public virtual T? Deserialize(ReadOnlySpan<byte> buffer);
}
```

It adds no reflection-based resolver of its own: a type the resolver has no formatter for throws `MessagePackSerializationException`. For Native AOT, build the resolver from a `[GeneratedMessagePackResolver]` class and `BuiltinResolver.Instance`:

```csharp
[GeneratedMessagePackResolver]
internal partial class MyResolver;

var serializer = new MessagePackSerializer<MyEvent>(
    CompositeResolver.Create(MyResolver.Instance, BuiltinResolver.Instance));
```

Before 3.0 the class also had a parameterless constructor and one that took only `MessagePackSerializerOptions`, both of which fell back to `MessagePackSerializerOptions.Standard` and its reflection-based resolver. They are removed. Code that relied on them passes the resolver explicitly, for example `new MessagePackSerializer<MyEvent>(StandardResolver.Instance)` where Native AOT is not a concern.

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

Pass `MessagePackSerializerOptions` as the second argument for settings such as compression. The adapter uses a copy of them with the resolver replaced by the first argument:

```csharp
services.AddSingleton<ISerializer<MyEvent>>(
    new MessagePackSerializer<MyEvent>(
        CompositeResolver.Create(NativeGuidResolver.Instance, MyResolver.Instance, BuiltinResolver.Instance),
        MessagePackSerializerOptions.Standard.WithCompression(MessagePackCompression.Lz4BlockArray)));
```
