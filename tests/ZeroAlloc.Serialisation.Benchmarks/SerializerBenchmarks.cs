using System.Buffers;
using BenchmarkDotNet.Attributes;
using ZeroAlloc.Serialisation;
using ZeroAlloc.Serialisation.MemoryPack;
using ZeroAlloc.Serialisation.MessagePack;
using ZeroAlloc.Serialisation.SystemTextJson;

namespace ZeroAlloc.Serialisation.Benchmarks;

[MemoryDiagnoser]
[SimpleJob]
public class SerializerBenchmarks
{
    private static readonly BenchDto s_dto = new() { Id = 42, Name = "Alice" };

    private readonly ISerializer<BenchDto> _mp = new MemoryPackSerializer<BenchDto>();
    private readonly ISerializer<BenchDto> _msg = new MessagePackSerializer<BenchDto>();
    private readonly ISerializer<BenchDto> _stj = new SystemTextJsonSerializer<BenchDto>(BenchContext.Default.BenchDto);

    private byte[] _mpBytes = [];
    private byte[] _msgBytes = [];
    private byte[] _stjBytes = [];

    [GlobalSetup]
    public void Setup()
    {
        var buf = new ArrayBufferWriter<byte>();
        _mp.Serialize(buf, s_dto); _mpBytes = buf.WrittenSpan.ToArray();
        buf = new ArrayBufferWriter<byte>();
        _msg.Serialize(buf, s_dto); _msgBytes = buf.WrittenSpan.ToArray();
        buf = new ArrayBufferWriter<byte>();
        _stj.Serialize(buf, s_dto); _stjBytes = buf.WrittenSpan.ToArray();
    }

    [Benchmark(Baseline = true)]
    public int Serialize_MemoryPack()
    {
        var buf = new ArrayBufferWriter<byte>();
        _mp.Serialize(buf, s_dto);
        return buf.WrittenCount;
    }

    [Benchmark]
    public int Serialize_MessagePack()
    {
        var buf = new ArrayBufferWriter<byte>();
        _msg.Serialize(buf, s_dto);
        return buf.WrittenCount;
    }

    [Benchmark]
    public int Serialize_SystemTextJson()
    {
        var buf = new ArrayBufferWriter<byte>();
        _stj.Serialize(buf, s_dto);
        return buf.WrittenCount;
    }

    [Benchmark]
    public BenchDto? Deserialize_MemoryPack() => _mp.Deserialize(_mpBytes);

    [Benchmark]
    public BenchDto? Deserialize_MessagePack() => _msg.Deserialize(_msgBytes);

    [Benchmark]
    public BenchDto? Deserialize_SystemTextJson() => _stj.Deserialize(_stjBytes);
}
