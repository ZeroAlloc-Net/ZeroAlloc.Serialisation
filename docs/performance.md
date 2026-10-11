---
id: performance
title: Performance
slug: /performance
description: Wrapper overhead of ZA.Serialisation vs raw MemoryPack / MessagePack / System.Text.Json.
sidebar_position: 7
---

# Performance

ZA.Serialisation is an abstraction layer — `ISerializer<T>` with three implementations (MemoryPack / MessagePack / System.Text.Json). It does not compete with those libraries; it unifies them behind one interface so callers can swap implementations without changing call sites.

The honest comparison is therefore: **does the wrapper add measurable overhead vs calling the raw library?** That's what this page measures.

## Methodology

- **Source**: `tests/ZeroAlloc.Serialisation.Benchmarks/RawVsWrappedBenchmark.cs`
- **BDN**: v0.15.8 with `[MemoryDiagnoser]`, ShortRun job
- **Runtime**: .NET 10, X64 RyuJIT AVX2
- **Payload**: `BenchDto { Id = 42, Name = "Alice" }` — a small object so wrapper overhead is the dominant cost (not payload work).

Each scenario runs the raw library API and the ZA wrapper through the same call. The bytes produced are byte-identical across the pair (the wrapper delegates to the underlying library). Both sides of a Serialize pair write into the same reused `IBufferWriter<byte>`, so the allocation column shows what the wrapper itself allocates, not the caller's buffer.

The allocation column is exact and repeatable. The timings come from a short run on a shared machine and vary by tens of nanoseconds between runs; read them as an order of magnitude.

## Head-to-head vs raw libraries

<!-- BENCH:START -->
_Last refreshed: 2026-10-11_

### Deserialize

| Library | Raw | ZA wrapper | Extra allocation |
|---|---:|---:|---:|
| MemoryPack | 53.5 ns / 64 B | 87.0 ns / 64 B | **0 B** |
| MessagePack | 156.5 ns / 64 B | 204.4 ns / 64 B | **0 B** |
| System.Text.Json | 395.4 ns / 64 B | 270.6 ns / 64 B | **0 B** |

The 64 B is the deserialized object itself. MessagePack has no span overload, so the wrapper copies the span into a pooled array before handing it over; the copy allocates nothing. See [the MessagePack backend](backends/messagepack.md#deserializing-from-a-span).

### Serialize

| Library | Raw | ZA wrapper | Extra allocation |
|---|---:|---:|---:|
| MemoryPack | 53.6 ns / 0 B | 51.2 ns / 0 B | **0 B** |
| MessagePack | 56.1 ns / 0 B | 74.0 ns / 0 B | **0 B** |
| System.Text.Json | 93.1 ns / 0 B | 134.0 ns / 0 B | **0 B** |

The raw System.Text.Json row reuses one `Utf8JsonWriter`, as a caller writing to an `IBufferWriter<byte>` would. The wrapper does the same with one writer per thread; until #198 it created a writer per call, 136 B.

### Generated serializers and dispatcher

`GeneratedSerializerBenchmarks` measures the serializers the source generator emits, and the generated `SerializerDispatcher`:

| Path | Allocated |
|---|---:|
| Generated Serialize, all three formats | 0 B |
| Generated Deserialize, all three formats | 64 B, the result |
| Dispatcher `Deserialize(data, type)`, all three formats | 64 B, the result |
| Dispatcher `Deserialize<T>(data)`, struct with no reference members, MemoryPack and MessagePack | 0 B |
| Dispatcher `Deserialize<T>(data)`, struct with no reference members, System.Text.Json | 32 B, the box `JsonSerializer` builds the struct in |
| Dispatcher `Serialize(object, Type)` | 312 B, the `ArrayBufferWriter<byte>` and array it returns, and the box for a struct |
| Dispatcher `Serialize(writer, value, type)` into a reused writer | 0 B |
| Dispatcher `Serialize<T>(writer, value)` into a reused writer, class or struct | 0 B |

The dispatcher's `Serialize(object, Type)` returns memory the caller owns, so that buffer is its contract. The `IBufferWriter<byte>` overloads write into the caller's writer instead, and the generic one does not box a struct; `DispatcherBufferWriterTests` asserts 0 B for the generic overload with a struct in each of the three formats and with a class, and for the by-type overload. A caller that needs an exact-size `byte[]` pays for that array only: one `ToArray()` of the written span, against 376 to 416 B for `Serialize(object, Type)` followed by `ToArray()`.

`Deserialize(data, type)` returns `object`, so a struct is boxed on top of what the format allocates. `Deserialize<T>(data)` returns the struct as `T`: `DispatcherGenericDeserializeTests` asserts that it allocates exactly what the per-type serializer allocates, and one 32 B box less than the `object` overload for a 16-byte struct, in each of the three formats. A positional record struct read with System.Text.Json costs 168 B inside `JsonSerializer`, for its constructor arguments, whichever overload reads it.
<!-- BENCH:END -->

## When the wrapper makes sense

- **Multi-format support** — your app needs to serialize the same DTO to MemoryPack for service-to-service and STJ for public APIs, with a single interface seam to swap on.
- **Pooled IBufferWriter** — you already use `ArrayPool<byte>` + `IBufferWriter<byte>` in your pipeline. The wrapper integrates cleanly.
- **DI registration** — your handlers depend on `ISerializer<T>` and switch between implementations via configuration.
- **AOT compatibility** — every wrapper is `[RequiresDynamicCode]`-suppressed because the underlying library's source generator emits the formatter at compile time.

## When to call the raw library directly

- **Hot path with no pool** — the wrapper writes to an `IBufferWriter<byte>` you supply. If you would otherwise create a fresh `ArrayBufferWriter<byte>` per call, about 264 B and up, a raw API that returns a `byte[]`, such as `MemoryPackSerializer.Serialize(dto)`, allocates less.
- **Single-format apps** — if you'll never switch from MemoryPack, the abstraction adds friction without value.
- **Streaming scenarios** — the raw library's stream-based APIs may be more ergonomic than wrapping `IBufferWriter` around a `MemoryStream`.

## Running the benchmarks yourself

```bash
cd tests/ZeroAlloc.Serialisation.Benchmarks
dotnet run -c Release -- --filter "*RawVsWrappedBenchmark*"
```
