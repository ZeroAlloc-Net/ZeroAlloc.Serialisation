using System.Collections.Immutable;
using Microsoft.CodeAnalysis;

namespace ZeroAlloc.Serialisation.Generator.Tests;

/// <summary>
/// The BCL reference set the test compilations are built against. It follows the TFM the tests run
/// on, because the test assemblies that get referenced next to it were compiled against that
/// runtime and a mismatched set fails with CS1705.
/// </summary>
internal static class TestReferences
{
#if NET11_0_OR_GREATER
    public static ImmutableArray<PortableExecutableReference> Bcl => Basic.Reference.Assemblies.Net110.References.All;
#else
    public static ImmutableArray<PortableExecutableReference> Bcl => Basic.Reference.Assemblies.Net100.References.All;
#endif
}
