# Changelog

All notable changes to this project will be documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.0.0/),
and this project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

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

## [Unreleased]

### Added
- `ISerializer<T>` interface with `IBufferWriter<byte>`-based `Serialize` and `ReadOnlySpan<byte>`-based `Deserialize`
- `ZeroAllocSerializableAttribute` with `SerializationFormat` enum (MemoryPack, MessagePack, SystemTextJson)
- Roslyn incremental source generator emitting AOT-safe `{TypeName}Serializer` per annotated type
- DI registration extension methods generated per type
- `MemoryPackSerializer<T>` base class (`ZeroAlloc.Serialisation.MemoryPack`)
- `MessagePackSerializer<T>` base class (`ZeroAlloc.Serialisation.MessagePack`)
- `SystemTextJsonSerializer<T>` base class with `JsonTypeInfo<T>` injection (`ZeroAlloc.Serialisation.SystemTextJson`)
