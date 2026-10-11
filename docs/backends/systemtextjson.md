# System.Text.Json Backend

## Package

```bash
dotnet add package ZeroAlloc.Serialisation.SystemTextJson
```

## Base Class

`SystemTextJsonSerializer<T>` implements `ISerializer<T>` and requires a `JsonTypeInfo<T>`:

```csharp
public sealed class SystemTextJsonSerializer<T> : ISerializer<T>
{
    private readonly JsonTypeInfo<T> _typeInfo;

    public SystemTextJsonSerializer(JsonTypeInfo<T> typeInfo) => _typeInfo = typeInfo;

    public void Serialize(IBufferWriter<byte> writer, T value)
    {
        var jsonWriter = Utf8JsonWriterCache.Rent(writer);
        try
        {
            JsonSerializer.Serialize(jsonWriter, value, _typeInfo);
            jsonWriter.Flush();
        }
        finally
        {
            Utf8JsonWriterCache.Return(jsonWriter);
        }
    }

    public T? Deserialize(ReadOnlySpan<byte> buffer)
    {
        if (buffer.IsEmpty) return default;
        return JsonSerializer.Deserialize(buffer, _typeInfo);
    }
}
```

The `Utf8JsonWriter` writes directly to the `IBufferWriter<byte>` — no intermediate `byte[]` — and is flushed before the method returns.

`Serialize` allocates nothing of its own. Each thread keeps one `Utf8JsonWriter` and points it at the caller's buffer writer with `Reset`, instead of creating a writer per call. The cache is safe to use from converters:

- A converter that serializes through a `SystemTextJsonSerializer<T>` again, on the same thread, while the outer call is still writing gets a writer of its own, so the outer one is not disturbed.
- After each call, finished or failed, the writer is reset. An exception thrown mid-write leaves nothing behind for the next call, and the writer keeps no reference to your buffer writer.

If `Serialize` throws, the bytes written so far are not flushed to your buffer writer.

The serializers the source generator emits for System.Text.Json do the same, with one cached writer per thread for each generated serializer.

## AOT Usage

`SystemTextJsonSerializer<T>` is AOT-safe even as a base class because the `JsonTypeInfo<T>` is injected — no reflection needed at runtime:

```csharp
// Define a JsonSerializerContext
[JsonSerializable(typeof(OrderCreated))]
public partial class AppJsonContext : JsonSerializerContext { }

// Register
services.AddSingleton<ISerializer<OrderCreated>>(
    new SystemTextJsonSerializer<OrderCreated>(AppJsonContext.Default.OrderCreated));
```

## Source Generator Usage

```csharp
[ZeroAllocSerializable(SerializationFormat.SystemTextJson)]
public class OrderCreated
{
    public Guid OrderId { get; set; }
    public string Product { get; set; } = "";
}
```

The generated serializer uses the default `JsonSerializerOptions`. For custom options or a `JsonSerializerContext`, use the base class directly.
