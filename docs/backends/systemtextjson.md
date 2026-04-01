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
        using var jsonWriter = new Utf8JsonWriter(writer);
        JsonSerializer.Serialize(jsonWriter, value, _typeInfo);
    }

    public T? Deserialize(ReadOnlySpan<byte> buffer)
    {
        if (buffer.IsEmpty) return default;
        return JsonSerializer.Deserialize(buffer, _typeInfo);
    }
}
```

The `Utf8JsonWriter` writes directly to the `IBufferWriter<byte>` — no intermediate `byte[]`. The `using` ensures the writer is flushed before the method returns.

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
