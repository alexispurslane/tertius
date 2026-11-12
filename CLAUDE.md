# Tertius: notes for Claude

ARCHITECTURE.md is the binding design, and the code and `tools/README.md` say how things work. This file holds only what they do not show: the user's rules for this project, the places where my first instinct was wrong, and the gotchas that cost time before.

## Workflow

- Run every build and test command in the dev shell: `nix develop --command bash -c '<command>'`.
- `bash tools/test-all.sh` runs every .NET test module in Checked and Release. Checked is the test configuration: Debug's contracts with Release's optimized code. An agent in an isolated worktree cannot run it, because Claude Code refuses a script there: it cannot prove that the script runs no git outside the worktree. Such an agent runs `nix develop --command dotnet test --solution Tertius.slnx -c Checked`, then the same with `-c Release`. Each command builds and runs every test project in about two minutes.
- The pre-commit hook runs only the Checked tests. Run the Release tests at least once before a merge into main. Run them with `tools/test-all.sh` or with `-c Release`.
- The hook skips a test project whose full Checked run passed on exactly the staged code (tools/README.md, "The test record"). So run the full Checked tests just before a commit, and the commit does not run them again.
- Run at most four agents that build at once (user, 2026-10-25). The global hook `~/.claude/hooks/heavy_queue.py` allows four heavy runs. On 2026-10-24, two agents brought the load of this 10-core machine to about 23. When four agents build together, each build and test runs slower.
- Do each ticket or epic on its own branch, named after the ticket, such as `ter-dtw2-vertical-slice`. Never commit work straight to main. The user reads the code in the diff pane of the Claude desktop app, against main.
- When the ticket closes, merge its branch into main in this checkout, and leave the checkout on main. Other sessions commit ticket notes in this checkout, and the user wants those on main.
- Use git, not jj. This overrides the jj rule in the user's other agent configuration.
- Push main to `origin` without asking, at least after each epic merges. The user decided on 2026-10-24 that a regular push is better than none.
- `origin` fetches from GitHub, the primary home since 2026-11-12. It pushes to GitHub, then to the knot, a push mirror (tools/README.md, "The remotes: GitHub and the knot mirror"). If a push fails on one URL, push again.
- The last step of every ticket that changes the engine updates ARCHITECTURE.md to match the code, before the merge. It is part of the public engine docs.
- At the end of every piece of work that shows on screen, send the user two to four screenshots of the rendered game. The user often follows from a phone and cannot run the game. Take them with a headless run of the real renderer, `dotnet run --project src/Tertius.Demo -- --script scripts/<name>.json --headless --out <folder>`. Look at each image first, and give each a one-line caption.
- Read the Jolt pitfalls in project memory, `~/.claude/projects/-Users-alexispurslane-Development-tertius/memory/tertius-jolt-pitfalls.md`, before you touch Jolt interop. A subagent gets no memory, so it reads that file. The double-precision Jolt library comes from `native/joltc.nix`.

## Style

[STYLE.md](STYLE.md) holds the C# style of `src`, with the priority order of performance and consistency. [tests/STYLE.md](tests/STYLE.md) holds the style of the tests. Read both before you write code.

## Design preferences

- Prefer simple, honest mechanisms over clever ones. Leave out a feature that is not needed yet, and say so.
- Prefer the real mechanism over a look-alike. If the real one needs an engine epic, the epic comes first. Example: the demo's exhaust plumes waited for real particles instead of glow meshes.
- Keep one store for each piece of state. A second store beside it, such as a side table, a mirror row or a mode enum, can disagree with the first.
- Stored state describes the last finished step, like the collision rows. A step in flight is a named, derived read of the pending intent.
- Automatic machinery lives in one choke-point method on the owner of the state, which calls internal pure predicates and acts directly. Use no planner and no plan object.
- Use no command buffer for deferred changes. Only creation and destruction wait for the sync point.
- An entity writes only itself, relative to `self.Frame`. Entities talk to each other only through messages.
- The user values determinism and wants a reason for every ordering rule.
- No multiplayer and no time warp, ever. Mods come later.
- The game must run on regular Vulkan on Linux, not only on MoltenVK on this Mac (user, 2026-10-25). Write to the Vulkan spec. Never rely on a behavior that only MoltenVK or Metal gives. Two examples are a half float blend that stops at 65,504 and a `clamp` that turns NaN into 0. A required device feature must be one that the desktop Linux drivers (NVIDIA, RADV, ANV) and lavapipe offer. Every reviewer checks this.

## Gotchas

- The pre-commit hook builds and tests the changed modules, and that takes minutes. A full Checked run that passed on the staged code just before the commit skips it. Its report of complex, long or duplicated code asks you to refactor the touched code in the same commit.

## Interpreting code quality metrics

Use these numbers to choose code to inspect and to explain a change. A refactoring improves design when it reduces caller knowledge or the places that must change together. A better number alone does not establish that improvement. Here, "class" means any measured type.

CC is cyclomatic complexity, the tool's count of branches plus one. SLOC is source lines of code. Function mass is `CC * sqrt(SLOC)`. WMC is the sum of CC across a type's handwritten functions, including private functions. An API entry is one declared member accessible outside its type, including internal and protected members. TCC pairs declared non-private ordinary instance methods within types that have instance state. Both methods must reach the same storage on this instance to count as sharing. The counter follows fields, property accessors and statically resolvable same-instance helpers, including helper chains and recursion. Private helpers supply storage access without adding eligible method pairs. [The counting rules](tools/README.md#type-coherence-and-source-evidence-ter-dyss) define eligibility and exclusions.

| Metric | Meaning and numerical anchors | How to judge it |
|---|---|---|
| Erosion | Mass in functions with CC above 10, divided by all function mass. `0.20` means 20% of measured mass is in those functions. | Lower is desirable when the same work becomes easier to understand. Inspect touched complex functions and their absolute mass. Adding simple code can lower the ratio without improving them. |
| Verbosity | Distinct rule-flagged or cloned lines divided by project SLOC. `0.03` means three flagged lines per 100 source lines. | Lower is desirable when unnecessary code disappears. Inspect the findings and absolute flagged-line count. Removing useful clarity to satisfy a heuristic is not an improvement. |
| TCC, tight class cohesion | Shared instance-storage method pairs divided by possible eligible pairs, pooled across complete applicable types. `0` means no sharing, `0.5` means half the pairs share, and `1` means all share. | Aim for `1`: every eligible method pair shares instance storage. Inspect gaps for unrelated responsibilities. Sharing one context field or forwarding through one dependency can give unrelated operations high TCC. Read excluded populations alongside the ratio. |
| Depth | Pooled WMC divided by pooled API entries from types with handwritten implementation. `2` means two counted complexity units per API entry. It is neither lines per method nor a percentage. | Aim for `> 2`: useful implementation behind a small usable interface. Inspect lower values for exposed bookkeeping or shallow wrappers. Declaration-only payloads have no applicable depth score. Extra branching also raises depth, so inspect erosion separately. |

### Preferred design targets

Our design goals are `TCC = 1` and `depth > 2` (user, 2026-10-30). Use these goals for applicable types and pooled project statistics. The v3 counter recognizes properties and resolvable helpers on the same instance. These scores better reflect common C# ways of expressing shared state. Use them as practical design signals with more confidence than the earlier direct-field counter.

For complete, applicable measurements below a goal, inspect the type and its callers for a concrete improvement. Favor a coherent responsibility and useful behavior behind a small interface. A justified exception can remain below a goal: record why its role and callers support the design. Not-applicable and incomplete measurements do not fail a goal.

Keep depth and erosion separate. Erosion checks excessive implementation complexity even when depth exceeds `2`. Do not add branches or artificial shared-state access to reach a target. These are project design preferences, not cutoffs validated by the reference sample. Automatic hook failure thresholds require a separate decision.

The [Earendil article](https://earendil.com/posts/measuring-code-sloppiness/) reports erosion of `0.31 ± 0.17` for established repositories and `0.68 ± 0.20` for agent code. It reports verbosity of `0.15 ± 0.06` and `0.33 ± 0.10`, respectively. These describe its samples. Our C# syntax rules, switch counting and source population limit cross-project comparisons, especially for verbosity. They are context, not pass/fail thresholds.

### Historical C# references

The [v3 reference study](docs/research/type-metric-reference-v3/README.md) remeasures selected established C# libraries released before 2021 with the same counter and metadata references. The [v2 study](docs/research/type-metric-reference-2026-10-30/README.md) retains original values, exact revisions and source manifests. Release dates provide a comparison period before widespread AI coding; they do not prove authorship or quality. The source populations did not change. Higher ratios here reflect changed counting rules and applicability, not refactoring.

<!-- historical-reference-table -->

| Project | Pooled TCC | Pair coverage | Pooled depth |
|---|---:|---:|---:|
| Dapper 2.0.78 | 0.9734 | 50.54% | 3.2537 |
| Serilog v2.10.0 | 0.9640 | 95.38% | 2.0839 |
| Noda Time 3.0.3 | 0.4064 | 93.15% | 1.6862 |
| Newtonsoft.Json 12.0.3 | 0.7778 | 3.70% | 3.1293 |
| NUnit v3.12 | 0.9231 | 54.18% | 1.8675 |
| Autofac v6.0.0 | 0.6594 | 77.73% | 2.0181 |

<!-- /historical-reference-table -->

Pair coverage is included possible pairs divided by included plus incomplete possible pairs. It excludes structurally not-applicable types. It describes the measured population, not confidence. Newtonsoft.Json's ratio represents too small a subset to serve as a project-wide quality estimate. Runtime dispatch, unavailable source and missing build symbols can exclude substantial populations even when compiler errors are absent. The study retains per-type reasons and unresolved source locations.

The six external TCC values span `0.4064–0.9734` (median `0.8504`), and depth spans `1.6862–3.2537` complexity units per API entry (median `2.0510`). Each project has equal weight in these medians. These describe different complete subsets, not an acceptable band. TCC can be low for a coherent value type (`NodaTime.LocalDate`: `36/300`) and one for a forwarding wrapper (`Dapper.DbWrappedReader`: `741/741`). Depth can be high because explicit interface implementations add WMC but no API entries (`Dapper.BasicWrappedReader`: `36/1`). Inspect the type's role and counting rules before judging either extreme.

### Applicability and incomplete evidence

A measured zero, not applicable and incomplete are separate outcomes. A complete type with state and at least two eligible methods can have a real zero TCC. Fewer than two eligible methods or known absence of instance storage makes TCC not applicable. Unresolved relevant state access makes an otherwise eligible type incomplete. Known storage, pair counts and unresolved source locations remain available; incomplete types do not enter the project ratio. An entirely incomplete pool displays `incomplete`, not zero.

A type without handwritten functions has no implementation depth. Its raw API count remains visible but does not dilute pooled depth. A type with implementation and no API entries has no per-type depth ratio, while its WMC still contributes to the pooled numerator. Legacy records without applicability metadata remain unavailable. Version or reference mismatches suppress deltas; old notes are retained.

### Local reference examples

Remeasuring the engine source from `bf5bf784` with `type-metrics-v3` gives TCC `7525/8487 = 0.8867` and depth `10122/3894 = 2.5994`. Cohesion includes 186 types, excludes 752 as not applicable and 36 as incomplete, and retains 83.21% of the included-plus-incomplete possible pairs. Depth has 481 applicable and 493 not-applicable types. Source analysis reports 3,344 binding errors and 203 distinct unresolved state-access locations. These are separate quantities; inspect the relevant evidence before judging a type.

Erosion `0.2000` and verbosity `0.0332` remain the historical values for that unchanged engine snapshot. Its v2 TCC was `1887/10179 = 0.1854` and depth was `10122/4771 = 2.1216`. The difference is measurement, not a code-quality improvement. The examples below use v3 and the hook's default metadata references.

| Type | TCC: shared/possible pairs | Depth: WMC/API entries | What the code demonstrates |
|---|---|---|---|
| [RendererOptions](src/Tertius.Rendering/RendererOptions.cs) | `0/0`, not applicable | Raw `0/20`, not applicable | A configuration payload has useful fields and properties without handwritten function bodies. It has no implementation depth to assess. |
| [DPose](src/Tertius.Core/Math/Pose.cs) | `21/21 = 1` | `17/13 = 1.3077` | Property traversal now exposes the shared transform storage. Its v2 direct TCC was zero. |
| [ToneMapPass](src/Tertius.Rendering/Passes/ToneMapPass.cs) | `1/1 = 1` | `11/4 = 2.7500` | The pass hides shader and output selection. Its maximum TCC rests on just one eligible pair. |
| [TopicSets](src/Tertius.Simulation/Internal/Messaging/TopicSets.cs) | `51/55 = 0.9273` | `83/13 = 6.3846` | Helper traversal connects subscription and membership operations. Their storage updates stay inside the owner. |
| [EventList](src/Tertius.Simulation/Events/EventList.cs) | `36/36 = 1` | `17/14 = 1.2143` | Maximum TCC coexists with exposed storage bookkeeping. Whole-event writes can bypass mask maintenance. [ter-c8lk](.tickets/ter-c8lk.md) records the defect and proposed ownership fix. |

### Calibrating a judgment

1. Compare the same counting version and metadata references. Read applicability and population counts: retain measured zeros, distinguish not applicable from incomplete, and treat missing legacy metadata as unavailable.
2. Compare types with similar roles: stateful owners, value types, configuration payloads, or interfaces. Inspect source and callers before looking at their scores. Record a concrete reason to keep or refactor each sampled type, or mark it uncertain.
3. Then examine scores, raw counts and counterexamples against the preferred targets: TCC `1` and depth `> 2`. Sample low, middle and high values within each role. Quantiles describe frequency, not quality. Before introducing an automatic failure threshold, review examples on both sides and test it on additional types.
4. For a project change, inspect both pooled sums and the types that changed. TCC weights types by their possible pairs, which grow quadratically with eligible method count. Depth pools implementation-bearing types, including WMC from types with zero API entries. Declaration-only API counts stay outside its denominator. Neither is a vote over types. A changed mix of types can move either ratio without improving existing code.

Splitting a type can remove non-sharing pairs and raise TCC without changing its behavior. Accept such a change only when the code and its callers demonstrate a design improvement. The earlier 22-type review supplied counterexamples and refactoring tickets. It did not validate automatic TCC/depth cutoffs. The preferred targets guide review. The hooks keep both as project statistics, with no automatic type-level finding thresholds.

## Testing

- `WorldOptions.DebugChecks` runs the world's invariant checks at the end of every sync point.
- Mutation testing follows the user's decision of 2026-11-01 (ter-u63a, ter-hxca). A ticket's hardening makes one table of mutants with one result each, and a script runs every mutant. An agent never edits, builds, tests and restores a mutant by hand. tools/README.md, "Mutation testing", has the commands, the list format, the table and the settings. A hardening brief names these two commands:
  - The changed C# lines of Core, Simulation and Physics go to the official Stryker run. This one run goes to the cloud by default, one module per CPU worker, from the Mac without the dev shell: `python3 -I tools/stryker/cloud.py main <out> --agent <name>`. On the Mac it runs as `nix develop --command python3 -I tools/stryker/changed.py main <out>`. Stryker runs only on these three modules, because their tests run on the CPU only. Most of a run is Stryker's per-test coverage capture: 2,517 s of Core's 2,674 s and 3,674 s of Simulation's 3,813 s on tiunr-e8xk. The tests run slowly there because they run on the mutated assembly. So the capture stays on: with it off, each Simulation mutant ran every test on that assembly and took about 11 minutes (t9-al5u, tools/README.md, "Run time"). The run uses one test host at nice 20 with four xunit threads, because Stryker.NET 5.0.0 loses kills at a higher concurrency (tools/README.md, "Stryker.NET pitfalls").
  - Every other changed code line goes to a list of mutants that the agent chooses: `nix develop --command python3 -I tools/mutate.py <list.json> <out>`. These are the lines of Rendering (C# and GLSL), Host, Demo and `analyzers/`. The test projects of the analyzers are `tests/Tertius.Unions.Tests` and `tests/Tertius.Style.Tests`. The list also holds the official run's no-mutant rows, removed rows and listed lines without a Stryker mutant. The official run prints the changed code outside it. The script builds only the test project in Checked with a plain incremental build, runs only the listed classes under a timeout of 120 s, and restores and touches each file. A Rendering mutant takes about 40 s.
  - Each survived or no-coverage mutant gets one of three answers. The first is a test that kills it, red on the mutant first. The second is a one-sentence reason why it is equivalent. The third is a one-sentence judgment call on why the behavior is not worth a test or no test can reach it. A Stryker reason goes in a Stryker ignore comment, and a listed mutant's reason goes in the ticket note. The ticket note holds the table of both tools.
  - An official run that prints NOT VALID (too many timeouts, or a Dubious test) proves nothing. Run it again on a quieter machine. If `tools/stryker/compare.py` then shows the same timeouts, those mutants make a test hang, and the timeout share needs a new calibration.
  - Any agent, also a subagent, can run the official Stryker command. Stryker starts only through `tools/stryker/run.py`, which takes the lock `/tmp/tertius-stryker.lock`. The lock keeps Stryker runs one at a time on the machine, because two runs compete for the CPU and their tests time out.
  - While an official run on the Mac goes on, build or test no Checked test project of the same worktree, and do not commit there. Stryker copies its mutated assembly into their output, and such a build replaces it. The run then prints NOT VALID. A cloud run builds nothing in the worktree.
  - Each epic runs coverage on its changed lines before the merge. A Stryker run over the whole vertical slice took hours, so mutation testing stays on the changed lines.
- Where the tests run: only the official Stryker run goes to the cloud by default. The other test runs stay on the Mac: the pre-commit hook, the Checked and Release suites before a merge, the `tools/mutate.py` list (a GPU mutant takes about 63 s in the cloud against 40 s on the Mac), and one Mac pass of the GPU tests before a merge. An agent can send the CPU-only test projects to the cloud's CPU workers to keep them off the Mac. GPU test shards go to the cloud only on demand, when an agent is blocked on the Mac (tools/runpod/WORKBENCH.md, "Tests in the cloud").
- A snapshot test proves only that nothing changed. So the first approval of a reference image is a real review against the scene description. If an agent adds or re-approves a reference image, or sees a snapshot fail, it opens three images with the Read tool. They are the reference, the actual image and the FLIP heat map, and its report describes them.
