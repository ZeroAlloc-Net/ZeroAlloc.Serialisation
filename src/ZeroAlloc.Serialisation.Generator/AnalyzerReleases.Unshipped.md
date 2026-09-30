; Unshipped analyzer release.
; https://github.com/dotnet/roslyn-analyzers/blob/main/src/Microsoft.CodeAnalysis.Analyzers/ReleaseTrackingAnalyzers.Help.md

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
