# AGENTS.md: ZeroAlloc.Serialisation

A source generator plus thin adapters that give MemoryPack, MessagePack and System.Text.Json one
`ISerializer<T>` and a generated per-type serializer, DI registration and `ISerializerDispatcher`.
Five NuGet packages ship from one solution; consumers reference `ZeroAlloc.Serialisation` (which bundles
the generator) plus one backend package.

## Layout

- Packable, under `src/`: `ZeroAlloc.Serialisation` (abstractions, bundles the generator DLL),
  `ZeroAlloc.Serialisation.Generator` (also published standalone, `netstandard2.0` only), and the
  `MemoryPack`, `MessagePack` and `SystemTextJson` adapters. Everything under `tests/` and `samples/` is
  not packable.
- The core project multi-targets `netstandard2.1;net8.0;net9.0;net10.0;net11.0`; the three adapters drop
  `netstandard2.1` (their backends need .NET 5 or later). The `SystemTextJson*Fallback*` files are
  `Compile Remove`d on `netstandard2.1`, so public API entries for them live in the separate
  `PublicAPI.*.modern.txt` pair.
- The generator's emitted text is pinned by the `*EmissionSnapshotTests`. An intended emission change
  means updating the `Expected_*` constants in the same PR.
- `tests/ZeroAlloc.Serialisation.Benchmarks` targets `net10.0` only (its own `Directory.Build.props`
  overrides the multi-target default, so BenchmarkDotNet's generated project restores) and has
  `TreatWarningsAsErrors=false`. `samples/ZeroAlloc.Serialisation.AotSmoke` targets `net11.0` only.
- `Directory.Build.props` fails `dotnet pack` with `ZAVERSION001` or `ZAVERSION002` unless the version was
  passed as `-p:Version` or `-p:PackageVersion`, and the built assemblies carry that same version.
  Build, test and publish are not affected.

## Build and test

SDK: `global.json` pins `11.0.100` with `rollForward: latestMinor`; the repo needs the .NET 11 SDK.
Test projects target `net10.0` and `net11.0`, so running both needs the .NET 10 and .NET 11 runtimes.
The AOT smoke publish needs `clang` and `zlib1g-dev`.

CI job `build` in `.github/workflows/ci.yml`:

```bash
dotnet tool restore
VERSION=$(dotnet gitversion /showvariable SemVer)
dotnet restore
dotnet build --no-restore -c Release -p:Version=$VERSION
dotnet test --no-build -c Release --verbosity normal --collect "XPlat Code Coverage" --settings coverage.runsettings --results-directory TestResults
```

The same job then runs the `coverage-gate` action from ci-baseline on
`TestResults/**/coverage.cobertura.xml`: it merges the reports, fails under 80 percent line coverage, and
checks `codecov.yml`. The commands above do not reproduce that gate. The job ends by packing the five
packages with `dotnet pack <csproj> --no-build -c Release -p:PackageVersion=$VERSION -o ./artifacts`.

CI job `aot-smoke` in `ci.yml`:

```bash
dotnet restore samples/ZeroAlloc.Serialisation.AotSmoke/ZeroAlloc.Serialisation.AotSmoke.csproj
dotnet publish samples/ZeroAlloc.Serialisation.AotSmoke/ZeroAlloc.Serialisation.AotSmoke.csproj -c Release -o ./aot-out
timeout --kill-after=10s 120s ./aot-out/ZeroAlloc.Serialisation.AotSmoke
```

Benchmarks, `.github/workflows/benchmarks.yml`: the `smoke` job runs the org's shared
`benchmark-smoke.yml` on `tests/ZeroAlloc.Serialisation.Benchmarks`. The manual `full` job runs:

```bash
dotnet build tests/ZeroAlloc.Serialisation.Benchmarks -c Release
dotnet run --project tests/ZeroAlloc.Serialisation.Benchmarks -c Release --no-build -- --filter "${FILTER:-*}"
```

Fast inner loop, verified locally with the .NET 11 SDK and the .NET 10 runtime, no CI job runs these:

```bash
dotnet build tests/ZeroAlloc.Serialisation.Generator.Tests -c Release -f net10.0 -p:Version=0.0.1
dotnet test tests/ZeroAlloc.Serialisation.Generator.Tests -c Release -f net10.0 --no-build --filter "FullyQualifiedName~SerializerDiagnosticTests"
dotnet build tests/ZeroAlloc.Serialisation.Tests -c Release -f net10.0 -p:Version=0.0.1
dotnet test tests/ZeroAlloc.Serialisation.Tests -c Release -f net10.0 --no-build --filter "FullyQualifiedName~HotPathAllocationTests"
```

## Conventions and gotchas

- `TreatWarningsAsErrors` and `EnforceCodeStyleInBuild` are on, and `RS0016` and `RS0017` always fail the
  build. A new or removed public symbol in `ZeroAlloc.Serialisation` needs its `PublicAPI.Unshipped.txt`
  line, or `PublicAPI.Unshipped.modern.txt` for the files not compiled on `netstandard2.1`. Do not edit the
  `*.Shipped*` files or `AnalyzerReleases.Shipped.md`: the `ship-release-tracking` job moves entries there
  on the release PR. A new generator diagnostic goes in `AnalyzerReleases.Unshipped.md`.
- The generator targets `netstandard2.0` with `EnforceExtendedAnalyzerRules`, so no APIs newer than
  `netstandard2.0` in `src/ZeroAlloc.Serialisation.Generator`. Its emitted code is the hot path: it must
  stay free of LINQ, reflection and allocation, and carries `UnconditionalSuppressMessage` attributes, not
  `#pragma`, for IL trim and AOT warnings.
- The hot-path budgets: serializing into a reused `IBufferWriter<byte>` allocates 0 B in all three
  formats, and deserializing allocates exactly what the raw library does. `HotPathAllocationTests` and
  the dispatcher tests assert these with `AllocationGate`; keep them exact when changing an adapter.
- The AotSmoke sample sets `WarningsNotAsErrors` for `IL2104` and `IL3053`. These are assembly-level
  rollups raised against MemoryPack and MessagePack binaries, so this is a deliberate exception to the
  org rule against blanket suppression, not a leftover. Leave it unless those backends stop raising them.
- Adding a package or a backend to the AOT smoke means referencing it in the sample with
  `SetTargetFramework=net11.0` and `UndefineProperties=PublishAot`, as the existing references do;
  otherwise `NETSDK1207` fires on the `netstandard2.1` and `netstandard2.0` projects.
- Benchmarks here target `net10.0` only, a deviation from the org rule of all LTS frameworks, because
  BenchmarkDotNet's generated project must restore.

<!-- org-core:start sha256=8faf417fa86f11bd8ad9bc98dfa4683fba44e440291e84c6cb9b0ec3ca0c0e7e (generated from docker-nuc scripts/agents-md/org-core-zeroalloc.md; do not edit here) -->
## Org rules

These rules hold in every ZeroAlloc-Net repo. Each library makes two promises: it works under
NativeAOT and trimming, and its hot path allocates nothing. The 2026-10-11 sweep found 16 of 28 repos
allocating on the hot path, most with docs claiming 0 B.

### Hot path

The operation a consumer runs repeatedly: `Send`, a cache hit, `Validate`, `Map`, `Serialize`, a
resilience call, a state transition, an append, a worker poll, including the idle poll. Startup and
registration are not hot path. For a source generator, the hot path is the code it emits: judge the
snapshots and the generated output.

### Allocation traps the sweep found

| Pattern | Instead |
|---|---|
| `async ValueTask` that only awaits and returns | `return new ValueTask<T>(inner.XAsync(ct));` when the method only forwards a `Task<T>` (return a forwarded `ValueTask<T>` directly). The wrapper stores a synchronous throw in the returned task; the direct return lets it, and the cancellation status of the callee, reach the caller. Keep the `async` wrapper when callers depend on either |
| `Task.WhenAll` over tasks that already completed | skip the await only when every task has `IsCompletedSuccessfully` (`Status == TaskStatus.RanToCompletion` on `netstandard2.0`); otherwise `await Task.WhenAll`, which also reports faults of completed tasks |
| `TagList` or log `params object[]` built before `Enabled`/`IsEnabled` | check first, or `LoggerMessage.Define` |
| event args allocated before checking for subscribers | copy the handler to a local, check, then allocate |
| `new Serializer()` / `new Utf8JsonWriter()` per call | cache stateless instances, reuse writers with `Reset` |
| `GetString()` / `ToString()` round-trips in JSON converters | parse and format through spans |
| `ToArray()` / `ToList()` copies between layers | pass `ReadOnlyMemory<T>` or spans, copy once at most |
| LINQ in emitted code | emit `for` loops |
| `yield` iterator behind a public `GetEnumerator` | public struct enumerator, from the first release |
| `Enum.IsDefined(typeof(E), v)` with a boxed value, `decimal.GetBits(v)` to read the scale | `Enum.IsDefined<E>(v)` when `v` has type `E` (.NET 5 or later, so not on .NET Framework, `netstandard2.0`, `netstandard2.1` or .NET Core 3.1); `v.Scale` for a scale read only (.NET 7 or later, never for the coefficient or sign). Keep the old call on older targets |
| configuration parsed or read per call | parse once and cache |
| an `AsyncLocal` write per call | pass state explicitly |
| a capturing lambda or method-group delegate per call | cache the delegate in a field, or a static lambda |
| a per-key object that is never removed | remove on release; otherwise it is a leak |

Inherent allocations are fine when documented: the consumer's own result, an exception on a failure
path, a one-time cache fill.

### Tests and benchmarks

- Every hot path has an allocation gate test (`AllocationGate` from ZeroAlloc.TestHelpers) that measures
  the whole public call, including any state setup.
- Budgets are exact. If a path allocates, assert that number and comment what it is.
- Test the generated code, not a hand-written stand-in, and cover every shape: each attribute, nested
  collections, and the asynchronous case with a fake that returns pending tasks.
- Benchmarks run in CI through the shared `benchmark-smoke.yml` and target the library's LTS
  frameworks. The benchmark body allocates nothing besides the library itself. Run locally with
  `--inProcess`.
- Publish exact allocations and same-run ratios against a `Baseline`, never absolute timings. Run with
  `--launchCount 3`, pin CPUs with `taskset`, give the spread, and label every table.

### NativeAOT and trimming

- Packable runtime projects, and projects consumers publish, set `IsAotCompatible=true` on modern
  target frameworks. Analyzers and source generators target `netstandard2.0`.
- No reflection or runtime code generation on a supported path. An opt-in reflection fallback carries
  honest `[RequiresUnreferencedCode]` / `[RequiresDynamicCode]`. Every suppression must be true;
  remove dead ones.
- The AOT smoke sample publishes with `TreatWarningsAsErrors=true`. If a third-party warning really
  blocks it, use a narrow, justified suppression for that warning. `WarningsNotAsErrors` selects codes,
  not assemblies, so it would also downgrade the same warning from generated code.
- The smoke runs the hot path of every package in the repo. A package without a smoke has unknown AOT
  status. Adapters run against real services in CI service containers; publish-only is a last resort.
  App and template smokes make authenticated requests.

### Docs and shipping

- Docs match measurements. After a hot-path change, re-measure and update `docs/performance.md` and the
  README. Never write "0 B" without a test that asserts it.
- New public API is `feat:`, even for a performance change. Public API changes are additive: never add
  an optional parameter to a shipped signature. A breaking end state goes in a `next-major` issue.
- A behaviour change gets a `## Behaviour change` section in the PR body.
- Conventional Commits with release-please. Subjects start lowercase, body lines stay within 100
  characters, and a body never contains nested parentheses: release-please drops the whole commit.
- Local packs use `9.9.9-local.<sha>`. After a release, verify the package on NuGet.
- Fix review findings in the PR; file an issue only for what cannot go in it.

Full rules, template wiring lessons and the reasons behind each rule: https://github.com/ZeroAlloc-Net/.github/blob/main/AGENTS.md
<!-- org-core:end -->
