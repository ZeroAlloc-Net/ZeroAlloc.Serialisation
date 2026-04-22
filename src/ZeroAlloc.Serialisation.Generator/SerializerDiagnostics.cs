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
}
