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
