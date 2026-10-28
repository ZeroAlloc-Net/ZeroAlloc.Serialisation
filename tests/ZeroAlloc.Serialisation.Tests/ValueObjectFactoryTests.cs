using System;
using System.Linq;
using System.Text.Json;
using MemoryPack;
using MessagePack;
using Xunit;

namespace ZeroAlloc.Serialisation.Tests;

/// <summary>
/// Generated readers build a value object through its own static factory, so a wire value that
/// the factory rejects is an error instead of an instance. See ZeroAlloc.Serialisation#248.
/// </summary>
public class ValueObjectFactoryTests
{
    private static byte[] MpBytes<T>(T value) => MessagePackSerializer.Serialize(value);
    private static byte[] MemBytes<T>(T value) => MemoryPackSerializer.Serialize(value);

    // A reference type is a one-member object on the MemoryPack wire: a header byte, then the value.
    private static byte[] MemObject(byte[] member) => new byte[] { 1 }.Concat(member).ToArray();

    private static bool HasInner<TException>(Exception ex) where TException : Exception
    {
        for (var current = ex; current is not null; current = current.InnerException!)
            if (current is TException) return true;
        return false;
    }

    // ---- System.Text.Json ----

    [Fact]
    public void Json_TryCreate_ValidValue_RoundTrips()
    {
        var sku = JsonSerializer.Deserialize<Sku>("\"SKU-0001\"");
        Assert.Equal("SKU-0001", sku.Value);
        Assert.Equal("\"SKU-0001\"", JsonSerializer.Serialize(sku));
    }

    [Fact]
    public void Json_TryCreate_InvalidValue_ThrowsJsonException() =>
        Assert.Throws<JsonException>(() => JsonSerializer.Deserialize<Sku>("\"not a sku\""));

    [Fact]
    public void Json_PrivateConstructorStruct_UsesTheFactory()
    {
        Assert.Equal("SKU-0001", JsonSerializer.Deserialize<PrivateCtorSku>("\"SKU-0001\"").Value);
        Assert.Throws<JsonException>(() => JsonSerializer.Deserialize<PrivateCtorSku>("\"nope\""));
    }

    [Fact]
    public void Json_ClassWithTryCreate_ValidatesAndKeepsNull()
    {
        Assert.Equal(7, JsonSerializer.Deserialize<Code>("7")!.Value);
        Assert.Throws<JsonException>(() => JsonSerializer.Deserialize<Code>("0"));
        Assert.Null(JsonSerializer.Deserialize<Code>("null"));
    }

    [Fact]
    public void Json_Create_ValidValue_RoundTrips() =>
        Assert.Equal("a-b", JsonSerializer.Deserialize<Slug>("\"a-b\"")!.Value);

    [Fact]
    public void Json_Create_Throwing_IsWrappedInJsonExceptionWithTheOriginalInside()
    {
        var ex = Assert.Throws<JsonException>(() => JsonSerializer.Deserialize<Slug>("\"a b\""));
        Assert.IsType<ArgumentException>(ex.InnerException);
    }

    [Fact]
    public void Json_BothShapes_PrefersTryCreate()
    {
        Assert.Equal(3, JsonSerializer.Deserialize<BothShapes>("3").Value);
        Assert.Throws<JsonException>(() => JsonSerializer.Deserialize<BothShapes>("-1"));
    }

    [Fact]
    public void Json_ExplicitAttribute_OverridesTheConvention()
    {
        Assert.Equal("x", JsonSerializer.Deserialize<Explicit>("\"x\"").Value);
        Assert.Throws<JsonException>(() => JsonSerializer.Deserialize<Explicit>("\"\""));
    }

    [Fact]
    public void Json_ExplicitAttribute_CanNameACreateShapedMethod()
    {
        Assert.Equal(4, JsonSerializer.Deserialize<ExplicitCreate>("4").Value);
        var ex = Assert.Throws<JsonException>(() => JsonSerializer.Deserialize<ExplicitCreate>("5"));
        Assert.IsType<ArgumentOutOfRangeException>(ex.InnerException);
    }

    [Fact]
    public void Json_TimeSpanPath_UsesTheFactory()
    {
        Assert.Equal(TimeSpan.FromMinutes(5), JsonSerializer.Deserialize<PositiveSpan>("\"00:05:00\"").Value);
        Assert.Throws<JsonException>(() => JsonSerializer.Deserialize<PositiveSpan>("\"-00:05:00\""));
    }

    // ---- MessagePack ----

    [Fact]
    public void MessagePack_TryCreate_ValidValue_RoundTrips()
    {
        var sku = MessagePackSerializer.Deserialize<Sku>(MpBytes("SKU-0001"));
        Assert.Equal("SKU-0001", sku.Value);
    }

    [Fact]
    public void MessagePack_TryCreate_InvalidValue_ThrowsMessagePackSerializationException() =>
        Assert.Throws<MessagePackSerializationException>(() => MessagePackSerializer.Deserialize<Sku>(MpBytes("not a sku")));

    [Fact]
    public void MessagePack_PrivateConstructorStruct_UsesTheFactory()
    {
        Assert.Equal("SKU-0001", MessagePackSerializer.Deserialize<PrivateCtorSku>(MpBytes("SKU-0001")).Value);
        Assert.Throws<MessagePackSerializationException>(() => MessagePackSerializer.Deserialize<PrivateCtorSku>(MpBytes("nope")));
    }

    [Fact]
    public void MessagePack_ClassWithTryCreate_ValidatesAndKeepsNil()
    {
        Assert.Equal(7, MessagePackSerializer.Deserialize<Code>(MpBytes(7))!.Value);
        Assert.Throws<MessagePackSerializationException>(() => MessagePackSerializer.Deserialize<Code>(MpBytes(0)));
        Assert.Null(MessagePackSerializer.Deserialize<Code>(new byte[] { 0xc0 }));
    }

    [Fact]
    public void MessagePack_Create_ValidValue_RoundTrips() =>
        Assert.Equal("a-b", MessagePackSerializer.Deserialize<Slug>(MpBytes("a-b"))!.Value);

    [Fact]
    public void MessagePack_Create_Throwing_KeepsTheOriginalAsInnerException()
    {
        var ex = Assert.Throws<MessagePackSerializationException>(() => MessagePackSerializer.Deserialize<Slug>(MpBytes("a b")));
        Assert.True(HasInner<ArgumentException>(ex));
    }

    [Fact]
    public void MessagePack_BothShapes_PrefersTryCreate()
    {
        Assert.Equal(3, MessagePackSerializer.Deserialize<BothShapes>(MpBytes(3)).Value);
        Assert.Throws<MessagePackSerializationException>(() => MessagePackSerializer.Deserialize<BothShapes>(MpBytes(-1)));
    }

    [Fact]
    public void MessagePack_ExplicitAttribute_OverridesTheConvention()
    {
        Assert.Equal("x", MessagePackSerializer.Deserialize<Explicit>(MpBytes("x")).Value);
        Assert.Throws<MessagePackSerializationException>(() => MessagePackSerializer.Deserialize<Explicit>(MpBytes("")));
    }

    [Fact]
    public void MessagePack_ExplicitAttribute_CanNameACreateShapedMethod()
    {
        Assert.Equal(4, MessagePackSerializer.Deserialize<ExplicitCreate>(MpBytes(4)).Value);
        var ex = Assert.Throws<MessagePackSerializationException>(() => MessagePackSerializer.Deserialize<ExplicitCreate>(MpBytes(5)));
        Assert.True(HasInner<ArgumentOutOfRangeException>(ex));
    }

    [Fact]
    public void MessagePack_TimeSpan_UsesTheFactory()
    {
        Assert.Equal(TimeSpan.FromMinutes(5), MessagePackSerializer.Deserialize<PositiveSpan>(MpBytes(TimeSpan.FromMinutes(5))).Value);
        Assert.Throws<MessagePackSerializationException>(() => MessagePackSerializer.Deserialize<PositiveSpan>(MpBytes(TimeSpan.FromMinutes(-5))));
    }

    // ---- MemoryPack ----

    [Fact]
    public void MemoryPack_TryCreate_ValidValue_RoundTrips() =>
        Assert.Equal("SKU-0001", MemoryPackSerializer.Deserialize<Sku>(MemBytes("SKU-0001")).Value);

    [Fact]
    public void MemoryPack_TryCreate_InvalidValue_ThrowsMemoryPackSerializationException() =>
        Assert.Throws<MemoryPackSerializationException>(() => MemoryPackSerializer.Deserialize<Sku>(MemBytes("not a sku")));

    [Fact]
    public void MemoryPack_PrivateConstructorStruct_UsesTheFactory()
    {
        Assert.Equal("SKU-0001", MemoryPackSerializer.Deserialize<PrivateCtorSku>(MemBytes("SKU-0001")).Value);
        Assert.Throws<MemoryPackSerializationException>(() => MemoryPackSerializer.Deserialize<PrivateCtorSku>(MemBytes("nope")));
    }

    [Fact]
    public void MemoryPack_ClassWithTryCreate_ValidatesAndKeepsNull()
    {
        Assert.Equal(7, MemoryPackSerializer.Deserialize<Code>(MemObject(MemBytes(7)))!.Value);
        Assert.Throws<MemoryPackSerializationException>(() => MemoryPackSerializer.Deserialize<Code>(MemObject(MemBytes(0))));
        Assert.Null(MemoryPackSerializer.Deserialize<Code>(MemBytes<Code?>(null)));
    }

    [Fact]
    public void MemoryPack_Create_ValidValue_RoundTrips() =>
        Assert.Equal("a-b", MemoryPackSerializer.Deserialize<Slug>(MemObject(MemBytes("a-b")))!.Value);

    [Fact]
    public void MemoryPack_Create_Throwing_IsWrappedWithTheOriginalInside()
    {
        var ex = Assert.Throws<MemoryPackSerializationException>(() => MemoryPackSerializer.Deserialize<Slug>(MemObject(MemBytes("a b"))));
        Assert.IsType<ArgumentException>(ex.InnerException);
    }

    // MemoryPack copies a struct that holds only unmanaged data, such as an int or a TimeSpan,
    // as raw memory and never asks the registered formatter, so the three tests below can only
    // check that a valid value reads back. A string, or a class, goes through the formatter.
    [Fact]
    public void MemoryPack_BothShapes_ValidValueReadsBack() =>
        Assert.Equal(3, MemoryPackSerializer.Deserialize<BothShapes>(MemBytes(3)).Value);

    [Fact]
    public void MemoryPack_ExplicitAttribute_OverridesTheConvention()
    {
        Assert.Equal("x", MemoryPackSerializer.Deserialize<Explicit>(MemBytes("x")).Value);
        Assert.Throws<MemoryPackSerializationException>(() => MemoryPackSerializer.Deserialize<Explicit>(MemBytes("")));
    }

    [Fact]
    public void MemoryPack_ExplicitCreateShapedMethod_ValidValueReadsBack() =>
        Assert.Equal(4, MemoryPackSerializer.Deserialize<ExplicitCreate>(MemBytes(4)).Value);

    [Fact]
    public void MemoryPack_TimeSpan_ValidValueReadsBack() =>
        Assert.Equal(TimeSpan.FromMinutes(5), MemoryPackSerializer.Deserialize<PositiveSpan>(MemBytes(TimeSpan.FromMinutes(5))).Value);
}
