# ZeroAlloc.Serialisation — Backlog

Candidate enhancements identified during real-world usage. Each item is independent and can be implemented in any order. Order is rough priority, not commitment. Items graduate from this backlog when the friction or value is concrete enough to justify the work.

---

## ~~V1 — Transparent serializers for single-property `[ValueObject]` types~~ — ✅ shipped 2.3.0 (2026-05-27)

**Shipped:** `ZeroAlloc.Serialisation.Generator` gains a parallel discovery pass that detects single-property `[ZeroAlloc.ValueObjects.ValueObjectAttribute]` partial structs (FQN match — no runtime reference to ZA.ValueObjects). Per-backend emission gated by `Compilation.ReferencedAssemblyNames`:

- References `ZeroAlloc.Serialisation.SystemTextJson` → emit `JsonConverter<T>` + `[JsonConverter(typeof(...))]`
- References `ZeroAlloc.Serialisation.MessagePack` → emit `IMessagePackFormatter<T>` + `[MessagePackFormatter(typeof(...))]`
- References `ZeroAlloc.Serialisation.MemoryPack` → emit `MemoryPackFormatter<T>` + `[ModuleInitializer]` that calls `MemoryPackFormatterProvider.Register<T>` (idempotent via `IsRegistered<T>()`). MemoryPack does NOT support per-type-attribute custom formatter registration — `MemoryPackCustomFormatterAttribute<,>` is abstract and only valid on properties/fields. Module-initializer is the canonical alternative.

Multi-property value-objects (`Money { Amount, Currency }`) fall through silently to the backend's default object-shape serialization. Existing `[ZeroAllocSerializable]`-marked types see byte-identical generator output.

**Design + plan:** [`docs/plans/2026-05-27-value-object-transparent-serializers-design.md`](plans/2026-05-27-value-object-transparent-serializers-design.md) + [`docs/plans/2026-05-27-value-object-transparent-serializers.md`](plans/2026-05-27-value-object-transparent-serializers.md).

**Decisions worth flagging** (durable record):

- **FQN-match on `[ValueObject]`.** Same pattern ZA.Validation 1.5.0 (B1) established. Symmetric move; both packages now consume ZA.ValueObjects via attribute name only.
- **Per-backend emission gated by assembly reference**, not by an explicit per-backend opt-in attribute on the user side. Adopters who reference multiple backends get parallel emission for the same `[ValueObject]`.
- **Single-property only.** Multi-property value-objects fall through to backend default (which is sensible). No diagnostic — the case isn't a bug.
- **Zero-config registration.** STJ + MessagePack use attribute-driven registration on the partial struct; MemoryPack uses module-initializer registration. All three are picked up without any user-side `Add*Converter(...)` call.

---

## V2 — Extend MessagePack underlying-type table to cover Guid/DateTime

**Surfaced during V1 code-review** (2026-05-27): `ValueObjectEmitter.MessagePackReadWriteForType` falls through to `reader.ReadString()!` for any underlying type outside `Int16/32/64`, `Single/Double`, `Boolean`, `String`. STJ's sibling already handles `Guid` and `DateTime` explicitly. The asymmetry means an adopter who tries `[ValueObject] struct OrderId { Guid Value }` with both STJ + MessagePack referenced will compile under STJ but hit a `CS1503` under MessagePack (`Guid` has no `string` ctor).

**Why deferred:** V1 spec was int-only; no template currently demands `Guid`-shaped IDs. Surfaces immediately the moment one does. Single-method-table extension; ~5 lines + a snapshot test.

---

## ~~V1.5 — JsonSerializerContext interop helper~~ — ✅ shipped 2.3.1 (2026-05-27)

**Shipped:** Generator emits a per-assembly public `ValueObjectJsonConvertersExtensions` class in the `ZeroAlloc.Serialisation.SystemTextJson` namespace, with an `AddZeroAllocValueObjectConverters` extension method on `JsonSerializerOptions`. Consumers using `JsonSerializerContext` source-gen call this once during STJ options configuration; STJ's `options.Converters` list takes precedence over the context's typeinfo, so the transparent-primitive wire format wins.

**Why it shipped:** 2.3.0's `[JsonConverter]` attribute approach works for reflection-based STJ but not for `JsonSerializerContext` source-gen — Roslyn incremental generators don't see each other's output in the same compilation pass. Surfaced during the [ZeroAlloc.Templates PR #127](https://github.com/ZeroAlloc-Net/ZeroAlloc.Templates/pull/127) migration; PR closed, this work unblocks the re-author.

**Design + plan:** [`docs/plans/2026-05-27-jsoncontext-interop-design.md`](plans/2026-05-27-jsoncontext-interop-design.md) + [`docs/plans/2026-05-27-jsoncontext-interop-helper.md`](plans/2026-05-27-jsoncontext-interop-helper.md).

---

## ~~V1.6 — JsonTypeInfoResolver emission for AOT/JsonContext interop~~ — ✅ shipped 2.3.2 (2026-05-27)

**Shipped:** Generator emits a per-assembly internal `ValueObjectJsonTypeInfoResolver` that returns pre-configured `JsonTypeInfo<T>` for every `[ValueObject]` type via `JsonMetadataServices.CreateValueInfo<T>`. The existing `AddZeroAllocValueObjectConverters` registrar inserts this resolver at `TypeInfoResolverChain` index 0 alongside its existing `Converters.Add` calls.

**Why it shipped:** 2.3.1 closed the runtime serialize/deserialize gap but not the startup typeinfo gap. ASP.NET Core's request-delegate factory pre-resolves typeinfo at startup, hitting the resolver chain directly (not the Converters list). [ZeroAlloc.Templates PR #128](https://github.com/ZeroAlloc-Net/ZeroAlloc.Templates/pull/128) za-clean AOT smoke caught this — same Roslyn-gens-can't-see-each-other root cause as the original 2.3.0 gap, just surfacing in a different STJ pipeline corner.

**Design + plan:** [`docs/plans/2026-05-27-jsontypeinfo-resolver-design.md`](plans/2026-05-27-jsontypeinfo-resolver-design.md) + [`docs/plans/2026-05-27-jsontypeinfo-resolver.md`](plans/2026-05-27-jsontypeinfo-resolver.md).

---

## ~~V2 — MessagePack registrar + IFormatterResolver~~ — ✅ shipped 2.3.3 (2026-05-28)

**Shipped:** Generator emits a per-assembly `internal sealed ValueObjectMessagePackResolver : IFormatterResolver` (with the canonical `FormatterCache<T>` generic-static-cache pattern) plus a public `AddZeroAllocValueObjectFormatters(this MessagePackSerializerOptions)` extension method — single-file emission because MessagePack has only one registration shape (resolver chain), unlike STJ's `Converters` list + `TypeInfoResolverChain` split. Consumers using `MessagePack.SourceGenerator` (AOT) call `options.WithResolver(GeneratedMessagePackResolver.Instance).AddZeroAllocValueObjectFormatters()` and value-object properties on `[MessagePackObject]` DTOs round-trip with bare-primitive wire format.

**Design + plan:** [`docs/plans/2026-05-28-messagepack-resolver-helper-design.md`](plans/2026-05-28-messagepack-resolver-helper-design.md) + [`docs/plans/2026-05-28-messagepack-resolver-helper.md`](plans/2026-05-28-messagepack-resolver-helper.md).

## V3 — Bebop backend support

Adding [Bebop](https://github.com/6over3/bebop) as a fourth supported backend. The 2.3.0 per-backend-emission architecture (gating on `compilation.ReferencedAssemblyNames`) scales cleanly — same shape as the MessagePack/MemoryPack additions, swapping in Bebop's serialization primitives. Recorded as a future direction; no concrete timeline.
