using System;
using System.Linq;
using Microsoft.CodeAnalysis;

namespace ZeroAlloc.Serialisation.Generator;

/// <summary>
/// Emits transparent serializers for <c>[ValueObject]</c>-decorated
/// single-property partial structs across the three supported backends
/// (System.Text.Json, MessagePack, MemoryPack). Each emission method is
/// gated by a check on the consuming compilation's assembly references —
/// adopters who don't reference a given backend package pay zero code-gen
/// for it.
/// </summary>
internal static class ValueObjectEmitter
{
    private const string SystemTextJsonBackendAssembly = "ZeroAlloc.Serialisation.SystemTextJson";
    private const string MessagePackBackendAssembly = "ZeroAlloc.Serialisation.MessagePack";
    private const string MemoryPackBackendAssembly = "ZeroAlloc.Serialisation.MemoryPack";

    internal static bool ReferencesSystemTextJson(Compilation compilation) =>
        ReferencesAssembly(compilation, SystemTextJsonBackendAssembly);

    internal static bool ReferencesMessagePack(Compilation compilation) =>
        ReferencesAssembly(compilation, MessagePackBackendAssembly);

    internal static bool ReferencesMemoryPack(Compilation compilation) =>
        ReferencesAssembly(compilation, MemoryPackBackendAssembly);

    private static bool ReferencesAssembly(Compilation compilation, string assemblyName) =>
        compilation.ReferencedAssemblyNames.Any(a =>
            string.Equals(a.Name, assemblyName, StringComparison.Ordinal));
}
