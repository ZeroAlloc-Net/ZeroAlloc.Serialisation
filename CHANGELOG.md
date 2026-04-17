# Changelog

All notable changes to this project will be documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.0.0/),
and this project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

## [1.2.0](https://github.com/ZeroAlloc-Net/ZeroAlloc.Serialisation/compare/v1.1.0...v1.2.0) (2026-04-17)


### Features

* add DispatcherEmitter — generates SerializerDispatcher per assembly ([f56d05a](https://github.com/ZeroAlloc-Net/ZeroAlloc.Serialisation/commit/f56d05a9256f11d5620c6049979b58c07dd14572))
* add ISerializerDispatcher runtime-type dispatch interface ([4766772](https://github.com/ZeroAlloc-Net/ZeroAlloc.Serialisation/commit/4766772eee5e24c8dfed9643938fd635308ae8d2))
* wire DispatcherEmitter into SerializerGenerator via Collect() ([6d61d67](https://github.com/ZeroAlloc-Net/ZeroAlloc.Serialisation/commit/6d61d678ab01dc11b3fd64318e2bee2cacff0127))


### Bug Fixes

* use TryAddSingleton in generated DI extensions to avoid overwriting user registrations ([9e68f1e](https://github.com/ZeroAlloc-Net/ZeroAlloc.Serialisation/commit/9e68f1e30d2cc1052242f6973003e868d6d4200a))

## [Unreleased]

### Added

- `ISerializerDispatcher` — new interface for runtime-type dispatch: `Serialize(object, Type)` and `Deserialize(ReadOnlyMemory<byte>, Type)`. Allocation-tolerant by design; intended for use by infrastructure packages like `ZeroAlloc.EventSourcing`.
- Source generator now emits `SerializerDispatcher.g.cs` per assembly — a `public sealed partial class SerializerDispatcher : ISerializerDispatcher` with a compile-time switch over all `[ZeroAllocSerializable]` types. Zero reflection, AOT-safe.
- Source generator now emits `SerializerDispatcherExtensions.g.cs` per assembly — adds `AddSerializerDispatcher()` to the generated `SerializerServiceCollectionExtensions` partial class.

### Fixed

- Generated per-type DI extensions now use `TryAddSingleton` instead of `AddSingleton`. User-provided registrations are no longer silently overwritten.

## [1.1.0](https://github.com/ZeroAlloc-Net/ZeroAlloc.Serialisation/compare/v1.0.0...v1.1.0) (2026-04-01)


### Features

* add package icon to all NuGet packages ([0a410a9](https://github.com/ZeroAlloc-Net/ZeroAlloc.Serialisation/commit/0a410a9e64e9b4eb56b0d5dced9fe88a262a2af1))

## 1.0.0 (2026-04-01)


### Features

* **core:** add ISerializer&lt;T&gt; and ZeroAllocSerializableAttribute ([035637d](https://github.com/ZeroAlloc-Net/ZeroAlloc.Serialisation/commit/035637d492009bf9e2bdfede322b35cef52eb42a))
* **generator:** add SerializerGenerator with model extraction and emitters ([7e83017](https://github.com/ZeroAlloc-Net/ZeroAlloc.Serialisation/commit/7e8301734e65b0636c3032410864445e29d56acd))
* **memorypack:** add MemoryPackSerializer&lt;T&gt; ([b8ab02c](https://github.com/ZeroAlloc-Net/ZeroAlloc.Serialisation/commit/b8ab02ceac3faa058ce7920a65f1aad88c6acd73))
* **messagepack:** add MessagePackSerializer&lt;T&gt; ([8c68719](https://github.com/ZeroAlloc-Net/ZeroAlloc.Serialisation/commit/8c687199cac8c10003bfb0d05387b5434aa45aa2))
* **systemtextjson:** add SystemTextJsonSerializer&lt;T&gt; ([5660e70](https://github.com/ZeroAlloc-Net/ZeroAlloc.Serialisation/commit/5660e7039afab7685e20228ca6c8e8200b17c500))


### Bug Fixes

* **build:** exclude netstandard2.1 from adapter projects incompatible with it ([f5bb003](https://github.com/ZeroAlloc-Net/ZeroAlloc.Serialisation/commit/f5bb003114538c562d80eb0e55436acf24b7361e))
* **core:** address code review issues — Utf8JsonWriter flush, MessagePack alloc comment, test fixes ([d2a30c5](https://github.com/ZeroAlloc-Net/ZeroAlloc.Serialisation/commit/d2a30c5cabb137a2728b6c76dab906251f5aefc8))
* **generator:** document internal class constraint, fix spurious async in tests ([6feaf36](https://github.com/ZeroAlloc-Net/ZeroAlloc.Serialisation/commit/6feaf3629b5feeda802470584cc8788af5810a21))
