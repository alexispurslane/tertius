# Historical C# references for direct TCC and depth

Measured on 2026-10-30 for [ter-1k3c](../../../.tickets/ter-1k3c.md).

These are selected reference cases from established C# projects. Stable releases
published before 2021-01-01 provide a comparison period before widespread AI
coding assistants. The dates do not prove that every line was written without
automation. Reputation and maturity do not establish that every type has a good
design. This sample does not estimate the distribution of all good C# code.

## What the numbers mean

Direct TCC (tight class cohesion) is the number of eligible method pairs that
directly access a shared instance field, divided by all eligible method pairs.
Each pair belongs to one type. Depth is weighted method count (WMC), the sum of
counted function cyclomatic complexity, divided by declared API entries. An API
entry is a member accessible outside its declaring type, including internal and
protected members. See the [exact counting rules](../../../tools/README.md#type-coherence-and-source-evidence-ter-dyss).

SLOC means source lines of code with active parsed C# tokens. It excludes
comments and disabled conditional text. Cyclomatic complexity counts branches
plus one under the counter's rules, including its rule that a switch counts once.

Both project ratios pool their absolute components before division. They do not
average type ratios. TCC gives more weight to types with more possible method
pairs. Depth includes WMC from types that have no API entries. An empty
denominator is not applicable; a defined zero remains zero.

## Pooled project measurements

<!-- pooled-table -->

| Project | Files / SLOC / types | Direct TCC: shared/possible pairs | Depth: WMC/API entries | Compiler errors |
|---|---:|---:|---:|---:|
| Dapper 2.0.78 | 48 / 5,984 / 61 | 789/1,549 = 0.5094 | 1,526/506 = 3.0158 | 0 |
| Serilog v2.10.0 | 99 / 5,961 / 101 | 58/3,500 = 0.0166 | 1,217/690 = 1.7638 | 0 |
| Noda Time 3.0.3 | 175 / 15,028 / 259 | 412/3,674 = 0.1121 | 3,332/2,261 = 1.4737 | 379 |
| Newtonsoft.Json 12.0.3 | 229 / 29,820 / 244 | 1,809/8,946 = 0.2022 | 6,074/2,288 = 2.6547 | 43 |
| NUnit v3.12 | 417 / 21,217 / 481 | 836/2,917 = 0.2866 | 4,553/2,714 = 1.6776 | 4 |
| Autofac v6.0.0 | 236 / 11,923 / 232 | 191/1,105 = 0.1729 | 2,006/1,230 = 1.6309 | 124 |
| Tertius (engine) | 379 / 55,783 / 974 | 1,887/10,179 = 0.1854 | 10,122/4,771 = 2.1216 | 3,344 |

<!-- /pooled-table -->

The Tertius row is the engine at `bf5bf784`, measured with the same counter and
metadata references. Its 3,344 binding errors limit semantic comparisons.
Library roles and an engine's mix of types also differ.

Across these six selected releases, project TCC ranges from `0.0166` to
`0.5094`, with a median of `0.1875`. Project depth ranges from `1.4737` to
`3.0158`, with a median of `1.7207` complexity units per API entry. Each project
has one vote in these medians. Tertius's `0.1854` TCC is near that sample median;
its `2.1216` depth is above the sample median and within the observed range.
These comparisons establish numerical context, not relative code quality.
In particular, NUnit and Newtonsoft.Json do not represent historical build
configurations. Six deliberately selected projects cannot validate quality
thresholds or confidence bounds for a wider population.

## Per-type distributions

Each applicable type has equal weight in this table. P10, P25, P50, P75 and P90
are the 10th, 25th, 50th, 75th and 90th percentiles. We interpolate linearly at
zero-based position `(n - 1) * p` in the sorted values, where `n` is the number
of applicable types and `p` is the percentile as a fraction. Undefined ratios
are omitted, and defined zeros remain. P50 is the median.

<!-- quantile-table -->

| Project | Metric | Applicable types | P10 | P25 | P50 | P75 | P90 |
|---|---|---:|---:|---:|---:|---:|---:|
| Dapper 2.0.78 | tcc | 12 | 0.0167 | 0.1667 | 0.3988 | 0.8000 | 1.0000 |
| Dapper 2.0.78 | depth | 59 | 0.0000 | 0.4500 | 1.2500 | 2.5000 | 5.6400 |
| Serilog v2.10.0 | tcc | 27 | 0.0000 | 0.0000 | 0.0083 | 0.7679 | 1.0000 |
| Serilog v2.10.0 | depth | 101 | 0.0000 | 1.0000 | 1.6667 | 3.0000 | 6.8000 |
| Noda Time 3.0.3 | tcc | 88 | 0.0000 | 0.0000 | 0.0357 | 0.3500 | 1.0000 |
| Noda Time 3.0.3 | depth | 247 | 0.0000 | 0.7250 | 1.0000 | 2.0000 | 3.5667 |
| Newtonsoft.Json 12.0.3 | tcc | 57 | 0.0000 | 0.0000 | 0.1667 | 1.0000 | 1.0000 |
| Newtonsoft.Json 12.0.3 | depth | 227 | 0.0000 | 0.0000 | 1.0000 | 2.6667 | 5.3333 |
| NUnit v3.12 | tcc | 115 | 0.0000 | 0.0000 | 0.0000 | 0.8028 | 1.0000 |
| NUnit v3.12 | depth | 456 | 0.0000 | 1.0000 | 1.2000 | 2.2860 | 3.9118 |
| Autofac v6.0.0 | tcc | 53 | 0.0000 | 0.0000 | 0.1212 | 1.0000 | 1.0000 |
| Autofac v6.0.0 | depth | 227 | 0.0000 | 0.2917 | 1.3750 | 2.3333 | 3.2912 |
| Tertius (engine) | tcc | 208 | 0.0000 | 0.0000 | 0.3258 | 1.0000 | 1.0000 |
| Tertius (engine) | depth | 870 | 0.0000 | 0.0000 | 0.5000 | 1.5649 | 3.5000 |

<!-- /quantile-table -->

These distributions describe frequency, not acceptable quality. The project
ratio and the median type answer different questions. For example, Serilog's
`Logger` alone contributes 3,160 of 3,500 possible pairs (90.29%); Dapper's two
reader types contribute 1,444 of 1,549 (93.22%). A small number of large method
surfaces can dominate pooled TCC.

## Source examples that constrain interpretation

| Project and type | Direct TCC | Depth | What source inspection shows |
|---|---:|---:|---|
| Noda Time `LocalDate` | `0/300 = 0` | `86/65 = 1.3231` | One date/calendar value uses properties and helpers. Direct field sharing misses this coherent responsibility. |
| Noda Time `Cache<TKey,TValue>` | `1/1 = 1` | `9/5 = 1.8` | Two cache operations share storage. Maximum TCC rests on just one pair. |
| Dapper `DbWrappedReader` | `741/741 = 1` | `52/48 = 1.0833` | Repeated forwarding through shared fields gives maximum TCC. It does not establish deep behavior. |
| Dapper `SqlMapper.GridReader` | `0/703 = 0` | `120/42 = 2.8571` | Reader operations use private helpers, outside direct field sharing. |
| Dapper `BasicWrappedReader` | Not applicable | `36/1 = 36` | Explicit interface implementations add WMC but no API entries under this counter. A high value can reflect counting conventions. |
| Serilog `Logger` | `12/3160 = 0.0038` | `114/84 = 1.3571` | Convenience overloads call helpers. This large method surface dominates pooled TCC. |
| Newtonsoft.Json `TraceJsonWriter` | `1326/1378 = 0.9623` | `73/54 = 1.3519` | Many overloads forward to the same two writers, producing many shared pairs. |
| Newtonsoft.Json `JsonTextReader` | `6/45 = 0.1333` | `340/15 = 22.6667` | A small interface hides parsing and buffering. Its WMC also includes branch complexity, which still needs erosion review. |
| NUnit `ConstraintExpression` | `10/1326 = 0.0075` | `77/77 = 1` | Constraint-building methods call helpers. Low direct TCC coexists with one expression-building responsibility. |
| NUnit `NUnitTestCaseBuilder` | Not applicable | `47/2 = 23.5` | One private signature-checking method has CC 35. High depth can include concentrated branching. |
| Autofac `RegistrationBuilder<TLimit, TActivatorData, TRegistrationStyle>` | `0/528 = 0` | `63/38 = 1.6579` | Registration methods use shared configuration through properties. None directly accesses a field. |
| Autofac `ComponentRegistry` | `21/21 = 1` | `12/13 = 0.9231` | Shared forwarding to the service tracker produces maximum direct TCC with little implementation per API entry. |

Exact source locations and type contributions are retained in each project's
JSON file. Its `repositoryUrl` and `sha` identify the source version. External project locations
have the normalization prefix `src/`; remove that one prefix to recover the
original upstream path. For example:

- [Noda Time LocalDate](https://github.com/nodatime/nodatime/blob/930e76086b19c5c250ffaf6c7b3423a63366f463/src/NodaTime/LocalDate.cs).
- [Dapper reader wrappers](https://github.com/DapperLib/Dapper/blob/6ab170c160f0689e2e2bf7e6eb603447f734d454/Dapper/WrappedReader.cs).
- [Serilog Logger](https://github.com/serilog/serilog/blob/24b67c6cb4535ff13095a7f8338070917d7bf550/src/Serilog/Core/Logger.cs).
- [Newtonsoft.Json TraceJsonWriter](https://github.com/JamesNK/Newtonsoft.Json/blob/7c3d7f8da7e35dde8fa74188b0decff70f8f10e3/Src/Newtonsoft.Json/Serialization/TraceJsonWriter.cs).
- [NUnit ConstraintExpression](https://github.com/nunit/nunit/blob/c933a883ea25263ec575a654dae69fa38000edd9/src/NUnitFramework/framework/Constraints/ConstraintExpression.cs).
- [Autofac ComponentRegistry](https://github.com/autofac/Autofac/blob/39696a967e8826f7f1ebc8c1ff4523c9dd75abe0/src/Autofac/Core/Registration/ComponentRegistry.cs).

These examples support role-aware source review. They do not establish that a
specific range is good or bad. A depth increase needs evidence that useful
behavior moved behind a usable interface. A TCC increase needs evidence that
responsibilities became more coherent. Inspect erosion separately: adding
branches can raise depth while making functions harder to understand.

## Scope and limitations

Only each project's core production assembly is included. Tests, examples,
benchmarks, tooling, vendored dependencies and generated source are excluded.
The source bytes and original paths are preserved under `input/src/`.
No project was built or restored. No historic target symbols, package metadata
or generated build output were supplied. The counter uses current runtime
metadata and its standard implicit imports. Matching counter provenance is
necessary for comparison, but does not reproduce a historical build.

| Project | Main comparison limit |
|---|---|
| Dapper | Zero binding errors, but historical target symbols are absent. Explicit interface implementations can inflate measured depth. Two reader types dominate TCC. |
| Serilog | Zero binding errors, but historical target symbols are absent. `Logger` dominates TCC; helper forwarding contributes little direct sharing. |
| Noda Time | All 379 binding errors concern excluded JetBrains annotations. Seven conditional directives in two files use `DEBUG`. The comparison is structurally useful with these qualifications. |
| Newtonsoft.Json | Forty errors concern excluded LinqBridge imports; three concern conditional factories or an ambiguous name. More importantly, 548 conditional directives in 108 files depend on missing project symbols. This source-only baseline is not an actual historical target. |
| NUnit | Two active `#error` directives and two missing conditional names produce four compiler errors. Missing target symbols disable 5,926 nonblank lines across 82 files, including comments. These are not SLOC, and this parse is not a historical target. The scope includes two linked assembly-information files. |
| Autofac | All 124 errors name resources from 49 excluded generated files. Missing target symbols disable 95 nonblank lines across two files, including comments. Modern runtime metadata replaces historical package metadata. |
| Tertius | The engine has 3,344 binding errors with default runtime references. Missing engine dependencies and generated symbols limit semantic certainty. |

The counter's `bindingErrorCount` includes all reported compilation errors,
including NUnit's two preprocessor `#error` diagnostics. A small count does
not measure the amount of inactive conditional source.

Zero binding errors do not prove that all historical code is active. A wholly
inactive conditional branch produces no binding error. Do not silently alter
symbols or references to repair one baseline and compare it with the others.
An improved build-aware experiment would need a separately recorded protocol.

External erosion and verbosity output is not used as a whole-repository
benchmark: the copied population omits tests and other callers, which can
affect usage rules. The [Earendil article](https://earendil.com/posts/measuring-code-sloppiness/)
supplies the original erosion/verbosity context; the C# adaptations and source
populations limit numerical comparisons with it.

## Reproduction and retained evidence

| Counter input | Value |
|---|---|
| Implementation commit | `bf5bf784de00be64bf6633421ad1f02771044046` |
| Original binary SHA256 | `6793bdd1941db4e10cafca7c4e63ca0d887a818fe755b0903c32ec738ccf05bc` |
| Counting version | `type-metrics-v2` |
| Metadata reference fingerprint | `6756909B5E79A0B8E806995EF6661640C81D1DB2BA5FCD39E4AA254CE606436B` |
| SDK | `11.0.100-rc.1.26425.128` |
| Runtime | `11.0.0-rc.1.26425.128`, `osx-arm64` |

Each project JSON retains the archive URL and SHA256, exact commit, release
date and evidence URL, C# inclusion/exclusion manifest with source hashes,
summary, diagnostic counts, quantiles, and primitive counts for every type.
It also records the original raw-report hash. Third-party source and large
member/pair detail arrays are not committed here. Full raw reports and
research notes remain under `/tmp/ter-1k3c-calibration/` on the research machine;
they are temporary working artifacts, not required to interpret the saved
counts. Tertius's JSON retains type counts and locations at the pinned commit.

Saved datasets: [Dapper](dapper.json), [Serilog](serilog.json),
[Noda Time](nodatime.json), [Newtonsoft.Json](newtonsoft.json),
[NUnit](nunit.json), [Autofac](autofac.json), and [Tertius](tertius.json).

To reconstruct an external input, download the project's `archiveUrl`, verify
`archiveSha256`, and copy exactly the included `sourceManifest` entries from
the archive root into `input/src/<path>`, verifying each `sha256`. Do not edit
the copied files. From the pinned Tertius checkout, build the counter and run:

```sh
nix develop --command dotnet build tools/Tertius.Slop -c Release
nix develop --command env -u TERTIUS_SLOP_REFERENCE_LIST dotnet tools/Tertius.Slop/bin/Release/net11.0/Tertius.Slop.dll all /absolute/path/to/input
```

Here `/absolute/path/to/input` is the reconstructed directory containing `src`.
`TERTIUS_SLOP_REFERENCE_LIST` is the optional external metadata-reference list;
unsetting it selects the counter's default runtime references. Confirm the
reported counting version and reference fingerprint before comparing results.
A different architecture or toolchain can change the reference fingerprint.
The original binary hash identifies the measured artifact; a rebuilt binary
need not be byte-identical.

Validation independently recomputed pooled sums, all five per-type quantiles,
diagnostic counts and copied source hashes from completed raw reports. Archive
hashes were also checked. This validates the retained arithmetic and inputs;
it does not validate an automatic quality threshold.
