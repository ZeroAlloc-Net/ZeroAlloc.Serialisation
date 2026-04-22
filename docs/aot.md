# AOT and Trimming

## Generated Code

Generated `{TypeName}Serializer` classes are fully AOT-safe:

- `T` is resolved at generation time — no open-generic reflection at runtime.
- **SystemTextJson**: the generator binds the call to a user-supplied `JsonSerializerContext` via `Context.Default.T`, resolved by scanning for `[JsonSerializable(typeof(T))]` on any `JsonSerializerContext`-derived class in the compilation. The emission is genuinely AOT-safe — no reflection fallback, no `[UnconditionalSuppressMessage]` needed. Missing context → `ZASZ004` error, emission skipped.
- **MemoryPack / MessagePack**: the backend's own source generator has already emitted the formatter, so the calls are safe to trim. The generator emits `[UnconditionalSuppressMessage("Trimming", "IL2026")]` and `[UnconditionalSuppressMessage("AOT", "IL3050")]` on the methods because the static backend APIs still carry `[RequiresUnreferencedCode]` / `[RequiresDynamicCode]` annotations.

No `[RequiresDynamicCode]` or `[RequiresUnreferencedCode]` attributes appear on generated code.

## Base Classes

The base classes (`MemoryPackSerializer<T>`, `MessagePackSerializer<T>`) carry `[RequiresDynamicCode]` and `[RequiresUnreferencedCode]` because they invoke open-generic serialization APIs. Use them only in contexts where AOT is not required.

`SystemTextJsonSerializer<T>` is AOT-safe even as a base class because it requires a `JsonTypeInfo<T>` injected at construction time.

## Native AOT Publishing

For Native AOT (`<PublishAot>true</PublishAot>`):

1. Use the source generator (annotate with `[ZeroAllocSerializable]`) — do not use base classes directly.
2. Ensure each backend's own generator is configured:
   - MemoryPack: add `ZeroAlloc.Serialisation.MemoryPack` (which pulls in `MemoryPack.Generator`)
   - MessagePack: add `ZeroAlloc.Serialisation.MessagePack` (which pulls in `MessagePack.Generator`)
   - SystemTextJson: declare a `partial class XxxContext : JsonSerializerContext` with `[JsonSerializable(typeof(T))]` for each serialisable type. The generator picks it up automatically and wires the generated serializer to `XxxContext.Default.T`. If omitted, `ZASZ004` halts the build with a clear message.
3. Verify with `dotnet publish -r linux-x64 -c Release` — the IL linker will report any remaining unsafe calls.
