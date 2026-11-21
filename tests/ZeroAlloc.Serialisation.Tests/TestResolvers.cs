using MessagePack;
using MessagePack.Resolvers;

namespace ZeroAlloc.Serialisation.Tests;

/// <summary>Resolvers the MessagePack adapter tests pass to <c>MessagePackSerializer&lt;T&gt;</c>.</summary>
internal static class TestResolvers
{
    /// <summary>
    /// The test assembly's source-generated formatters and MessagePack's built-in ones: no reflection, as under
    /// Native AOT.
    /// </summary>
    public static readonly IFormatterResolver Aot = CompositeResolver.Create(AotTestResolver.Instance, BuiltinResolver.Instance);
}
