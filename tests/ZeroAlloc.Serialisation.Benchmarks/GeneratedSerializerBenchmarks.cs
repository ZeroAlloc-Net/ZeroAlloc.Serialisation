using System.Buffers;
using BenchmarkDotNet.Attributes;
using ZeroAlloc.Serialisation;

namespace ZeroAlloc.Serialisation.Benchmarks;

/// <summary>
/// The serializers the generator emits, called directly and through the generated
/// <c>SerializerDispatcher</c>. Direct Serialize writes into one reused buffer writer, so the
/// allocation column is the serializer's own. The dispatcher's memory-returning Serialize returns
/// memory the caller owns, so it allocates that buffer by contract; its IBufferWriter overloads write
/// into the same reused writer instead.
/// </summary>
[MemoryDiagnoser]
[SimpleJob]
public class GeneratedSerializerBenchmarks
{
    private static readonly GenMpDto s_mp = new() { Id = 42, Name = "Alice" };
    private static readonly GenMsgDto s_msg = new() { Id = 42, Name = "Alice" };
    private static readonly GenStjDto s_stj = new() { Id = 42, Name = "Alice" };
    private static readonly GenMpStructDto s_mpStruct = new() { Id = 42, Name = "Alice" };

    private readonly GenMpDtoSerializer _mp = new();
    private readonly GenMsgDtoSerializer _msg = new();
    private readonly GenStjDtoSerializer _stj = new();
    private readonly SerializerDispatcher _dispatcher = new();
    private readonly ArrayBufferWriter<byte> _buffer = new(1024);

    private byte[] _mpBytes = [];
    private byte[] _msgBytes = [];
    private byte[] _stjBytes = [];

    [GlobalSetup]
    public void Setup()
    {
        _mpBytes = _dispatcher.Serialize(s_mp, typeof(GenMpDto)).ToArray();
        _msgBytes = _dispatcher.Serialize(s_msg, typeof(GenMsgDto)).ToArray();
        _stjBytes = _dispatcher.Serialize(s_stj, typeof(GenStjDto)).ToArray();
    }

    [Benchmark]
    public int Generated_Serialize_MemoryPack()
    {
        _buffer.ResetWrittenCount();
        _mp.Serialize(_buffer, s_mp);
        return _buffer.WrittenCount;
    }

    [Benchmark]
    public int Generated_Serialize_MessagePack()
    {
        _buffer.ResetWrittenCount();
        _msg.Serialize(_buffer, s_msg);
        return _buffer.WrittenCount;
    }

    [Benchmark]
    public int Generated_Serialize_SystemTextJson()
    {
        _buffer.ResetWrittenCount();
        _stj.Serialize(_buffer, s_stj);
        return _buffer.WrittenCount;
    }

    [Benchmark]
    public GenMpDto? Generated_Deserialize_MemoryPack() => _mp.Deserialize(_mpBytes);

    [Benchmark]
    public GenMsgDto? Generated_Deserialize_MessagePack() => _msg.Deserialize(_msgBytes);

    [Benchmark]
    public GenStjDto? Generated_Deserialize_SystemTextJson() => _stj.Deserialize(_stjBytes);

    [Benchmark]
    public ReadOnlyMemory<byte> Dispatcher_Serialize_MessagePack() => _dispatcher.Serialize(s_msg, typeof(GenMsgDto));

    [Benchmark]
    public ReadOnlyMemory<byte> Dispatcher_Serialize_SystemTextJson() => _dispatcher.Serialize(s_stj, typeof(GenStjDto));

    [Benchmark]
    public ReadOnlyMemory<byte> Dispatcher_Serialize_MemoryPackStruct() => _dispatcher.Serialize(s_mpStruct, typeof(GenMpStructDto));

    [Benchmark]
    public int Dispatcher_SerializeToWriter_ByType_SystemTextJson()
    {
        _buffer.ResetWrittenCount();
        _dispatcher.Serialize(_buffer, s_stj, typeof(GenStjDto));
        return _buffer.WrittenCount;
    }

    [Benchmark]
    public int Dispatcher_SerializeToWriter_Generic_SystemTextJson()
    {
        _buffer.ResetWrittenCount();
        _dispatcher.Serialize(_buffer, s_stj);
        return _buffer.WrittenCount;
    }

    [Benchmark]
    public int Dispatcher_SerializeToWriter_Generic_MemoryPackStruct()
    {
        _buffer.ResetWrittenCount();
        _dispatcher.Serialize(_buffer, s_mpStruct);
        return _buffer.WrittenCount;
    }

    [Benchmark]
    public object? Dispatcher_Deserialize_MemoryPack() => _dispatcher.Deserialize(_mpBytes, typeof(GenMpDto));

    [Benchmark]
    public object? Dispatcher_Deserialize_MessagePack() => _dispatcher.Deserialize(_msgBytes, typeof(GenMsgDto));

    [Benchmark]
    public object? Dispatcher_Deserialize_SystemTextJson() => _dispatcher.Deserialize(_stjBytes, typeof(GenStjDto));
}
