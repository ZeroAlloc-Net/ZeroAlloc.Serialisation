; Shipped analyzer releases.
; https://github.com/dotnet/roslyn-analyzers/blob/main/src/Microsoft.CodeAnalysis.Analyzers/ReleaseTrackingAnalyzers.Help.md

## Release 2.1.0

### New Rules

Rule ID | Category                | Severity | Notes
--------|-------------------------|----------|-----------------------------------------------------------------------------------
ZASZ001 | ZeroAlloc.Serialisation | Error    | [ZeroAllocSerializable] cannot be applied to an open generic type
ZASZ002 | ZeroAlloc.Serialisation | Error    | Unknown SerializationFormat value
ZASZ003 | ZeroAlloc.Serialisation | Warning  | Missing per-format attribute (MemoryPackable / MessagePackObject)
ZASZ004 | ZeroAlloc.Serialisation | Error    | SystemTextJson type has no matching [JsonSerializable] on a JsonSerializerContext

## Release 2.5.0

### New Rules

Rule ID | Category                | Severity | Notes
--------|-------------------------|----------|-----------------------------------------------------------------------------------
ZASZ005 | ZeroAlloc.Serialisation | Error    | [ValueObject] type is generic, nested in a generic type, or file-local
ZASZ006 | ZeroAlloc.Serialisation | Warning  | MemoryPack formatter for a [ValueObject] in a private or protected type cannot be registered
ZASZ007 | ZeroAlloc.Serialisation | Error    | [assembly: ZeroAllocSerializable] names an open generic type
ZASZ008 | ZeroAlloc.Serialisation | Error    | [assembly: ZeroAllocSerializable] names a non-generic type
ZASZ009 | ZeroAlloc.Serialisation | Error    | Type is declared serializable more than once
ZASZ010 | ZeroAlloc.Serialisation | Error    | [ZeroAllocSerializable] form does not fit where it is applied
ZASZ011 | ZeroAlloc.Serialisation | Error    | Closed generic type would get the same generated names as another serializable type

## Release 2.5.1

### New Rules

Rule ID | Category                | Severity | Notes
--------|-------------------------|----------|-----------------------------------------------------------------------------------
ZASZ012 | ZeroAlloc.Serialisation | Error    | Invalid ZeroAllocGeneratedAccessibility value
