using System.Reflection;
using Xunit;
using ZeroAlloc.Serialisation.Tests.ClosedGenerics;

namespace ZeroAlloc.Serialisation.Tests;

/// <summary>
/// This project sets ZeroAllocGeneratedAccessibility to Internal through the package's build/
/// props file, as a consumer would, so every generated entry point must be internal (#185).
/// </summary>
public sealed class GeneratedAccessibilityTests
{
    [Fact]
    public void SerializerDispatcher_IsInternal()
    {
        Assert.False(typeof(SerializerDispatcher).IsPublic);
        Assert.True(typeof(SerializerDispatcher).IsNotPublic);
    }

    [Fact]
    public void RegistrationExtensions_AreInternal()
    {
        var dispatcherExtensions = typeof(SerializerDispatcher).Assembly.GetType("SerializerServiceCollectionExtensions", throwOnError: true)!;
        var perTypeExtensions = typeof(MpEnvelope<>).Assembly.GetType(
            "ZeroAlloc.Serialisation.Tests.ClosedGenerics.SerializerServiceCollectionExtensions", throwOnError: true)!;

        Assert.False(dispatcherExtensions.IsPublic);
        Assert.False(perTypeExtensions.IsPublic);
        Assert.NotNull(perTypeExtensions.GetMethod("AddMpEnvelopeOfMpPurchaseSerializer", BindingFlags.Public | BindingFlags.Static));
    }

    [Fact]
    public void ValueObjectRegistrationExtensions_AreInternal()
    {
        var assembly = typeof(SerializerDispatcher).Assembly;

        Assert.False(assembly.GetType("ZeroAlloc.Serialisation.SystemTextJson.ValueObjectJsonConvertersExtensions", throwOnError: true)!.IsPublic);
        Assert.False(assembly.GetType("ZeroAlloc.Serialisation.MessagePack.ValueObjectMessagePackFormattersExtensions", throwOnError: true)!.IsPublic);
    }
}
