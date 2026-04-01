# Dependency Injection

## Generated Extension Methods

For each type annotated with `[ZeroAllocSerializable]`, the generator emits an `Add{TypeName}Serializer` extension on `IServiceCollection`:

```csharp
services.AddOrderCreatedSerializer();
// equivalent to:
services.AddSingleton<ISerializer<OrderCreated>, OrderCreatedSerializer>();
```

The generated class is `internal`, so it's only accessible through the DI extension.

## Manual Registration

If you're using a base class directly (for non-AOT or ad-hoc use) you can register it manually:

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
