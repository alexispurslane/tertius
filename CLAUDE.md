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

CC is cyclomatic complexity, the tool's count of branches plus one. SLOC is source lines of code. Function mass is `CC * sqrt(SLOC)`. WMC is the sum of CC across a type's handwritten functions, including private functions. An API entry is one declared member accessible outside its type, including internal and protected members. TCC pairs declared non-private ordinary instance methods within types that have instance state. Both methods must directly access the same field on this instance to count as sharing. [The counting rules](tools/README.md#type-coherence-and-source-evidence-ter-dyss) define eligibility and exclusions.

| Metric | Meaning and numerical anchors | How to judge it |
|---|---|---|
| Erosion | Mass in functions with CC above 10, divided by all function mass. `0.20` means 20% of measured mass is in those functions. | Lower is desirable when the same work becomes easier to understand. Inspect touched complex functions and their absolute mass. Adding simple code can lower the ratio without improving them. |
| Verbosity | Distinct rule-flagged or cloned lines divided by project SLOC. `0.03` means three flagged lines per 100 source lines. | Lower is desirable when unnecessary code disappears. Inspect the findings and absolute flagged-line count. Removing useful clarity to satisfy a heuristic is not an improvement. |
| Direct TCC, tight class cohesion | Shared instance-field method pairs divided by possible eligible pairs, pooled across types. `0` means no direct sharing, `0.5` means half the pairs share, and `1` means all share. | More sharing supports a common stateful responsibility. Low values merit inspection for unrelated responsibilities. Property access and helper calls can hide real cohesion from this measure. Sharing one context field can give unrelated operations high TCC. |
| Depth | Pooled WMC divided by pooled API entries. `2` means two counted complexity units per API entry. It is neither lines per method nor a percentage. | For the same responsibility, more useful behavior behind a small usable interface is desirable. Values near or below `1` can suggest little hidden behavior in a service. They can be appropriate for data types. Higher values need evidence of information hiding. Extra branching also raises depth, so inspect erosion separately. |

The [Earendil article](https://earendil.com/posts/measuring-code-sloppiness/) reports erosion of `0.31 ± 0.17` for established repositories and `0.68 ± 0.20` for agent code. It reports verbosity of `0.15 ± 0.06` and `0.33 ± 0.10`, respectively. These describe its samples. Our C# syntax rules, switch counting and source population limit cross-project comparisons, especially for verbosity. They are context, not pass/fail thresholds.

### Historical C# references

The [historical reference study](docs/research/type-metric-reference-2026-10-30/README.md) records selected established C# libraries released before 2021, measured with the same counter and metadata references. It retains exact revisions, source manifests, absolute counts and per-type distributions. Release dates provide a comparison period before widespread AI coding; they do not prove authorship or quality.

<!-- historical-reference-table -->

| Project | Pooled direct TCC | Pooled depth |
|---|---:|---:|
| Dapper 2.0.78 | 0.5094 | 3.0158 |
| Serilog v2.10.0 | 0.0166 | 1.7638 |
| Noda Time 3.0.3 | 0.1121 | 1.4737 |
| Newtonsoft.Json 12.0.3 | 0.2022 | 2.6547 |
| NUnit v3.12 | 0.2866 | 1.6776 |
| Autofac v6.0.0 | 0.1729 | 1.6309 |

<!-- /historical-reference-table -->

In this six-project sample, pooled direct TCC spans `0.0166–0.5094` (median `0.1875`), and depth spans `1.4737–3.0158` complexity units per API entry (median `1.7207`). Each project has equal weight in these medians. These put the local reference below in numerical context; they do not define an acceptable band.

Read these as observed reference values, not a quality ranking or a validated acceptable range. Direct TCC can be zero for a coherent value type using properties (`NodaTime.LocalDate`) and one for a forwarding wrapper (`Dapper.DbWrappedReader`). Dapper's two reader types supply 93.22% of its possible pairs; Serilog's `Logger` supplies 90.29%. Depth can be high because explicit interface implementations add WMC but no API entries (`Dapper.BasicWrappedReader`: `36/1`). Check the type's role and the counting rules before judging either extreme. Missing symbols and inactive conditional code also limit comparisons, especially for NUnit, Newtonsoft.Json and Tertius; the study records those limits.

### Local reference examples

At `bf5bf784` (2026-10-30), project erosion was `0.2000`, verbosity `0.0332`, TCC `1887/10179 = 0.1854`, and depth `10122/4771 = 2.1216`. These are historical reference values, not targets. The following examples use that snapshot and `type-metrics-v2` with the hook's default metadata references. Source analysis reported 3,344 binding errors, so the counts describe available symbols. Inspect relevant binding errors before judging a type.

| Type | TCC: shared/possible pairs | Depth: WMC/API entries | What the code demonstrates |
|---|---|---|---|
| [RendererOptions](src/Tertius.Rendering/RendererOptions.cs) | `0/0`, not applicable | `0/20 = 0` | A configuration payload has useful fields and properties without handwritten function bodies. Zero depth is expected here. |
| [DPose](src/Tertius.Core/Math/Pose.cs) | `0/21 = 0` | `17/13 = 1.3077` | Transform operations form one mathematical concept. They access properties, which do not count as direct field sharing. Zero TCC does not establish incoherence. |
| [ToneMapPass](src/Tertius.Rendering/Passes/ToneMapPass.cs) | `1/1 = 1` | `11/4 = 2.7500` | The pass hides shader and output selection. Its maximum TCC rests on just one eligible pair. |
| [TopicSets](src/Tertius.Simulation/Internal/Messaging/TopicSets.cs) | `33/55 = 0.6000` | `83/13 = 6.3846` | Membership and subscriptions serve one recipient policy. Their storage updates stay inside the owner. This is evidence of useful depth. |
| [EventList](src/Tertius.Simulation/Events/EventList.cs) | `28/36 = 0.7778` | `17/14 = 1.2143` | High TCC coexists with exposed storage bookkeeping. Whole-event writes can bypass mask maintenance. [ter-c8lk](.tickets/ter-c8lk.md) records the defect and proposed ownership fix. |

### Calibrating a judgment

1. Compare the same counting version and metadata references. Treat an empty denominator as not applicable and missing legacy data as unavailable.
2. Compare types with similar roles: stateful owners, value types, configuration payloads, or interfaces. Inspect source and callers before looking at their scores. Record a concrete reason to keep or refactor each sampled type, or mark it uncertain.
3. Then examine scores, raw counts and counterexamples. Sample low, middle and high values within each role. Quantiles describe frequency, not quality. Before proposing a cutoff, review examples on both sides and test it on additional types.
4. For a project change, inspect both pooled sums and the types that changed. TCC weights types by their possible pairs, which grow quadratically with eligible method count. Depth pools complexity and API counts, including WMC from types with zero API entries. Neither is a vote over types. A changed mix of types can move either ratio without improving existing code.

Keep TCC and depth separate. Splitting a type can remove non-sharing pairs and raise TCC without changing its behavior. Adding branches can raise depth while worsening erosion. Accept such a change only when the code and its callers demonstrate a design improvement. The earlier 22-type review supplied counterexamples and refactoring tickets. It did not validate TCC/depth cutoffs. The hooks therefore keep both as project statistics, with no automatic type-level finding thresholds.

## Testing

- `WorldOptions.DebugChecks` runs the world's invariant checks at the end of every sync point.
- Mutation testing is not a gate on every epic, because a Stryker run over the whole vertical slice took hours.
  - Every implementation brief asks for hand mutations. For each new test, the agent breaks the line that the test protects, watches the test fail, and restores the line. The report names each mutation. A plain incremental build picks up a mutation, in src and in the analyzers alike (measured on 2026-10-24: 1 to 4 s per build), so mutation builds do not use `--no-incremental`. A mutation can make a loop that never ends, and the tests have no time limit. So each mutation builds only its test project and runs only the test class that covers it, under `timeout 120`. A run that the timeout stops counts as killed. A script that restores a mutated file touches it afterwards, because a restored file with its old time leaves a stale build.
  - Each epic runs coverage on its changed lines before the merge.
  - Stryker runs only on Core, Simulation and Physics, scoped to files or to changed lines.
- A snapshot test proves only that nothing changed. So the first approval of a reference image is a real review against the scene description. If an agent adds or re-approves a reference image, or sees a snapshot fail, it opens three images with the Read tool. They are the reference, the actual image and the FLIP heat map, and its report describes them.
