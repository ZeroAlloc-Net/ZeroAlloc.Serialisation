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
- **BDN**: v0.14.0 with `[MemoryDiagnoser]`
- **Runtime**: .NET 10.0.7, X64 RyuJIT AVX2
- **Payload**: `BenchDto { Id = 42, Name = "Alice" }` — a small object so wrapper overhead is the dominant cost (not payload work).

Each scenario runs the raw library API and the ZA wrapper through the same call. The bytes produced are byte-identical across the pair (the wrapper delegates to the underlying library).

## Head-to-head vs raw libraries

<!-- BENCH:START -->
_Last refreshed: 2026-05-13_

### Deserialize — wrapper is thin

| Library | Raw | ZA wrapper | Overhead |
|---|---:|---:|---:|
| MemoryPack | 47.6 ns / 64 B | 55.2 ns / 64 B | **+16%, 0 B** |
| MessagePack | 123.9 ns / 64 B | 182.7 ns / 96 B | **+47%, +32 B** |
| System.Text.Json | 303.3 ns / 64 B | 374.5 ns / 64 B | **+23%, 0 B** |

The deserialize wrapper is a thin pass-through: same allocation (0–32 B extra), 16–47% extra time for the interface dispatch. The MessagePack extra 32 B is the `ReadOnlySpan<byte>` → array boxing for the underlying API call.

### Serialize — IBufferWriter pattern adds measurable cost

| Library | Raw | ZA wrapper | Overhead |
|---|---:|---:|---:|
| MemoryPack | 74.6 ns / 48 B | 159.7 ns / 312 B | **+114%, +264 B** |
| MessagePack | 128.1 ns / 32 B | 215.2 ns / 312 B | **+68%, +280 B** |
| System.Text.Json | 225.7 ns / 48 B | 287.7 ns / 448 B | **+27%, +400 B** |

The serialize wrapper costs more because `ISerializer<T>.Serialize` takes an `IBufferWriter<byte>` (the buffer abstraction). The 264–400 B is the `ArrayBufferWriter<byte>` instance + its internal buffer, allocated fresh per call by the benchmark.

This is the cost of the abstraction. **The wrapper is fastest when the caller pools the buffer writer** — the IBufferWriter pattern is designed for that scenario. The benchmark measures worst case (fresh writer per call); a real application that pools writers across N calls amortises the 264 B to ~0 per call.
<!-- BENCH:END -->

## When the wrapper makes sense

- **Multi-format support** — your app needs to serialize the same DTO to MemoryPack for service-to-service and STJ for public APIs, with a single interface seam to swap on.
- **Pooled IBufferWriter** — you already use `ArrayPool<byte>` + `IBufferWriter<byte>` in your pipeline. The wrapper integrates cleanly.
- **DI registration** — your handlers depend on `ISerializer<T>` and switch between implementations via configuration.
- **AOT compatibility** — every wrapper is `[RequiresDynamicCode]`-suppressed because the underlying library's source generator emits the formatter at compile time.

## When to call the raw library directly

- **Hot path with no pool** — if you serialize at >100k calls/sec and don't pool the buffer writer, the 264 B/call overhead is measurable; call `MemoryPackSerializer.Serialize(dto)` directly.
- **Single-format apps** — if you'll never switch from MemoryPack, the abstraction adds friction without value.
- **Streaming scenarios** — the raw library's stream-based APIs may be more ergonomic than wrapping `IBufferWriter` around a `MemoryStream`.

## Running the benchmarks yourself

```bash
cd tests/ZeroAlloc.Serialisation.Benchmarks
dotnet run -c Release -- --filter "*RawVsWrappedBenchmark*"
```
