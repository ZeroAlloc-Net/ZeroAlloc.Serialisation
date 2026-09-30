# Dependency Injection

## Per-Type Extension Methods

For each type annotated with `[ZeroAllocSerializable]`, the generator emits an `Add{TypeName}Serializer` extension on `IServiceCollection`:

```csharp
services.AddOrderCreatedSerializer();
// equivalent to (uses TryAddSingleton — does not overwrite existing registrations):
services.TryAddSingleton<ISerializer<OrderCreated>, OrderCreatedSerializer>();
```

The generated class is `internal`, so it is only accessible through this DI extension.

A closed generic type declared on the assembly gets its extension the same way, named after the type and its type arguments:

```csharp
[assembly: ZeroAllocSerializable(typeof(Envelope<Order>), SerializationFormat.MemoryPack)]

services.AddEnvelopeOfOrderSerializer();   // ISerializer<Envelope<Order>>
```

See [Closed Generic Types](source-generator.md#closed-generic-types) for the naming rule.

Two serializable types in one namespace with the same name, such as the nested types `A.Inner` and `B.Inner`, would get the same generated names. Those types, and only those, get names qualified with their namespace and containing types, dots replaced by underscores: `Demo.A.Inner` gets `Demo_A_InnerSerializer` and `AddDemo_A_InnerSerializer()`. Every other type keeps `Add{TypeName}Serializer`.

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

## Making the Generated Registration Internal

By default every entry point the generator emits is `public`: the `SerializerDispatcher` class, the `SerializerServiceCollectionExtensions` classes with `AddSerializerDispatcher()` and every `Add{TypeName}Serializer()`, and, for `[ValueObject]` types, `ValueObjectJsonConvertersExtensions` and `ValueObjectMessagePackFormattersExtensions`. That is the right default for an application, but usually wrong for a **library**:

- It adds public API that registers the library's own serializers, which `PublicApiAnalyzers`-style tooling flags and which consumers should never call directly.
- `SerializerDispatcher` is generated in the global namespace of every assembly that uses the generator. An application that references such a library and uses the generator itself sees two public `SerializerDispatcher` types and gets `CS0436`, which fails a build with `TreatWarningsAsErrors`. The same happens for `SerializerServiceCollectionExtensions` when both generate into one namespace.

Set the `ZeroAllocGeneratedAccessibility` MSBuild property to make the generator emit `internal` instead:

```xml
<PropertyGroup>
  <ZeroAllocGeneratedAccessibility>Internal</ZeroAllocGeneratedAccessibility>
</PropertyGroup>
```

```csharp
// Library: only AddOrdering() is public. The generated registrations are internal
// implementation details called from inside the library.
public static IServiceCollection AddOrdering(this IServiceCollection services)
    => services.AddOrderCreatedSerializer().AddSerializerDispatcher();
```

- Allowed values are `Public` (the default when the property is unset or empty) and `Internal`, compared case-insensitively. Any other value is rejected with a **ZASZ012** error naming the property, the offending value and the allowed values, and the generator falls back to `Public`. See [Diagnostics](source-generator.md#diagnostics).
- It applies to **every** generated entry point listed above. The generated `{TypeName}Serializer` classes are already `internal` regardless of this setting.
- If you add your own part of `partial class SerializerDispatcher` or `SerializerServiceCollectionExtensions`, leave the accessibility modifier off it, so it takes whatever the generated part declares.
- It is the **same property name** across every ZeroAlloc source-generator package — setting it once in a project's `.csproj` (or in a shared `Directory.Build.props`) covers all of them.
- With the property unset or `Public`, generated output is unchanged from previous versions.

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
