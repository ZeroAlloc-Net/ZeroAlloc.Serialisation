using Microsoft.CodeAnalysis;

namespace ZeroAlloc.Serialisation.Generator;

internal static class SerializerDiagnostics
{
    private const string Category = "ZeroAlloc.Serialisation";

    public static readonly DiagnosticDescriptor OpenGeneric = new(
        id: "ZASZ001",
        title: "[ZeroAllocSerializable] cannot be applied to an open generic type",
        messageFormat: "[ZeroAllocSerializable] cannot be applied to open generic type '{0}'; declare each closed construction on the assembly instead, for example [assembly: ZeroAllocSerializable(typeof(Envelope<Order>), SerializationFormat.MessagePack)]",
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

    public static readonly DiagnosticDescriptor AssemblyFormOpenGeneric = new(
        id: "ZASZ007",
        title: "[assembly: ZeroAllocSerializable] names an open generic type",
        messageFormat: "[assembly: ZeroAllocSerializable] names open generic type '{0}'; name a closed construction such as typeof(Envelope<Order>)",
        category: Category,
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true,
        description: "A serializer is generated for one concrete type. An open generic type has no concrete type to generate for, and creating one at run time would need reflection, which is not NativeAOT-safe. Declare every closed construction you serialize.");

    public static readonly DiagnosticDescriptor AssemblyFormNonGeneric = new(
        id: "ZASZ008",
        title: "[assembly: ZeroAllocSerializable] names a non-generic type",
        messageFormat: "[assembly: ZeroAllocSerializable] names non-generic type '{0}'; the assembly form is for closed generic types, so apply [ZeroAllocSerializable({1})] to the type declaration instead",
        category: Category,
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    public static readonly DiagnosticDescriptor DuplicateDeclaration = new(
        id: "ZASZ009",
        title: "Type is declared serializable more than once",
        messageFormat: "'{0}' is already declared serializable; remove this duplicate [ZeroAllocSerializable] declaration",
        category: Category,
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true,
        description: "Each type gets one serializer, so it is declared serializable once. Only the first declaration generates code.");

    public static readonly DiagnosticDescriptor FormDoesNotFitTarget = new(
        id: "ZASZ010",
        title: "[ZeroAllocSerializable] form does not fit where it is applied",
        messageFormat: "{0}",
        category: Category,
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true,
        description: "[ZeroAllocSerializable(format)] declares the class or struct it is applied to. [assembly: ZeroAllocSerializable(typeof(Envelope<Order>), format)] declares a closed generic type and is only valid on the assembly.");

    public static readonly DiagnosticDescriptor GeneratedNameCollision = new(
        id: "ZASZ011",
        title: "Closed generic type would get the same generated names as another serializable type",
        messageFormat: "'{0}' would get the generated names {1}Serializer and Add{1}Serializer, which '{2}' already gets in namespace '{3}'",
        category: Category,
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true,
        description: "A closed generic type's generated names are built from the simple names of the type and its type arguments, such as EnvelopeOfOrder for Envelope<Order>. Type arguments with the same simple name from different namespaces give the same names. Rename one of the types, or serialize them from different assemblies.");

    public static readonly DiagnosticDescriptor InvalidGeneratedAccessibility = new(
        id: "ZASZ012",
        title: "Invalid ZeroAllocGeneratedAccessibility value",
        messageFormat: "MSBuild property 'ZeroAllocGeneratedAccessibility' has invalid value '{0}'; allowed values are 'Public' and 'Internal'",
        category: Category,
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true,
        description: "ZeroAllocGeneratedAccessibility sets the accessibility of the generated SerializerDispatcher and of the generated registration extensions. Only Public, the default, and Internal are allowed, compared case-insensitively. Any other value is reported and treated as Public.");
}
