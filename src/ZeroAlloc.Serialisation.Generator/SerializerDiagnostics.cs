using Microsoft.CodeAnalysis;

namespace ZeroAlloc.Serialisation.Generator;

internal static class SerializerDiagnostics
{
    private const string Category = "ZeroAlloc.Serialisation";

    public static readonly DiagnosticDescriptor OpenGeneric = new(
        id: "ZASZ001",
        title: "[ZeroAllocSerializable] cannot be applied to an open generic type",
        messageFormat: "[ZeroAllocSerializable] cannot be applied to open generic type '{0}'; use a closed concrete type",
        category: Category,
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    public static readonly DiagnosticDescriptor UnknownFormat = new(
        id: "ZASZ002",
        title: "Unknown SerializationFormat value",
        messageFormat: "[ZeroAllocSerializable] value {0} is not a known SerializationFormat",
        category: Category,
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    public static readonly DiagnosticDescriptor MissingFormatAttribute = new(
        id: "ZASZ003",
        title: "Missing per-format attribute",
        messageFormat: "[ZeroAllocSerializable(SerializationFormat.{0})] typically requires the '{1}' attribute on the same type. Add it to enable serialization.",
        category: Category,
        defaultSeverity: DiagnosticSeverity.Warning,
        isEnabledByDefault: true);

    public static readonly DiagnosticDescriptor MissingJsonSerializerContext = new(
        id: "ZASZ004",
        title: "SystemTextJson type has no JsonSerializerContext binding",
        messageFormat: "[ZeroAllocSerializable(SerializationFormat.SystemTextJson)] requires '{0}' to be registered via [JsonSerializable(typeof({0}))] on a JsonSerializerContext-derived class in the same compilation. Without one, the generated serializer cannot be AOT-safe and emission is skipped.",
        category: Category,
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    public static readonly DiagnosticDescriptor ValueObjectCannotBeGenerated = new(
        id: "ZASZ005",
        title: "[ValueObject] type cannot get generated serializers",
        messageFormat: "[ValueObject] type '{0}' gets no generated serializers because {1}",
        category: Category,
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true,
        description: "The generator extends a [ValueObject] type with a partial declaration and converters. A file-local type cannot be extended from another file, and a generic type, or a type nested in one, would need its converters created by reflection at run time, which is not NativeAOT-safe. Move the type out of the generic type, make it non-file-local, or write its converters by hand.");

    public static readonly DiagnosticDescriptor ValueObjectMemoryPackNotRegistered = new(
        id: "ZASZ006",
        title: "MemoryPack formatter for a [ValueObject] in a private or protected type cannot be registered",
        messageFormat: "The MemoryPack formatter for [ValueObject] type '{0}' cannot be registered because its containing type '{1}' is {2}; System.Text.Json and MessagePack serializers are still generated",
        category: Category,
        defaultSeverity: DiagnosticSeverity.Warning,
        isEnabledByDefault: true,
        description: "MemoryPack formatters are registered from a [ModuleInitializer], which must be accessible from the whole assembly. Inside a private or protected containing type it cannot be, so no MemoryPack formatter is generated. Make the containing types internal or public to get one.");
}
