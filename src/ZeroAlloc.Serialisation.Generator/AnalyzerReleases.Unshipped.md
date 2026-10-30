; Unshipped analyzer release.
; https://github.com/dotnet/roslyn-analyzers/blob/main/src/Microsoft.CodeAnalysis.Analyzers/ReleaseTrackingAnalyzers.Help.md

### New Rules

Rule ID | Category                | Severity | Notes
--------|-------------------------|----------|-----------------------------------------------------------------------------------
ZASZ015 | ZeroAlloc.Serialisation | Warning  | MemoryPack does not validate a struct value object that holds no references
