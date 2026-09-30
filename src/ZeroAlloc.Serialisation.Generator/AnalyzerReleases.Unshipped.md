; Unshipped analyzer release.
; https://github.com/dotnet/roslyn-analyzers/blob/main/src/Microsoft.CodeAnalysis.Analyzers/ReleaseTrackingAnalyzers.Help.md

### New Rules

Rule ID | Category                | Severity | Notes
--------|-------------------------|----------|-----------------------------------------------------------------------------------
ZASZ005 | ZeroAlloc.Serialisation | Error    | [ValueObject] type is generic, nested in a generic type, or file-local
ZASZ006 | ZeroAlloc.Serialisation | Warning  | MemoryPack formatter for a [ValueObject] in a private or protected type cannot be registered
