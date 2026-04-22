; Unshipped analyzer release
; https://github.com/dotnet/roslyn-analyzers/blob/main/src/Microsoft.CodeAnalysis.Analyzers/ReleaseTrackingAnalyzers.Help.md

### New Rules

Rule ID | Category | Severity | Notes
--------|----------|----------|------
ZASZ001 | ZeroAlloc.Serialisation | Error   | [ZeroAllocSerializable] cannot be applied to an open generic type
ZASZ002 | ZeroAlloc.Serialisation | Error   | Unknown SerializationFormat value
ZASZ003 | ZeroAlloc.Serialisation | Warning | Missing per-format attribute (MemoryPackable / MessagePackObject)
