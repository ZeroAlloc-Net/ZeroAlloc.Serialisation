using System.Buffers;
using System.Diagnostics.CodeAnalysis;
using System.Reflection;
using MemoryPack;
using MessagePack;
using MessagePack.Resolvers;
using Xunit;
using ZeroAlloc.Serialisation.MemoryPack;
using ZeroAlloc.Serialisation.MessagePack;

namespace ZeroAlloc.Serialisation.Tests;

[MemoryPackable]
public sealed partial record AotMemoryPackRecord(int Id, string Name);

// Used only by the registration test: nothing else may touch it, so it is unregistered until that test constructs the serializer.
[MemoryPackable]
internal sealed partial record RegistrationProbeRecord(int Id);

[MessagePackObject]
public sealed class AotMessagePackDto
{
    [Key(0)] public int Id { get; set; }
    [Key(1)] public string Name { get; set; } = "";
}

[GeneratedMessagePackResolver]
internal sealed partial class AotTestResolver
{
}

public class AotSafeAdapterTests
{
    [Fact]
    public void MemoryPackableSerializer_RoundTripsMemoryPackableRecord()
    {
        var serializer = new MemoryPackableSerializer<AotMemoryPackRecord>();
        var buffer = new ArrayBufferWriter<byte>();

        serializer.Serialize(buffer, new AotMemoryPackRecord(5, "Eve"));
        var result = serializer.Deserialize(buffer.WrittenSpan);

        Assert.Equal(new AotMemoryPackRecord(5, "Eve"), result);
    }

    [Fact]
    public void MemoryPackableSerializer_Constructor_RegistersFormatter()
    {
        Assert.False(MemoryPackFormatterProvider.IsRegistered<RegistrationProbeRecord>());

        _ = new MemoryPackableSerializer<RegistrationProbeRecord>();

        Assert.True(MemoryPackFormatterProvider.IsRegistered<RegistrationProbeRecord>());
    }

    [Fact]
    public void MemoryPackableSerializer_EmptySpan_ReturnsDefault()
    {
        var serializer = new MemoryPackableSerializer<AotMemoryPackRecord>();
        Assert.Null(serializer.Deserialize(ReadOnlySpan<byte>.Empty));
    }

    [Fact]
    public void MessagePackSerializer_WithResolver_RoundTripsMessagePackObject()
    {
        var resolver = CompositeResolver.Create(AotTestResolver.Instance, BuiltinResolver.Instance);
        var serializer = new MessagePackSerializer<AotMessagePackDto>(resolver);
        var buffer = new ArrayBufferWriter<byte>();

        serializer.Serialize(buffer, new AotMessagePackDto { Id = 9, Name = "Zed" });
        var result = serializer.Deserialize(buffer.WrittenSpan);

        Assert.NotNull(result);
        Assert.Equal(9, result.Id);
        Assert.Equal("Zed", result.Name);
    }

    [Fact]
    public void MessagePackSerializer_NullResolver_Throws()
    {
        Assert.Throws<ArgumentNullException>(() => new MessagePackSerializer<AotMessagePackDto>((IFormatterResolver)null!));
    }

    // #195: neither adapter has a constructor that picks a reflection path, so no trim or AOT annotation is left
    // on their public API.
    [Theory]
    [InlineData(typeof(MemoryPackableSerializer<AotMemoryPackRecord>))]
    [InlineData(typeof(MessagePackSerializer<AotMessagePackDto>))]
    public void Adapter_PublicApi_CarriesNoTrimOrAotAnnotations(Type adapter)
    {
        var members = adapter.GetMembers(BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly)
            .Append(adapter);

        foreach (var member in members)
        {
            Assert.False(member.IsDefined(typeof(RequiresUnreferencedCodeAttribute), inherit: false), member.ToString());
            Assert.False(member.IsDefined(typeof(RequiresDynamicCodeAttribute), inherit: false), member.ToString());
        }
    }

    [Fact]
    public void MessagePackSerializer_OnlyConstructor_TakesAResolver()
    {
        var constructor = Assert.Single(typeof(MessagePackSerializer<AotMessagePackDto>).GetConstructors());

        Assert.Equal(
            [typeof(IFormatterResolver), typeof(MessagePackSerializerOptions)],
            constructor.GetParameters().Select(p => p.ParameterType));
    }

    [Fact]
    public void MemoryPackAdapter_HasNoReflectionBasedSerializer()
    {
        var assembly = typeof(MemoryPackableSerializer<>).Assembly;

        Assert.Null(assembly.GetType("ZeroAlloc.Serialisation.MemoryPack.MemoryPackSerializer`1"));
        Assert.Equal(typeof(object), typeof(MemoryPackableSerializer<AotMemoryPackRecord>).BaseType);
        Assert.Single(typeof(MemoryPackableSerializer<AotMemoryPackRecord>).GetConstructors());
    }

    [Fact]
    public void MessagePackSerializer_WithResolverAndOptions_KeepsTheOptionsAndUsesTheResolver()
    {
        var resolver = CompositeResolver.Create(AotTestResolver.Instance, BuiltinResolver.Instance);
        // The options' own resolver has no formatter for the DTO, so a round trip shows the constructor's resolver
        // replaced it.
        var options = MessagePackSerializerOptions.Standard
            .WithResolver(BuiltinResolver.Instance)
            .WithCompression(MessagePackCompression.Lz4BlockArray);
        var serializer = new MessagePackSerializer<AotMessagePackDto>(resolver, options);
        var dto = new AotMessagePackDto { Id = 3, Name = new string('a', 512) };
        var buffer = new ArrayBufferWriter<byte>();

        serializer.Serialize(buffer, dto);
        var result = serializer.Deserialize(buffer.WrittenSpan);

        // Lz4 compresses the 512 repeated characters, so the options were kept.
        Assert.True(buffer.WrittenCount < 512);
        Assert.NotNull(result);
        Assert.Equal(dto.Name, result.Name);
    }

    [Fact]
    public void MessagePackSerializer_ResolverWithoutTheFormatter_Throws()
    {
        // Only the built-in formatters: the serializer adds no reflection-based resolver of its own.
        var serializer = new MessagePackSerializer<AotMessagePackDto>(BuiltinResolver.Instance);

        Assert.Throws<MessagePackSerializationException>(
            () => serializer.Serialize(new ArrayBufferWriter<byte>(), new AotMessagePackDto { Id = 1 }));
    }
}
