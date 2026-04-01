# Changelog

All notable changes to this project will be documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.0.0/),
and this project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

## [Unreleased]

### Added
- `ISerializer<T>` interface with `IBufferWriter<byte>`-based `Serialize` and `ReadOnlySpan<byte>`-based `Deserialize`
- `ZeroAllocSerializableAttribute` with `SerializationFormat` enum (MemoryPack, MessagePack, SystemTextJson)
- Roslyn incremental source generator emitting AOT-safe `{TypeName}Serializer` per annotated type
- DI registration extension methods generated per type
- `MemoryPackSerializer<T>` base class (`ZeroAlloc.Serialisation.MemoryPack`)
- `MessagePackSerializer<T>` base class (`ZeroAlloc.Serialisation.MessagePack`)
- `SystemTextJsonSerializer<T>` base class with `JsonTypeInfo<T>` injection (`ZeroAlloc.Serialisation.SystemTextJson`)
