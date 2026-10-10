# AOT and Trimming

## Generated Code

Generated `{TypeName}Serializer` classes are fully AOT-safe:

- `T` is resolved at generation time — no open-generic reflection at runtime.
- **SystemTextJson**: the generator binds the call to a user-supplied `JsonSerializerContext` via `Context.Default.T`, resolved by scanning for `[JsonSerializable(typeof(T))]` on any `JsonSerializerContext`-derived class in the compilation. The emission is genuinely AOT-safe — no reflection fallback, no `[UnconditionalSuppressMessage]` needed. Missing context → `ZASZ004` error, emission skipped.
- **MemoryPack / MessagePack**: the backend's own source generator has already emitted the formatter, so the calls are safe to trim. The generator emits `[UnconditionalSuppressMessage("Trimming", "IL2026")]` and `[UnconditionalSuppressMessage("AOT", "IL3050")]` on the methods because the static backend APIs still carry `[RequiresUnreferencedCode]` / `[RequiresDynamicCode]` annotations.

No `[RequiresDynamicCode]` or `[RequiresUnreferencedCode]` attributes appear on generated code.

## Closed Generic Types

A closed generic type declared with `[assembly: ZeroAllocSerializable(typeof(Envelope<Order>), format)]` gets a serializer and dispatcher entry that name `Envelope<Order>` directly, so NativeAOT compiles them ahead of time like any other type. See [Closed Generic Types](source-generator.md#closed-generic-types).

Under System.Text.Json and MemoryPack this is AOT-safe end to end, and the AOT smoke test covers `Envelope<Order>`, `Pair<int, Order>` and `Envelope<Pair<int, Order>>` in both. Under MessagePack it is not: for a generic `[MessagePackObject]` type, MessagePack's own source generator emits a resolver that creates the formatter with `Type.MakeGenericType`. NativeAOT reports that as `IL3050`, and a construction over a value type, such as `Pair<int, Order>`, throws at run time. Use System.Text.Json or MemoryPack for closed generic types in a NativeAOT app. Tracked in [#184](https://github.com/ZeroAlloc-Net/ZeroAlloc.Serialisation/issues/184).

## Base Classes

The reflection-based constructors of the base classes (`MemoryPackSerializer<T>()`, `MessagePackSerializer<T>()` and `MessagePackSerializer<T>(MessagePackSerializerOptions)`) carry `[RequiresDynamicCode]` and `[RequiresUnreferencedCode]`, because MemoryPack and MessagePack then find formatters through reflection. Use them only where AOT is not required. For Native AOT use `MemoryPackableSerializer<T>` for a `[MemoryPackable]` type, or `new MessagePackSerializer<T>(resolver)` with a resolver built from a `[GeneratedMessagePackResolver]` class and `BuiltinResolver.Instance`.

`SystemTextJsonSerializer<T>` is AOT-safe even as a base class because it requires a `JsonTypeInfo<T>` injected at construction time.

## Native AOT Publishing

For Native AOT (`<PublishAot>true</PublishAot>`):

1. Use the source generator (annotate with `[ZeroAllocSerializable]`) — do not use base classes directly.
2. Ensure each backend's own generator is configured:
   - MemoryPack: add `ZeroAlloc.Serialisation.MemoryPack` (which pulls in `MemoryPack.Generator`)
   - MessagePack: add `ZeroAlloc.Serialisation.MessagePack` (which pulls in `MessagePack.Generator`)
   - SystemTextJson: declare a `partial class XxxContext : JsonSerializerContext` with `[JsonSerializable(typeof(T))]` for each serialisable type. The generator picks it up automatically and wires the generated serializer to `XxxContext.Default.T`. If omitted, `ZASZ004` halts the build with a clear message.
3. Verify with `dotnet publish -r linux-x64 -c Release` — the IL linker will report any remaining unsafe calls.
