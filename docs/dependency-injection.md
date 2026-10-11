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

`Serialize(object, Type)` returns a self-contained `ReadOnlyMemory<byte>`, so it allocates that buffer, an `ArrayBufferWriter<byte>` and its array, on every call, and boxes a struct value.

### Serializing into your own `IBufferWriter<byte>`

When you already have somewhere to write, such as a pooled writer, a reused `ArrayBufferWriter<byte>` or a pipe, use one of the overloads that write into it:

```csharp
// Generic: dispatches on typeof(T), so a struct is not boxed.
dispatcher.Serialize(writer, orderCreated);

// By runtime type, when only a Type is known.
dispatcher.Serialize(writer, @event, @event.GetType());
```

The generated `SerializerDispatcher` writes directly into `writer`, with no intermediate buffer. Into a reused writer, `Serialize<T>` allocates nothing for a registered type, struct or class, in any of the three formats, and the by-type overload allocates nothing beyond the box a struct needs to be passed as `object`; the tests assert 0 B. The bytes are appended, so anything the writer already holds is kept.

`Serialize<T>` compares `typeof(T)` with each registered type, a check the JIT and NativeAOT resolve per instantiation. When `T` is not itself a registered type, for example `object` or an interface, it falls back to the runtime-type overload, which boxes a struct.

A consumer that persists a `byte[]` can serialize into a reused writer and copy once to an exact-size array, instead of the dispatcher's buffer plus a `ToArray()` copy:

```csharp
// t_writer: a [ThreadStatic] ArrayBufferWriter<byte> field, reused across calls on one thread.
var writer = t_writer ??= new ArrayBufferWriter<byte>();
writer.ResetWrittenCount();
dispatcher.Serialize(writer, job);
byte[] payload = writer.WrittenSpan.ToArray();
```

Both overloads are default interface members, so an `ISerializerDispatcher` you wrote yourself still compiles and works: its inherited implementation calls your `Serialize(object, Type)` and copies the bytes into the writer. Override them to write directly. `SystemTextJsonFallbackDispatcher` forwards both to the dispatcher it wraps and serializes an unregistered type with `System.Text.Json`, as its memory-returning `Serialize` does.

### Deserializing to a known type

`Deserialize(ReadOnlyMemory<byte>, Type)` returns `object`, so a struct comes back boxed and the caller unboxes it. When the type is known at compile time, use the generic overload:

```csharp
OrderCreated created = dispatcher.Deserialize<OrderCreated>(data);
```

The generated `SerializerDispatcher` compares `typeof(T)` with each registered type and returns that type's serializer result as `T`, so a struct is not boxed. It allocates nothing beyond what the format's own deserializer allocates for the value; the tests assert that it allocates exactly as much as the per-type serializer, and exactly one 32 B box less than the `object` overload for a 16-byte struct, in all three formats.

What the format itself allocates depends on the type. For a struct with no reference members, MemoryPack and MessagePack allocate nothing, so the call is 0 B. System.Text.Json builds a struct as a boxed object inside `JsonSerializer.Deserialize` and unboxes it at the end, 32 B for that struct, and a positional record struct, which it binds through its constructor, costs it 168 B. Strings and other reference members are allocated by every format.

The parameter is `ReadOnlyMemory<byte>`, like the `object` overload's, because every format reads it without a copy: MessagePack reads memory or a sequence, never a span, so the dispatcher passes it the memory directly, where the span-based `ISerializer<T>.Deserialize` copies a MessagePack payload into a pooled array first. There is no span overload for that reason.

Errors match the `object` overload. A `T` that is not itself a registered type throws `NotSupportedException`, with the same message: reading has no runtime type to dispatch on, so `object`, an interface or `Nullable<T>` of a registered struct is not supported. Empty data returns `default`: `null` for a class, the zero value for a struct.

`Deserialize<T>` is a default interface member too. A dispatcher you wrote yourself inherits an implementation that calls your `Deserialize(data, typeof(T))` and unboxes the result: `default` when it returns `null`, and an `InvalidCastException` when it returns an object that is not a `T`. `SystemTextJsonFallbackDispatcher` forwards `Deserialize<T>` to the dispatcher it wraps, so a registered struct is not boxed there either, and reads an unregistered type with `System.Text.Json` as `T`, with the same options as its `object` overload.

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
