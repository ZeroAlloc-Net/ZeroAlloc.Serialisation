namespace ZeroAlloc.Serialisation.Generator.Models;

internal sealed record SerializerModel(
    string Namespace,
    string TypeName,
    string FullTypeName,
    string FormatName  // "MemoryPack" | "MessagePack" | "SystemTextJson"
);
