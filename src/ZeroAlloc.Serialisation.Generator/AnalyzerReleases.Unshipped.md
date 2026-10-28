; Unshipped analyzer release.
; https://github.com/dotnet/roslyn-analyzers/blob/main/src/Microsoft.CodeAnalysis.Analyzers/ReleaseTrackingAnalyzers.Help.md

### New Rules

Rule ID | Category                | Severity | Notes
--------|-------------------------|----------|-----------------------------------------------------------------------------------
ZASZ013 | ZeroAlloc.Serialisation | Error    | [ValueObjectFactory] does not name a usable factory
ZASZ014 | ZeroAlloc.Serialisation | Error    | [ValueObject] type has no way for a generated reader to construct it
