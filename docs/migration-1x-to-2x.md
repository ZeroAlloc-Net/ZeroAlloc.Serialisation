---
id: migration-1x-to-2x
title: Migrating 1.x → 2.x
slug: /docs/migration-1x-to-2x
description: One change — stop referencing the standalone generator package — and the two build errors you hit if you do it in the wrong order.
sidebar_position: 9
---

# Migrating 1.x → 2.x

There is one change to make, and it is a subtraction.

**Reference `ZeroAlloc.Serialisation` and nothing else for the generator.** Since 2.0 it ships the source generator itself, wired up automatically.

```xml
<!-- 2.x -->
<PackageReference Include="ZeroAlloc.Serialisation" Version="2.*" />
<PackageReference Include="ZeroAlloc.Serialisation.MemoryPack" Version="2.*" />
```

Delete both of the following if you have them:

```xml
<!-- 1.x — remove both -->
<PackageReference Include="ZeroAlloc.Serialisation.Generator" Version="1.*"
                  OutputItemType="Analyzer" ReferenceOutputAssembly="false" />

<Analyzer Include="$(PkgZeroAlloc_Serialisation_Generator)\lib\netstandard2.0\ZeroAlloc.Serialisation.Generator.dll" />
```

## Why there were two things to remove

1.x shipped the generator at the wrong path — `lib/netstandard2.0/` rather than `analyzers/dotnet/cs/`. NuGet treats `lib/` as a normal library reference, so the generator never ran on its own and consumers hand-wired it with an `<Analyzer Include=…>` pointing into the package folder.

2.x fixed the layout. That fix is what makes both removals necessary, and doing them one at a time produces two different errors:

| you removed | error | why |
|---|---|---|
| nothing yet, just upgraded | `CS0006: Metadata file '…\lib\netstandard2.0\ZeroAlloc.Serialisation.Generator.dll' could not be found` | the hand-wired path no longer exists in 2.x |
| only the `<Analyzer Include=…>` | `CS0101` / `CS0111` on generated types | both packages now ship the generator under `analyzers/dotnet/cs/`, so it loads twice |

The second is the one worth understanding. Referencing `ZeroAlloc.Serialisation` **and** `ZeroAlloc.Serialisation.Generator` at 2.x gives Roslyn the same generator twice, and it emits everything twice:

```
CS0101  The namespace already contains a definition for 'WeatherResponseSerializer'
CS0111  'SerializerDispatcher' already defines a member called 'Serialize'
CS0111  'SerializerServiceCollectionExtensions' already defines 'AddSerializerDispatcher'
```

Remove both at once and neither error appears.

## Verifying

The generator is running when your `[ZeroAllocSerializable]` types have a generated `…Serializer` and `AddSerializerDispatcher()` resolves. If you want to see the emitted files directly:

```xml
<PropertyGroup>
  <EmitCompilerGeneratedFiles>true</EmitCompilerGeneratedFiles>
</PropertyGroup>
```

They appear under `obj/<Configuration>/<TFM>/generated/`. Seeing each type **once** confirms the double-load is gone.

## Is `ZeroAlloc.Serialisation.Generator` still needed?

No. It stays published so existing direct references keep resolving, but nothing needs it: the generator ships inside `ZeroAlloc.Serialisation`. New projects should never reference it, and upgrading projects should drop it.
