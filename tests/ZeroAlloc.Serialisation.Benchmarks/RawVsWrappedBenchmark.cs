using System.Buffers;
using System.Text.Json;
using BenchmarkDotNet.Attributes;
using MemoryPack;
using MessagePack;
using ZeroAlloc.Serialisation;
using ZeroAlloc.Serialisation.MemoryPack;
using ZeroAlloc.Serialisation.MessagePack;
using ZeroAlloc.Serialisation.SystemTextJson;

namespace ZeroAlloc.Serialisation.Benchmarks;

// ZA.Serialisation is an abstraction layer — ISerializer<T> with three
// implementations (MemoryPack / MessagePack / System.Text.Json). It does not
// compete with those libraries; it unifies them behind one interface so the
// caller can swap implementations without changing call sites.
//
// The honest comparison is therefore: does the wrapper add measurable
// overhead vs calling the raw library? This benchmark runs each library
// through its native API and through the ZA.Serialisation wrapper to measure
// the gap.
//
// Expected result: near-parity. Any meaningful overhead is a bug to file.
[MemoryDiagnoser]
[SimpleJob]
public class RawVsWrappedBenchmark
{
    private static readonly BenchDto s_dto = new() { Id = 42, Name = "Alice" };

    // ZA wrappers
    private readonly ISerializer<BenchDto> _zaMp = new MemoryPackSerializer<BenchDto>();
    private readonly ISerializer<BenchDto> _zaMsg = new MessagePackSerializer<BenchDto>();
    private readonly ISerializer<BenchDto> _zaStj = new SystemTextJsonSerializer<BenchDto>(BenchContext.Default.BenchDto);

    // Pre-serialized payloads (same shape for ZA + raw, since the wrappers
    // delegate to the same underlying library — bytes are byte-identical).
    private byte[] _mpBytes = [];
    private byte[] _msgBytes = [];
    private byte[] _stjBytes = [];

    [GlobalSetup]
    public void Setup()
    {
        _mpBytes = global::MemoryPack.MemoryPackSerializer.Serialize(s_dto);
        _msgBytes = global::MessagePack.MessagePackSerializer.Serialize(s_dto);
        _stjBytes = JsonSerializer.SerializeToUtf8Bytes(s_dto, BenchContext.Default.BenchDto);
    }

    // ── MemoryPack: serialize ───────────────────────────────────────────────

    [Benchmark(Baseline = true, Description = "Raw MemoryPack: Serialize")]
    [BenchmarkCategory("Serialize_MP")]
    public byte[] Raw_MemoryPack_Serialize()
        => global::MemoryPack.MemoryPackSerializer.Serialize(s_dto);

    [Benchmark(Description = "ZA wrapper over MemoryPack: Serialize")]
    [BenchmarkCategory("Serialize_MP")]
    public int Za_MemoryPack_Serialize()
    {
        var buf = new ArrayBufferWriter<byte>();
        _zaMp.Serialize(buf, s_dto);
        return buf.WrittenCount;
    }

    // ── MemoryPack: deserialize ─────────────────────────────────────────────

    [Benchmark(Description = "Raw MemoryPack: Deserialize")]
    [BenchmarkCategory("Deserialize_MP")]
    public BenchDto? Raw_MemoryPack_Deserialize()
        => global::MemoryPack.MemoryPackSerializer.Deserialize<BenchDto>(_mpBytes);

    [Benchmark(Description = "ZA wrapper over MemoryPack: Deserialize")]
    [BenchmarkCategory("Deserialize_MP")]
    public BenchDto? Za_MemoryPack_Deserialize()
        => _zaMp.Deserialize(_mpBytes);

    // ── MessagePack: serialize ───────────────────────────────────────────────

    [Benchmark(Description = "Raw MessagePack: Serialize")]
    [BenchmarkCategory("Serialize_Msg")]
    public byte[] Raw_MessagePack_Serialize()
        => global::MessagePack.MessagePackSerializer.Serialize(s_dto);

    [Benchmark(Description = "ZA wrapper over MessagePack: Serialize")]
    [BenchmarkCategory("Serialize_Msg")]
    public int Za_MessagePack_Serialize()
    {
        var buf = new ArrayBufferWriter<byte>();
        _zaMsg.Serialize(buf, s_dto);
        return buf.WrittenCount;
    }

    // ── MessagePack: deserialize ────────────────────────────────────────────

    [Benchmark(Description = "Raw MessagePack: Deserialize")]
    [BenchmarkCategory("Deserialize_Msg")]
    public BenchDto? Raw_MessagePack_Deserialize()
        => global::MessagePack.MessagePackSerializer.Deserialize<BenchDto>(_msgBytes);

    [Benchmark(Description = "ZA wrapper over MessagePack: Deserialize")]
    [BenchmarkCategory("Deserialize_Msg")]
    public BenchDto? Za_MessagePack_Deserialize()
        => _zaMsg.Deserialize(_msgBytes);

    // ── System.Text.Json: serialize ─────────────────────────────────────────

    [Benchmark(Description = "Raw System.Text.Json: Serialize")]
    [BenchmarkCategory("Serialize_Stj")]
    public byte[] Raw_SystemTextJson_Serialize()
        => JsonSerializer.SerializeToUtf8Bytes(s_dto, BenchContext.Default.BenchDto);

    [Benchmark(Description = "ZA wrapper over System.Text.Json: Serialize")]
    [BenchmarkCategory("Serialize_Stj")]
    public int Za_SystemTextJson_Serialize()
    {
        var buf = new ArrayBufferWriter<byte>();
        _zaStj.Serialize(buf, s_dto);
        return buf.WrittenCount;
    }

    // ── System.Text.Json: deserialize ───────────────────────────────────────

    [Benchmark(Description = "Raw System.Text.Json: Deserialize")]
    [BenchmarkCategory("Deserialize_Stj")]
    public BenchDto? Raw_SystemTextJson_Deserialize()
        => JsonSerializer.Deserialize(_stjBytes, BenchContext.Default.BenchDto);

    [Benchmark(Description = "ZA wrapper over System.Text.Json: Deserialize")]
    [BenchmarkCategory("Deserialize_Stj")]
    public BenchDto? Za_SystemTextJson_Deserialize()
        => _zaStj.Deserialize(_stjBytes);
}
