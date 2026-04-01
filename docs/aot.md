# AOT and Trimming

## Generated Code

Generated `{TypeName}Serializer` classes are fully AOT-safe:

- `T` is resolved at generation time — no open-generic reflection at runtime
- `#pragma warning disable IL2026, IL3050` suppresses trim/AOT analyzer warnings on the static backend calls
- The backend's own source generator (MemoryPack / MessagePack) has already emitted the necessary formatter, so the Serialize/Deserialize calls are safe to trim

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
   - SystemTextJson: use `[JsonSerializable]` on a `JsonSerializerContext` and pass `JsonTypeInfo<T>` to the base class or generated code
3. Verify with `dotnet publish -r linux-x64 -c Release` — the IL linker will report any remaining unsafe calls
