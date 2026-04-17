# Dependency Injection

## Per-Type Extension Methods

For each type annotated with `[ZeroAllocSerializable]`, the generator emits an `Add{TypeName}Serializer` extension on `IServiceCollection`:

```csharp
services.AddOrderCreatedSerializer();
// equivalent to (uses TryAddSingleton — does not overwrite existing registrations):
services.TryAddSingleton<ISerializer<OrderCreated>, OrderCreatedSerializer>();
```

The generated class is `internal`, so it is only accessible through this DI extension.

## Runtime Dispatch — `ISerializerDispatcher`

The generator also emits one `SerializerDispatcher` class per assembly that covers **all** `[ZeroAllocSerializable]` types in that assembly. Register it with the generated `AddSerializerDispatcher()` extension:

```csharp
services.AddOrderCreatedSerializer();
services.AddOrderShippedSerializer();
services.AddSerializerDispatcher();   // registers ISerializerDispatcher → SerializerDispatcher
```

`AddSerializerDispatcher()` uses `TryAddSingleton`, so registering your own `ISerializerDispatcher` before calling it takes precedence.

Inject `ISerializerDispatcher` where you need to serialize/deserialize by `Type` at runtime — for example, inside an event sourcing serializer or a generic persistence layer:

```csharp
public class MyEventSerializer(ISerializerDispatcher dispatcher)
{
    public ReadOnlyMemory<byte> Serialize(object @event, Type type)
        => dispatcher.Serialize(@event, type);

    public object? Deserialize(ReadOnlyMemory<byte> data, Type type)
        => dispatcher.Deserialize(data, type);
}
```

`ISerializerDispatcher.Serialize` allocates an intermediate `ArrayBufferWriter<byte>` buffer by design — this layer is intentionally allocation-tolerant to return a self-contained `ReadOnlyMemory<byte>`.

## Manual Registration

If you are using a backend class directly (for non-AOT or ad-hoc use) you can register it manually:

```csharp
// MemoryPack — open-generic base class, carries [RequiresDynamicCode]
services.AddSingleton<ISerializer<MyType>, MemoryPackSerializer<MyType>>();

// SystemTextJson — requires JsonTypeInfo<T>
services.AddSingleton<ISerializer<MyType>>(
    new SystemTextJsonSerializer<MyType>(MyTypeJsonContext.Default.MyType));
```

## Resolving at Runtime

Inject `ISerializer<T>` directly by the closed type:

```csharp
public class MyService(ISerializer<OrderCreated> serializer) { }
```

Or resolve generically in infrastructure code:

```csharp
var serializer = serviceProvider.GetRequiredService<ISerializer<OrderCreated>>();
```
