using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Text;
using Xunit;

namespace ZeroAlloc.Serialisation.Generator.Tests;

/// <summary>
/// Every ZASZ diagnostic is reported at the [ZeroAllocSerializable] attribute it is about, bound
/// to the syntax tree, so the IDE can point at it and <c>#pragma warning disable</c> can suppress
/// one case. A source marked with [| and |] gives the expected span; the markers are removed
/// before it runs.
/// </summary>
public sealed class SerializerDiagnosticLocationTests
{
    [Theory]
    [InlineData("ZASZ001", """
        using ZeroAlloc.Serialisation;
        namespace Demo;
        [[|ZeroAllocSerializable(SerializationFormat.SystemTextJson)|]]
        public sealed class Wrapper<T> { public T? Value { get; set; } }
        """)]
    [InlineData("ZASZ002", """
        using ZeroAlloc.Serialisation;
        namespace Demo;
        [[|ZeroAllocSerializable((SerializationFormat)999)|]]
        public sealed class Bad { public string V { get; set; } = ""; }
        """)]
    [InlineData("ZASZ003", """
        using ZeroAlloc.Serialisation;
        namespace Demo;
        [[|ZeroAllocSerializable(SerializationFormat.MemoryPack)|]]
        public sealed class MissingMpAttr { public string V { get; set; } = ""; }
        """)]
    [InlineData("ZASZ004", """
        using ZeroAlloc.Serialisation;
        namespace Demo;
        [[|ZeroAllocSerializable(SerializationFormat.SystemTextJson)|]]
        public sealed class Orphan { public string V { get; set; } = ""; }
        """)]
    [InlineData("ZASZ007", """
        using ZeroAlloc.Serialisation;
        [assembly: [|ZeroAllocSerializable(typeof(Demo.Envelope<>), SerializationFormat.MessagePack)|]]
        namespace Demo { public sealed class Envelope<T> { } }
        """)]
    [InlineData("ZASZ008", """
        using ZeroAlloc.Serialisation;
        [assembly: [|ZeroAllocSerializable(typeof(Demo.Order), SerializationFormat.MessagePack)|]]
        namespace Demo { public sealed class Order { } }
        """)]
    [InlineData("ZASZ009", """
        using ZeroAlloc.Serialisation;
        [assembly: ZeroAllocSerializable(typeof(Demo.Envelope<int>), SerializationFormat.SystemTextJson)]
        [assembly: [|ZeroAllocSerializable(typeof(Demo.Envelope<int>), SerializationFormat.SystemTextJson)|]]
        namespace Demo { public sealed class Envelope<T> { } }
        """)]
    [InlineData("ZASZ010", """
        using ZeroAlloc.Serialisation;
        [assembly: [|ZeroAllocSerializable(SerializationFormat.SystemTextJson)|]]
        """)]
    [InlineData("ZASZ011", """
        using ZeroAlloc.Serialisation;
        [assembly: ZeroAllocSerializable(typeof(Demo.Envelope<A.Order>), SerializationFormat.MemoryPack)]
        [assembly: [|ZeroAllocSerializable(typeof(Demo.Envelope<B.Order>), SerializationFormat.MemoryPack)|]]
        namespace Demo { public sealed class Envelope<T> { } }
        namespace A { public sealed class Order { } }
        namespace B { public sealed class Order { } }
        """)]
    public void Diagnostic_IsReportedAtTheAttribute(string id, string marked)
    {
        var (source, span) = Parse(marked);

        var diagnostics = GeneratorTestHost.GenerateOnFile(source);

        var diagnostic = One(diagnostics, d => string.Equals(d.Id, id, StringComparison.Ordinal));
        AssertAt(diagnostic.Location, span);
    }

    [Fact]
    public void PragmaAroundOneType_SuppressesThatZASZ003Only()
    {
        var diagnostics = GeneratorTestHost.GenerateOnFile("""
            using ZeroAlloc.Serialisation;
            namespace Demo;
            #pragma warning disable ZASZ003
            [ZeroAllocSerializable(SerializationFormat.MemoryPack)]
            public sealed class Quiet { public string V { get; set; } = ""; }
            #pragma warning restore ZASZ003
            [ZeroAllocSerializable(SerializationFormat.MessagePack)]
            public sealed class Loud { public string V { get; set; } = ""; }
            """);

        var zasz003 = diagnostics.Where(d => string.Equals(d.Id, "ZASZ003", StringComparison.Ordinal)).ToList();
        Assert.Equal(2, zasz003.Count);
        Assert.True(One(zasz003, d => Message(d).Contains("MemoryPack)]", StringComparison.Ordinal)).IsSuppressed);
        Assert.False(One(zasz003, d => Message(d).Contains("MessagePack)]", StringComparison.Ordinal)).IsSuppressed);
    }

    [Fact]
    public void PragmaAroundOneType_SuppressesThatZASZ004Only_WhenItsSeverityIsLowered()
    {
        // A #pragma cannot suppress an error, so the rule is lowered to a warning, as an
        // .editorconfig would. The pragma then applies only if the diagnostic is bound to the tree.
        var diagnostics = GeneratorTestHost.GenerateOnFile(
            """
            using ZeroAlloc.Serialisation;
            namespace Demo;
            #pragma warning disable ZASZ004
            [ZeroAllocSerializable(SerializationFormat.SystemTextJson)]
            public sealed class Quiet { public string V { get; set; } = ""; }
            #pragma warning restore ZASZ004
            [ZeroAllocSerializable(SerializationFormat.SystemTextJson)]
            public sealed class Loud { public string V { get; set; } = ""; }
            """,
            new Dictionary<string, ReportDiagnostic>(StringComparer.Ordinal) { ["ZASZ004"] = ReportDiagnostic.Warn });

        var zasz004 = diagnostics.Where(d => string.Equals(d.Id, "ZASZ004", StringComparison.Ordinal)).ToList();
        Assert.Equal(2, zasz004.Count);
        Assert.True(One(zasz004, d => Message(d).Contains("'Demo.Quiet'", StringComparison.Ordinal)).IsSuppressed);
        Assert.False(One(zasz004, d => Message(d).Contains("'Demo.Loud'", StringComparison.Ordinal)).IsSuppressed);
    }

    internal static T One<T>(IEnumerable<T> items, Func<T, bool> predicate)
    {
        var matches = items.Where(predicate).ToList();
        Assert.True(matches.Count == 1, $"Expected exactly one match, found {matches.Count}.");
        return matches[0];
    }

    private static string Message(Diagnostic diagnostic) => diagnostic.GetMessage(CultureInfo.InvariantCulture);

    private static void AssertAt(Location location, TextSpan expected)
    {
        // A source location, bound to the tree, is what #pragma and the IDE need.
        Assert.Equal(LocationKind.SourceFile, location.Kind);
        Assert.NotNull(location.SourceTree);
        Assert.Equal(GeneratorTestHost.TestFilePath, location.SourceTree!.FilePath);
        Assert.Equal(expected, location.SourceSpan);
    }

    private static (string Source, TextSpan Span) Parse(string marked)
    {
        var start = marked.IndexOf("[|", StringComparison.Ordinal);
        var end = marked.IndexOf("|]", StringComparison.Ordinal) - 2;
        var source = marked.Replace("[|", string.Empty, StringComparison.Ordinal)
            .Replace("|]", string.Empty, StringComparison.Ordinal);
        return (source, TextSpan.FromBounds(start, end));
    }
}
